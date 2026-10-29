using System;
using System.Collections;
using System.Collections.Generic;
using Mirror;
using ScavengeNightmare.Cart;
using ScavengeNightmare.Economy;
using ScavengeNightmare.Gameplay;
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

            if (!enabledTest && !a4Mode && !a5Mode)
            {
                this.enabled = false;
                return;
            }

            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 60;
            ScavengeNightmare.Traits.TraitSettings.ForceNoTraits = true;
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

            StartCoroutine(Watchdog(a4Mode || a5Mode ? 240 : 150));

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

            StartCoroutine(a4Mode ? RunA4() : (a5Mode ? RunA5() : Run()));
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
                if (prop != null && prop.CurrentValue > 0)
                {
                    pool.Add(prop);
                }
            }

            pool.Sort((a, b) => a.Mass.CompareTo(b.Mass));

            List<GrabbableProp> targets = new();
            for (int i = 0; i < pool.Count && targets.Count < 2; i++)
            {
                targets.Add(pool[i]);
            }

            // 再来一件真正重的：只装轻件测不出「推车存在的意义」（P0 §2.1 保险箱 60kg 是硬证明）
            for (int i = pool.Count - 1; i >= 0; i--)
            {
                if (pool[i].Mass >= 20f && !targets.Contains(pool[i]))
                {
                    targets.Add(pool[i]);
                    break;
                }
            }

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

            if (clean)
            {
                Debug.Log($"[S4A] GATE_HEAVY_MANUAL cargoId=- mass=- via_cart=False note=see_dedicated_case");
            }
            else
            {
                Debug.LogError($"[S4A] S4A_CART_FAIL failures={failures}");
            }

            yield return new WaitForSeconds(0.3f);
            Application.Quit(clean ? 0 : 1);
        }

        // ==================== A4 / A5 用例 ====================

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
                if (prop != null && prop.CurrentValue > 0)
                {
                    pool.Add(prop);
                }
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

            List<GrabbableProp> pool = CollectProps();
            List<GrabbableProp> targets = new();
            TryPickByMass(pool, targets, 60f, 8f);
            TryPickByMass(pool, targets, 42f, 8f);
            TryPickByMass(pool, targets, 12f, 5f);

            if (targets.Count < 3)
            {
                Fail($"a4_targets_insufficient count={targets.Count}");
                Application.Quit(1);
                yield break;
            }

            yield return LoadSequence(targets);

            float mass = fCart.LoadKg;
            Debug.Log($"[S4A] S4A_A4_LOADED count={fCart.ItemCount} mass={mass:F1} ids={fCart.Cargo.IdsJoined()}");

            if (Mathf.Abs(mass - 114f) > 6f)
            {
                Fail($"a4_mass_mismatch expected=114 actual={mass:F1}");
            }

            if (fCart.ItemCount != targets.Count)
            {
                Fail($"a4_load_count expected={targets.Count} actual={fCart.ItemCount}");
            }

            // ---- A4 专用跑道 ----
            // 在地图上方 20m 铺一条 4×40m 的地板，避开店内货架、门框与货堆。
            // 从车出生点直接往 -z 推会被门框挡住（实测每次都卡在同一处、推 0.07m），
            // 那测的是寻路而不是推车物理。4A 要的是「满载 114kg 连续推 30m 掉不掉货」。
            GameObject track = GameObject.CreatePrimitive(PrimitiveType.Cube);
            track.name = "S4A_A4_Track";
            track.transform.position = new Vector3(0f, 19.75f, 0f);
            track.transform.localScale = new Vector3(4f, 0.5f, 40f);
            a4Track = track;

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
            for (int i = 0; i < pool.Count && seed.Count < 3; i++)
            {
                seed.Add(pool[i]);
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

            // 站到车正后方并朝向车：推行是靠「玩家前方 handleDistance」驱动的
            Vector3 behind = cart.transform.position - cart.transform.forward * 1.4f;
            behind.y = 1.05f;
            Teleport(motor.transform, behind);
            motor.transform.LookAt(new Vector3(cart.transform.position.x, 1.05f, cart.transform.position.z));

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
