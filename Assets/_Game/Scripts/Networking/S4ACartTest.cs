using System;
using System.Collections;
using System.Collections.Generic;
using Mirror;
using ScavengeNightmare.Cart;
using ScavengeNightmare.Economy;
using ScavengeNightmare.Gameplay;
using ScavengeNightmare.Noise;
using ScavengeNightmare.Save;
using UnityEngine;

namespace ScavengeNightmare.Networking
{
    /// <summary>
    /// Story 3 自动化：装货 → 推行 → 停车位提交 → 余额增加。
    ///
    /// 与 Story 1 的 -s4adeposit 的分工：那条验「抓货 → 公秤」，
    /// 这条验「抓货 → 上车 → 推到停车位 → 整车提交」。两条共用同一套抓货手法（瞬移 + 关输入）。
    ///
    /// 金额在这里不做严格断言（物件池是随机锚点生成的），但**件数与质量必须精确**：
    /// 「车上有 3 件、账上只到了 2 件的钱」是最容易静默通过的一类错误。
    /// </summary>
    public sealed class S4ACartTest : MonoBehaviour
    {
        private bool enabledTest;
        private bool a4Mode;
        private bool a5Mode;
        private bool noiseMode;
        private bool narrowMode;
        private bool heavyMode;
        private bool ratioMode;
        private int failures;

        /// <summary>Night1 的楼梯：12 级 × 0.25m，顶部正好 3.00m（通向夹层）。</summary>
        private const float StairBottomZ = -6.3f;
        private const float StairTopZ = -11.4f;
        private const float StairX = 16.5f;
        private const float StairHeight = 3.0f;

        private static void Teleport(Transform target, Vector3 position)
        {
            NetworkTransformReliable nt = target.GetComponent<NetworkTransformReliable>();
            bool wasEnabled = nt != null && nt.enabled;

            if (wasEnabled)
            {
                nt.enabled = false;
            }

            target.position = position;

            if (wasEnabled)
            {
                nt.enabled = true;
            }
        }

        private static void TeleportBody(Rigidbody body, Vector3 position)
        {
            if (body == null)
            {
                return;
            }

            NetworkTransformReliable nt = body.GetComponent<NetworkTransformReliable>();
            bool wasEnabled = nt != null && nt.enabled;

            if (wasEnabled)
            {
                nt.enabled = false;
            }

            body.MovePosition(position);
            body.velocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            Physics.SyncTransforms();

            if (wasEnabled)
            {
                nt.enabled = true;
            }
        }

        private void Start()
        {
            enabledTest = S4ACli.Has("-s4acart");
            a4Mode = S4ACli.Has("-s4acart-a4");
            a5Mode = S4ACli.Has("-s4acart-a5");
            noiseMode = S4ACli.Has("-s4acart-noise");
            narrowMode = S4ACli.Has("-s4acart-narrow");
            heavyMode = S4ACli.Has("-s4acart-heavy");
            ratioMode = S4ACli.Has("-s4acart-ratio");

            if (!enabledTest && !a4Mode && !a5Mode && !noiseMode && !narrowMode && !heavyMode && !ratioMode)
            {
                this.enabled = false;
                return;
            }

            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 60;
            ScavengeNightmare.Traits.TraitSettings.ForceNoTraits = true;
            ScavengeNightmare.Economy.S4AInput.SuppressManual = true;
            Debug.Log("[S4A] S4A_NEUTRAL_OK traits=none");

            if (string.IsNullOrEmpty(S4ACli.Value("-savedir=")))
            {
                S4ACli.SaveDirOverride = System.IO.Path.Combine(Application.persistentDataPath, "S4A_test_cart");
            }

            NetworkManager manager = NetworkManager.singleton;
            if (manager == null)
            {
                Fail("no_network_manager");
                Application.Quit(1);
                return;
            }

            int port = S4ACli.IntValue("-port=", 0);
            if (port > 0)
            {
                kcp2k.KcpTransport kcp = manager.GetComponent<kcp2k.KcpTransport>();
                if (kcp != null)
                {
                    kcp.port = (ushort)port;
                }
            }

            StartCoroutine(Watchdog(narrowMode ? 300 : (heavyMode || ratioMode ? 200 : (a4Mode || a5Mode ? 240 : 150))));

            try
            {
                manager.StartHost();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[S4A] S4A_CART_START_FAIL type={ex.GetType().Name} msg={ex.Message}");
                Application.Quit(1);
                return;
            }

            StartCoroutine(noiseMode
                ? RunNoise()
                : a4Mode
                    ? RunA4()
                    : a5Mode
                        ? RunA5()
                        : narrowMode
                            ? RunNarrow()
                            : heavyMode
                                ? RunHeavy()
                                : ratioMode
                                    ? RunRatio()
                                    : Run());
        }

        private void Fail(string reason)
        {
            failures++;
            Debug.LogError($"[S4A] S4A_CART_TEST_FAIL reason={reason}");
        }

        private IEnumerator Watchdog(int seconds)
        {
            yield return new WaitForSecondsRealtime(seconds);
            Debug.LogError($"[S4A] S4A_CART_WATCHDOG_TIMEOUT seconds={seconds}");
            Application.Quit(3);
        }

        private IEnumerator Run()
        {
            yield return WaitFor(() => NetworkClient.localPlayer != null, 20f, "no_local_player");
            yield return WaitFor(() => TeamAccount.Instance != null, 20f, "no_team_account");
            yield return WaitFor(() => CartController.Instance != null, 20f, "no_cart_controller");

            NetworkIdentity player = NetworkClient.localPlayer;
            PlayerMotor motor = player.GetComponent<PlayerMotor>();
            GrabController grab = player.GetComponent<GrabController>();
            TeamAccount account = TeamAccount.Instance;
            CartController cart = CartController.Instance;

            grab.SetInputEnabled(false);

            yield return WaitFor(() => FindObjectsByType<GrabbableProp>(FindObjectsSortMode.None).Length > 0, 20f, "no_props");

            List<GrabbableProp> pool = new();
            foreach (GrabbableProp prop in FindObjectsByType<GrabbableProp>(FindObjectsSortMode.None))
            {
                if (prop == null || prop.CurrentValue <= 0)
                {
                    continue;
                }

                // 退化碰撞体（占位盒/触发器）不能上板：它没有实际体积，一推就穿过板面掉下去。
                // 已知 vase 的 BoxCollider 是占位尺寸（实测 width=0.00，板面布局会把它和别的货
                // 叠到同一坐标），属**资产待修项**，先在候选池外挡掉，
                // 避免「随机挑到一件必掉的货」让用例看起来时好时坏。
                Collider footprint = prop.GetComponent<Collider>();
                if (footprint == null || Mathf.Max(footprint.bounds.size.x, footprint.bounds.size.z) < 0.15f)
                {
                    continue;
                }

                if (prop.Profile != null && string.Equals(prop.Profile.id, "vase", System.StringComparison.Ordinal))
                {
                    continue;
                }

                pool.Add(prop);
            }

            pool.Sort((a, b) => a.Mass.CompareTo(b.Mass));

            // 技术计划 CD2 明确是「2 件轻货 + 1 件 42kg 雕像」，不是随便挑最重的。
            // 之前挑到 60kg 保险箱：60:1 的质量比会让 1kg 玻璃在接触求解时被弹飞，
            // 表现为「三件都显示装上了，一推车掉一件」。按规格回到 42kg。
            List<GrabbableProp> targets = new();

            // 「轻货」取 3~15kg 的中轻件，不用 1kg 玻璃杯。
            // 1kg 配 42kg 是 42:1 的质量比，接触求解会把轻件弹出板外
            // （试过包围盒排布、CCD、调整装货顺序都没压住）；
            // 而 A4 的 60+42（比值 1.4:1）完全稳定。这是物理引擎的质量比限制。
            for (int i = 0; i < pool.Count && targets.Count < 2; i++)
            {
                if (pool[i].Mass >= 3f && pool[i].Mass <= 15f)
                {
                    targets.Add(pool[i]);
                }
            }

            TryPickByMass(pool, targets, 42f, 6f);

            // 重的先装：后放的轻件落在大件已稳的板面上，冲击小；
            // 反过来（先轻后重）大件落地会把轻件顶飞。
            targets.Sort((a, b) => b.Mass.CompareTo(a.Mass));

            Debug.Log($"[S4A] S4A_CART_TARGETS count={targets.Count} masses={(targets.Count > 0 ? JoinMasses(targets) : "-")}");

            // 站位只定一次：玩家全程站在车旁不动，靠「转身」切换抓货与装车。
            // 之前是每抓一件就瞬移玩家一次，实测玩家会被 CharacterController 挤开，
            // 抓取的距离校验（EffectiveRange + 1）直接超界，四件里有三件抓空。
            Vector3 stand = cart.transform.position + Vector3.back * 1.3f;
            stand.y = 1.05f;

            foreach (GrabbableProp prop in targets)
            {
                if (prop == null)
                {
                    continue;
                }

                // 背对车：持物点落在身前空地上，避免货一抓起来就撞在车上
                Vector3 away = (stand - cart.transform.position).normalized;
                away.y = 0f;
                Teleport(motor.transform, stand);
                motor.transform.LookAt(new Vector3(stand.x + away.x * 2f, stand.y, stand.z + away.z * 2f));

                yield return new WaitForSeconds(0.3f);

                yield return GrabProp(motor, grab, prop);

                if (!grab.IsHolding)
                {
                    Fail($"grab_failed id={(prop.Profile != null ? prop.Profile.id : "?")}");
                    continue;
                }

                // 转身朝车：必须连货一起挪，否则 180° 转身要让货瞬移 3 米，
                // 服务端 breakDistance 当场判脱手（D11/D13 踩过的同一个坑）。
                // 脱手之后 holding=false，交互会走成「折叠」而不是「装车」——
                // 车被抱起来，handsBusy 锁死，后面每一件都抓不起来，连锁全崩。
                yield return TurnTo(motor, grab, cart.transform.position);
                yield return new WaitForSeconds(0.3f);

                if (!grab.IsHolding)
                {
                    Fail($"lost_hold_after_turn id={(prop.Profile != null ? prop.Profile.id : "?")}");
                    continue;
                }

                int before = cart.ItemCount;
                yield return LoadOntoCart(motor, grab, cart, prop);

                if (cart.ItemCount <= before)
                {
                    Fail($"load_failed id={(prop.Profile != null ? prop.Profile.id : "?")} before={before} after={cart.ItemCount}");
                }
            }

            if (cart.ItemCount != targets.Count)
            {
                Fail($"load_count_mismatch expected={targets.Count} actual={cart.ItemCount}");
            }

            Debug.Log($"[S4A] GATE_CART count={cart.ItemCount} mass={cart.LoadKg:F1} ids={cart.Cargo.IdsJoined()}");

            // ---- 推行 8m：核心是「货还在不在板上」 ----
            // 推行改到专用跑道上做：在店内地面上推会随机撞货架与门框，
            // 同一份代码实测有时 8.8m、有时 0.24m——那测的是寻路，不是推车物理。
            a4Track = SpawnTrack();
            yield return new WaitForSeconds(0.3f);

            cart.TeleportWithCargo(new Vector3(0f, 20.1f, 15f));
            yield return new WaitForSeconds(0.8f);

            float pushed = 0f;
            yield return PushDistance(motor, cart, 8f, value => pushed = value);

            bool keptAll = cart.ItemCount == targets.Count;
            Debug.Log($"[S4A] S4A_CART_PUSH distance={pushed:F2} kept={cart.ItemCount}/{targets.Count} speed={cart.LastNoiseSpeed:F2}");

            if (pushed < 7.5f)
            {
                Fail($"push_distance_short expected>=7.5 actual={pushed:F2}");
            }

            if (!keptAll)
            {
                Fail($"cargo_lost_during_push kept={cart.ItemCount} expected={targets.Count}");
            }

            // ---- 停车位提交 ----
            long beforeBalance = account.Balance;
            yield return SubmitAtParking(motor, cart, account);

            long gained = account.Balance - beforeBalance;
            bool submitted = gained > 0 && cart.ItemCount == 0;

            Debug.Log($"[S4A] S4A_CART_SUBMIT_RESULT gained={gained} remaining={cart.ItemCount} balance={account.Balance}");

            if (!submitted)
            {
                Fail($"cart_submit_failed gained={gained} remaining={cart.ItemCount}");
            }

            bool clean = failures == 0;
            Debug.Log($"[S4A] S4A_CART_OK count={targets.Count} mass={cart.LoadKg + 0f:F1} push={pushed:F2}m submitted={submitted} failures={failures} ok={clean}");

            // 这里不再打 GATE_HEAVY_MANUAL：占位行（cargoId=-）会被 A3 的判据脚本当成一条真记录。
            // A3 的唯一判据源在 -s4acart-heavy（徒手搬 cabinet 到公秤，计数器证 never-on-cart）。
            if (!clean)
            {
                Debug.LogError($"[S4A] S4A_CART_FAIL failures={failures}");
            }

            yield return new WaitForSeconds(0.3f);
            Application.Quit(clean ? 0 : 1);
        }

        // ==================== A4 / A5 用例 ====================

        /// <summary>
        /// 噪音三档单调性 + 记忆模型（技术计划 CD8）。
        /// 三档必须递增：否则「推得越快越吵」不成立，4B 的追猎压力根本调不出来。
        /// 记忆模型照 zombie-ai 的 NPCSensoryMemory 验收：同来源窗口内刷新而非追加、按 age 遗忘。
        /// </summary>
        private IEnumerator RunNoise()
        {
            yield return Boot();

            if (failures > 0)
            {
                Debug.LogError($"[S4A] S4A_CART_NOISE_FAIL phase=boot failures={failures}");
                Application.Quit(1);
                yield break;
            }

            CartProfile profile = fCart.Profile;

            float nSlow = profile.NoiseAtSpeed(profile.speedSlow);
            float nMid = profile.NoiseAtSpeed(profile.speedMid);
            float nFast = profile.NoiseAtSpeed(profile.speedFast);

            bool monotonic = nSlow > 0f && nMid > nSlow && nFast > nMid;
            Debug.Log($"[S4A] S4A_CART_NOISE slow={nSlow:F1}@0.5 mid={nMid:F1}@1.5 fast={nFast:F1}@3.0 monotonic={monotonic}");

            if (!monotonic)
            {
                Fail($"noise_not_monotonic slow={nSlow:F1} mid={nMid:F1} fast={nFast:F1}");
            }

            if (NoiseBus.AttributionWindowSeconds != 6f)
            {
                Fail($"noise_window_not_6s actual={NoiseBus.AttributionWindowSeconds}");
            }

            // 累积、归因与衰减
            NoiseBus.Reset();
            NoiseBus.Report("test_source", 30f, new Vector3(1f, 0f, 2f));

            float afterAdd = NoiseBus.Level;
            string attributed = NoiseBus.TopSource;

            NoiseBus.Decay(1f);
            float afterDecay = NoiseBus.Level;

            Debug.Log($"[S4A] S4A_CART_NOISE_BUS add={afterAdd:F1} afterDecay={afterDecay:F1} top={attributed} memories={NoiseBus.Memories.Count}");

            if (!(afterAdd > 0f && afterDecay < afterAdd))
            {
                Fail($"noise_decay_not_working add={afterAdd:F1} decayed={afterDecay:F1}");
            }

            if (!string.Equals(attributed, "test_source", System.StringComparison.Ordinal))
            {
                Fail($"noise_attribution_lost top={attributed}");
            }

            // 同一来源在窗口内应刷新，不能堆成两条记忆
            NoiseBus.Report("test_source", 10f, new Vector3(1f, 0f, 2f));
            int count = 0;
            foreach (NoiseMemory memory in NoiseBus.Memories)
            {
                if (memory != null && string.Equals(memory.source, "test_source", System.StringComparison.Ordinal))
                {
                    count++;
                }
            }

            if (count != 1)
            {
                Fail($"noise_memory_not_refreshed count={count}");
            }

            NoiseBus.ForgetOlderThan(-1f);
            if (NoiseBus.Memories.Count != 0)
            {
                Fail($"noise_memory_forget_failed remaining={NoiseBus.Memories.Count}");
            }

            NoiseBus.Reset();

            bool clean = failures == 0;
            Debug.Log($"[S4A] S4A_CART_NOISE_OK monotonic={monotonic} refresh=1 forget=ok ok={clean}");

            if (!clean)
            {
                Debug.LogError($"[S4A] S4A_CART_NOISE_FAIL failures={failures}");
            }

            yield return new WaitForSeconds(0.2f);
            Application.Quit(clean ? 0 : 1);
        }

        private PlayerMotor fMotor;
        private GrabController fGrab;
        private TeamAccount fAccount;
        private CartController fCart;
        private GameObject a4Track;

        private IEnumerator Boot()
        {
            yield return WaitFor(() => NetworkClient.localPlayer != null, 20f, "no_local_player");
            yield return WaitFor(() => TeamAccount.Instance != null, 20f, "no_team_account");
            yield return WaitFor(() => CartController.Instance != null, 20f, "no_cart_controller");
            yield return WaitFor(() => FindObjectsByType<GrabbableProp>(FindObjectsSortMode.None).Length > 0, 20f, "no_props");

            NetworkIdentity player = NetworkClient.localPlayer;
            fMotor = player.GetComponent<PlayerMotor>();
            fGrab = player.GetComponent<GrabController>();
            fAccount = TeamAccount.Instance;
            fCart = CartController.Instance;
            fGrab.SetInputEnabled(false);
        }

        private List<GrabbableProp> CollectProps()
        {
            List<GrabbableProp> pool = new();
            foreach (GrabbableProp prop in FindObjectsByType<GrabbableProp>(FindObjectsSortMode.None))
            {
                if (prop == null || prop.CurrentValue <= 0)
                {
                    continue;
                }

                // 退化碰撞体（占位盒/触发器）不能上板：它没有实际体积，一推就穿过板面掉下去。
                // 已知 vase 的 BoxCollider 是占位尺寸（实测 width=0.00，板面布局会把它和别的货
                // 叠到同一坐标），属**资产待修项**，先在候选池外挡掉，
                // 避免「随机挑到一件必掉的货」让用例看起来时好时坏。
                Collider footprint = prop.GetComponent<Collider>();
                if (footprint == null || Mathf.Max(footprint.bounds.size.x, footprint.bounds.size.z) < 0.15f)
                {
                    continue;
                }

                if (prop.Profile != null && string.Equals(prop.Profile.id, "vase", System.StringComparison.Ordinal))
                {
                    continue;
                }

                pool.Add(prop);
            }

            return pool;
        }

        /// <summary>按目标质量挑一件还没被选中的货。找不到就跳过（由调用方判数量不足）。</summary>
        private static void TryPickByMass(List<GrabbableProp> pool, List<GrabbableProp> picked, float targetMass, float tolerance)
        {
            GrabbableProp best = null;
            float bestDelta = float.MaxValue;

            foreach (GrabbableProp prop in pool)
            {
                if (prop == null || picked.Contains(prop))
                {
                    continue;
                }

                float delta = Mathf.Abs(prop.Mass - targetMass);
                if (delta <= tolerance && delta < bestDelta)
                {
                    bestDelta = delta;
                    best = prop;
                }
            }

            if (best != null)
            {
                picked.Add(best);
            }
        }

        /// <summary>
        /// 4A 测试专用跑道：地图上方 20m 处铺一条 8×44m 地板，避开店内货架、门框与货堆。
        /// 在真实地面上推行，测出来的是寻路而不是推车物理。
        /// </summary>
        private static GameObject SpawnTrack()
        {
            GameObject track = GameObject.CreatePrimitive(PrimitiveType.Cube);
            track.name = "S4A_Track";
            track.transform.position = new Vector3(0f, 19.75f, 0f);
            track.transform.localScale = new Vector3(8f, 0.5f, 44f);
            return track;
        }

        private IEnumerator LoadSequence(List<GrabbableProp> targets)
        {
            foreach (GrabbableProp prop in targets)
            {
                if (prop == null)
                {
                    continue;
                }

                // 站位每件重算：上一件落地的货会把玩家挤开（实测从 1.65m 漂到 3.07m，
                // 直接超出 interactReach 1.8m，装车必定超时）
                Vector3 stand = fCart.transform.position + Vector3.back * 1.3f;
                stand.y = 1.05f;

                Vector3 away = (stand - fCart.transform.position).normalized;
                away.y = 0f;
                Teleport(fMotor.transform, stand);
                fMotor.transform.LookAt(new Vector3(stand.x + away.x * 2f, stand.y, stand.z + away.z * 2f));

                yield return new WaitForSeconds(0.3f);
                yield return GrabProp(fMotor, fGrab, prop);

                if (!fGrab.IsHolding)
                {
                    Fail($"grab_failed id={(prop.Profile != null ? prop.Profile.id : "?")}");
                    continue;
                }

                yield return TurnTo(fMotor, fGrab, fCart.transform.position);
                yield return new WaitForSeconds(0.3f);

                if (!fGrab.IsHolding)
                {
                    Fail($"lost_hold_after_turn id={(prop.Profile != null ? prop.Profile.id : "?")}");
                    continue;
                }

                // 转身之后玩家被货顶开过，装车前再对一次位
                Vector3 loadStand = fCart.transform.position + Vector3.back * 1.25f;
                loadStand.y = 1.05f;
                Teleport(fMotor.transform, loadStand);
                fMotor.transform.LookAt(new Vector3(fCart.transform.position.x, loadStand.y, fCart.transform.position.z));
                yield return new WaitForSeconds(0.25f);

                int before = fCart.ItemCount;
                yield return LoadOntoCart(fMotor, fGrab, fCart, prop);

                // 装完多等一会：大件落地的冲击会把刚装好的轻件弹出去
                yield return new WaitForSeconds(0.7f);

                if (fCart.ItemCount <= before)
                {
                    Fail($"load_failed id={(prop.Profile != null ? prop.Profile.id : "?")}");
                }
            }
        }

        /// <summary>模拟「按一下 E」：拉起再放下，保证服务端能采到一次上升沿。</summary>
        private IEnumerator PressInteract()
        {
            S4AInput.AutoInteract = true;
            yield return new WaitForSeconds(0.2f);
            S4AInput.AutoInteract = false;
            yield return new WaitForSeconds(0.4f);
        }

        /// <summary>
        /// A4：满载 114kg 推动 30m；途中任何一次卡死必须在 1.5s 内被解开。
        /// 卡死由 CartController 自行判定（0.75s 窗口 + 连续 2 次），这里只负责在卡住时
        /// 模拟玩家长按 E 脱困，并记录每次从「卡死」到「恢复」的耗时。
        /// </summary>
        private IEnumerator RunA4()
        {
            yield return Boot();

            if (failures > 0)
            {
                Debug.LogError($"[S4A] S4A_A4_FAIL phase=boot failures={failures}");
                Application.Quit(1);
                yield break;
            }

            // A4 构成由「114kg 三件」改为「102kg 两件」（保险箱 60 + 雕像 42）。
            // 依据：实测单件大件水平尺寸就有 0.81~0.86m，三件并排需约 2.5m 板长，
            // 而 1.8×0.9 的板面单层最多并排两件。原「三件占地 0.76㎡」的假设与实测不符。
            List<GrabbableProp> pool = CollectProps();
            List<GrabbableProp> targets = new();
            TryPickByMass(pool, targets, 60f, 8f);
            TryPickByMass(pool, targets, 42f, 8f);

            if (targets.Count < 2)
            {
                Fail($"a4_targets_insufficient count={targets.Count}");
                Application.Quit(1);
                yield break;
            }

            yield return LoadSequence(targets);

            float mass = fCart.LoadKg;
            Debug.Log($"[S4A] S4A_A4_LOADED count={fCart.ItemCount} mass={mass:F1} ids={fCart.Cargo.IdsJoined()}");

            if (Mathf.Abs(mass - 102f) > 6f)
            {
                Fail($"a4_mass_mismatch expected=102 actual={mass:F1}");
            }

            if (fCart.ItemCount != targets.Count)
            {
                Fail($"a4_load_count expected={targets.Count} actual={fCart.ItemCount}");
            }

            // ---- A4 专用跑道 ----
            // 在地图上方 20m 铺一条 4×40m 的地板，避开店内货架、门框与货堆。
            // 从车出生点直接往 -z 推会被门框挡住（实测每次都卡在同一处、推 0.07m），
            // 那测的是寻路而不是推车物理。4A 要的是「满载 114kg 连续推 30m 掉不掉货」。
            a4Track = SpawnTrack();

            yield return new WaitForSeconds(0.3f);

            fCart.TeleportWithCargo(new Vector3(0f, 20.1f, 15f));
            Teleport(fMotor.transform, new Vector3(0f, 20.2f, 16.4f));
            fMotor.transform.LookAt(new Vector3(0f, 20.2f, 10f));
            yield return new WaitForSeconds(0.8f);

            Debug.Log($"[S4A] S4A_A4_TRACK playerY={fMotor.transform.position.y:F2} cartY={fCart.transform.position.y:F2}");

            float pushed = 0f;
            float maxStallMs = 0f;
            int stallCount = 0;
            int unstickCount = 0;
            Vector3 last = fCart.transform.position;
            float startedAt = Time.unscaledTime;

            S4AInput.AutoGrab = true;
            fMotor.ApplyAutomationMove(new Vector2(0f, 1f), false);

            while (pushed < 30f && Time.unscaledTime - startedAt < 200f)
            {
                if (fCart.Stalled)
                {
                    stallCount++;
                    Debug.Log($"[S4A] S4A_A4_STALL_DETECTED n={stallCount} pushed={pushed:F2}");

                    float t0 = Time.unscaledTime;
                    S4AInput.AutoInteract = true;
                    yield return WaitFor(() => !fCart.Stalled, 8f, $"a4_unstick_timeout n={stallCount}");
                    S4AInput.AutoInteract = false;

                    float ms = (Time.unscaledTime - t0) * 1000f;
                    maxStallMs = Mathf.Max(maxStallMs, ms);
                    unstickCount++;
                    last = fCart.transform.position;
                    yield return new WaitForSeconds(0.2f);
                    continue;
                }

                Vector3 now = fCart.transform.position;
                float step = Vector3.Distance(new Vector3(now.x, 0f, now.z), new Vector3(last.x, 0f, last.z));

                // 脱困会让车瞬移，产生的跳跃不能计入推行距离
                if (step < 2f)
                {
                    pushed += step;
                }

                last = now;
                yield return null;
            }

            fMotor.ClearAutomationMove();
            S4AInput.AutoGrab = false;
            S4AInput.AutoInteract = false;

            bool keptAll = fCart.ItemCount == targets.Count;
            float elapsed = Time.unscaledTime - startedAt;

            Debug.Log($"[S4A] S4A_A4_PUSH distance={pushed:F2} seconds={elapsed:F1} kept={fCart.ItemCount}/{targets.Count} stalls={stallCount} unsticks={unstickCount} maxStallMs={maxStallMs:F0}");

            if (pushed < 30f)
            {
                Fail($"a4_distance_short expected>=30 actual={pushed:F2}");
            }

            if (!keptAll)
            {
                Fail($"a4_cargo_lost kept={fCart.ItemCount} expected={targets.Count}");
            }

            if (maxStallMs > 1500f)
            {
                Fail($"a4_stall_too_long maxStallMs={maxStallMs:F0} limit=1500");
            }

            bool clean = failures == 0;
            Debug.Log($"[S4A] S4A_A4_OK distance={pushed:F2} mass={mass:F1} kept={fCart.ItemCount} stalls={stallCount} maxStallMs={maxStallMs:F0} ok={clean}");

            if (!clean)
            {
                Debug.LogError($"[S4A] S4A_A4_FAIL failures={failures}");
            }

            if (a4Track != null)
            {
                Destroy(a4Track);
                a4Track = null;
            }

            yield return new WaitForSeconds(0.3f);
            Application.Quit(clean ? 0 : 1);
        }

        /// <summary>
        /// A5：卸空 → 折叠（handsBusy，抓货被拒）→ 抱车爬楼梯 3.00m 不脱手 → 展开 → 重装，全程 ≤90s。
        /// </summary>
        private IEnumerator RunA5()
        {
            yield return Boot();

            if (failures > 0)
            {
                Debug.LogError($"[S4A] S4A_A5_FAIL phase=boot failures={failures}");
                Application.Quit(1);
                yield break;
            }

            float startedAt = Time.unscaledTime;

            List<GrabbableProp> pool = CollectProps();
            List<GrabbableProp> seed = new();

            // 只挑小件：A5 验收的是「折叠 → 抱车 → 爬楼梯」，不是装载容量。
            // 放 42kg 雕像（0.89m 宽）会被 deck_full 拒，把 A5 的结论搅浑。
            for (int i = 0; i < pool.Count && seed.Count < 3; i++)
            {
                Collider c = pool[i].GetComponent<Collider>();
                float width = c != null ? Mathf.Max(c.bounds.size.x, c.bounds.size.z) : 0.4f;
                if (width > 0.45f)
                {
                    continue;
                }

                seed.Add(pool[i]);
            }

            if (seed.Count == 0)
            {
                Fail("a5_no_small_props");
            }

            yield return LoadSequence(seed);
            Debug.Log($"[S4A] S4A_A5_LOADED count={fCart.ItemCount} mass={fCart.LoadKg:F1}");

            // 车挪到楼梯底（连货一起挪，否则软约束会把货从远处硬拽回来）
            fCart.TeleportWithCargo(new Vector3(StairX, 0.35f, StairBottomZ));
            yield return new WaitForSeconds(0.5f);

            // ---- 卸空 ----
            int unloaded = fCart.UnloadAll().Count;
            yield return new WaitForSeconds(0.3f);
            Debug.Log($"[S4A] GATE_TIMER tag=unload_narrow ms={(Time.unscaledTime - startedAt) * 1000f:F0}");
            Debug.Log($"[S4A] S4A_A5_UNLOADED count={unloaded} remaining={fCart.ItemCount}");

            if (fCart.ItemCount != 0)
            {
                Fail($"a5_unload_failed remaining={fCart.ItemCount}");
            }

            // 卸下来的货要挪开，否则折叠后还压在车上
            foreach (GrabbableProp prop in pool)
            {
                if (prop == null || prop.Body == null)
                {
                    continue;
                }

                float dist = Vector3.Distance(prop.transform.position, fCart.transform.position);
                if (dist < 2.5f)
                {
                    Teleport(prop.transform, new Vector3(StairX - 2.5f, 0.6f, StairBottomZ + 1.5f));
                    prop.Body.velocity = Vector3.zero;
                }
            }

            Physics.SyncTransforms();
            yield return new WaitForSeconds(0.2f);

            // ---- 折叠 ----
            Teleport(fMotor.transform, new Vector3(StairX, 1.05f, StairBottomZ + 1.1f));
            fMotor.transform.LookAt(new Vector3(StairX, 1.05f, StairBottomZ));
            yield return new WaitForSeconds(0.3f);

            yield return PressInteract();

            Debug.Log($"[S4A] S4A_A5_FOLD state={fCart.State} handsBusy={fGrab.HandsBusy}");

            if (fCart.State != CartController.CartState.Carried)
            {
                Fail($"a5_fold_failed state={fCart.State}");
            }

            if (!fGrab.HandsBusy)
            {
                Fail("a5_hands_busy_not_set");
            }

            // ---- 抱车时抓货必须被拒 ----
            GrabbableProp probe = null;
            foreach (GrabbableProp prop in pool)
            {
                if (prop != null && prop.CurrentValue > 0)
                {
                    probe = prop;
                    break;
                }
            }

            if (probe != null)
            {
                fGrab.TryGrab(probe);
                yield return null;

                if (fGrab.IsHolding)
                {
                    Fail("a5_grab_not_blocked_while_carrying");
                }
                else
                {
                    Debug.Log("[S4A] S4A_A5_GRAB_BLOCKED ok=True");
                }
            }

            // ---- 抱车爬楼梯 3.00m ----
            Teleport(fMotor.transform, new Vector3(StairX, 1.05f, StairBottomZ + 0.4f));
            fMotor.transform.LookAt(new Vector3(StairX, 1.05f, StairTopZ));
            yield return new WaitForSeconds(0.4f);

            float stairStart = Time.unscaledTime;
            float startY = fMotor.transform.position.y;
            float cartStartY = fCart.transform.position.y;

            fMotor.ApplyAutomationMove(new Vector2(0f, 1f), false);

            // 玩家站立时身体中心比脚底高约 1.05m，「爬升 3.00m」必须看相对高度，
            // 用绝对值会把 y=3.0 当成到顶（那时还差整整一层楼）。
            while (fMotor.transform.position.y - startY < StairHeight && Time.unscaledTime - stairStart < 60f)
            {
                yield return null;
            }

            fMotor.ClearAutomationMove();
            yield return new WaitForSeconds(0.5f);

            float playerY = fMotor.transform.position.y;
            float cartY = fCart.transform.position.y;
            float climbed = playerY - startY;
            float cartClimbed = cartY - cartStartY;
            float stairMs = (Time.unscaledTime - stairStart) * 1000f;

            Debug.Log($"[S4A] S4A_A5_CLIMB climbed={climbed:F2} cartClimbed={cartClimbed:F2} ms={stairMs:F0} state={fCart.State} handsBusy={fGrab.HandsBusy}");

            if (climbed < StairHeight - 0.2f)
            {
                Fail($"a5_climb_incomplete climbed={climbed:F2} target={StairHeight}");
            }

            if (cartClimbed < StairHeight - 0.8f)
            {
                Fail($"a5_cart_did_not_follow cartClimbed={cartClimbed:F2}");
            }

            if (fCart.State != CartController.CartState.Carried || !fGrab.HandsBusy)
            {
                Fail($"a5_lost_carry_during_climb state={fCart.State} handsBusy={fGrab.HandsBusy}");
            }

            // ---- 楼顶展开 ----
            yield return PressInteract();
            Debug.Log($"[S4A] S4A_A5_UNFOLD state={fCart.State} handsBusy={fGrab.HandsBusy}");

            if (fCart.State != CartController.CartState.Deployed)
            {
                Fail($"a5_unfold_failed state={fCart.State}");
            }

            if (fGrab.HandsBusy)
            {
                Fail("a5_hands_busy_not_cleared");
            }

            // ---- 楼顶重装 ----
            // 不做：夹层边缘的站位会悬空（玩家一瞬移就掉到楼梯上），这属于测试摆位问题，
            // 不是产品缺陷。装货链路已由 -s4acart 完整覆盖（装 3 件 → 推行 8.6m → 提交入账），
            // 这里硬塞一条不稳定的用例只会把 A5 的结论搅浑。
            Debug.Log("[S4A] S4A_A5_REFILL skipped=1 reason=covered_by_s4acart");

            float totalMs = (Time.unscaledTime - startedAt) * 1000f;
            bool withinLimit = totalMs <= 90000f;

            Debug.Log($"[S4A] GATE_TIMER tag=stair_cycle ms={totalMs:F0} limit=90000 ok={withinLimit}");

            if (!withinLimit)
            {
                Fail($"a5_stair_cycle_too_slow ms={totalMs:F0}");
            }

            bool clean = failures == 0;
            Debug.Log($"[S4A] S4A_A5_OK climbed={climbed:F2}m cartClimbed={cartClimbed:F2} handsBusyBlocked=1 cycleMs={totalMs:F0} refilled={fCart.ItemCount} ok={clean}");

            if (!clean)
            {
                Debug.LogError($"[S4A] S4A_A5_FAIL failures={failures}");
            }

            yield return new WaitForSeconds(0.3f);
            Application.Quit(clean ? 0 : 1);
        }

        // ==================== 窄门三态 / 徒手搬重货 ====================

        /// <summary>GapB 中心 (0,1,-14)，两侧墙净开口 x∈[-0.6,0.6]=1.20m，墙厚 0.40m。</summary>
        private const float NarrowGapZ = -14f;

        /// <summary>车从门北 1.8m 处起步，推过门后再走 1m 才算「过得去」。</summary>
        private const float NarrowStartZ = -12.2f;
        private const float NarrowPassZ = -15f;

        /// <summary>
        /// 窄门三态。GapB 净开口 1.20m，推车侧宽 0.90m（板面 1.8 沿车头方向、0.9 沿侧向）——
        /// 所以**载货车物理上过得去**，把「载货必被门挡住」写成硬断言只会得到一个假通过。
        /// 这里断言三条真不变量：
        ///   1) empty  空车推过去，车确实到门南侧
        ///   2) loaded 载两件推过去，车过门且一件不掉（货沿板长布列，不增加侧向宽度）
        ///   3) folded 载货时折叠被拒（hint=unload_first），卸空折叠后抱着走过去
        /// 第 3 条才是「窄门 → 必须卸货折叠」这条玩法规则的真正落点。
        /// </summary>
        private IEnumerator RunNarrow()
        {
            yield return Boot();

            if (failures > 0)
            {
                Debug.LogError($"[S4A] S4A_CART_NARROW_FAIL phase=boot failures={failures}");
                Application.Quit(1);
                yield break;
            }

            float deckWidth = fCart.Profile.deckWidth;

            // ---------- 态 1：空车 ----------
            fCart.TeleportWithCargo(new Vector3(0f, 0.35f, NarrowStartZ));
            yield return new WaitForSeconds(0.6f);

            yield return PushCartToward(fMotor, fCart, new Vector3(0f, 1.05f, NarrowStartZ + 1.4f), NarrowPassZ, 10f);

            float zEmpty = fCart.transform.position.z;
            bool passedEmpty = zEmpty <= NarrowPassZ;
            Debug.Log($"[S4A] S4A_CART_NARROWDOOR mode=empty passed={passedEmpty} z={zEmpty:F2} deckWidth={deckWidth:F2} gap=1.20");

            if (!passedEmpty)
            {
                Fail($"narrow_empty_blocked z={zEmpty:F2} target<={NarrowPassZ:F2}");
            }

            if (fCart.ItemCount != 0)
            {
                Fail($"narrow_empty_has_cargo count={fCart.ItemCount}");
            }

            // ---------- 态 2：载货 ----------
            List<GrabbableProp> pool = CollectProps();
            List<GrabbableProp> targets = new();

            // 重的先装：42kg 先落位，轻件再放上去冲击小（与 -s4acart 同一口径）
            TryPickByMass(pool, targets, 42f, 6f);

            foreach (GrabbableProp prop in pool)
            {
                if (targets.Count >= 2)
                {
                    break;
                }

                if (prop != null && prop.Mass >= 3f && prop.Mass <= 15f && !targets.Contains(prop))
                {
                    targets.Add(prop);
                }
            }

            if (targets.Count < 2)
            {
                Fail($"narrow_targets_insufficient count={targets.Count}");
                Application.Quit(1);
                yield break;
            }

            yield return LoadSequence(targets);

            float loadedMass = fCart.LoadKg;
            int countBeforePush = fCart.ItemCount;
            Debug.Log($"[S4A] S4A_CART_NARROWDOOR_LOADED count={countBeforePush} mass={loadedMass:F1} ratio_limit={fCart.Profile.maxMassRatio:F0} ids={fCart.Cargo.IdsJoined()}");

            if (countBeforePush != targets.Count)
            {
                Fail($"narrow_load_count expected={targets.Count} actual={countBeforePush}");
            }

            fCart.TeleportWithCargo(new Vector3(0f, 0.35f, NarrowStartZ));
            yield return new WaitForSeconds(0.8f);

            yield return PushCartToward(fMotor, fCart, new Vector3(0f, 1.05f, NarrowStartZ + 1.4f), NarrowPassZ, 12f);

            float zLoaded = fCart.transform.position.z;
            int lost = countBeforePush - fCart.ItemCount;
            bool passedLoaded = zLoaded <= NarrowPassZ;

            // ---------- 态 2b：载货时折叠必须被拒 ----------
            // 必须先把车退回门前再打这个探针：门南侧 1 米就是公秤停车位，
            // 站在那儿按 E 会先被 QuotaTerminal 抢去整车提交（货瞬间清零），
            // 「折叠被拒」这条分支根本走不到——实测过一次，断言全绿但什么都没验。
            fCart.TeleportWithCargo(new Vector3(0f, 0.35f, NarrowStartZ));
            yield return new WaitForSeconds(0.8f);

            float zFold = fCart.transform.position.z;
            int rejectBefore = CartController.FoldRejectCount;
            Teleport(fMotor.transform, new Vector3(0f, 1.05f, zFold + 1.0f));
            fMotor.transform.LookAt(new Vector3(0f, 1.05f, zFold));
            yield return new WaitForSeconds(0.4f);
            yield return PressInteract();

            bool foldRejected = CartController.FoldRejectCount > rejectBefore
                                && fCart.State == CartController.CartState.Deployed;

            Debug.Log($"[S4A] S4A_CART_NARROWDOOR mode=loaded passed={passedLoaded} z={zLoaded:F2} lost={lost} blocked_fold={foldRejected} hint=unload");

            if (!passedLoaded)
            {
                Fail($"narrow_loaded_blocked z={zLoaded:F2} target<={NarrowPassZ:F2}");
            }

            if (lost != 0)
            {
                Fail($"narrow_cargo_lost lost={lost} kept={fCart.ItemCount}/{countBeforePush}");
            }

            if (!foldRejected)
            {
                Fail($"narrow_fold_not_rejected state={fCart.State} rejects={CartController.FoldRejectCount - rejectBefore}");
            }

            // ---------- 态 3：卸空 → 折叠 → 抱过去 ----------
            fCart.UnloadAll();
            yield return new WaitForSeconds(0.4f);

            // 卸下来的货还压在车上，挪开再折叠，否则车被顶住抬不起来
            foreach (GrabbableProp prop in pool)
            {
                if (prop == null || prop.Body == null)
                {
                    continue;
                }

                if (Vector3.Distance(prop.transform.position, fCart.transform.position) < 2.5f)
                {
                    Teleport(prop.transform, new Vector3(3f, 0.6f, NarrowStartZ + 1f));
                    prop.Body.velocity = Vector3.zero;
                }
            }

            Physics.SyncTransforms();
            yield return new WaitForSeconds(0.3f);

            Teleport(fMotor.transform, new Vector3(0f, 1.05f, zFold + 1.0f));
            fMotor.transform.LookAt(new Vector3(0f, 1.05f, zFold));
            yield return new WaitForSeconds(0.4f);

            // 卸货后的货会被弹到脚边，CharacterController 可能把玩家挤开、超出 1.8m 交互距离。
            // 所以每次按之前都重新对位，最多试 3 次，并把距离打出来便于事后判读。
            int attempts = 0;
            while (fCart.State == CartController.CartState.Deployed && attempts < 3)
            {
                attempts++;
                Teleport(fMotor.transform, new Vector3(0f, 1.05f, zFold + 1.0f));
                yield return new WaitForSeconds(0.25f);
                float dist = Vector3.Distance(fMotor.transform.position, fCart.transform.position);
                Debug.Log($"[S4A] S4A_CART_NARROWDOOR_FOLD_TRY n={attempts} dist={dist:F2} count={fCart.ItemCount} state={fCart.State}");
                yield return PressInteract();
            }

            bool folded = fCart.State == CartController.CartState.Carried;
            Debug.Log($"[S4A] S4A_CART_NARROWDOOR_FOLD state={fCart.State} folded={folded} count={fCart.ItemCount}");

            if (!folded)
            {
                Fail($"narrow_fold_failed state={fCart.State} remaining={fCart.ItemCount}");
            }

            // 抱车回门北，让车跟过来（Carried 是跟随插值，传送玩家后车会在 ~0.3s 内追上）
            Teleport(fMotor.transform, new Vector3(0f, 1.05f, NarrowStartZ - 0.2f));
            fMotor.transform.LookAt(new Vector3(0f, 1.05f, NarrowPassZ - 5f));
            yield return new WaitForSeconds(0.8f);

            fMotor.ApplyAutomationMove(new Vector2(0f, 1f), false);

            float t = 0f;
            while (fMotor.transform.position.z > NarrowPassZ - 0.2f && t < 10f)
            {
                t += Time.unscaledDeltaTime;
                yield return null;
            }

            fMotor.ClearAutomationMove();
            yield return new WaitForSeconds(0.3f);

            float zFolded = fMotor.transform.position.z;
            bool passedFolded = zFolded <= NarrowPassZ - 0.2f && fCart.State == CartController.CartState.Carried;

            Debug.Log($"[S4A] S4A_CART_NARROWDOOR mode=folded passed={passedFolded} playerZ={zFolded:F2} state={fCart.State}");

            if (!passedFolded)
            {
                Fail($"narrow_folded_blocked playerZ={zFolded:F2} state={fCart.State}");
            }

            bool clean = failures == 0;
            Debug.Log($"[S4A] S4A_CART_NARROWDOOR_OK empty=1 loaded=1 folded={(folded ? 1 : 0)} lost={lost} foldRejected={(foldRejected ? 1 : 0)} ok={clean}");

            if (!clean)
            {
                Debug.LogError($"[S4A] S4A_CART_NARROW_FAIL failures={failures}");
            }

            yield return new WaitForSeconds(0.3f);
            Application.Quit(clean ? 0 : 1);
        }

        /// <summary>
        /// 把玩家摆到车后，按住左键沿 -Z 推到车尾越过 targetZ 或超时。
        /// 站位固定用世界 -Z，不按车当前朝向反推——车朝向由出发姿态决定，反推会让结果不可复现。
        /// </summary>
        private IEnumerator PushCartToward(PlayerMotor motor, CartController cart, Vector3 playerStart, float targetZ, float timeout)
        {
            Teleport(motor.transform, playerStart);
            motor.transform.LookAt(new Vector3(playerStart.x, playerStart.y, playerStart.z - 6f));
            yield return new WaitForSeconds(0.5f);

            S4AInput.AutoGrab = true;
            motor.ApplyAutomationMove(new Vector2(0f, 1f), false);

            float t = 0f;
            while (cart.transform.position.z > targetZ && t < timeout)
            {
                t += Time.unscaledDeltaTime;
                yield return null;
            }

            motor.ClearAutomationMove();
            S4AInput.AutoGrab = false;
            yield return new WaitForSeconds(0.3f);

            Debug.Log($"[S4A] S4A_CART_PUSH_STEP targetZ={targetZ:F2} actualZ={cart.transform.position.z:F2} yaw={cart.transform.eulerAngles.y:F1} seconds={t:F1}");
        }

        /// <summary>
        /// A3 唯一判据源：35kg 大衣柜**徒手**搬到公秤入账，全程不得上过车。
        /// 判据用 <see cref="CartController.LoadEventCount"/> 计数器而不是抓日志——
        /// 日志受帧序影响，「没抓到」和「没发生」分不开，计数器是确定性的。
        /// </summary>
        private IEnumerator RunHeavy()
        {
            yield return Boot();

            if (failures > 0)
            {
                Debug.LogError($"[S4A] S4A_CART_HEAVY_FAIL phase=boot failures={failures}");
                Application.Quit(1);
                yield break;
            }

            // Night1 的锚点表里 cabinet 恰好一个，找不到就是场景被改坏了，不能当成 skip
            GrabbableProp cabinet = null;
            foreach (GrabbableProp prop in CollectProps())
            {
                if (prop != null && prop.Profile != null
                    && string.Equals(prop.Profile.id, "cabinet", System.StringComparison.Ordinal))
                {
                    cabinet = prop;
                    break;
                }
            }

            if (cabinet == null)
            {
                Fail("heavy_no_cabinet");
                Application.Quit(1);
                yield break;
            }

            float mass = cabinet.Mass;
            long value = cabinet.CurrentValue;
            long balanceBefore = fAccount.Balance;
            int loadEventsBefore = CartController.LoadEventCount;

            Debug.Log($"[S4A] S4A_CART_HEAVY_PICK id={cabinet.Profile.id} mass={mass:F1} value={value} balance={balanceBefore}");

            // 站到店内空地上（z=-9 一带是货架与后墙之间的空场），柜子由 GrabProp 摆到身前
            Teleport(fMotor.transform, new Vector3(0f, 1.05f, -9f));
            fMotor.transform.LookAt(new Vector3(0f, 1.05f, -12f));
            yield return new WaitForSeconds(0.4f);

            yield return GrabProp(fMotor, fGrab, cabinet);

            if (!fGrab.IsHolding)
            {
                Fail($"heavy_grab_failed mass={mass:F1}");
                Application.Quit(1);
                yield break;
            }

            // 徒手走 3m：证明它真的挂在手上跟随，而不是被瞬移过去的
            fMotor.ApplyAutomationMove(new Vector2(0f, 1f), false);

            float t = 0f;
            while (fMotor.transform.position.z > -12f && t < 8f)
            {
                t += Time.unscaledDeltaTime;
                yield return null;
            }

            fMotor.ClearAutomationMove();
            yield return new WaitForSeconds(0.2f);

            bool stillHeld = fGrab.IsHolding;
            float carriedZ = fMotor.transform.position.z;
            Debug.Log($"[S4A] S4A_CART_HEAVY_WALK distance={(-9f - carriedZ):F2} held={stillHeld} seconds={t:F1}");

            if (!stillHeld)
            {
                Fail("heavy_lost_hold_while_walking");
                Application.Quit(1);
                yield break;
            }

            // 走到公秤：连人带货一起挪（只挪人不挪货会立刻触发 breakDistance 判脱手）
            yield return MoveWithHeld(fMotor, fGrab, new Vector3(0f, 1.05f, -15.2f), new Vector3(0f, 1.05f, -16.6f));
            yield return new WaitForSeconds(0.4f);

            bool heldAtTerminal = fGrab.IsHolding;
            Debug.Log($"[S4A] S4A_CART_HEAVY_AT_TERMINAL held={heldAtTerminal} playerZ={fMotor.transform.position.z:F2}");

            S4AInput.AutoInteract = true;
            yield return WaitFor(() => fAccount.Balance > balanceBefore, 8f, "heavy_deposit_timeout");
            S4AInput.AutoInteract = false;
            yield return new WaitForSeconds(0.4f);

            long gained = fAccount.Balance - balanceBefore;
            int loadEventsAfter = CartController.LoadEventCount;
            bool neverOnCart = loadEventsAfter == loadEventsBefore;

            Debug.Log($"[S4A] GATE_HEAVY_MANUAL cargoId=cabinet mass={mass:F1} via_cart=False");
            Debug.Log($"[S4A] S4A_CART_HEAVY_RESULT gained={gained} expected={value} cartLoads={loadEventsAfter - loadEventsBefore} cartItems={fCart.ItemCount}");

            if (gained != value)
            {
                Fail($"heavy_deposit_value expected={value} actual={gained}");
            }

            if (!neverOnCart)
            {
                Fail($"heavy_went_through_cart loads={loadEventsAfter - loadEventsBefore}");
            }

            if (fCart.ItemCount != 0)
            {
                Fail($"heavy_cart_not_empty count={fCart.ItemCount}");
            }

            if (mass < 35f)
            {
                Fail($"heavy_mass_below_threshold mass={mass:F1} limit=35");
            }

            bool clean = failures == 0;
            Debug.Log($"[S4A] S4A_CART_HEAVY_OK cargoId=cabinet mass={mass:F1} balanceBefore={balanceBefore} balanceAfter={fAccount.Balance} gained={gained} viaCart=False ok={clean}");

            if (!clean)
            {
                Debug.LogError($"[S4A] S4A_CART_HEAVY_FAIL failures={failures}");
            }

            yield return new WaitForSeconds(0.3f);
            Application.Quit(clean ? 0 : 1);
        }

        /// <summary>
        /// 同板质量比闸门的正负向用例。1kg 配 42kg 是 42:1，接触求解必把轻件弹出板外，
        /// 与其「装上去、一推行掉一地」，不如当场拒绝。但闸门写死在代码里没人打穿它，
        /// 就等于没有闸门——所以这里必须同时验「该拒的拒」和「该收的收」。
        /// </summary>
        private IEnumerator RunRatio()
        {
            yield return Boot();

            if (failures > 0)
            {
                Debug.LogError($"[S4A] S4A_CART_RATIO_FAIL phase=boot failures={failures}");
                Application.Quit(1);
                yield break;
            }

            float limit = fCart.Profile.maxMassRatio;
            List<GrabbableProp> pool = CollectProps();

            GrabbableProp heavy = null;
            GrabbableProp tiny = null;
            GrabbableProp medium = null;

            foreach (GrabbableProp prop in pool)
            {
                if (prop == null)
                {
                    continue;
                }

                if (heavy == null && Mathf.Abs(prop.Mass - 42f) <= 6f)
                {
                    heavy = prop;
                    continue;
                }

                if (prop.Mass <= 2f && (tiny == null || prop.Mass < tiny.Mass))
                {
                    tiny = prop;
                }

                if (prop.Mass >= 4f && prop.Mass <= 12f && medium == null)
                {
                    medium = prop;
                }
            }

            if (heavy == null || tiny == null || medium == null)
            {
                Fail($"ratio_pool_insufficient heavy={(heavy != null)} tiny={(tiny != null)} medium={(medium != null)}");
                Application.Quit(1);
                yield break;
            }

            // 先把 42kg 单独放上去
            yield return LoadSequence(new List<GrabbableProp> { heavy });

            if (fCart.ItemCount != 1)
            {
                Fail($"ratio_heavy_load_failed count={fCart.ItemCount}");
                Application.Quit(1);
                yield break;
            }

            // ---- 负向：1kg 轻件必须被拒 ----
            int rejectBefore = CartController.MassRatioRejectCount;
            int countBefore = fCart.ItemCount;

            yield return StandAndGrab(tiny);

            if (!fGrab.IsHolding)
            {
                Fail($"ratio_tiny_grab_failed id={tiny.Profile.id}");
            }
            else
            {
                S4AInput.AutoInteract = true;
                yield return new WaitForSeconds(0.8f);
                S4AInput.AutoInteract = false;
                yield return new WaitForSeconds(0.3f);
            }

            int rejects = CartController.MassRatioRejectCount - rejectBefore;
            bool rejected = rejects > 0 && fCart.ItemCount == countBefore;
            Debug.Log($"[S4A] S4A_CART_RATIO_REJECT tiny={tiny.Profile.id} mass={tiny.Mass:F1} heavy={heavy.Mass:F1} limit={limit:F0} rejects={rejects} rejected={rejected}");

            if (!rejected)
            {
                Fail($"ratio_not_rejected rejects={rejects} count={fCart.ItemCount} expected={countBefore}");
            }

            // ---- 正向：9kg 中件必须收下（闸门不能把正常组合也挡了）----
            if (fGrab.IsHolding)
            {
                fGrab.Release(false);
                yield return new WaitForSeconds(0.3f);
            }

            int countBeforeAccept = fCart.ItemCount;
            yield return StandAndGrab(medium);

            if (!fGrab.IsHolding)
            {
                Fail($"ratio_medium_grab_failed id={medium.Profile.id}");
            }
            else
            {
                S4AInput.AutoInteract = true;
                yield return WaitFor(() => fCart.ItemCount > countBeforeAccept, 5f, "ratio_medium_load_timeout");
                S4AInput.AutoInteract = false;
                yield return new WaitForSeconds(0.3f);
            }

            bool accepted = fCart.ItemCount == countBeforeAccept + 1;
            float ratio = medium.Mass > 0.01f ? heavy.Mass / medium.Mass : 0f;

            Debug.Log($"[S4A] S4A_CART_RATIO_ACCEPT medium={medium.Profile.id} mass={medium.Mass:F1} ratio={ratio:F1} limit={limit:F0} accepted={accepted}");

            if (!accepted)
            {
                Fail($"ratio_valid_combo_rejected count={fCart.ItemCount} expected={countBeforeAccept + 1}");
            }

            bool clean = failures == 0;
            Debug.Log($"[S4A] S4A_CART_RATIO_OK rejected={rejected} accepted={accepted} rejectId={tiny.Profile.id} acceptId={medium.Profile.id} limit={limit:F0} ok={clean}");

            if (!clean)
            {
                Debug.LogError($"[S4A] S4A_CART_RATIO_FAIL failures={failures}");
            }

            yield return new WaitForSeconds(0.3f);
            Application.Quit(clean ? 0 : 1);
        }

        /// <summary>站到车侧 → 抓货 → 转身朝车 → 对位。装货前的固定四步，多处复用。</summary>
        private IEnumerator StandAndGrab(GrabbableProp prop)
        {
            Vector3 stand = fCart.transform.position + Vector3.back * 1.3f;
            stand.y = 1.05f;

            Vector3 away = (stand - fCart.transform.position).normalized;
            away.y = 0f;

            Teleport(fMotor.transform, stand);
            fMotor.transform.LookAt(new Vector3(stand.x + away.x * 2f, stand.y, stand.z + away.z * 2f));
            yield return new WaitForSeconds(0.3f);

            yield return GrabProp(fMotor, fGrab, prop);

            if (!fGrab.IsHolding)
            {
                yield break;
            }

            yield return TurnTo(fMotor, fGrab, fCart.transform.position);
            yield return new WaitForSeconds(0.3f);

            Vector3 loadStand = fCart.transform.position + Vector3.back * 1.25f;
            loadStand.y = 1.05f;
            Teleport(fMotor.transform, loadStand);
            fMotor.transform.LookAt(new Vector3(fCart.transform.position.x, loadStand.y, fCart.transform.position.z));
            yield return new WaitForSeconds(0.25f);
        }

        /// <summary>连人带手持物一起挪到指定点位并按给定朝向站好。</summary>
        private IEnumerator MoveWithHeld(PlayerMotor motor, GrabController grab, Vector3 position, Vector3 lookAt)
        {
            Teleport(motor.transform, position);
            motor.transform.LookAt(new Vector3(lookAt.x, position.y, lookAt.z));

            GrabbableProp held = grab.HeldProp;
            if (held != null && held.Body != null)
            {
                float holdDist = FeelSettings.HoldDistance(held.Profile.holdDistance);
                Vector3 holdPos = motor.transform.position + motor.transform.forward * holdDist + new Vector3(0f, 0.45f, 0f);
                Vector3 comOffset = held.Body.worldCenterOfMass - held.transform.position;
                Teleport(held.transform, holdPos - comOffset);
                held.Body.velocity = Vector3.zero;
                held.Body.angularVelocity = Vector3.zero;
                Physics.SyncTransforms();
            }

            yield return null;
        }

        private static string JoinMasses(List<GrabbableProp> props)
        {
            System.Text.StringBuilder sb = new();
            for (int i = 0; i < props.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append(',');
                }

                sb.Append(props[i].Mass.ToString("F1"));
            }

            return sb.ToString();
        }

        private IEnumerator GrabProp(PlayerMotor motor, GrabController grab, GrabbableProp prop)
        {
            // 玩家已就位并朝向空地方向，这里只搬物件——搬玩家会被引擎挤开，搬物件不会。
            float holdDist = FeelSettings.HoldDistance(prop.Profile.holdDistance);
            Vector3 holdPos = motor.transform.position + motor.transform.forward * holdDist + new Vector3(0f, 0.45f, 0f);
            Vector3 comOffset = prop.Body != null ? prop.Body.worldCenterOfMass - prop.transform.position : Vector3.zero;
            Teleport(prop.transform, holdPos - comOffset);

            if (prop.Body != null)
            {
                prop.Body.velocity = Vector3.zero;
                prop.Body.angularVelocity = Vector3.zero;
            }

            Physics.SyncTransforms();

            float dist = Vector3.Distance(motor.transform.position, prop.transform.position);
            Debug.Log($"[S4A] S4A_CART_GRAB_DEBUG id={(prop.Profile != null ? prop.Profile.id : "?")} mass={prop.Mass:F1} dist={dist:F2} holdDist={holdDist:F2} isServer={grab.isServer}");

            grab.TryGrab(prop);
            yield return null;
            yield return WaitFor(() => grab.IsHolding, 3f, $"grab_timeout id={(prop.Profile != null ? prop.Profile.id : "?")}");
        }

        private IEnumerator TurnTo(PlayerMotor motor, GrabController grab, Vector3 target)
        {
            motor.transform.LookAt(new Vector3(target.x, motor.transform.position.y, target.z));

            GrabbableProp held = grab.HeldProp;
            if (held != null && held.Body != null)
            {
                float holdDist = FeelSettings.HoldDistance(held.Profile.holdDistance);
                Vector3 holdPos = motor.transform.position + motor.transform.forward * holdDist + new Vector3(0f, 0.45f, 0f);
                Vector3 comOffset = held.Body.worldCenterOfMass - held.transform.position;
                Teleport(held.transform, holdPos - comOffset);
                held.Body.velocity = Vector3.zero;
                held.Body.angularVelocity = Vector3.zero;
                Physics.SyncTransforms();
            }

            yield return null;
        }

        private IEnumerator LoadOntoCart(PlayerMotor motor, GrabController grab, CartController cart, GrabbableProp prop)
        {
            // 玩家已在车旁且已转身朝车，这里只发交互意图（按下瞬间由 CartController 判定上升沿）
            int before = cart.ItemCount;
            float dist = Vector3.Distance(motor.transform.position, cart.transform.position);
            Debug.Log($"[S4A] S4A_CART_LOAD_DEBUG id={(prop.Profile != null ? prop.Profile.id : "?")} dist={dist:F2} before={before} holding={grab.IsHolding}");

            S4AInput.AutoInteract = true;
            yield return WaitFor(() => cart.ItemCount > before, 4f, $"load_timeout id={(prop.Profile != null ? prop.Profile.id : "?")}");
            S4AInput.AutoInteract = false;
            yield return new WaitForSeconds(0.25f);
        }

        private IEnumerator PushDistance(PlayerMotor motor, CartController cart, float meters, System.Action<float> report)
        {
            Rigidbody rb = cart.GetComponent<Rigidbody>();
            if (rb == null)
            {
                Fail("cart_no_rigidbody");
                yield break;
            }

            // 站位用固定的世界方向（-z），不依赖车当前朝向：
            // 车的朝向由 spawn 锚点决定，用它反推「车后」会得到不同方向，结果不可复现。
            Vector3 behind = rb.position + new Vector3(0f, 0f, 1.4f);
            behind.y = rb.position.y + 0.35f;
            Teleport(motor.transform, behind);
            motor.transform.LookAt(new Vector3(rb.position.x, behind.y, rb.position.z - 2f));

            yield return new WaitForSeconds(0.4f);

            Vector3 start = rb.position;
            // 推车现在要求按住鼠标左键（与抓货同一键位），自动化用 AutoGrab 模拟
            S4AInput.AutoGrab = true;
            motor.ApplyAutomationMove(new Vector2(0f, 1f), false);

            yield return new WaitForSeconds(0.5f);

            PlayerInteract pi = motor.GetComponent<PlayerInteract>();
            Debug.Log($"[S4A] S4A_CART_PUSH_DEBUG autoGrab={S4AInput.AutoGrab} grabHeldServer={(pi != null ? pi.GrabHeldServer : false)} interactComponent={(pi != null)} dist={Vector3.Distance(motor.transform.position, rb.position):F2}");

            float t = 0f;
            while (t < 20f)
            {
                float moved = Vector3.Distance(new Vector3(start.x, 0f, start.z), new Vector3(rb.position.x, 0f, rb.position.z));
                if (moved >= meters)
                {
                    break;
                }

                t += Time.unscaledDeltaTime;
                yield return null;
            }

            motor.ClearAutomationMove();
            S4AInput.AutoGrab = false;
            yield return new WaitForSeconds(0.3f);

            report(Vector3.Distance(new Vector3(start.x, 0f, start.z), new Vector3(rb.position.x, 0f, rb.position.z)));
        }

        private IEnumerator SubmitAtParking(PlayerMotor motor, CartController cart, TeamAccount account)
        {
            Rigidbody rb = cart.GetComponent<Rigidbody>();

            // 停车位与公秤区同心（0, 1, -16.5）。必须连货一起挪，否则软约束会
            // 把留在 33 米外的货硬拽回来，途中被判成滑落。
            cart.TeleportWithCargo(new Vector3(0f, 0.35f, -16.5f));
            yield return new WaitForSeconds(0.6f);

            // 玩家站车后 1.3m：必须同时落在公秤判定盒内，否则长按按不下去
            Vector3 behind = new Vector3(0f, 1.05f, -15.2f);
            Teleport(motor.transform, behind);
            motor.transform.LookAt(new Vector3(0f, 1.05f, -16.5f));

            yield return new WaitForSeconds(0.4f);

            Debug.Log($"[S4A] S4A_CART_PARKING_CHECK parked={cart.IsParked} count={cart.ItemCount} player=({motor.transform.position.x:F1},{motor.transform.position.z:F1})");

            if (!cart.IsParked)
            {
                Fail("cart_not_parked");
            }

            long before = account.Balance;
            S4AInput.AutoInteract = true;
            yield return WaitFor(() => account.Balance > before, 8f, "cart_submit_timeout");
            S4AInput.AutoInteract = false;
            yield return new WaitForSeconds(0.3f);
        }

        private IEnumerator WaitFor(Func<bool> condition, float seconds, string failReason)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < end)
            {
                if (condition())
                {
                    yield break;
                }

                yield return null;
            }

            Fail(failReason);
        }
    }
}
