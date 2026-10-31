using System.Collections;
using System.Collections.Generic;
using Mirror;
using ScavengeNightmare.Curses;
using ScavengeNightmare.Economy;
using ScavengeNightmare.Gameplay;
using ScavengeNightmare.Monsters;
using ScavengeNightmare.Save;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ScavengeNightmare.Networking
{
    /// <summary>
    /// S4B-5 三关梯度验收（单人 Host，连跑 3 关）。
    ///
    /// 判据全部来自日志原文：
    ///   GATE_LEVEL n=1/2/3 quota=1800/2400/2800 source=table anchors=12/14/16
    ///            totalValue=3155/4365/4785 ratio=50–60%（57.0 / 55.0 / 58.5）
    ///   S4B_MON_LEG leg=n spawned=1/3/4
    ///   GATE_CURSE_SPAWN leg=n want=0/1/2
    ///   GATE_PROPS_MODE mode=p0leg（不许出现 legacy4a）
    ///   GATE_BUY_REJECT reason=wrong_scene（Night1 里的商店终端必须拒绝）
    ///   GATE_LEVEL_ADVANCE 之后 legProgress 归零、余额单调不减、dupBlocked≥1
    ///
    /// 注意：本钩子是 -s4bleg，与 S4B-4 的 -s4blegcurse 是两套夹具；
    /// 一个进程只能有一个 StartHost，两个 Story 共用开关会让它们各调一次。
    /// </summary>
    public sealed class S4BLegTest : MonoBehaviour
    {
        private struct LogLine
        {
            public string Text;
            public float Time;
        }

        private const float RatioMin = 50f;
        private const float RatioMax = 60f;

        private readonly List<LogLine> lines = new();

        private int failures;
        private TeamAccount account;

        private long lastBalance = -1;
        private int legCount;
        private int lastDupBlocked = -1;
        private string ratioText = string.Empty;

        private void Start()
        {
            if (!S4ACli.Has("-s4bleg"))
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
                S4ACli.SaveDirOverride = System.IO.Path.Combine(Application.persistentDataPath, "S4B_test_leg");
            }

            Application.logMessageReceived += OnLog;

            NetworkManager manager = NetworkManager.singleton;
            if (manager == null)
            {
                Fail("no_network_manager");
                Application.Quit(1);
                return;
            }

            StartCoroutine(Watchdog(360));

            try
            {
                manager.StartHost();
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[S4B] S4B_LEG_FAIL reason=start_host type={ex.GetType().Name}");
                Application.Quit(1);
                return;
            }

            StartCoroutine(Run());
        }

        private void OnDestroy()
        {
            Application.logMessageReceived -= OnLog;
            S4AInput.AutoInteract = false;
        }

        private void OnLog(string text, string stackTrace, LogType type)
        {
            lines.Add(new LogLine { Text = text, Time = Time.time });

            if (lines.Count > 6000)
            {
                lines.RemoveRange(0, 3000);
            }
        }

        private IEnumerator Watchdog(int seconds)
        {
            yield return new WaitForSecondsRealtime(seconds);
            Debug.LogError($"[S4B] S4B_LEG_WATCHDOG_TIMEOUT seconds={seconds}");
            Application.Quit(3);
        }

        private void Fail(string reason)
        {
            failures++;
            Debug.LogError($"[S4B] S4B_LEG_TEST_FAIL reason={reason}");
        }

        private void Check(bool condition, string reason)
        {
            if (!condition)
            {
                Fail(reason);
            }
        }

        // ==================== 主流程 ====================

        private IEnumerator Run()
        {
            yield return WaitFor(() => NetworkClient.localPlayer != null, 25f, "no_local_player");
            yield return WaitFor(() => TeamAccount.Instance != null, 25f, "no_team_account");

            account = TeamAccount.Instance;

            PlayerVitalsRef();


            // 存档可能带着上一次的 legIndex 回来（开发机反复跑）。先归一化到第 1 关，
            // 否则第一行 GATE_LEVEL 的 n 就不是 1，整条梯度断言会从起点就错位。
            if (account.LegIndex != 1 || account.LegQuota != LegContentTable.Instance.QuotaFor(1))
            {
                if (!account.ServerEndRun("leg_test_reset"))
                {
                    Fail("reset_run_failed");
                    Application.Quit(1);
                    yield break;
                }

                yield return new WaitForSeconds(0.6f);
                DespawnAllProps();
                FindFirst<PropSpawner>()?.RespawnForNewScene();
            }

            yield return WaitFor(() => FindLine("GATE_PROPS_MODE mode=p0leg") >= 0, 12f, "no_p0leg_mode");
            yield return WaitFor(() => FindLine("GATE_LEVEL n=1 quota=") >= 0, 12f, "no_gate_level_1");
            yield return new WaitForSeconds(0.5f);

            for (int leg = 1; leg <= 3; leg++)
            {
                if (leg > 1)
                {
                    yield return WaitFor(() => account.LegIndex == leg, 10f, $"leg_index_not_{leg}");
                    yield return WaitFor(() => FindLine($"GATE_LEVEL n={leg} quota=") >= 0, 10f, $"no_gate_level_{leg}");
                    yield return WaitFor(() => FindLine($"S4B_MON_LEG leg={leg} spawned=") >= 0, 10f, $"no_monster_leg_{leg}");
                    yield return WaitFor(() => FindLine($"GATE_CURSE_SPAWN leg={leg} want=") >= 0, 10f, $"no_curse_leg_{leg}");
                    yield return new WaitForSeconds(0.4f);
                }

                yield return VerifyLeg(leg);

                // 怪物只用来证明梯度，不参与后面的交互：留着一只会把玩家打到倒地，
                // 让「换关 / 商店 / 入账」的断言混进一条与轻量无关的红。
                FindFirst<MonsterSpawner>()?.ServerDespawnAll();

                CheckProgressReset(leg);
                CheckBalanceMonotonic(leg);

                yield return RunWrongSceneBuy();
                yield return RunDupBlocked();

                if (leg < 3)
                {
                    yield return FillQuota(leg);
                    yield return RoundTrip(leg + 1);
                }
            }

            lastDupBlocked = account.DupBlocked;
            Check(lastDupBlocked >= 1, $"dup_blocked actual={lastDupBlocked}");
            Check(FindLine("GATE_PROPS_MODE mode=legacy4a") < 0, "legacy4a_mode_leaked");
            Check(FindLine("S4B_SCENE_REBUILD order=clear_monsters>noise_reset>props>monsters>curses") >= 0,
                "no_scene_rebuild_order_line");

            bool ok = failures == 0;
            string metrics = $"legs={legCount} ratios={ratioText} balance={account.Balance} " +
                             $"dupBlocked={lastDupBlocked} legProgress={account.LegProgress}";

            if (ok)
            {
                Debug.Log($"[S4B] S4B_LEG_OK {metrics} ok=True");
            }
            else
            {
                Debug.LogError($"[S4B] S4B_LEG_FAIL {metrics} failures={failures}");
            }

            yield return new WaitForSeconds(0.3f);
            Application.Quit(ok ? 0 : 1);
        }

        private void PlayerVitalsRef()
        {
            GrabController grab = NetworkClient.localPlayer != null
                ? NetworkClient.localPlayer.GetComponent<GrabController>()
                : null;

            // 与所有既有用例一致：关掉本地鼠标读入，避免「没按住左键」把刚抓的货扔出去。
            grab?.SetInputEnabled(false);
        }

        // ---------------- 单关判据 ----------------

        private IEnumerator VerifyLeg(int leg)
        {
            int wantAnchors = leg == 1 ? 12 : (leg == 2 ? 14 : 16);
            int wantValue = leg == 1 ? 3155 : (leg == 2 ? 4365 : 4785);
            int wantQuota = leg == 1 ? 1800 : (leg == 2 ? 2400 : 2800);
            int wantMonsters = leg == 1 ? 1 : (leg == 2 ? 3 : 4);
            int wantCurses = leg - 1;

            if (leg == 1)
            {
                // 第 1 关的货与怪都由本夹具显式驱动（生成器接到 -s4bleg 会跳过自动生成）。
                FindFirst<MonsterSpawner>()?.ServerSpawnForLeg(1);
                yield return new WaitForSeconds(0.3f);
                FindFirst<CurseRuntime>()?.ServerSpawnForLeg(1);
                yield return new WaitForSeconds(0.3f);
            }

            int lineIndex = FindLine($"GATE_LEVEL n={leg} quota=");
            Check(lineIndex >= 0, $"gate_level_missing leg={leg}");

            string text = lineIndex >= 0 ? lines[lineIndex].Text : string.Empty;
            Check(text.Contains($"quota={wantQuota}"), $"quota_wrong leg={leg} text={text}");
            Check(text.Contains("source=table"), $"quota_not_from_table leg={leg} text={text}");
            Check(text.Contains($"anchors={wantAnchors}"), $"anchors_wrong leg={leg} text={text}");
            Check(text.Contains($"totalValue={wantValue}"), $"total_value_wrong leg={leg} text={text}");

            float ratio = ParseFloat(text, "ratio=");
            Check(ratio >= RatioMin && ratio <= RatioMax, $"ratio_out_of_band leg={leg} actual={ratio:F1}");
            ratioText += (ratioText.Length == 0 ? "" : "/") + $"{ratio:F1}";

            Check(FindLine($"S4B_MON_LEG leg={leg} spawned={wantMonsters}") >= 0,
                $"monster_count_wrong leg={leg} want={wantMonsters}");
            Check(FindLine($"GATE_CURSE_SPAWN leg={leg} want={wantCurses} count={wantCurses}") >= 0,
                $"curse_count_wrong leg={leg} want={wantCurses}");

            legCount = leg;

            Debug.Log($"[S4B] S4B_LEG_METRICS leg={leg} anchors={wantAnchors} value={wantValue} " +
                      $"quota={wantQuota} ratio={ratio:F1} monsters={wantMonsters} curses={wantCurses}");
        }

        private void CheckProgressReset(int leg)
        {
            Check(account.LegIndex == leg, $"leg_index actual={account.LegIndex} want={leg}");
            Check(account.LegProgress == 0, $"progress_not_zero leg={leg} actual={account.LegProgress}");
            Check(account.LegQuota == LegContentTable.Instance.QuotaFor(leg),
                $"account_quota_wrong leg={leg} actual={account.LegQuota}");
        }

        private void CheckBalanceMonotonic(int leg)
        {
            Check(account.Balance >= lastBalance, $"balance_regressed leg={leg} actual={account.Balance} prev={lastBalance}");
            lastBalance = account.Balance;
        }

        // ---------------- 商店：夜路里必须拒 ----------------

        private IEnumerator RunWrongSceneBuy()
        {
            ShopTerminal terminal = FindFirst<ShopTerminal>();
            GameObject probe = null;

            if (terminal == null)
            {
                // S4C-3 起夜路场景里已经没有商店终端（T12 收口）。
                // 「夜路拒买」这条负向注入不能就此消失——临时放一个测试探针，
                // 仍然走真实的 ServerHandleBuy 守卫，验完立即销毁。
                Debug.Log("[S4B] S4B_LEG_WRONG_SCENE_PROBE reason=no_terminal_in_night");
                probe = new GameObject("ShopTerminal_NightProbe");
                probe.transform.position = new Vector3(0f, 0.6f, -6f);
                terminal = probe.AddComponent<ShopTerminal>();
                yield return new WaitForSeconds(0.15f);
            }

            NetworkIdentity local = NetworkClient.localPlayer;
            if (local == null)
            {
                Fail("wrong_scene_no_player");
                yield break;
            }

            Vector3 at = terminal.transform.position;
            Teleport(local.transform, new Vector3(at.x + 1f, 1f, at.z));
            yield return new WaitForSeconds(0.3f);

            int before = CountLines("GATE_BUY_REJECT");
            terminal.ServerHandleBuy(local.transform, 1);
            yield return new WaitForSeconds(0.2f);

            int index = FindLine("GATE_BUY_REJECT");
            bool newLine = CountLines("GATE_BUY_REJECT") > before;
            Check(newLine, "wrong_scene_no_reject_line");
            Check(index >= 0 && lines[index].Text.Contains("reason=wrong_scene"),
                "wrong_scene_reason_mismatch");
            Check(index >= 0 && lines[index].Text.Contains("scene=Night1"), "wrong_scene_scene_field");

            // 换回入口附近，别把玩家留在商店终端旁边影响后续换关。
            Teleport(local.transform, new Vector3(0f, 1f, 18f));
            yield return new WaitForSeconds(0.2f);

            if (probe != null)
            {
                Object.Destroy(probe);
                yield return null;
            }
        }

        // ---------------- 重复入账 ----------------

        private IEnumerator RunDupBlocked()
        {
            int before = CountLines("S4A_DEPOSIT_DUP");
            account.DepositId("leg-dup-probe", 100);
            yield return new WaitForSeconds(0.15f);
            account.DepositId("leg-dup-probe", 100);
            yield return new WaitForSeconds(0.25f);

            int after = CountLines("S4A_DEPOSIT_DUP");
            Check(after > before, $"dup_not_blocked before={before} after={after}");
            Check(account.DupBlocked >= 1, $"dup_blocked_counter actual={account.DupBlocked}");

            Debug.Log($"[S4B] S4B_LEG_DUP dupBlocked={account.DupBlocked} newLines={after - before}");
        }

        // ---------------- 凑够过关额 + 往返换关 ----------------

        private IEnumerator FillQuota(int leg)
        {
            int missing = Mathf.Max(0, account.LegQuota - account.LegProgress);

            if (missing > 0)
            {
                account.DepositId($"leg-fill-{leg}", missing);
                yield return new WaitForSeconds(0.4f);
            }

            Check(account.QuotaReached, $"quota_not_reached leg={leg} progress={account.LegProgress} quota={account.LegQuota}");
            Debug.Log($"[S4B] S4B_LEG_FILL leg={leg} progress={account.LegProgress} quota={account.LegQuota} balance={account.Balance}");
        }

        private IEnumerator RoundTrip(int nextLeg)
        {
            long walletBefore = account.Balance;

            yield return UsePortal(new Vector3(-6f, 1.05f, -15.2f), "Safehouse");

            yield return WaitFor(() => NetworkClient.localPlayer != null && TeamAccount.Instance != null,
                20f, "no_account_in_safehouse");
            account = TeamAccount.Instance;

            Check(account.Balance == walletBefore, $"wallet_lost_entering_safehouse actual={account.Balance} want={walletBefore}");

            yield return UsePortal(new Vector3(0f, 1.05f, -4.6f), "Night1");

            yield return WaitFor(() => NetworkClient.localPlayer != null && TeamAccount.Instance != null,
                20f, "no_account_after_return");
            account = TeamAccount.Instance;

            if (account.LegIndex != nextLeg)
            {
                Fail($"leg_not_advanced actual={account.LegIndex} want={nextLeg}");
            }

            Check(account.LegProgress == 0, $"progress_not_reset_on_advance leg={nextLeg} actual={account.LegProgress}");
            Check(account.Balance == walletBefore, $"wallet_changed_after_return actual={account.Balance} want={walletBefore}");

            // 换关重建是异步的：等 GATE_LEVEL 行自己出现，不用固定 sleep 赌帧数。
            yield return WaitFor(() => FindLine($"GATE_LEVEL n={nextLeg} quota=") >= 0, 12f,
                $"no_gate_level_after_return_{nextLeg}");

            Debug.Log($"[S4B] S4B_LEG_ADVANCE leg={nextLeg} balance={account.Balance} progress={account.LegProgress}");
        }

        /// <summary>站到门前长按交互，直到场景切到 targetScene 或超时（与 S4ALoopTest 同构）。</summary>
        private IEnumerator UsePortal(Vector3 stand, string targetScene)
        {
            yield return new WaitForSeconds(1.8f);

            NetworkIdentity local = NetworkClient.localPlayer;
            if (local == null)
            {
                Fail($"portal_no_player target={targetScene}");
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

            Debug.Log($"[S4B] S4B_LEG_PORTAL target={targetScene} actual={SceneManager.GetActiveScene().name} seconds={t:F1} " +
                      $"ok={(SceneManager.GetActiveScene().name == targetScene ? 1 : 0)}");

            Check(SceneManager.GetActiveScene().name == targetScene, $"portal_failed target={targetScene}");

            yield return new WaitForSeconds(1.4f);
        }

        private static void DespawnAllProps()
        {
            if (!NetworkServer.active)
            {
                return;
            }

            foreach (GrabbableProp prop in FindObjectsByType<GrabbableProp>(FindObjectsSortMode.None))
            {
                if (prop == null || prop.IsHeld)
                {
                    continue;
                }

                NetworkServer.Destroy(prop.gameObject);
            }
        }

        // ==================== 日志工具 ====================

        private int FindLine(string fragment)
        {
            for (int i = lines.Count - 1; i >= 0; i--)
            {
                if (lines[i].Text != null && lines[i].Text.Contains(fragment))
                {
                    return i;
                }
            }

            return -1;
        }

        private int CountLines(string fragment)
        {
            int count = 0;
            foreach (LogLine line in lines)
            {
                if (line.Text != null && line.Text.Contains(fragment))
                {
                    count++;
                }
            }

            return count;
        }

        private static float ParseFloat(string text, string key)
        {
            if (string.IsNullOrEmpty(text))
            {
                return -1f;
            }

            int at = text.IndexOf(key, System.StringComparison.Ordinal);
            if (at < 0)
            {
                return -1f;
            }

            int start = at + key.Length;
            int end = start;

            while (end < text.Length && (char.IsDigit(text[end]) || text[end] == '.' || text[end] == '-'))
            {
                end++;
            }

            return float.TryParse(
                text.Substring(start, end - start),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out float value)
                ? value
                : -1f;
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

        private static T FindFirst<T>() where T : Object
        {
            T[] found = Object.FindObjectsByType<T>(FindObjectsSortMode.None);
            return found.Length > 0 ? found[0] : null;
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
    }
}
