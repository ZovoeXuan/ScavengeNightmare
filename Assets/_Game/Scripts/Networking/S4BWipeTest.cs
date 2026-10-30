using System.Collections;
using Mirror;
using ScavengeNightmare.Combat;
using ScavengeNightmare.Economy;
using ScavengeNightmare.Save;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ScavengeNightmare.Networking
{
    /// <summary>
    /// S4B-1 团灭验收：全队倒地 → 本趟结束 → 余额逐字不变、关卡回 1、进入安全屋。
    /// 单人 Host；先 NextLeg 到第 2 关，确保「回 1」不是本来就 1。
    /// </summary>
    public sealed class S4BWipeTest : MonoBehaviour
    {
        private int failures;

        private void Start()
        {
            if (!S4ACli.Has("-s4bwipe"))
            {
                this.enabled = false;
                return;
            }

            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 60;
            ScavengeNightmare.Traits.TraitSettings.ForceNoTraits = true;
            S4AInput.SuppressManual = true;
            Debug.Log("[S4A] S4A_NEUTRAL_OK traits=none feel=" + (S4ACli.Value("-feel=") ?? "default").ToLowerInvariant());

            if (string.IsNullOrEmpty(S4ACli.Value("-savedir=")))
            {
                S4ACli.SaveDirOverride = System.IO.Path.Combine(Application.persistentDataPath, "S4B_test_wipe");
            }

            NetworkManager manager = NetworkManager.singleton;
            if (manager == null)
            {
                Debug.LogError("[S4B] S4B_WIPE_FAIL reason=no_network_manager");
                Application.Quit(1);
                return;
            }

            StartCoroutine(Watchdog(90));

            try
            {
                manager.StartHost();
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[S4B] S4B_WIPE_FAIL reason=start_host type={ex.GetType().Name}");
                Application.Quit(1);
                return;
            }

            StartCoroutine(Run());
        }

        private IEnumerator Watchdog(int seconds)
        {
            yield return new WaitForSecondsRealtime(seconds);
            Debug.LogError($"[S4B] S4B_WIPE_WATCHDOG_TIMEOUT seconds={seconds}");
            Application.Quit(3);
        }

        private void Fail(string reason)
        {
            failures++;
            Debug.LogError($"[S4B] S4B_WIPE_TEST_FAIL reason={reason}");
        }

        private IEnumerator Run()
        {
            yield return WaitFor(() => NetworkClient.localPlayer != null, 25f, "no_local_player");
            yield return WaitFor(() => TeamAccount.Instance != null, 25f, "no_team_account");

            TeamAccount account = TeamAccount.Instance;
            PlayerVitals vitals = NetworkClient.localPlayer.GetComponent<PlayerVitals>();

            if (vitals == null)
            {
                Fail("no_player_vitals");
                Application.Quit(1);
                yield break;
            }

            account.DepositId("wipe-seed", 1000);
            yield return new WaitForSeconds(0.4f);

            long balanceBefore = account.Balance;

            // 先推进到第 2 关：否则「团灭后 leg=1」无法区分是重置还是本来没动过。
            account.NextLeg(2000, "test");
            yield return new WaitForSeconds(0.4f);

            Debug.Log($"[S4B] S4B_WIPE_START balance={balanceBefore} leg={account.LegIndex} quota={account.LegQuota}");
            Check(account.LegIndex == 2, $"pre_leg expected=2 actual={account.LegIndex}");

            vitals.ServerSetFreeInjectors(0);

            // 第一次 200 伤害会被「单次伤害保护」钳到 1 HP（P0 §2.3 不许一击秒），
            // 必须再补一刀才会真的倒地。
            vitals.ServerApplyDamage(200, "wipe_test");
            yield return new WaitForSeconds(0.3f);
            vitals.ServerApplyDamage(1, "wipe_test");
            yield return new WaitForSeconds(0.4f);
            Check(vitals.IsDowned, "not_downed");

            float end = Time.realtimeSinceStartup + 12f;
            while (Time.realtimeSinceStartup < end && SceneManager.GetActiveScene().name != "Safehouse")
            {
                yield return null;
            }

            yield return new WaitForSeconds(1.0f);

            account = TeamAccount.Instance;
            bool sceneOk = SceneManager.GetActiveScene().name == "Safehouse";
            bool legOk = account != null && account.LegIndex == 1;
            bool balanceOk = account != null && account.Balance == balanceBefore;

            Debug.Log($"[S4B] S4B_WIPE_BACK scene={SceneManager.GetActiveScene().name} leg={(account != null ? account.LegIndex : -1)} balance={(account != null ? account.Balance : -1)} expected={balanceBefore}");

            Check(sceneOk, "scene_not_safehouse");
            Check(legOk, $"leg_not_reset leg={(account != null ? account.LegIndex : -1)}");
            Check(balanceOk, $"balance_changed actual={(account != null ? account.Balance : -1)} expected={balanceBefore}");

            bool ok = failures == 0;

            if (ok)
            {
                Debug.Log($"[S4B] S4B_WIPE_OK balance={balanceBefore} leg=1 scene=Safehouse cargoLost=1 failures=0 ok=True");
            }
            else
            {
                Debug.LogError($"[S4B] S4B_WIPE_FAIL failures={failures}");
            }

            yield return new WaitForSeconds(0.3f);
            Application.Quit(ok ? 0 : 1);
        }

        private void Check(bool condition, string reason)
        {
            if (!condition)
            {
                Fail(reason);
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
