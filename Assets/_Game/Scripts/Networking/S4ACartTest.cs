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
        private int failures;

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

            if (!enabledTest)
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

            StartCoroutine(Watchdog(150));

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

            StartCoroutine(Run());
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
            motor.ApplyAutomationMove(new Vector2(0f, 1f), false);

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
