using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Mirror;
using ScavengeNightmare.Economy;
using ScavengeNightmare.Gameplay;
using ScavengeNightmare.Save;
using UnityEngine;

namespace ScavengeNightmare.Networking
{
    /// <summary>
    /// Story 1 自动化：单人进 Night1，抱货到公秤长按提交，验证「逐件入账 + 同 cargoId 被显式拒绝」。
    /// 启动参数：Builds/Sprint4A/ScavengeNightmare.exe -s4adeposit
    ///
    /// 硬性纪律：任何一条失败都必须计入 failures，并体现在最后的 S4A_STORY1_OK / FAIL 上。
    /// 只打日志不计数的话，超时之后脚本照样继续跑、最后照样打一行 ok=True —— 那是最坏的假通过。
    /// </summary>
    public sealed class S4ADepositTest : MonoBehaviour
    {
        private bool enabledTest;
        private int failures;

        private void Fail(string reason)
        {
            failures++;
            Debug.LogError($"[S4A] S4A_DEPOSIT_FAIL reason={reason}");
        }

        private void Start()
        {
            foreach (string a in Environment.GetCommandLineArgs())
            {
                if (string.Equals(a, "-s4adeposit", StringComparison.OrdinalIgnoreCase))
                {
                    enabledTest = true;
                }
            }

            if (!enabledTest)
            {
                this.enabled = false;
                return;
            }

            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 60;

            // 4A 必须中性身份：身份/缺点牌倍率会污染负重减速与交互距离，
            // 而且断言会在“被污染的世界里自洽通过”，是最难查的一类假通过。
            ScavengeNightmare.Traits.TraitSettings.ForceNoTraits = true;
            ScavengeNightmare.Economy.S4AInput.SuppressManual = true;
            Debug.Log("[S4A] S4A_NEUTRAL_OK traits=none");

            // Story 2 起账本会落盘。这个用例的 TALLY 断言的是「本轮 accepted == 本轮 unique」，
            // 一旦把上一轮的 txn/balance 恢复进来，断言会立刻变成历史累计值而对不上。
            // 所以未显式指定目录时，用独立测试目录并在开跑前清空——绝不碰默认的玩家存档目录。
            if (string.IsNullOrEmpty(S4ACli.Value("-savedir=")))
            {
                string testDir = Path.Combine(Application.persistentDataPath, "S4A_test_deposit");
                try
                {
                    if (Directory.Exists(testDir))
                    {
                        Directory.Delete(testDir, true);
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[S4A] S4A_TEST_DIR_CLEAN_FAIL dir={testDir} type={ex.GetType().Name}");
                }

                S4ACli.SaveDirOverride = testDir;
                Debug.Log($"[S4A] S4A_TEST_SAVE_DIR dir={testDir} cleaned=1");
            }

            NetworkManager manager = NetworkManager.singleton;
            if (manager == null)
            {
                Fail("no_network_manager");
                return;
            }

            manager.StartHost();
            StartCoroutine(Run());
        }

        private IEnumerator Run()
        {
            yield return WaitFor(() => NetworkClient.localPlayer != null, 20f, "no_local_player");
            yield return WaitFor(() => TeamAccount.Instance != null, 20f, "no_team_account");
            yield return WaitFor(() => QuotaTerminal.Instance != null, 20f, "no_quota_terminal");

            NetworkIdentity player = NetworkClient.localPlayer;
            PlayerMotor motor = player.GetComponent<PlayerMotor>();
            GrabController grab = player.GetComponent<GrabController>();
            // 必须关掉玩家输入：GrabController.Update 会在「按住左键」不成立时立刻 RequestRelease，
            // 自动化没有鼠标按下，否则刚抓起来下一帧就被判松手（实测 immediate=True / afterFrame=False）。
            grab.SetInputEnabled(false);
            TeamAccount account = TeamAccount.Instance;
            QuotaTerminal terminal = QuotaTerminal.Instance;

            // 交互必须走 PlayerInteract（客户端上报），不能让服务端读自己的键盘：
            // 主机模式下房主按 E 能跑通、远端客户端按不动，是最典型的一类假联机。
            // 组件缺失要在这里就炸，而不是等到双人冒烟才发现。
            PlayerInteract interact = player.GetComponent<PlayerInteract>();
            if (interact == null)
            {
                Fail("player_missing_player_interact");
                yield break;
            }

            yield return WaitFor(() => FindObjectsByType<GrabbableProp>(FindObjectsSortMode.None).Length > 0, 20f, "no_props");

            List<GrabbableProp> pool = new();
            foreach (GrabbableProp prop in FindObjectsByType<GrabbableProp>(FindObjectsSortMode.None))
            {
                if (prop != null && prop.CurrentValue > 0)
                {
                    pool.Add(prop);
                }
            }

            // 先用重件：小件可能被 Loadout.IsSmallItem 拦截，测提交不该被“抓不到小件”卡住
            pool.Sort((a, b) => b.Mass.CompareTo(a.Mass));

            List<GrabbableProp> targets = new();
            foreach (GrabbableProp prop in pool)
            {
                if (targets.Count >= 4)
                {
                    break;
                }

                targets.Add(prop);
            }

            if (targets.Count == 0)
            {
                Fail("no_valuable_props");
                yield break;
            }

            S4AInput.AutoInteract = true;

            foreach (GrabbableProp prop in targets)
            {
                if (prop == null)
                {
                    continue;
                }

                // --- 以下三项都是自动化专用手段，不是玩法实现，正式流程里不存在 ---
                // 1) 瞬移必须连货一起挪，否则服务端 breakDistance 会立刻判脱手（D11/D13 踩过的坑）。
                // 2) NetworkTransformReliable 会用上一帧快照把手动瞬移拉回去，必须临时停一下再改坐标。
                Vector3 stand = terminal.transform.position;
                stand.y = 1.05f;
                Teleport(motor.transform, stand);
                motor.transform.LookAt(new Vector3(terminal.transform.position.x, stand.y, terminal.transform.position.z + 2f));

                yield return new WaitForSeconds(0.25f);

                // 3) 必须放到持物点，不是“挪近就行”：持物目标 = 前方 holdDistance + 抬高 0.45，
                //    放偏了会首帧超过 breakDistance 直接脱手。位置要在等待之后按“当前玩家位置”算，
                //    否则玩家落地过程中的位移会把货甩开；判定比的是 body.worldCenterOfMass 不是 position。
                float holdDist = FeelSettings.HoldDistance(prop.Profile.holdDistance);
                Vector3 holdPos = motor.transform.position + motor.transform.forward * holdDist + new Vector3(0f, 0.45f, 0f);
                Vector3 comOffset = prop.Body != null ? prop.Body.worldCenterOfMass - prop.transform.position : Vector3.zero;
                Teleport(prop.transform, holdPos - comOffset);
                if (prop.Body != null)
                {
                    prop.Body.linearVelocity = Vector3.zero;
                    prop.Body.angularVelocity = Vector3.zero;
                }

                Physics.SyncTransforms();

                float dist = Vector3.Distance(motor.transform.position, prop.transform.position);
                Debug.Log($"[S4A] S4A_DEPOSIT_DEBUG id={(prop.Profile != null ? prop.Profile.id : "?")} mass={prop.Mass:F0} dist={dist:F2} isServer={grab.isServer} grabEnabled={grab.enabled} hasBody={prop.Body != null}");

                grab.TryGrab(prop);
                bool immediate = grab.IsHolding;
                yield return null;
                Debug.Log($"[S4A] S4A_DEPOSIT_DEBUG2 id={(prop.Profile != null ? prop.Profile.id : "?")} immediate={immediate} afterFrame={grab.IsHolding} heldNull={grab.HeldProp == null}");
                yield return WaitFor(() => grab.IsHolding, 2f, $"grab_timeout id={(prop.Profile != null ? prop.Profile.id : "?")}");

                if (!grab.IsHolding)
                {
                    Fail($"grab_failed id={(prop.Profile != null ? prop.Profile.id : "?")} mass={prop.Mass:F0}");
                    continue;
                }

                long before = account.Balance;
                yield return WaitFor(() => account.Balance > before, 4f, $"submit_timeout id={(prop.Profile != null ? prop.Profile.id : "?")}");
            }

            S4AInput.AutoInteract = false;

            // 主动重放同一个 cargoId：必须被显式拒绝，不能靠“物件已销毁”假装通过
            string dupId = account.LastCommittedId;
            int dupValue = account.LastCommittedValue;
            if (string.IsNullOrEmpty(dupId))
            {
                Fail("no_committed_id_for_replay");
            }
            else
            {
                long beforeDup = account.Balance;
                bool replayAccepted = account.DepositId(dupId, dupValue);
                if (replayAccepted || account.Balance != beforeDup)
                {
                    Fail($"duplicate_accepted cargoId={dupId} balanceMoved={account.Balance - beforeDup}");
                }
            }

            // Story 1 最易返工的一条：ServerChangeScene 重载场景后，账本必须还在、钱必须是同一笔
            long beforeReload = account.Balance;
            int beforeAccepted = account.AcceptedCount;
            NetworkManager.singleton.ServerChangeScene("Night1");
            yield return new WaitForSeconds(2f);
            yield return WaitFor(() => TeamAccount.Instance != null, 10f, "account_lost_after_reload");

            TeamAccount after = TeamAccount.Instance;
            Debug.Log($"[S4A] S4A_RELOAD_CHECK before={beforeReload} after={(after != null ? after.Balance : -1)} instanceAlive={after != null} legIndex={(after != null ? after.LegIndex : -1)}");

            if (after == null)
            {
                Fail("account_lost_after_reload");
                yield break;
            }

            if (after.Balance != beforeReload || after.AcceptedCount != beforeAccepted)
            {
                Fail($"reload_changed_state before={beforeReload} after={after.Balance}");
            }

            account = after;

            int accepted = account.AcceptedCount;
            int unique = account.UniqueCargoCount;
            int expected = account.CommittedTotal;
            long actual = account.Balance;
            bool tallyOk = accepted == unique && expected == (int)actual && account.DupBlocked >= 1;
            bool clean = failures == 0;

            Debug.Log($"[S4A] S4A_DEPOSIT_TALLY accepted={accepted} uniqueCargo={unique} expectedSum={expected} actualSum={actual} dupBlocked={account.DupBlocked} failures={failures} ok={tallyOk && clean}");

            P0SaveService save = P0SaveService.Instance;
            string walletPath = save != null ? Path.Combine(save.SaveDir, P0SaveService.SavName) : "(memory)";
            Debug.Log($"[S4A] GATE_WALLET expected={expected} actual={actual} delta={(int)actual - expected} path={walletPath} persisted={TeamAccount.Persisted}");

            if (tallyOk && clean)
            {
                Debug.Log($"[S4A] S4A_STORY1_OK balance={actual} dupBlocked={account.DupBlocked} reloadSurvived=True");
            }
            else
            {
                Debug.LogError($"[S4A] S4A_STORY1_FAIL failures={failures} tallyOk={tallyOk}");
            }

            yield return new WaitForSeconds(0.3f);
            Application.Quit(tallyOk && clean ? 0 : 1);
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
