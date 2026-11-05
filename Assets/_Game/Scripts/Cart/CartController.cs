using System.Collections.Generic;
using Mirror;
using ScavengeNightmare.Audio;
using ScavengeNightmare.Combat;
using ScavengeNightmare.Gameplay;
using ScavengeNightmare.Networking;
using ScavengeNightmare.Noise;
using UnityEngine;

namespace ScavengeNightmare.Cart
{
    /// <summary>
    /// 推车（服务端权威）V2 升级版：
    /// 1) 修复推行中直接折叠抱车导致原推行者双手锁死(handsBusy)与永久减速的 Bug；
    /// 2) 修复抱车玩家倒地或断线时推车悬空卡死在 Carried 状态的 Bug；
    /// 3) 修复其他人展开推车时未解除原抱车者 handsBusy 的 Bug；
    /// 4) 支持从车板上直接抓走单件货物并即时同步 loadKg / itemCount。
    /// </summary>
    public sealed class CartController : NetworkBehaviour
    {
        public enum CartState
        {
            Deployed = 0,
            Folded = 1,
            Carried = 2
        }

        public static CartController Instance { get; private set; }

        public static int LoadEventCount { get; private set; }
        public static int FoldRejectCount { get; private set; }
        public static int MassRatioRejectCount { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Instance = null;
            LoadEventCount = 0;
            FoldRejectCount = 0;
            MassRatioRejectCount = 0;
        }

        [SerializeField] private CartProfile profile;
        [Tooltip("玩家进入推行状态的把手距离")]
        [SerializeField] private float handleReach = 1.6f;
        [Tooltip("推行时车保持在玩家前方多远")]
        [SerializeField] private float handleDistance = 1.15f;
        [SerializeField] private float yawLerp = 8f;
        [Tooltip("装货 / 折叠的可交互距离")]
        [SerializeField] private float interactReach = 1.8f;
        [SerializeField] private float carriedHoldDistance = 0.7f;
        [SerializeField] private float carriedHoldHeight = 0.55f;

        [SyncVar] private int stateInt;
        [SyncVar] private float loadKg;
        [SyncVar] private int itemCount;
        [SyncVar] private float pushSpeedCap;
        [SyncVar] private bool parked;

        private Rigidbody body;
        private BoxCollider hullCollider;
        private Collider[] allSolidColliders;
        private CartCargo cargo;

        private PlayerMotor pusher;
        private PlayerMotor carrier;

        private readonly Dictionary<PlayerMotor, bool> holdPrev = new();
        private readonly Dictionary<PlayerMotor, float> holdTime = new();
        private readonly Dictionary<GrabbableProp, float> loadHalfWidth = new();

        private float stallWindowTimer;
        private Vector3 stallAnchor;
        private int stallCount;
        private bool stalled;
        private float stallStartUnscaled;
        private float unstickReadyAt;
        private float lastNoiseSpeed;

        private PlayerMotor[] playerCache;
        private float nextPlayerRefresh;
        private float interactDebugTimer;

        public CartState State => System.Enum.IsDefined(typeof(CartState), stateInt)
            ? (CartState)stateInt
            : CartState.Deployed;

        public float LoadKg => loadKg;
        public int ItemCount => itemCount;
        public bool HasCargo => itemCount > 0;
        public bool IsParked => parked;
        public bool Stalled => stalled;
        public CartCargo Cargo => cargo;
        public CartProfile Profile => profile;
        public float LastNoiseSpeed => lastNoiseSpeed;
        public PlayerMotor CurrentPusher => pusher;

        public bool WantsUnstickPrompt => stalled && Time.unscaledTime >= unstickReadyAt;

        private void Awake()
        {
            Instance = this;
            body = GetComponent<Rigidbody>();
            hullCollider = GetComponent<BoxCollider>();
            allSolidColliders = GetComponentsInChildren<Collider>(true);
            cargo = GetComponent<CartCargo>();
        }

        private void OnDestroy()
        {
            if (isServer)
            {
                ReleasePusher();
                ReleaseCarrier();
            }

            if (Instance == this)
            {
                Instance = null;
            }
        }

        public override void OnStartServer()
        {
            base.OnStartServer();

            if (profile == null)
            {
                Debug.LogError("[S4A] S4A_CART_FAIL reason=missing_profile");
                return;
            }

            if (cargo == null)
            {
                cargo = gameObject.AddComponent<CartCargo>();
            }

            cargo.Configure(profile, transform);

            if (body != null)
            {
                body.mass = profile.cartMassKg;
            }

            stateInt = (int)CartState.Deployed;
            stallAnchor = transform.position;
            Debug.Log($"[S4A] S4A_CART_READY mass={profile.cartMassKg} deck={profile.deckLength}x{profile.deckWidth}");
        }

        private void Update()
        {
            if (!isServer || profile == null)
            {
                return;
            }

            UpdateInteractions();
        }

        private void FixedUpdate()
        {
            if (!isServer || profile == null || body == null)
            {
                return;
            }

            float dt = Time.fixedDeltaTime;

            if (State == CartState.Carried)
            {
                UpdateCarried(dt);
                return;
            }

            UpdatePushing(dt);
            UpdateStall(dt);

            cargo.Tick(dt);

            loadKg = cargo.LoadKg;
            itemCount = cargo.Count;
        }

        /// <summary>服务端通知：某件货被玩家从车上直接抓走或被偷货怪抢走。</summary>
        [Server]
        public void ServerNotifyPropGrabbed(GrabbableProp prop)
        {
            if (cargo == null || prop == null)
            {
                return;
            }

            if (cargo.Unload(prop))
            {
                loadHalfWidth.Remove(prop);
                loadKg = cargo.LoadKg;
                itemCount = cargo.Count;
            }
        }

        private void UpdatePushing(float dt)
        {
            PlayerMotor found = FindPusher(handleReach);

            if (found != null)
            {
                if (pusher != found)
                {
                    ReleasePusher();
                    pusher = found;

                    GrabController grab = found.GetComponent<GrabController>();
                    grab?.SetHandsBusy(true);

                    Debug.Log($"[S4A] S4A_CART_PUSHER netId={found.netId}");
                }

                DriveBehind(pusher, dt);
                EmitNoise(dt);
            }
            else if (pusher != null)
            {
                ReleasePusher();
            }
            else
            {
                EmitNoise(dt);
            }

            pushSpeedCap = pusher != null ? profile.PushSpeedAtLoad(cargo.LoadKg) : -1f;
        }

        private void DriveBehind(PlayerMotor player, float dt)
        {
            float cap = profile.PushSpeedAtLoad(cargo.LoadKg);

            PlayerMotor motor = player.GetComponent<PlayerMotor>();
            if (motor != null)
            {
                motor.SetExternalSpeedCap(cap);
            }

            Vector3 target = player.transform.position + player.transform.forward * handleDistance;
            target.y = body.position.y;

            Vector3 toTarget = target - body.position;
            toTarget.y = 0f;

            float dist = toTarget.magnitude;

            Vector3 playerVelocity = player.CurrentVelocity;
            Vector3 planarPlayer = new Vector3(playerVelocity.x, 0f, playerVelocity.z);
            float playerSpeed = planarPlayer.magnitude;

            Vector3 desired;
            if (playerSpeed > 0.05f)
            {
                desired = planarPlayer / playerSpeed * Mathf.Min(playerSpeed, cap);
            }
            else
            {
                desired = Vector3.zero;
            }

            if (dist > 0.02f)
            {
                desired += toTarget / dist * Mathf.Clamp(dist * 3f, 0f, 1f);
            }

            body.velocity = new Vector3(desired.x, body.velocity.y, desired.z);

            Quaternion want = Quaternion.Euler(0f, player.transform.eulerAngles.y - 90f, 0f);
            body.MoveRotation(Quaternion.Slerp(body.rotation, want, Mathf.Clamp01(yawLerp * dt)));
        }

        private void UpdateCarried(float dt)
        {
            // 修复 BUG-CART-04：抱车人断线或倒地时自动展开落地，绝不让推车悬在半空失联
            if (carrier == null || carrier.VitalsDowned)
            {
                ForceUnfoldToGround();
                return;
            }

            if (!body.isKinematic)
            {
                body.isKinematic = true;
            }

            SetSolidCollidersEnabled(false);

            Vector3 target = carrier.transform.position
                           + carrier.transform.forward * carriedHoldDistance
                           + Vector3.up * carriedHoldHeight;

            body.MovePosition(Vector3.Lerp(body.position, target, Mathf.Clamp01(12f * dt)));
            body.MoveRotation(Quaternion.Slerp(body.rotation, Quaternion.Euler(0f, carrier.transform.eulerAngles.y, 0f), Mathf.Clamp01(12f * dt)));
        }

        private void SetSolidCollidersEnabled(bool enabled)
        {
            if (hullCollider != null && hullCollider.enabled != enabled)
            {
                hullCollider.enabled = enabled;
            }

            if (allSolidColliders == null)
            {
                return;
            }

            foreach (Collider col in allSolidColliders)
            {
                if (col != null && !col.isTrigger && col.enabled != enabled)
                {
                    col.enabled = enabled;
                }
            }
        }

        private void EmitNoise(float dt)
        {
            if (dt <= 0f)
            {
                return;
            }

            Vector3 v = body.velocity;
            float planar = new Vector2(v.x, v.z).magnitude;
            lastNoiseSpeed = planar;

            if (planar >= 1.5f)
            {
                float pitch = 0.8f + Mathf.Clamp01((planar - 1.5f) / 1.5f) * 0.4f;
                AudioBus.Play(AudioCue.CartWheel, body.position, pitch, $"speed={planar:F1}");
            }

            if (planar < 0.05f)
            {
                return;
            }

            float perSecond = profile.NoiseAtSpeed(planar);
            NoiseBus.ReportRate("cart", perSecond, dt, body.position);
        }

        private void UpdateStall(float dt)
        {
            bool pushing = pusher != null || PushingCandidate();
            stallWindowTimer += dt;

            if (stallWindowTimer < profile.stallWindowSeconds)
            {
                return;
            }

            stallWindowTimer = 0f;
            Vector3 planar = new Vector3(body.position.x, 0f, body.position.z);
            float moved = Vector3.Distance(planar, new Vector3(stallAnchor.x, 0f, stallAnchor.z));
            stallAnchor = body.position;

            if (pushing && moved < profile.stallDistanceMeters)
            {
                stallCount++;

                if (stallCount >= profile.stallCountToJudge && !stalled)
                {
                    stalled = true;
                    stallStartUnscaled = Time.unscaledTime;
                    Debug.LogWarning($"[S4A] S4A_CART_STALL_BEGIN mass={cargo.LoadKg:F1} count={cargo.Count} where={body.position:F2}");
                }
            }
            else
            {
                if (stalled)
                {
                    ReportStallEnd("self");
                }

                stallCount = 0;
            }
        }

        private bool PushingCandidate()
        {
            foreach (PlayerMotor motor in Players())
            {
                if (motor == null || motor.VitalsDowned || !motor.HasMoveInput)
                {
                    continue;
                }

                PlayerInteract interact = motor.GetComponent<PlayerInteract>();
                if (interact == null || !interact.GrabHeldServer)
                {
                    continue;
                }

                if (Vector3.Distance(motor.transform.position, transform.position) <= 2.8f)
                {
                    return true;
                }
            }

            return false;
        }

        private void ReportStallEnd(string rescued)
        {
            float ms = (Time.unscaledTime - stallStartUnscaled) * 1000f;
            Debug.Log($"[S4A] GATE_CART_STALL duration={ms:F0} mass={cargo.LoadKg:F1} count={cargo.Count} where={body.position:F2} rescued={rescued}");
            stalled = false;
            stallCount = 0;
        }

        private void UpdateInteractions()
        {
            PlayerMotor[] players = Players();

            foreach (PlayerMotor motor in players)
            {
                if (motor == null)
                {
                    continue;
                }

                if (motor.VitalsDowned)
                {
                    holdPrev[motor] = false;
                    holdTime[motor] = 0f;
                    continue;
                }

                PlayerInteract interact = motor.GetComponent<PlayerInteract>();
                bool held = interact != null && interact.InteractHeldServer;

                holdPrev.TryGetValue(motor, out bool prev);
                bool pressed = held && !prev;
                holdPrev[motor] = held;

                Vector3 a = motor.transform.position;
                Vector3 b = transform.position;
                a.y = 0f;
                b.y = 0f;
                float dist = Vector3.Distance(a, b);
                bool inReach = dist <= interactReach
                            && Mathf.Abs(motor.transform.position.y - transform.position.y) <= 2f;

                if (held)
                {
                    holdTime[motor] = holdTime.TryGetValue(motor, out float t) ? t + Time.unscaledDeltaTime : Time.unscaledDeltaTime;
                }
                else
                {
                    holdTime[motor] = 0f;
                }

                if (stalled && inReach && held && holdTime[motor] >= profile.unstickHoldSeconds && Time.unscaledTime >= unstickReadyAt)
                {
                    Unstick(motor);
                    holdTime[motor] = 0f;
                    continue;
                }

                if (!pressed || !inReach)
                {
                    continue;
                }

                GrabController grab = motor.GetComponent<GrabController>();
                bool holding = grab != null && grab.IsHolding;

                bool grabHeld = interact != null && interact.GrabHeldServer;

                interactDebugTimer += Time.unscaledDeltaTime;
                if (interactDebugTimer >= 0.4f)
                {
                    interactDebugTimer = 0f;
                    Debug.Log($"[S4A] S4A_CART_INTERACT_DEBUG held={held} grab={grabHeld} pressed={pressed} inReach={inReach} holding={holding} state={State}");
                }

                if (holding)
                {
                    TryLoadFrom(grab);
                    continue;
                }

                ToggleFold(motor, grab);
            }
        }

        private void TryLoadFrom(GrabController grab)
        {
            GrabbableProp prop = grab.HeldProp;

            if (prop == null)
            {
                return;
            }

            if (State != CartState.Deployed)
            {
                Debug.Log("[S4A] S4A_CART_LOAD_REJECT reason=not_deployed");
                return;
            }

            float minMass = prop.Mass;
            float maxMass = prop.Mass;

            foreach (GrabbableProp item in cargo.Items)
            {
                if (item == null)
                {
                    continue;
                }

                minMass = Mathf.Min(minMass, item.Mass);
                maxMass = Mathf.Max(maxMass, item.Mass);
            }

            if (minMass > 0.01f && maxMass / minMass > profile.maxMassRatio)
            {
                MassRatioRejectCount++;
                string ratioId = prop.Profile != null ? prop.Profile.id : "?";
                Debug.Log($"[S4A] S4A_CART_LOAD_REJECT reason=mass_ratio id={ratioId} mass={prop.Mass:F1} min={minMass:F1} max={maxMass:F1} limit={profile.maxMassRatio:F0}");
                return;
            }

            PruneLoadWidths();

            float myHalf = HalfWidth(prop);
            BoxCollider hull = GetComponent<BoxCollider>();
            float hullTop = hull != null ? hull.center.y + hull.size.y * 0.5f : profile.deckHeight;

            if (!cargo.TryFindOpenDeckPosition(myHalf, hullTop, out Vector3 localAnchor))
            {
                Debug.Log($"[S4A] S4A_CART_LOAD_REJECT reason=deck_full id={(prop.Profile != null ? prop.Profile.id : "?")}");
                return;
            }

            if (!cargo.TryLoad(prop, out string reason))
            {
                Debug.Log($"[S4A] S4A_CART_LOAD_REJECT reason={reason} mass={prop.Mass:F1} count={cargo.Count}");
                return;
            }

            ScavengeNightmare.Curses.CursedItem cursed = prop.GetComponent<ScavengeNightmare.Curses.CursedItem>();
            if (cursed != null)
            {
                cursed.ServerSetCartOwner(grab.netId);
                Debug.Log($"[S4B] S4B_CURSE_CART_LOAD id={cursed.CurseId} loader={grab.netId}");
            }

            LoadEventCount++;
            loadHalfWidth[prop] = myHalf;
            cargo.RegisterHalfWidth(prop, myHalf);

            grab.ServerForceRelease();

            Vector3 top = transform.TransformPoint(localAnchor);
            cargo.RegisterAnchor(prop, localAnchor, 0f);

            if (prop.Body != null)
            {
                prop.Body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                prop.Body.MovePosition(top);
                prop.Body.MoveRotation(Quaternion.identity);
                prop.Body.velocity = Vector3.zero;
                prop.Body.angularVelocity = Vector3.zero;
            }

            loadKg = cargo.LoadKg;
            itemCount = cargo.Count;

            Debug.Log($"[S4A] GATE_CART count={cargo.Count} mass={cargo.LoadKg:F1} ids={cargo.IdsJoined()} at_x={localAnchor.x:F2}");

            if (cargo.IsOverCapacity())
            {
                Debug.LogWarning($"[S4A] S4A_CART_OVER_CAPACITY mass={cargo.LoadKg:F1} capacity={profile.capacityKg}");
            }

            if (cargo.IsOverloaded())
            {
                Debug.LogWarning($"[S4A] S4A_CART_OVERLOAD_WARN mass={cargo.LoadKg:F1} hint=tippy");
            }
        }

        private static float HalfWidth(GrabbableProp prop)
        {
            if (prop == null)
            {
                return 0.2f;
            }

            BoxCollider box = prop.GetComponent<BoxCollider>() ?? prop.GetComponentInChildren<BoxCollider>();
            if (box != null)
            {
                Vector3 scale = box.transform.lossyScale;
                float width = Mathf.Max(Mathf.Abs(box.size.x * scale.x), Mathf.Abs(box.size.z * scale.z));

                if (width > 0.05f)
                {
                    return Mathf.Max(width * 0.5f, 0.1f);
                }
            }

            Collider collider = prop.GetComponent<Collider>();
            if (collider == null)
            {
                return 0.2f;
            }

            Vector3 size = collider.bounds.size;
            return Mathf.Max(Mathf.Max(size.x, size.z) * 0.5f, 0.1f);
        }

        private float HalfWidthAtLoad(GrabbableProp prop)
        {
            if (prop != null && loadHalfWidth.TryGetValue(prop, out float width))
            {
                return width;
            }

            return HalfWidth(prop);
        }

        private bool ContainsItem(GrabbableProp prop)
        {
            IReadOnlyList<GrabbableProp> items = cargo.Items;

            for (int i = 0; i < items.Count; i++)
            {
                if (items[i] == prop)
                {
                    return true;
                }
            }

            return false;
        }

        private void PruneLoadWidths()
        {
            if (loadHalfWidth.Count == 0)
            {
                return;
            }

            List<GrabbableProp> stale = null;

            foreach (GrabbableProp key in loadHalfWidth.Keys)
            {
                if (key == null || !ContainsItem(key))
                {
                    stale ??= new List<GrabbableProp>();
                    stale.Add(key);
                }
            }

            if (stale == null)
            {
                return;
            }

            foreach (GrabbableProp key in stale)
            {
                loadHalfWidth.Remove(key);
            }
        }

        private void ToggleFold(PlayerMotor motor, GrabController grab)
        {
            if (State == CartState.Deployed)
            {
                if (cargo.Count > 0)
                {
                    FoldRejectCount++;
                    Debug.Log($"[S4A] S4A_CART_FOLD_REJECT reason=has_cargo count={cargo.Count} hint=unload_first");
                    return;
                }

                // 修复 BUG-CART-03：折叠前必须先释放推行者状态
                ReleasePusher();
                ReleaseCarrier();

                stateInt = (int)CartState.Carried;
                carrier = motor;
                grab?.SetHandsBusy(true);

                if (body != null)
                {
                    body.isKinematic = true;
                    body.velocity = Vector3.zero;
                }

                SetSolidCollidersEnabled(false);

                Debug.Log($"[S4A] S4A_CART_FOLD state=Carried carrier={motor.netId} handsBusy=True");
                return;
            }

            if (State == CartState.Carried)
            {
                ForceUnfoldToGround();
            }
        }

        private void ForceUnfoldToGround()
        {
            stateInt = (int)CartState.Deployed;
            ReleaseCarrier();

            if (body != null)
            {
                body.isKinematic = false;
            }

            SetSolidCollidersEnabled(true);

            stallAnchor = transform.position;
            stallCount = 0;
            stalled = false;

            Debug.Log("[S4A] S4A_CART_UNFOLD state=Deployed handsBusy=False");
        }

        private void ReleaseCarrier()
        {
            if (carrier == null)
            {
                return;
            }

            GrabController carrierGrab = carrier.GetComponent<GrabController>();
            carrierGrab?.SetHandsBusy(false);
            carrier = null;
        }

        private void Unstick(PlayerMotor motor)
        {
            Vector3 origin = motor.transform.position - motor.transform.forward * 1.0f;
            Vector3 point = origin;
            bool found = false;

            for (float r = 0f; r <= profile.unstickMaxDistance && !found; r += 0.5f)
            {
                for (int a = 0; a < 8 && !found; a++)
                {
                    float angle = a * 45f * Mathf.Deg2Rad;
                    Vector3 probe = origin + new Vector3(Mathf.Cos(angle) * r, 1.5f, Mathf.Sin(angle) * r);

                    if (Physics.Raycast(probe, Vector3.down, out RaycastHit hit, 3f))
                    {
                        point = hit.point;
                        found = true;
                    }
                }
            }

            if (!found)
            {
                Debug.LogWarning("[S4A] S4A_CART_UNSTICK_FAIL reason=no_ground_point");
                return;
            }

            TeleportWithCargo(point + Vector3.up * 0.25f);

            NoiseBus.Report("cart_unstick", profile.unstickNoise, body.position);
            unstickReadyAt = Time.unscaledTime + profile.unstickCooldownSeconds;

            ReportStallEnd("unstick");
            Debug.Log($"[S4A] S4A_CART_UNSTICK at=({point.x:F2},{point.z:F2}) cooldown={profile.unstickCooldownSeconds:F0}s");
        }

        private void ReleasePusher()
        {
            if (pusher == null)
            {
                return;
            }

            PlayerMotor motor = pusher.GetComponent<PlayerMotor>();
            if (motor != null)
            {
                motor.SetExternalSpeedCap(-1f);
            }

            GrabController grab = pusher.GetComponent<GrabController>();
            grab?.SetHandsBusy(false);

            pusher = null;
            pushSpeedCap = -1f;
        }

        private PlayerMotor FindPusher(float reach)
        {
            PlayerMotor best = null;
            float bestDistance = reach;

            foreach (PlayerMotor motor in Players())
            {
                if (motor == null || motor.VitalsDowned)
                {
                    continue;
                }

                PlayerInteract interact = motor.GetComponent<PlayerInteract>();
                if (interact == null || !interact.GrabHeldServer)
                {
                    continue;
                }

                Vector3 a = motor.transform.position;
                Vector3 b = transform.position;
                a.y = 0f;
                b.y = 0f;

                if (Mathf.Abs(motor.transform.position.y - transform.position.y) > 2f)
                {
                    continue;
                }

                float d = Vector3.Distance(a, b);
                if (d <= bestDistance)
                {
                    bestDistance = d;
                    best = motor;
                }
            }

            return best;
        }

        private PlayerMotor[] Players()
        {
            if (playerCache != null && Time.unscaledTime < nextPlayerRefresh)
            {
                return playerCache;
            }

            nextPlayerRefresh = Time.unscaledTime + 0.25f;
            playerCache = FindObjectsByType<PlayerMotor>(FindObjectsSortMode.None);
            return playerCache;
        }

        [Server]
        public void TeleportWithCargo(Vector3 position)
        {
            if (body == null)
            {
                return;
            }

            Vector3 delta = position - body.position;
            body.MovePosition(position);
            body.velocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;

            if (cargo != null)
            {
                foreach (GrabbableProp prop in cargo.Items)
                {
                    if (prop == null || prop.Body == null)
                    {
                        continue;
                    }

                    prop.Body.MovePosition(prop.Body.position + delta);
                    prop.Body.velocity = Vector3.zero;
                    prop.Body.angularVelocity = Vector3.zero;
                }
            }

            Physics.SyncTransforms();
            Debug.Log($"[S4A] S4A_CART_TELEPORT to=({position.x:F1},{position.z:F1}) cargo={(cargo != null ? cargo.Count : 0)}");
        }

        [Server]
        public void SetParked(bool value)
        {
            parked = value;
        }

        [Server]
        public List<GrabbableProp> UnloadAll()
        {
            List<GrabbableProp> removed = cargo.UnloadAll();
            loadHalfWidth.Clear();
            loadKg = 0f;
            itemCount = 0;
            return removed;
        }
    }
}
