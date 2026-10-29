using System.Collections.Generic;
using Mirror;
using ScavengeNightmare.Gameplay;
using ScavengeNightmare.Networking;
using ScavengeNightmare.Noise;
using UnityEngine;

namespace ScavengeNightmare.Cart
{
    /// <summary>
    /// 推车（服务端权威）。三条不可动摇的边界：
    /// 1) 物理位姿只在服务端算，客户端同步（NetworkTransform ServerToClient）。
    /// 2) 折叠状态与载重是 SyncVar 单真值——载重影响速度与噪音，绝不能双端各算一套。
    /// 3) 抱车走的是独立跟随，**不走 GrabController**：抓取链路有 breakDistance 脱手判定，
    ///    抱车爬楼梯会被它判成脱手（技术计划风险表已列为必须规避）。
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
        private BoxCollider deckCollider;
        private CartCargo cargo;

        private PlayerMotor pusher;
        private PlayerMotor carrier;

        private readonly Dictionary<PlayerMotor, bool> holdPrev = new();
        private readonly Dictionary<PlayerMotor, float> holdTime = new();

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

        public CartState State => (CartState)stateInt;
        public float LoadKg => loadKg;
        public int ItemCount => itemCount;
        public bool HasCargo => itemCount > 0;
        public bool IsParked => parked;
        public bool Stalled => stalled;
        public CartCargo Cargo => cargo;
        public CartProfile Profile => profile;
        public float LastNoiseSpeed => lastNoiseSpeed;

        public bool WantsUnstickPrompt => stalled && Time.unscaledTime >= unstickReadyAt;

        private void Awake()
        {
            Instance = this;
            body = GetComponent<Rigidbody>();
            hullCollider = GetComponent<BoxCollider>();
            deckCollider = GetComponentInChildren<BoxCollider>();
            cargo = GetComponent<CartCargo>();
        }

        private void OnDestroy()
        {
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
            Debug.Log($"[S4A] S4A_CART_READY mass={profile.cartMassKg} deck={profile.deckLength}x{profile.deckWidth} items={profile.maxItems} cap={profile.capacityKg}");
        }

        // ---------------- 服务端：每帧 ----------------

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

        private void UpdatePushing(float dt)
        {
            PlayerMotor found = FindPusher(handleReach);

            if (found != null)
            {
                if (pusher != found)
                {
                    ReleasePusher();
                    pusher = found;

                    // 推车占住双手：要么抓货、要么推车，不能一边抱着货一边推车。
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
                // 没人推但车在动（被撞、被货砸）：也要算噪音——
                // 「不按按键就不产生噪音」会让玩家用车撞墙变成零成本操作。
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

            // 用「玩家速度」驱动，不用「距离差」驱动。
            // 距离差驱动在死区（0.04m）附近会反复启停：追上就停、停下又被拉开，
            // 实测 200 秒只前进 20 米（平均 0.1 m/s），而限速本来是 2.05 m/s。
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

            // 位置修正：落后越多追得越快（最多 +1 m/s），保证贴住而不是掉队
            if (dist > 0.02f)
            {
                desired += toTarget / dist * Mathf.Clamp(dist * 3f, 0f, 1f);
            }

            body.velocity = new Vector3(desired.x, body.velocity.y, desired.z);

            Quaternion want = Quaternion.Euler(0f, player.transform.eulerAngles.y, 0f);
            body.MoveRotation(Quaternion.Slerp(body.rotation, want, Mathf.Clamp01(yawLerp * dt)));
        }

        private void UpdateCarried(float dt)
        {
            if (carrier == null)
            {
                return;
            }

            // 抱车期间关掉物理：否则上楼梯时车会被台阶撞回，玩家永远爬不上去（A5）
            if (!body.isKinematic)
            {
                body.isKinematic = true;
            }

            // 抱着的车不能挡自己的路：车悬在玩家身前 0.7m，若碰撞体还开着，
            // 它会先一步顶在台阶上，玩家被自己的车挡在楼梯口（实测 40 秒爬升 0 米）。
            if (hullCollider != null && hullCollider.enabled)
            {
                hullCollider.enabled = false;
            }

            Vector3 target = carrier.transform.position
                             + carrier.transform.forward * carriedHoldDistance
                             + Vector3.up * carriedHoldHeight;

            body.MovePosition(Vector3.Lerp(body.position, target, Mathf.Clamp01(12f * dt)));
            body.MoveRotation(Quaternion.Slerp(body.rotation, Quaternion.Euler(0f, carrier.transform.eulerAngles.y, 0f), Mathf.Clamp01(10f * dt)));
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

            if (planar < 0.05f)
            {
                return;
            }

            float perSecond = profile.NoiseAtSpeed(planar);
            NoiseBus.ReportRate("cart", perSecond, dt, body.position);
        }

        /// <summary>
        /// 卡死判定：0.75s 窗口内「有人推」且水平位移 &lt; 0.08m 记一次停滞，连续 2 次判卡死。
        /// 用计数不用计时，是为了避免单帧抖动把一次误判成卡死。
        /// </summary>
        private void UpdateStall(float dt)
        {
            // 不能只看 pusher：车被墙挡住之后玩家继续往前走，两者距离拉开，
            // pusher 会因为超出 handleReach 变成 null，卡死就永远检测不到（实测 150 秒不动）。
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
                    Debug.LogWarning($"[S4A] S4A_CART_STALL_BEGIN mass={cargo.LoadKg:F1} count={cargo.Count} where=({body.position.x:F2},{body.position.z:F2})");
                }
            }
            else
            {
                if (stalled)
                {
                    // 自己出来了：也算一次卡死结束，rescued=self
                    ReportStallEnd("self");
                }

                stallCount = 0;
            }
        }

        /// <summary>
        /// 2.8m 内有玩家、该玩家按着左键、且确实在按方向键 —— 视为「在推」。
        /// 左键这一条不能省：推行现在要求按住左键，没按左键的人只是在车旁边站着，
        /// 那种情况下车不动是正确行为，不该算卡死。
        /// 用输入意图而不是位移：顶着墙时位移为零，但那是在用力推。
        /// </summary>
        private bool PushingCandidate()
        {
            foreach (PlayerMotor motor in Players())
            {
                if (motor == null || !motor.HasMoveInput)
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
            Debug.Log($"[S4A] GATE_CART_STALL duration={ms:F0} mass={cargo.LoadKg:F1} count={cargo.Count} where=({body.position.x:F2},{body.position.z:F2}) rescued={rescued}");
            stalled = false;
            stallCount = 0;
        }

        // ---------------- 服务端：交互 ----------------

        private void UpdateInteractions()
        {
            PlayerMotor[] players = Players();

            foreach (PlayerMotor motor in players)
            {
                if (motor == null)
                {
                    continue;
                }

                PlayerInteract interact = motor.GetComponent<PlayerInteract>();
                bool held = interact != null && interact.InteractHeldServer;

                holdPrev.TryGetValue(motor, out bool prev);
                bool pressed = held && !prev;
                holdPrev[motor] = held;

                float dist = Vector3.Distance(motor.transform.position, transform.position);
                bool inReach = dist <= interactReach;

                if (held)
                {
                    holdTime[motor] = holdTime.TryGetValue(motor, out float t) ? t + Time.unscaledDeltaTime : Time.unscaledDeltaTime;
                }
                else
                {
                    holdTime[motor] = 0f;
                }

                // 脱困优先：卡死时长按 E 抬起推车（1.0s，刻意区别于公秤提交的 1.2s）
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
                    Debug.Log($"[S4A] S4A_CART_INTERACT_DEBUG held={held} grab={grabHeld} pressed={pressed} inReach={inReach} dist={dist:F2} holding={holding} state={State} count={cargo.Count}");
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

            // 先按货物**实际包围盒**算落点，放不下就直接拒绝。
            // 装上去再被挤下来（实测 60kg 保险箱落位后把 1kg 玻璃顶到车前 1.85m）
            // 比拒绝难排查得多，对玩家也更困惑：明明显示装上了，一推车东西掉一地。
            float halfLen = profile.deckLength * 0.5f;
            float cursor = -halfLen;

            foreach (GrabbableProp item in cargo.Items)
            {
                if (item == null)
                {
                    continue;
                }

                cursor += HalfWidth(item) * 2f + 0.04f;
            }

            float myHalf = HalfWidth(prop);
            float lx = cursor + myHalf;
            float lz = 0f;

            if (lx + myHalf > halfLen)
            {
                Debug.Log($"[S4A] S4A_CART_LOAD_REJECT reason=deck_full id={(prop.Profile != null ? prop.Profile.id : "?")} need={lx + myHalf:F2} limit={halfLen:F2} width={myHalf * 2f:F2}");
                return;
            }

            if (!cargo.TryLoad(prop, out string reason))
            {
                Debug.Log($"[S4A] S4A_CART_LOAD_REJECT reason={reason} mass={prop.Mass:F1} count={cargo.Count}");
                return;
            }

            grab.Release(false);

            // 放到碰撞体上表面之上一点点，让它自然落到板面。
            // 放进碰撞体内部的话物理会把它弹飞——症状是「装车成功、一推行全掉」。
            BoxCollider hull = GetComponent<BoxCollider>();
            float hullTop = hull != null ? hull.center.y + hull.size.y * 0.5f : profile.deckHeight;

            Vector3 top = transform.TransformPoint(new Vector3(lx, hullTop + 0.06f, lz));
            cargo.RegisterAnchor(prop, new Vector3(lx, hullTop + 0.06f, lz), 0f);

            if (prop.Body != null)
            {
                prop.Body.MovePosition(top);
                prop.Body.MoveRotation(Quaternion.identity);
                prop.Body.velocity = Vector3.zero;
                prop.Body.angularVelocity = Vector3.zero;
            }

            loadKg = cargo.LoadKg;
            itemCount = cargo.Count;

            Debug.Log($"[S4A] GATE_CART count={cargo.Count} mass={cargo.LoadKg:F1} ids={cargo.IdsJoined()} at_x={lx:F2} width={myHalf * 2f:F2}");

            if (cargo.IsOverCapacity())
            {
                Debug.LogWarning($"[S4A] S4A_CART_OVER_CAPACITY mass={cargo.LoadKg:F1} capacity={profile.capacityKg}");
            }

            if (cargo.IsOverloaded())
            {
                Debug.LogWarning($"[S4A] S4A_CART_OVERLOAD_WARN mass={cargo.LoadKg:F1} hint=tippy");
            }
        }

        /// <summary>
        /// 货物的水平半宽。取世界轴对齐包围盒的 x/z 较大值，对旋转物体偏保守——
        /// 保守只会多留空隙，不会挤到旁边的货。
        /// </summary>
        private static float HalfWidth(GrabbableProp prop)
        {
            if (prop == null)
            {
                return 0.2f;
            }

            Collider collider = prop.GetComponent<Collider>();
            if (collider == null)
            {
                return 0.2f;
            }

            Vector3 size = collider.bounds.size;
            return Mathf.Max(size.x, size.z) * 0.5f;
        }

        private void ToggleFold(PlayerMotor motor, GrabController grab)
        {
            if (State == CartState.Deployed)
            {
                if (cargo.Count > 0)
                {
                    Debug.Log($"[S4A] S4A_CART_FOLD_REJECT reason=has_cargo count={cargo.Count} hint=unload_first");
                    return;
                }

                stateInt = (int)CartState.Carried;
                carrier = motor;
                grab?.SetHandsBusy(true);

                if (body != null)
                {
                    body.isKinematic = true;
                    body.velocity = Vector3.zero;
                }

                Debug.Log($"[S4A] S4A_CART_FOLD state=Carried carrier={motor.netId} handsBusy=True");
                return;
            }

            if (State == CartState.Carried)
            {
                stateInt = (int)CartState.Deployed;
                carrier = null;
                grab?.SetHandsBusy(false);

                if (body != null)
                {
                    body.isKinematic = false;
                }

                if (hullCollider != null)
                {
                    hullCollider.enabled = true;
                }

                stallAnchor = transform.position;
                stallCount = 0;
                stalled = false;

                Debug.Log("[S4A] S4A_CART_UNFOLD state=Deployed handsBusy=False");
            }
        }

        private void Unstick(PlayerMotor motor)
        {
            Vector3 origin = motor.transform.position - motor.transform.forward * 1.0f;
            Vector3 point = origin;
            bool found = false;

            // 在玩家身后 3m 内向四周采样找合法地面点：直接瞬移到原点可能落在墙里或悬空
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

            body.position = point + Vector3.up * 0.25f;
            body.velocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;

            NoiseBus.Report("cart_unstick", profile.unstickNoise, body.position);
            unstickReadyAt = Time.unscaledTime + profile.unstickCooldownSeconds;

            ReportStallEnd("unstick");
            Debug.Log($"[S4A] S4A_CART_UNSTICK at=({point.x:F2},{point.z:F2}) cooldown={profile.unstickCooldownSeconds}s noise=+{profile.unstickNoise}");
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

        /// <summary>
        /// 找正在推车的玩家：**靠近 + 按住左键**两条同时满足才算。
        /// 只按靠近就推会让玩家没有参与感，也不符合抓握的肌肉记忆；
        /// 而只按左键不看距离又会出现隔空推车。
        /// </summary>
        private PlayerMotor FindPusher(float reach)
        {
            PlayerMotor best = null;
            float bestDistance = reach;

            foreach (PlayerMotor motor in Players())
            {
                if (motor == null)
                {
                    continue;
                }

                PlayerInteract interact = motor.GetComponent<PlayerInteract>();
                if (interact == null || !interact.GrabHeldServer)
                {
                    continue;
                }

                // 用水平距离判定：把 y 算进去的话，玩家站在稍高/稍低的地面就会超出把手范围，
                // 推行判定直接失效（实测抬高 0.85m 后距离 1.64m > reach 1.6m，车速归零）。
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

        /// <summary>
        /// 玩家列表 0.25s 缓存。A12 性能是 P0 的阻断项，推行与交互都在每帧跑，
        /// 不能每帧做一次整表分配（D12 的公秤已经因为同样的问题改过一次）。
        /// </summary>
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

        /// <summary>
        /// 自动化专用：瞬移推车，并让车上货物跟着一起走。
        /// 正式玩法里不存在「车瞬移」，这只是为了让 -s4acart 能一步把车挪到公秤停车位。
        /// 不带上货的话，下一帧的水平锚点约束会把货从几十米外硬拽回来，中途被判成滑落。
        /// </summary>
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

        /// <summary>停车位触发区调用（服务端）。</summary>
        [Server]
        public void SetParked(bool value)
        {
            parked = value;
        }

        /// <summary>整车卸货，交给公秤入账。返回卸下的货物。</summary>
        [Server]
        public List<GrabbableProp> UnloadAll()
        {
            List<GrabbableProp> removed = cargo.UnloadAll();
            loadKg = 0f;
            itemCount = 0;
            return removed;
        }
    }
}
