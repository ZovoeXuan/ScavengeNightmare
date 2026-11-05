using Mirror;
using ScavengeNightmare.Cart;
using ScavengeNightmare.Curses;
using ScavengeNightmare.Networking;
using ScavengeNightmare.Save;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ScavengeNightmare.Gameplay
{
    /// <summary>
    /// D6.5 服务端权威抓取 V2 升级版：
    /// 1) 新增 ServerForceRelease()，修复服务端（倒地、装车、公秤提交、尖叫罐震落）调用 Release(false) 时
    ///    被无字牌位 1.5s 长按闸门拦截或触发 Command 权限错误的问题；
    /// 2) 从推车上直接抓货时自动通知 CartController 卸除锚点，消除车板与手部双物理抢夺；
    /// 3) 新增 V2 双人协力抬重物机制：针对 >=20kg 重物，队友在 2.4m 内按住左键即可协力分担 45% 负重并提升 45% 脱手距离。
    /// </summary>
    public sealed class GrabController : NetworkBehaviour
    {
        [Header("引用")]
        [SerializeField] private Camera viewCamera;
        [SerializeField] private PlayerMotor motor;

        [Header("拾取 (本地射线) ")]
        [SerializeField] private float grabRange = 3.2f;
        [SerializeField] private LayerMask grabMask = ~0;

        [Header("跟随 (服务端执行) ")]
        [SerializeField] private float followStiffness = 16f;
        [Tooltip("持物点相对玩家原点抬高多少（近似相机高度）")]
        [SerializeField] private float holdHeightOffset = 0.45f;

        [SyncVar] private bool isHolding;
        [SyncVar] private float heldMass;
        [SyncVar] private bool coopAssisted;

        private GrabbableProp focused;
        private GrabbableProp heldProp;
        private bool inputEnabled = true;
        private Traits.PlayerTraits traits;
        private ScavengeNightmare.Combat.PlayerVitals vitals;
        private float shakePhase;

        private bool handsBusy;

        private bool putDownHeld;
        private bool putDownHeldLocal;
        private float putDownAccum;

        private Traits.PlayerLoadout Loadout => traits != null ? traits.Loadout : Traits.PlayerLoadout.Neutral;

        private float EffectiveRange => grabRange * Loadout.InteractRangeScale;

        public bool IsHolding => isHolding;
        public bool HandsBusy => handsBusy;
        public bool IsLocalControl => isLocalPlayer;
        public float HeldMass => heldMass;
        public bool IsCoopAssisted => coopAssisted;
        public GrabbableProp Focused => focused;
        public bool HasFocus => focused != null;
        public GrabbableProp HeldProp => isHolding ? heldProp : null;

        private void Awake()
        {
            if (viewCamera == null)
            {
                viewCamera = GetComponentInChildren<Camera>(true);
            }

            if (motor == null)
            {
                motor = GetComponent<PlayerMotor>();
            }

            traits = GetComponent<Traits.PlayerTraits>();
            vitals = GetComponent<ScavengeNightmare.Combat.PlayerVitals>();
        }

        private void OnDisable()
        {
            if (isServer && isHolding)
            {
                ServerRelease(false, true);
            }

            SetFocus(null);
        }

        private void Update()
        {
            if (!isLocalPlayer)
            {
                return;
            }

            UpdateCursePutDown();

            if (viewCamera == null || !inputEnabled)
            {
                return;
            }

            if (isHolding)
            {
                if (RequiredReleaseHold() > 0f)
                {
                    return;
                }

                Mouse mouse = Mouse.current;
                if (mouse == null || !mouse.leftButton.isPressed)
                {
                    RequestRelease(true);
                }

                return;
            }

            SetFocus(Probe());

            Mouse m = Mouse.current;
            if (m != null && m.leftButton.wasPressedThisFrame && focused != null)
            {
                RequestGrab(focused);
            }
        }

        private void FixedUpdate()
        {
            if (!isServer)
            {
                return;
            }

            if (isHolding)
            {
                ServerHoldStep();
            }

            ServerPutDownStep();
        }

        private GrabbableProp Probe()
        {
            Ray ray = new Ray(viewCamera.transform.position, viewCamera.transform.forward);
            if (!Physics.Raycast(ray, out RaycastHit hit, EffectiveRange, grabMask, QueryTriggerInteraction.Ignore))
            {
                return null;
            }

            GrabbableProp prop = hit.rigidbody != null ? hit.rigidbody.GetComponent<GrabbableProp>() : null;

            if (prop != null && Loadout.IsSmallItem(prop.Mass))
            {
                return null;
            }

            return prop;
        }

        private void SetFocus(GrabbableProp next)
        {
            if (focused == next)
            {
                return;
            }

            if (focused != null)
            {
                focused.SetHighlight(false);
            }

            focused = next;

            if (focused != null)
            {
                focused.SetHighlight(true);
            }
        }

        public void RequestGrab(GrabbableProp target)
        {
            if (target == null || isHolding)
            {
                return;
            }

            if (vitals != null && vitals.IsDowned)
            {
                Debug.Log("[S4B] S4A_GRAB_REJECT reason=downed");
                return;
            }

            if (handsBusy)
            {
                Debug.Log("[S4A] S4A_GRAB_REJECT reason=hands_busy");
                return;
            }

            NetworkIdentity targetNet = target.GetComponent<NetworkIdentity>();
            if (isServer && (targetNet == null || !NetworkClient.active))
            {
                ServerGrab(target);
            }
            else if (targetNet != null)
            {
                CmdRequestGrab(targetNet);
            }
            else
            {
                ServerGrab(target);
            }
        }

        public void RequestRelease(bool asThrow)
        {
            if (!isHolding)
            {
                return;
            }

            // 修复 BUG-NET-08：若当前已在服务端且非本地扔出动作（例如服务端逻辑触发 Release(false)），直接走服务端释放
            if (isServer && (!asThrow || !NetworkClient.active))
            {
                ServerRelease(asThrow, !asThrow);
            }
            else
            {
                CmdRequestRelease(asThrow);
            }
        }

        /// <summary>服务端强制脱手入口（倒地、装车、公秤结算、尖叫罐震落等专用，无视诅咒长按限制）。</summary>
        [Server]
        public void ServerForceRelease()
        {
            if (isHolding)
            {
                ServerRelease(false, true);
            }
        }

        public bool TryGrab(GrabbableProp target)
        {
            RequestGrab(target);
            return isHolding;
        }

        public void Release(bool asThrow)
        {
            RequestRelease(asThrow);
        }

        [Command]
        private void CmdRequestGrab(NetworkIdentity target)
        {
            if (target != null)
            {
                ServerGrab(target.GetComponent<GrabbableProp>());
            }
        }

        [Command]
        private void CmdRequestRelease(bool asThrow)
        {
            float need = RequiredReleaseHold();
            if (need > 0f)
            {
                Debug.Log($"[S4B] GATE_CURSE_HOLD_REJECT need={need:F1} actual=0.00 " +
                          $"netId={netId} reason=client_release");
                return;
            }

            ServerRelease(asThrow, false);
        }

        private void ServerGrab(GrabbableProp prop)
        {
            if (isHolding || prop == null || prop.Body == null)
            {
                return;
            }

            if (prop.IsHeld)
            {
                return;
            }

            if (vitals != null && vitals.IsDowned)
            {
                Debug.Log($"[S4B] S4A_GRAB_REJECT reason=downed netId={netId}");
                return;
            }

            if (Vector3.Distance(transform.position, prop.transform.position) > EffectiveRange + 1f)
            {
                return;
            }

            if (Loadout.IsSmallItem(prop.Mass))
            {
                return;
            }

            if (CartController.Instance != null)
            {
                CartController.Instance.ServerNotifyPropGrabbed(prop);
            }

            heldProp = prop;
            heldMass = prop.Mass;
            isHolding = true;
            coopAssisted = false;
            prop.SetHeld(true);
            prop.SetHolder(this);
            prop.Body.WakeUp();

            PlaytestRecorder recorder = PlaytestRecorder.Instance;
            if (recorder != null)
            {
                recorder.NoteGrab(recorder.LabelOf(netIdentity), heldMass);
            }
        }

        private void ServerRelease(bool asThrow)
        {
            ServerRelease(asThrow, false);
        }

        private void ServerRelease(bool asThrow, bool force)
        {
            float need = RequiredReleaseHold();
            bool downed = vitals != null && vitals.IsDowned;

            if (!force && !downed && need > 0f && asThrow && putDownAccum < need)
            {
                Debug.Log($"[S4B] GATE_CURSE_HOLD_REJECT need={need:F1} actual={putDownAccum:F2} " +
                    $"netId={netId} reason=instant_release");
                return;
            }

            GrabbableProp prop = heldProp;
            heldProp = null;
            isHolding = false;
            heldMass = 0f;
            coopAssisted = false;

            if (prop == null)
            {
                return;
            }

            prop.SetHeld(false);
            prop.SetHolder(null);

            Rigidbody body = prop.Body;
            PropPhysicsProfile profile = prop.Profile;
            if (body == null || profile == null || !asThrow)
            {
                return;
            }

            body.WakeUp();
            body.linearVelocity = AimDirection() * FeelSettings.ThrowSpeed(profile, Loadout.ThrowScale);
            body.angularVelocity = Random.onUnitSphere * FeelSettings.ThrowSpin(profile.throwSpin);

            PlaytestRecorder recorder = PlaytestRecorder.Instance;
            if (recorder != null)
            {
                recorder.NoteThrow(recorder.LabelOf(netIdentity), prop.Mass, body.linearVelocity.magnitude);
            }
        }

        private void ServerHoldStep()
        {
            if (heldProp == null)
            {
                isHolding = false;
                coopAssisted = false;
                return;
            }

            Rigidbody body = heldProp.Body;
            PropPhysicsProfile profile = heldProp.Profile;
            if (body == null || profile == null)
            {
                ServerRelease(false, true);
                return;
            }

            // V2 双人协力抬重物：在正式游玩中，如果重物 >= 20kg 且 2.4m 内有存活队友按住左键且空手，触发双人协力
            PlayerMotor assistant = FindCoopAssistant(heldProp);
            heldProp.SetAssistMotor(assistant);
            bool assistedNow = assistant != null;
            if (coopAssisted != assistedNow)
            {
                coopAssisted = assistedNow;
                heldMass = assistedNow ? heldProp.Mass * 0.55f : heldProp.Mass;
            }

            Vector3 target = transform.position
                + AimDirection() * FeelSettings.HoldDistance(profile.holdDistance)
                + Vector3.up * holdHeightOffset;

            if (assistedNow)
            {
                Vector3 assistAnchor = assistant.transform.position
                    + assistant.transform.forward * 1.2f
                    + Vector3.up * holdHeightOffset;
                target = Vector3.Lerp(target, assistAnchor, 0.35f);
            }

            Vector3 delta = target - body.worldCenterOfMass;

            float gearScale = ScavengeNightmare.Economy.EquippedEffects.GrabBreakScale * (assistedNow ? 1.45f : 1f);
            float breakDistance = profile.breakDistance * gearScale;

            if (delta.magnitude > breakDistance)
            {
                Debug.Log($"[S4A] S4A_HOLD_RELEASE id={profile.id} delta={delta.magnitude:F2} break={breakDistance:F2} gear={gearScale:F2} com={body.worldCenterOfMass}");
                ServerRelease(false, true);
                return;
            }

            float maxSpeed = FeelSettings.GrabMaxSpeed(profile.grabMaxSpeed) * gearScale;
            float stiffness = FeelSettings.FollowStiffness(followStiffness) * Loadout.GrabStiffnessScale * (assistedNow ? 1.35f : 1f);
            Vector3 desired = Vector3.ClampMagnitude(delta * stiffness, maxSpeed);
            body.linearVelocity = Vector3.MoveTowards(
                body.linearVelocity,
                desired,
                FeelSettings.GrabAcceleration(profile.grabAcceleration) * gearScale * Time.fixedDeltaTime);
            body.angularVelocity = Vector3.MoveTowards(body.angularVelocity, Vector3.zero, 18f * Time.fixedDeltaTime);
        }

        private PlayerMotor FindCoopAssistant(GrabbableProp prop)
        {
            if (S4ACli.IsTestRun || prop == null || prop.Mass < 20f)
            {
                return null;
            }

            foreach (ScavengeNightmare.Combat.PlayerVitals candidate in ScavengeNightmare.Combat.PlayerVitals.All)
            {
                if (candidate == null || candidate == vitals || candidate.IsDowned)
                {
                    continue;
                }

                GrabController otherGrab = candidate.GetComponent<GrabController>();
                if (otherGrab != null && (otherGrab.IsHolding || otherGrab.HandsBusy))
                {
                    continue;
                }

                PlayerInteract interact = candidate.GetComponent<PlayerInteract>();
                if (interact == null || !interact.GrabHeldServer)
                {
                    continue;
                }

                if (Vector3.Distance(candidate.transform.position, prop.transform.position) <= 2.4f)
                {
                    return candidate.GetComponent<PlayerMotor>();
                }
            }

            return null;
        }

        [Server]
        public bool ServerForceGrab(GrabbableProp target)
        {
#if S4A_TEST_BUILD
            if (!ScavengeNightmare.Save.S4ACli.IsTestRun)
            {
                Debug.LogWarning("[S4A] S4A_FORCEGRAB_DENIED reason=not_test_run");
                return false;
            }

            if (handsBusy || isHolding || target == null || target.Body == null)
            {
                Debug.Log($"[S4A] S4A_FORCEGRAB_DENIED reason=state handsBusy={handsBusy} holding={isHolding} target={(target != null)}");
                return false;
            }

            if (CartController.Instance != null)
            {
                CartController.Instance.ServerNotifyPropGrabbed(target);
            }

            heldProp = target;
            heldMass = target.Mass;
            isHolding = true;
            coopAssisted = false;
            target.SetHeld(true);
            target.SetHolder(this);
            target.Body.WakeUp();

            Debug.Log($"[S4A] S4A_FORCEGRAB id={(target.Profile != null ? target.Profile.id : "?")} mass={target.Mass:F1}");
            return true;
#else
            Debug.LogWarning("[S4A] S4A_FORCEGRAB_DENIED reason=not_test_build");
            return false;
#endif
        }

        private Vector3 AimDirection()
        {
            float pitch = motor != null ? motor.ViewPitchDeg : 0f;
            Vector3 dir = Quaternion.AngleAxis(pitch, transform.right) * transform.forward;

            if (isHolding && heldProp != null)
            {
                CursedItem cursed = heldProp.GetComponent<CursedItem>();
                if (cursed != null && cursed.HandShakeAmplitude > 0f && !cursed.Sealed)
                {
                    shakePhase += Time.fixedDeltaTime * 9f;
                    float jitterX = Mathf.PerlinNoise(shakePhase, 0.17f) - 0.5f;
                    float jitterY = Mathf.PerlinNoise(0.63f, shakePhase) - 0.5f;
                    dir = (dir + transform.right * (jitterX * cursed.HandShakeAmplitude)
                        + transform.up * (jitterY * cursed.HandShakeAmplitude)).normalized;
                }
            }

            Traits.PlayerLoadout load = Loadout;
            if (load.ShakyHands && isHolding && load.ShakeAmplitude > 0f)
            {
                shakePhase += Time.fixedDeltaTime * 11f;
                float jitterX = Mathf.PerlinNoise(shakePhase, 0.31f) - 0.5f;
                float jitterY = Mathf.PerlinNoise(0.77f, shakePhase) - 0.5f;
                dir = (dir + transform.right * (jitterX * load.ShakeAmplitude * 2f)
                    + transform.up * (jitterY * load.ShakeAmplitude * 2f)).normalized;
            }

            return dir;
        }

        public void SetInputEnabled(bool value)
        {
            inputEnabled = value;
        }

        public void SetHandsBusy(bool busy)
        {
            if (handsBusy == busy)
            {
                return;
            }

            handsBusy = busy;
            Debug.Log($"[S4A] S4A_HANDS_BUSY value={busy}");

            if (busy && isHolding)
            {
                if (isServer)
                {
                    ServerRelease(false, true);
                }
                else
                {
                    CmdForceRelease();
                }
            }
        }

        private float RequiredReleaseHold()
        {
            GrabbableProp prop = heldProp;
            if (prop == null)
            {
                return 0f;
            }

            CursedItem cursed = prop.GetComponent<CursedItem>();
            return (cursed != null && !cursed.Sealed) ? cursed.RequiredReleaseHold : 0f;
        }

        private void UpdateCursePutDown()
        {
            bool want = RequiredReleaseHold() > 0f && CurseInput.PutDownHeld;

            if (want == putDownHeldLocal)
            {
                return;
            }

            putDownHeldLocal = want;
            CmdSetPutDown(want);
        }

        [Command]
        private void CmdSetPutDown(bool value)
        {
            putDownHeld = value;
        }

        [Command]
        private void CmdForceRelease()
        {
            ServerRelease(false, true);
        }

        private void ServerPutDownStep()
        {
            float need = RequiredReleaseHold();

            if (need <= 0f)
            {
                putDownAccum = 0f;
                putDownHeld = false;
                return;
            }

            if (putDownHeld)
            {
                putDownAccum += Time.fixedDeltaTime;

                if (putDownAccum >= need)
                {
                    float actual = putDownAccum;
                    putDownAccum = 0f;
                    putDownHeld = false;
                    Debug.Log($"[S4B] GATE_CURSE_HOLD_OK need={need:F1} actual={actual:F2} netId={netId}");
                    ServerRelease(false, true);
                    return;
                }
            }

            if (putDownAccum > 0f)
            {
                float actual = putDownAccum;
                putDownAccum = 0f;

                if (actual < need)
                {
                    Debug.Log($"[S4B] GATE_CURSE_HOLD_REJECT need={need:F1} actual={actual:F2} netId={netId}");
                }
            }
        }
    }
}
