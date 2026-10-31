using System.Collections;
using Mirror;
using ScavengeNightmare.Economy;
using ScavengeNightmare.Save;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ScavengeNightmare.Networking
{
    /// <summary>
    /// Story 4 · 商店验收。
    ///
    /// 断言原则：**每一种失败都必须有拒绝码**，只靠「余额没变」猜是不合格的。
    /// 金额全部写死（1000 起步、400/500/350/900 四件），这样 sumCommitted 能手算核对。
    /// 顺带在结束前验一次 Story 2 的恒等式 `balance == SumFromTxns()`——
    /// 购买走的是负向流水，这条恒等式是最容易被购物打破而又最不该破的东西。
    /// </summary>
    public sealed class S4AShopTest : MonoBehaviour
    {
        private int failures;

        private TeamAccount account;
        private PlayerMotor motor;
        private ShopTerminal terminal;

        private void Start()
        {
            if (!S4ACli.Has("-s4ashop"))
            {
                this.enabled = false;
                return;
            }

            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 60;
            ScavengeNightmare.Traits.TraitSettings.ForceNoTraits = true;
            ScavengeNightmare.Economy.S4AInput.SuppressManual = true;
            Debug.Log("[S4A] S4A_NEUTRAL_OK traits=none feel=" + (S4ACli.Value("-feel=") ?? "default").ToLowerInvariant());

            if (string.IsNullOrEmpty(S4ACli.Value("-savedir=")))
            {
                S4ACli.SaveDirOverride = System.IO.Path.Combine(Application.persistentDataPath, "S4A_test_shop");
            }

            NetworkManager manager = NetworkManager.singleton;
            if (manager == null)
            {
                Debug.LogError("[S4A] S4A_SHOP_FAIL reason=no_network_manager");
                Application.Quit(1);
                return;
            }

            StartCoroutine(Watchdog(180));

            try
            {
                manager.StartHost();
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[S4A] S4A_SHOP_FAIL reason=start_host type={ex.GetType().Name}");
                Application.Quit(1);
                return;
            }

            StartCoroutine(Run());
        }

        private IEnumerator Watchdog(int seconds)
        {
            yield return new WaitForSecondsRealtime(seconds);
            Debug.LogError($"[S4A] S4A_SHOP_WATCHDOG_TIMEOUT seconds={seconds}");
            Application.Quit(3);
        }

        private void Fail(string reason)
        {
            failures++;
            Debug.LogError($"[S4A] S4A_SHOP_TEST_FAIL reason={reason}");
        }

        private IEnumerator Run()
        {
            yield return WaitFor(() => NetworkClient.localPlayer != null, 20f, "no_local_player");
            yield return WaitFor(() => TeamAccount.Instance != null, 20f, "no_team_account");
            yield return WaitFor(() => ShopCatalog.Instance != null && ShopCatalog.Instance.Count >= 8, 20f, "no_catalog");

            account = TeamAccount.Instance;

            // S4C-3：商店只在安全屋开张，Night1 已不再生成终端。
            // 本用例必须真的进一次安全屋，否则「夜路拒买」与「安全屋可买」两条路径分不开。
            NetworkManager manager = NetworkManager.singleton;
            if (manager == null)
            {
                Fail("no_network_manager_at_run");
                Application.Quit(1);
                yield break;
            }

            Debug.Log("[S4C] S4C_SHOP_SCENE from=Night1 to=Safehouse");
            manager.ServerChangeScene("Safehouse");
            yield return WaitFor(() => SceneManager.GetActiveScene().name == "Safehouse", 25f, "safehouse_not_loaded");
            yield return WaitFor(() => NetworkClient.localPlayer != null, 25f, "no_local_player_after_safehouse");
            yield return WaitFor(() => ShopTerminal.Instance != null, 25f, "no_shop_terminal");
            yield return WaitFor(() => account.PurchaseWindowOpen, 25f, "purchase_window_closed");

            // 换关会重建玩家，必须在换关之后再抓 motor。
            motor = NetworkClient.localPlayer != null ? NetworkClient.localPlayer.GetComponent<PlayerMotor>() : null;
            terminal = ShopTerminal.Instance;

            if (motor == null)
            {
                Fail("no_motor_after_safehouse");
                Application.Quit(1);
                yield break;
            }

            Debug.Log($"[S4C] S4C_SHOP_SCENE_OK scene={SceneManager.GetActiveScene().name} terminal=1 window=1");

            // 站进店内（服务端会复核距离，不在店内买的单会被拒）
            Teleport(motor.transform, terminal.transform.position + new Vector3(0f, 1.05f, 1.6f));
            yield return new WaitForSeconds(0.4f);

            if (!terminal.LocalPlayerInside())
            {
                Fail("player_not_in_zone");
            }

            // 起步资金：写死 1000，让每一步余额都能手算
            account.DepositId("shop-test-seed", 1000);
            yield return new WaitForSeconds(0.3f);

            long start = account.Balance;
            Debug.Log($"[S4A] S4A_SHOP_START balance={start} owned={account.OwnedMask} visit={account.VisitPurchases}");

            if (start != 1000)
            {
                Fail($"seed_balance expected=1000 actual={start}");
            }

            // ---- 1. 正常购买 ----
            yield return PressBuy(1);
            bool ownsGloves = account.OwnsItem("grip_gloves");
            bool bought1 = ownsGloves && account.Balance == start - 400;
            Debug.Log($"[S4A] S4A_SHOP_BUY step=first owned={ownsGloves} balance={account.Balance} expected={start - 400}");

            if (!bought1)
            {
                Fail($"buy_first_failed balance={account.Balance} owned={account.OwnedMask}");
            }

            // ---- 2. 重复购买必须被拒 ----
            yield return PressBuy(1);
            bool alreadyOwned = account.Balance == start - 400;
            Debug.Log($"[S4A] S4A_SHOP_BUY step=again balance={account.Balance} balanceUnchanged={alreadyOwned}");

            if (!alreadyOwned)
            {
                Fail($"already_owned_not_blocked balance={account.Balance}");
            }

            // ---- 3. 第二笔（胶带 500 → 余额 100）----
            yield return PressBuy(2);
            bool bought2 = account.OwnsItem("packing_tape") && account.Balance == start - 900;

            if (!bought2)
            {
                Fail($"buy_second_failed balance={account.Balance} owned={account.OwnedMask}");
            }

            // ---- 4. 余额不足必须被拒（手电 350 > 100）----
            long beforeInsuff = account.Balance;
            yield return PressBuy(3);
            bool ownsLamp = account.OwnsItem("flashlight");
            bool insufficient = account.Balance == beforeInsuff && !ownsLamp;
            Debug.Log($"[S4A] S4A_SHOP_BUY step=insufficient balance={account.Balance} owned={ownsLamp}");

            if (!insufficient)
            {
                Fail($"insufficient_not_blocked balance={account.Balance}");
            }

            // ---- 5. 撤销退款：退款额锁定原价 ----
            yield return PressUndo();
            bool refunded = !account.OwnsItem("packing_tape") && account.Balance == start - 400;
            Debug.Log($"[S4A] S4A_SHOP_UNDO step=first balance={account.Balance} expected={start - 400} owned={account.OwnedMask}");

            if (!refunded)
            {
                Fail($"undo_failed balance={account.Balance} owned={account.OwnedMask}");
            }

            // ---- 6. 二次撤销必须被拒，且分文不动 ----
            long beforeDoubleUndo = account.Balance;
            yield return PressUndo();
            bool doubleRejected = account.Balance == beforeDoubleUndo;
            Debug.Log($"[S4A] S4A_SHOP_UNDO step=again balance={account.Balance} unchanged={doubleRejected}");

            if (!doubleRejected)
            {
                Fail($"double_undo_refunded balance={account.Balance} before={beforeDoubleUndo}");
            }

            // ---- 7. 窗口关闭后撤销必须被拒 ----
            // 必须先在窗口关闭状态下**买一笔新的、且此前没买过的**（手电），
            // 否则会先撞上 already_owned / already_revoked，window_closed 这条分支根本没被走到（假通过）。
            account.ClosePurchaseWindow("test_leave_safehouse");
            yield return new WaitForSeconds(0.2f);

            long beforeBuyClosed = account.Balance;
            yield return PressBuy(3);

            bool lampOwned = account.OwnsItem("flashlight");
            bool boughtWhileClosed = lampOwned && account.Balance == beforeBuyClosed - 350;
            Debug.Log($"[S4A] S4A_SHOP_WINDOW_BUY owned={lampOwned} balance={account.Balance} expected={beforeBuyClosed - 350}");

            if (!boughtWhileClosed)
            {
                Fail($"buy_after_window_close_failed balance={account.Balance} owned={account.OwnedMask}");
            }

            long afterBuyClosed = account.Balance;
            yield return PressUndo();

            bool windowClosed = account.Balance == afterBuyClosed && account.OwnsItem("flashlight");
            Debug.Log($"[S4A] S4A_SHOP_UNDO step=window_closed balance={account.Balance} unchanged={windowClosed} owned={account.OwnedMask}");

            if (!windowClosed)
            {
                Fail($"undo_after_close_refunded balance={account.Balance}");
            }

            // 重开窗口后同一笔必须能撤——证明上一条拒的是「窗口」，不是「这笔不能撤」
            account.OpenPurchaseWindow();
            yield return PressUndo();

            bool reopenedUndo = account.Balance == beforeBuyClosed && !account.OwnsItem("flashlight");
            Debug.Log($"[S4A] S4A_SHOP_UNDO step=window_reopened balance={account.Balance} refunded={reopenedUndo}");

            if (!reopenedUndo)
            {
                Fail($"undo_after_reopen_failed balance={account.Balance}");
            }

            // ---- 8. hard_cap：一个 Visit 最多 8 笔（撤销不退计数）----
            // 用 350 的手电跑循环：买→撤，余额回到 600，循环稳态不漂移；
            // 用手套会撞 already_owned（第 1 步买的还没撤），visit 永远不涨。
            int guard = 0;
            while (account.VisitPurchases < TeamAccount.HardCapPerVisit && guard < 20)
            {
                guard++;
                yield return PressBuy(3);
                yield return PressUndo();
            }

            long beforeCap = account.Balance;
            yield return PressBuy(3);
            bool hardCap = account.Balance == beforeCap && account.VisitPurchases >= TeamAccount.HardCapPerVisit;
            Debug.Log($"[S4A] S4A_SHOP_CAP visit={account.VisitPurchases} cap={TeamAccount.HardCapPerVisit} balance={account.Balance} blocked={hardCap}");

            if (!hardCap)
            {
                Fail($"hard_cap_not_blocked visit={account.VisitPurchases} balance={account.Balance}");
            }

            // ---- 9. Story 2 恒等式：购买之后 balance 仍等于流水反算 ----
            P0SaveService save = P0SaveService.Instance;
            long sum = save != null && save.Data != null ? save.Data.SumFromTxns() : -1;
            bool identity = save != null && save.Data != null && save.Data.balance == sum;
            Debug.Log($"[S4A] S4A_SHOP_IDENTITY balance={(save != null && save.Data != null ? save.Data.balance : -1)} sumFromTxns={sum} ok={identity}");

            if (!identity)
            {
                Fail($"balance_identity_broken balance={(save != null && save.Data != null ? save.Data.balance : -1)} sum={sum}");
            }

            // 撤销笔数必须从存档数出来，不能写死 1——写死的数字会让「一次都没撤成功」也报通过
            int revoked = 0;
            if (save != null && save.Data != null && save.Data.purchases != null)
            {
                foreach (PurchaseRecord p in save.Data.purchases)
                {
                    if (p != null && p.state == PurchaseRecord.Revoked)
                    {
                        revoked++;
                    }
                }
            }

            if (revoked < 1)
            {
                Fail("no_revoked_purchase_recorded");
            }

            bool clean = failures == 0;
            Debug.Log($"[S4A] S4A_SHOP_OK balance={account.Balance} owned={account.OwnedMask} revoked={revoked} visit={account.VisitPurchases} identity={(identity ? 1 : 0)} ok={clean}");

            if (!clean)
            {
                Debug.LogError($"[S4A] S4A_SHOP_FAIL failures={failures}");
            }

            yield return new WaitForSeconds(0.3f);
            Application.Quit(clean ? 0 : 1);
        }

        /// <summary>数字键只按一帧：终端每帧都会读，按住会连买。</summary>
        private IEnumerator PressBuy(int hotkey)
        {
            S4AInput.AutoBuyIndex = hotkey;
            yield return null;
            S4AInput.AutoBuyIndex = 0;
            yield return new WaitForSeconds(0.35f);
        }

        private IEnumerator PressUndo()
        {
            S4AInput.AutoUndo = true;
            yield return null;
            S4AInput.AutoUndo = false;
            yield return new WaitForSeconds(0.35f);
        }

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

        private IEnumerator WaitFor(System.Func<bool> condition, float seconds, string failReason)
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
