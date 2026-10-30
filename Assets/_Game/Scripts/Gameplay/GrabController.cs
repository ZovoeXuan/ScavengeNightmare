using Mirror;
using ScavengeNightmare.Curses;
using ScavengeNightmare.Networking;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ScavengeNightmare.Gameplay
{
    /// <summary>
    /// D6.5 服务端权威抓取。
    ///
    /// 客户端只做两件事：准星指向谁（本地射线，零延迟高亮）、按下 / 松开（发 Command）。
    /// 真正的「拖着重物走」由服务端在 FixedUpdate 里做速度控制，位置通过 NetworkTransform
    /// （固定 ServerToClient）回传给所有客户端。
    ///
    /// 为什么不再用客户端权威：Mirror 的 NetworkTransform 同步方向不能运行时翻转，
    /// 翻转会重置可靠基线，客户端只能按旧快照慢慢爬（实测同一时刻位置差 8.67m）。
    /// </summary>
    public sealed class GrabController : NetworkBehaviour
    {
        [Header("引用")]
        [SerializeField] private Camera viewCamera;
        [SerializeField] private PlayerMotor motor;

        [Header("拾取（本地射线）")]
        [SerializeField] private float grabRange = 3.2f;
        [SerializeField] private LayerMask grabMask = ~0;

        [Header("跟随（服务端执行）")]
        [SerializeField] private float followStiffness = 16f;
        [Tooltip("持物点相对玩家原点抬高多少（近似相机高度）")]
        [SerializeField] private float holdHeightOffset = 0.45f;

        [SyncVar] private bool isHolding;
        [SyncVar] private float heldMass;

        private GrabbableProp focused;
        private GrabbableProp heldProp;
        private bool inputEnabled = true;
        private Traits.PlayerTraits traits;
        private ScavengeNightmare.Combat.PlayerVitals vitals;
        private float shakePhase;

        /// <summary>Story 3：双手被占用（折叠抱车中）。默认 false。</summary>
        private bool handsBusy;

        /// <summary>S4B-4：诅咒物「长按放下」。客户端只上报按下/松开两个边沿，计时由服务端做。</summary>
        private bool putDownHeld;
        private bool putDownHeldLocal;
        private float putDownAccum;

        /// <summary>D8：角色身份 × 缺点牌。未配置时为中性值，行为与 D7 一致。</summary>
        private Traits.PlayerLoadout Loadout => traits != null ? traits.Loadout : Traits.PlayerLoadout.Neutral;

        /// <summary>D8 交互距离：近视眼看得近。</summary>
        private float EffectiveRange => grabRange * Loadout.InteractRangeScale;

        public bool IsHolding => isHolding;

        /// <summary>Story 3：双手被占用（折叠抱车中）。默认 false，对 D3–D13 完全惰性。</summary>
        public bool HandsBusy => handsBusy;

        /// <summary>UI 用：只有本地玩家才画准星与提示。</summary>
        public bool IsLocalControl => isLocalPlayer;
        public float HeldMass => heldMass;
        public GrabbableProp Focused => focused;
        public bool HasFocus => focused != null;

        /// <summary>当前手持物件（D13 撤离结算用它读价值）。没拿东西时为 null。</summary>
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

            // 诅咒物的「长按放下」不能受 inputEnabled 影响：
            // 4A/4B 的自动化一律把这个开关关掉（否则「没按住左键」会在下一帧把货扔出去），
            // 而放下的长按恰恰是这些用例要驱动的动作。
            UpdateCursePutDown();

            if (viewCamera == null || !inputEnabled)
            {
                return;
            }

            if (isHolding)
            {
                // 无字牌位没有「松手就扔」这条路（§4.4：不能秒扔）。
                // 想放下只能长按 1.5s，中途松手会被服务端打回 GATE_CURSE_HOLD_REJECT。
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
            // D8 近视眼：交互距离按 InteractRangeScale 缩短
            if (!Physics.Raycast(ray, out RaycastHit hit, EffectiveRange, grabMask, QueryTriggerInteraction.Ignore))
            {
                return null;
            }

            GrabbableProp prop = hit.rigidbody != null ? hit.rigidbody.GetComponent<GrabbableProp>() : null;

            // D8「看不清小件」：小物件不高亮、抓不到，必须凑近或让队友帮忙
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

        // ---------- 请求入口 ----------

        public void RequestGrab(GrabbableProp target)
        {
            if (target == null || isHolding)
            {
                return;
            }

            // 4B：倒地的人不能抓货。只判 SyncVar，不判 inputEnabled——
            // inputEnabled 是「本地是否读鼠标」的开关，4A/D4/D6 自动化用它防止自动脱手，
            // 若拿它当抓取闸门，所有既有用例都会当场失效。
            if (vitals != null && vitals.IsDowned)
            {
                Debug.Log("[S4B] S4A_GRAB_REJECT reason=downed");
                return;
            }

            // Story 3：抱车时双手被占，抓货必须被拒（A5 要求折叠抱车期间不能同时抓货）。
            // 默认 false，Main / Supermarket 的 D3–D13 行为一行不变。
            if (handsBusy)
            {
                Debug.Log("[S4A] S4A_GRAB_REJECT reason=hands_busy");
                return;
            }

            if (isServer)
            {
                ServerGrab(target);
                return;
            }

            NetworkIdentity id = target.GetComponent<NetworkIdentity>();
            if (id == null)
            {
                return;
            }

            CmdRequestGrab(id);
        }

        public void RequestRelease(bool asThrow)
        {
            if (!isHolding)
            {
                return;
            }

            if (isServer)
            {
                ServerRelease(asThrow);
            }
            else
            {
                CmdRequestRelease(asThrow);
            }
        }

        /// <summary>兼容 D4 / D6 自动化用例的旧接口。</summary>
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
            // 诅咒物的脱手只能由服务端裁决：长按累计在服务端，倒地/装车这类也是服务端动作。
            // 客户端直接喊「放手」一律不受理——否则改个包就能绕过 1.5s，
            // 而「长按」的整个意义就是不信任客户端的计时。
            float need = RequiredReleaseHold();
            if (need > 0f)
            {
                Debug.Log($"[S4B] GATE_CURSE_HOLD_REJECT need={need:F1} actual=0.00 " +
                          $"netId={netId} reason=client_release");
                return;
            }

            ServerRelease(asThrow);
        }

        // ---------- 服务端实现 ----------

        private void ServerGrab(GrabbableProp prop)
        {
            if (isHolding || prop == null || prop.Body == null)
            {
                return;
            }

            if (vitals != null && vitals.IsDowned)
            {
                Debug.Log($"[S4B] S4A_GRAB_REJECT reason=downed netId={netId}");
                return;
            }

            // 距离校验：防止客户端隔空抓。D8 近视眼的交互距离更短
            if (Vector3.Distance(transform.position, prop.transform.position) > EffectiveRange + 1f)
            {
                return;
            }

            // D8「看不清小件」：服务端同样拦截，客户端不能靠改包绕过
            if (Loadout.IsSmallItem(prop.Mass))
            {
                return;
            }

            heldProp = prop;
            heldMass = prop.Mass;
            isHolding = true;
            prop.SetHeld(true);
            // 门夹等外部事件需要能反向让持有者脱手
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

        /// <param name="force">true = 绕过「长按放下」闸门（倒地、双手被占、超出脱手距离、断开连接）。</param>
        private void ServerRelease(bool asThrow, bool force)
        {
            float need = RequiredReleaseHold();

            // 倒地强制脱手优先于诅咒闸门：4B-1 的「倒地必须掉手持物」不能被一件货挡住。
            bool downed = vitals != null && vitals.IsDowned;

            // 只堵「扔」。脱手（装车、被门卡住、抱车、断线）由各条服务端路径自己 force，
            // 因为 §4.4 说的是「不能秒扔」，不是「不许任何东西把它拿走」。
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
            // D8：大力士扔得远；「手抖」牌的补偿收益也落在这一轴上
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
                return;
            }

            Rigidbody body = heldProp.Body;
            PropPhysicsProfile profile = heldProp.Profile;
            if (body == null || profile == null)
            {
                ServerRelease(false, true);
                return;
            }

            Vector3 target = transform.position
                             + AimDirection() * FeelSettings.HoldDistance(profile.holdDistance)
                             + Vector3.up * holdHeightOffset;
            Vector3 delta = target - body.worldCenterOfMass;

            // 防滑手套：脱手阈值与跟随加速度同时按倍率放大。
            // 阈值放大是「抓得更牢」，加速度放大是「重货不会拖在身后」——两者缺一，
            // 抱 42kg 冲刺时依然会被自己的速度拽脱手。
            float gearScale = ScavengeNightmare.Economy.EquippedEffects.GrabBreakScale;
            float breakDistance = profile.breakDistance * gearScale;

            if (delta.magnitude > breakDistance)
            {
                // 被门框 / 墙卡住：直接脱手，而不是硬拽穿模
                Debug.Log($"[S4A] S4A_HOLD_RELEASE id={profile.id} delta={delta.magnitude:F2} break={breakDistance:F2} gear={gearScale:F2} com={body.worldCenterOfMass} target={target}");
                ServerRelease(false, true);
                return;
            }

            float maxSpeed = FeelSettings.GrabMaxSpeed(profile.grabMaxSpeed) * gearScale;
            // D8：角色手笨/手抖降低跟随刚度，物件更容易落后、被环境磕到
            float stiffness = FeelSettings.FollowStiffness(followStiffness) * Loadout.GrabStiffnessScale;
            Vector3 desired = Vector3.ClampMagnitude(delta * stiffness, maxSpeed);
            body.linearVelocity = Vector3.MoveTowards(
                body.linearVelocity,
                desired,
                FeelSettings.GrabAcceleration(profile.grabAcceleration) * gearScale * Time.fixedDeltaTime);
            body.angularVelocity = Vector3.MoveTowards(body.angularVelocity, Vector3.zero, 18f * Time.fixedDeltaTime);
        }

        /// <summary>
        /// **仅测试构建**：跳过距离与射线校验，直接把目标挂到手上。
        ///
        /// 为什么需要它：自动化抓货是「瞬移玩家 → 瞬移货物 → TryGrab」，
        /// 而 CharacterController 的位姿是延迟更新的，瞬移后立刻读到的仍是上一帧坐标，
        /// 货物被摆到相对旧坐标的持握点上，距离偶尔超过 EffectiveRange + 1，
        /// 于是抓取静默失败、后面整条链崩掉（A4 只推 0.48m、narrow 一件都没装上都是这个）。
        ///
        /// 作用域：方法体只在定义了 S4A_TEST_BUILD 的构建里存在（BuildWindows 的 sprint4a
        /// 分支会加这个宏，正式包没有），且运行期还要求 S4ACli.IsTestRun。
        /// 真人路径的抓取校验仍由 A3 / D13 的真实按键覆盖，不会因此被削弱。
        /// </summary>
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

            // 与 ServerGrab 的唯一差别：不做距离 / 射线 / 近视眼校验。
            // 其余持有状态必须逐字对齐，否则 CartController / CartCargo 看到的是半吊子持有态。
            heldProp = target;
            heldMass = target.Mass;
            isHolding = true;
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

        /// <summary>
        /// 朝向 + 俯仰合成准星方向；俯仰由 PlayerMotor 同步到服务端。
        /// D8「手抖」在这里注入抖动：抖动加在服务端计算的持物目标点上，
        /// 因此四个人都能看到东西在手里晃，而不只是本地画面抖。
        /// </summary>
        private Vector3 AimDirection()
        {
            float pitch = motor != null ? motor.ViewPitchDeg : 0f;
            Vector3 dir = Quaternion.AngleAxis(pitch, transform.right) * transform.forward;

            // S4B-4：抓着无字牌位时手抖。抖动加在服务端算出来的持物目标点上，
            // 所以四个人都能看见端不稳，不只是本地画面在晃。
            if (isHolding && heldProp != null)
            {
                CursedItem cursed = heldProp.GetComponent<CursedItem>();
                if (cursed != null && cursed.HandShakeAmplitude > 0f)
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
                // 两条不同频率的正交噪声：比单纯随机数更像「手在抖」，也不会高频抽搐
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

        /// <summary>
        /// Story 3：折叠抱车时双手被占。置 true 会立刻放下手持物——
        /// 不允许出现「一手抱车、一手抱货」这种物理上不成立的状态。
        /// </summary>
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
                // 强制：不许出现「一手抱车、一手抱着甩不掉的牌位」这种物理上不成立的状态。
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

        // ---------- S4B-4：诅咒物的长按放下 ----------

        /// <summary>手持物要求的放下长按秒数；普通货恒为 0。</summary>
        private float RequiredReleaseHold()
        {
            GrabbableProp prop = heldProp;
            if (prop == null)
            {
                return 0f;
            }

            CursedItem cursed = prop.GetComponent<CursedItem>();
            return cursed != null ? cursed.RequiredReleaseHold : 0f;
        }

        /// <summary>
        /// 客户端只上报「按下 / 松开」两个边沿，长按多久由服务端自己累计。
        /// 反过来（客户端报时长）等于把闸门交给客户端，改个包就能秒扔。
        /// </summary>
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
                }

                return;
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
