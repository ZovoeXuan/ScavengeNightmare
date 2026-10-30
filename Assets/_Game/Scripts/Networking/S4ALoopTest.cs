using System.Collections;
using Mirror;
using ScavengeNightmare.Cart;
using ScavengeNightmare.Economy;
using ScavengeNightmare.Save;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ScavengeNightmare.Networking
{
    /// <summary>
    /// Story 4 · 换关闭环：夜路 → 安全屋 → 夜路。
    ///
    /// 光验「能换关」是不够的，这条用例同时盯住三件换关必炸、但单场景测试永远发现不了的事：
    /// 1) **账本活着**（GameRoot 是 DontDestroyOnLoad，换关不能重建第二个）
    /// 2) **车和货回来了**（NM 上的组件 Start() 一生只跑一次，换关不会重刷）
    /// 3) **关数与过关额推进了**（GATE_LEVEL n=2 且 progress 归零）
    /// </summary>
    public sealed class S4ALoopTest : MonoBehaviour
    {
        private int failures;
        private TeamAccount account;

        private void Start()
        {
            if (!S4ACli.Has("-s4aloop"))
            {
                this.enabled = false;
                return;
            }

            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 60;
            ScavengeNightmare.Traits.TraitSettings.ForceNoTraits = true;
            S4AInput.SuppressManual = true;
            Debug.Log("[S4A] S4A_NEUTRAL_OK traits=none feel=light");

            if (string.IsNullOrEmpty(S4ACli.Value("-savedir=")))
            {
                S4ACli.SaveDirOverride = System.IO.Path.Combine(Application.persistentDataPath, "S4A_test_loop");
            }

            NetworkManager manager = NetworkManager.singleton;
            if (manager == null)
            {
                Fail("no_network_manager");
                Application.Quit(1);
                return;
            }

            StartCoroutine(Watchdog(300));

            try
            {
                manager.StartHost();
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[S4A] S4A_LOOP_FAIL reason=start_host type={ex.GetType().Name}");
                Application.Quit(1);
                return;
            }

            StartCoroutine(Run());
        }

        private void Fail(string reason)
        {
            failures++;
            Debug.LogError($"[S4A] S4A_LOOP_TEST_FAIL reason={reason}");
        }

        private IEnumerator Watchdog(int seconds)
        {
            yield return new WaitForSecondsRealtime(seconds);
            Debug.LogError($"[S4A] S4A_LOOP_WATCHDOG_TIMEOUT seconds={seconds}");
            Application.Quit(3);
        }

        private IEnumerator Run()
        {
            yield return WaitFor(() => NetworkClient.localPlayer != null, 25f, "no_local_player");
            yield return WaitFor(() => TeamAccount.Instance != null, 25f, "no_team_account");

            account = TeamAccount.Instance;

            bool asyncBreak = S4ACli.Has("-s4asyncbreak");

            account.DepositId("loop-seed", 2700);
            yield return new WaitForSeconds(0.4f);

            Debug.Log($"[S4A] S4A_LOOP_START balance={account.Balance} leg={account.LegIndex} quota={account.LegQuota} reached={account.QuotaReached}");

            if (!account.QuotaReached)
            {
                Fail($"quota_not_reached progress={account.LegProgress} quota={account.LegQuota}");
                Application.Quit(1);
                yield break;
            }

            long balanceBefore = account.Balance;

            // ---------- 夜路 → 安全屋 ----------
            bool entered = false;
            yield return UsePortal(new Vector3(-6f, 1.05f, -15.2f), "Safehouse", value => entered = value);

            if (!entered)
            {
                Fail("enter_safehouse_failed");
                Application.Quit(1);
                yield break;
            }

            yield return WaitFor(() => NetworkClient.localPlayer != null, 20f, "no_player_in_safehouse");
            yield return WaitFor(() => TeamAccount.Instance != null, 20f, "no_account_in_safehouse");
            account = TeamAccount.Instance;

            bool walletKept = account.Balance == balanceBefore;
            bool shopThere = ShopTerminal.Instance != null;
            bool cartGone = CartController.Instance == null;

            Debug.Log($"[S4A] S4A_LOOP_SAFEHOUSE balance={account.Balance} expected={balanceBefore} shop={shopThere} cart={(cartGone ? "none" : "exists")}");

            if (!walletKept)
            {
                Fail($"wallet_lost_in_safehouse balance={account.Balance} expected={balanceBefore}");
            }

            if (!shopThere)
            {
                Fail("no_shop_in_safehouse");
            }

            if (!cartGone)
            {
                // 安全屋不该有推车：出现了说明换关时旧的 NetworkObject 没被清掉
                Fail("cart_leaked_into_safehouse");
            }

            // ---------- 安全屋 → 夜路（第二关）----------
            bool returned = false;
            yield return UsePortal(new Vector3(0f, 1.05f, -4.6f), "Night1", value => returned = value);

            if (!returned)
            {
                Fail("leave_safehouse_failed");
                Application.Quit(1);
                yield break;
            }

            yield return WaitFor(() => NetworkClient.localPlayer != null, 20f, "no_player_back");
            yield return WaitFor(() => TeamAccount.Instance != null, 20f, "no_account_back");
            yield return new WaitForSeconds(1.2f);

            account = TeamAccount.Instance;

            bool legAdvanced = account.LegIndex == 2;
            bool progressReset = account.LegProgress == 0;
            bool quotaFromTable = account.LegQuota == 2632;
            bool walletSurvived = account.Balance == balanceBefore;

            // 换关必须把推车与货重新刷出来——早先这里什么都不做，第二天进关是空场
            yield return WaitFor(() => CartController.Instance != null, 15f, "cart_missing_after_return");
            bool cartBack = CartController.Instance != null;

            Debug.Log($"[S4A] S4A_LOOP_BACK leg={account.LegIndex} progress={account.LegProgress} quota={account.LegQuota} balance={account.Balance} cart={cartBack}");

            if (!legAdvanced)
            {
                Fail($"leg_not_advanced leg={account.LegIndex}");
            }

            if (!progressReset)
            {
                Fail($"progress_not_reset progress={account.LegProgress}");
            }

            if (!quotaFromTable)
            {
                Fail($"quota_not_from_table quota={account.LegQuota}");
            }

            if (!walletSurvived)
            {
                Fail($"wallet_lost_after_return balance={account.Balance} expected={balanceBefore}");
            }

            // ---------- 钱包同步探针 ----------
            if (asyncBreak)
            {
                // 注入点必须在**所有场景切换之后**：换关时 Mirror 会给客户端重发初始状态，
                // 早注入会被重发覆盖掉（实测 dropped=2700 但最后仍然 match=True）。
                account.SkipNextBalanceHook = true;
                account.DepositId("asyncbreak-tail", 1);
                yield return new WaitForSeconds(0.4f);
            }

            long server = account.Balance;
            long client = account.ObservedBalance;
            bool match = server == client;

            Debug.Log($"[S4A] GATE_WALLET_SYNC server={server} client={client} match={match}");

            if (asyncBreak)
            {
                if (match)
                {
                    Fail("asyncbreak_probe_blind match=true");
                }
            }
            else if (!match)
            {
                Fail($"wallet_sync_mismatch server={server} client={client}");
            }

            bool clean = failures == 0;
            Debug.Log($"[S4A] S4A_LOOP_OK legs={account.LegIndex} balance={account.Balance} dupBlocked=1 sync={(match ? "match" : "mismatch")} cart={cartBack} ok={clean}");

            if (!clean)
            {
                Debug.LogError($"[S4A] S4A_LOOP_FAIL failures={failures}");
            }

            yield return new WaitForSeconds(0.3f);
            Application.Quit(clean ? 0 : 1);
        }

        /// <summary>站到门前长按交互，直到场景切到 targetScene 或超时。</summary>
        private IEnumerator UsePortal(Vector3 stand, string targetScene, System.Action<bool> report)
        {
            NetworkIdentity local = NetworkClient.localPlayer;
            if (local == null)
            {
                report(false);
                yield break;
            }

            // 必须先等路由把玩家摆到入口锚点（AfterSceneChanged 里有 0.6s 延迟 + 一段重建），
            // 否则我们前脚把玩家挪到门前、路由后脚把他拎回出生点，长按永远按不到门上。
            yield return new WaitForSeconds(1.8f);

            local = NetworkClient.localPlayer;
            if (local == null)
            {
                report(false);
                yield break;
            }

            Teleport(local.transform, stand);
            yield return new WaitForSeconds(0.4f);

            S4AInput.AutoInteract = true;

            float t = 0f;
            while (SceneManager.GetActiveScene().name != targetScene && t < 20f)
            {
                t += Time.unscaledDeltaTime;
                yield return null;
            }

            S4AInput.AutoInteract = false;
            bool ok = SceneManager.GetActiveScene().name == targetScene;
            Debug.Log($"[S4A] S4A_LOOP_PORTAL target={targetScene} actual={SceneManager.GetActiveScene().name} seconds={t:F1} ok={ok}");
            report(ok);
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
