using System.Collections;
using System.Collections.Generic;
using Mirror;
using ScavengeNightmare.Economy;
using ScavengeNightmare.Monsters;
using ScavengeNightmare.Save;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ScavengeNightmare.Networking
{
    /// <summary>
    /// S4C-2 全队「继续」确认（双进程）：
    ///   Host：-s4clegclienthost
    ///   Client：-s4clegclient -address=127.0.0.1
    ///
    /// 逐关验证安全屋出口的两条规则：
    ///   1) 只有 1/2 人确认时**不换关**（反向验证 C3）——一个人到位就换关直接判 FAIL，
    ///      这正是 4A「房主代决」要防的假通过。
    ///   2) 全员确认后才推进，`S4C_CONTINUE_ADVANCE` 与关数必须同步前进。
    ///
    /// 夜路撤离仍按「任意一人」：由 Host 长按撤离门，Client 只负责安全屋确认。
    ///
    /// ⚠ 双人自动化只证明代码路径，**不等于 A2 通过**；A2 仍是真人门（P0 §六）。
    /// </summary>
    public sealed class S4CLegClientTest : MonoBehaviour
    {
        private static readonly Vector3 NightStand = new(-6f, 1.05f, -15.2f);
        private static readonly Vector3 SafehouseStand = new(0f, 1.05f, -4.6f);

        /// <summary>Host 看到「1/2 已确认」后等多久才去按门：必须长于 Client 自己的 2s 观察窗。</summary>
        private const float LoneReadyWindow = 2.5f;

        private readonly List<string> lines = new();

        private int failures;
        private TeamAccount account;

        private void Start()
        {
            bool host = S4ACli.Has("-s4clegclienthost");
            bool client = S4ACli.Has("-s4clegclient");

            if (!host && !client)
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
                S4ACli.SaveDirOverride = System.IO.Path.Combine(
                    Application.persistentDataPath,
                    host ? "S4C_test_legclient_host" : "S4C_test_legclient_client");
            }

            Application.logMessageReceived += OnLog;

            NetworkManager manager = NetworkManager.singleton;
            if (manager == null)
            {
                Debug.LogError($"[S4C] S4C_LEGCLIENT_FAIL reason=no_network_manager role={(host ? "host" : "client")}");
                Application.Quit(1);
                return;
            }

            StartCoroutine(Watchdog(420));

            try
            {
                if (host)
                {
                    manager.StartHost();
                    StartCoroutine(RunHost());
                }
                else
                {
                    string address = S4ACli.Value("-address=");
                    manager.networkAddress = string.IsNullOrEmpty(address) ? "127.0.0.1" : address;
                    manager.StartClient();
                    StartCoroutine(RunClient());
                }
            }
            catch (System.Exception ex)
            {
                // 双进程夹具最近在 Client 侧报过 SocketException：只打类型会让排查原地打转，
                // 这里必须把 message / stack 一起落盘，才能区分「端口没开」「地址解析」「本机 socket 资源」。
                Debug.LogError($"[S4C] S4C_LEGCLIENT_FAIL reason=start type={ex.GetType().Name} " +
                               $"msg={ex.Message} address={manager.networkAddress} stack={ex.StackTrace}");
                Application.Quit(1);
            }
        }

        private void OnDestroy()
        {
            Application.logMessageReceived -= OnLog;
            S4AInput.AutoInteract = false;
        }

        private void OnLog(string text, string stackTrace, LogType type)
        {
            lines.Add(text);

            if (lines.Count > 4000)
            {
                lines.RemoveRange(0, 2000);
            }
        }

        private IEnumerator Watchdog(int seconds)
        {
            yield return new WaitForSecondsRealtime(seconds);
            Debug.LogError($"[S4C] S4C_LEGCLIENT_WATCHDOG_TIMEOUT seconds={seconds}");
            Application.Quit(3);
        }

        private void Fail(string reason)
        {
            failures++;
            Debug.LogError($"[S4C] S4C_LEGCLIENT_TEST_FAIL reason={reason}");
        }

        private void Check(bool condition, string reason)
        {
            if (!condition)
            {
                Fail(reason);
            }
        }

        private bool HasLine(string fragment)
        {
            foreach (string line in lines)
            {
                if (line.Contains(fragment))
                {
                    return true;
                }
            }

            return false;
        }

        // ==================== Host ====================

        private IEnumerator RunHost()
        {
            yield return WaitFor(() => NetworkClient.localPlayer != null, 25f, "no_local_player");
            yield return WaitFor(() => TeamAccount.Instance != null, 25f, "no_team_account");

            account = TeamAccount.Instance;

            yield return WaitFor(HasRemotePlayer, 25f, "no_client_player");
            Debug.Log($"[S4C] S4C_LEGCLIENT_DUAL players={CountPlayers()}");

            // 开发机反复跑会带着上一次的 legIndex 回来：先归一化，否则第一关的断言从起点就错位。
            if (account.LegIndex != 1 || account.LegQuota != LegContentTable.Instance.QuotaFor(1))
            {
                if (!account.ServerEndRun("legclient_reset"))
                {
                    Fail("reset_run_failed");
                    Application.Quit(1);
                    yield break;
                }

                yield return new WaitForSeconds(0.6f);
            }

            int advances = 0;

            for (int leg = 1; leg <= 3; leg++)
            {
                yield return WaitFor(() => account.LegIndex == leg, 10f, $"leg_index_not_{leg}");

                // 怪物只用来证明梯度，不参与这条用例：留着会把站在撤离门前的 Host 打倒。
                FindFirst<MonsterSpawner>()?.ServerDespawnAll();

                yield return FillQuota(leg);
                yield return WaitFor(() => account.QuotaReached, 8f, $"quota_not_reached_leg{leg}");

                yield return HostExitNight();
                yield return WaitFor(() => SceneManager.GetActiveScene().name == "Safehouse", 15f, $"no_safehouse_leg{leg}");

                // 等 Client 单独确认。Host 不按门，这样 C3 的「1/2 不换关」才是真观察。
                yield return WaitFor(() => account.ContinueReady >= 1, 20f, $"client_never_ready_leg{leg}");

                LogHostDiag(leg);

                float loneAt = Time.realtimeSinceStartup;
                yield return new WaitForSeconds(LoneReadyWindow);

                bool stillSafehouse = SceneManager.GetActiveScene().name == "Safehouse";
                bool waitLogged = HasLine($"S4C_CONTINUE_WAIT leg={leg} reason=not_all_ready count=1/2");

                Debug.Log($"[S4C] S4C_LEGCLIENT_LONE leg={leg} ready={account.ContinueReady}/{account.ContinueTotal} " +
                          $"scene={SceneManager.GetActiveScene().name} stayed={stillSafehouse} waitLog={waitLogged} " +
                          $"seconds={(Time.realtimeSinceStartup - loneAt):F1}");

                Check(stillSafehouse, $"advanced_on_one_player leg={leg}");
                Check(waitLogged, $"continue_wait_missing leg={leg}");
                Check(account.ContinueReady < account.ContinueTotal, $"ready_count_wrong leg={leg}");

                // 现在 Host 也去确认，全队齐了才应该换关。
                yield return WaitFor(() => SceneManager.GetActiveScene().name == "Safehouse", 1f, "left_safehouse_early");

                S4AInput.AutoInteract = false;
                yield return new WaitForSeconds(0.8f);

                NetworkIdentity local = NetworkClient.localPlayer;
                if (local == null)
                {
                    Fail($"no_local_player_leg{leg}");
                    Application.Quit(1);
                    yield break;
                }

                Teleport(local.transform, SafehouseStand);
                yield return new WaitForSeconds(0.4f);
                S4AInput.AutoInteract = true;

                bool advanced = false;
                float t = 0f;
                float nextDiag = 0f;

                // 按住期间每帧把房主钉回门口：房主是服务端权威，位置靠它自己的物理推进，
                // 之前实测会从 5.5 滑到 0（离门 6m，直接掉出半径）甚至掉下地面。
                // 双人用例只验「确认逻辑」，站位必须由夹具保证，不能交给坡度和重力。
                while (t < 15f)
                {
                    t += Time.unscaledDeltaTime;

                    if (SceneManager.GetActiveScene().name != "Safehouse")
                    {
                        advanced = true;
                        break;
                    }

                    Teleport(local.transform, SafehouseStand);

                    if (t >= nextDiag)
                    {
                        nextDiag = t + 1f;
                        LogHostDiag(leg);
                    }

                    yield return null;
                }

                S4AInput.AutoInteract = false;

                Check(advanced, $"no_advance_after_all_ready leg={leg}");
                Check(HasLine($"S4C_CONTINUE_ADVANCE leg={leg} count=2/2"), $"advance_line_missing leg={leg}");

                yield return WaitFor(() => SceneManager.GetActiveScene().name == "Night1", 15f, $"no_night_after_leg{leg}");
                yield return WaitFor(() => account.LegIndex == leg + 1, 10f, $"leg_not_advanced_{leg}");

                advances++;
                Debug.Log($"[S4C] S4C_LEGCLIENT_STEP leg={leg} advanced={advanced} legIndex={account.LegIndex} " +
                          $"balance={account.Balance} advanceLog={HasLine($"S4C_CONTINUE_ADVANCE leg={leg}")}");
                yield return new WaitForSeconds(1.2f);
            }

            bool ok = failures == 0;

            if (ok)
            {
                Debug.Log($"[S4C] S4C_LEGCLIENT_OK legs=3 dual=1 advances={advances} " +
                          $"balance={account.Balance} ok=True");
            }
            else
            {
                Debug.LogError($"[S4C] S4C_LEGCLIENT_FAIL legs=3 dual=1 advances={advances} " +
                               $"balance={account.Balance} failures={failures}");
            }

            yield return new WaitForSeconds(0.3f);
            Application.Quit(ok ? 0 : 1);
        }

        /// <summary>夜路撤离：任意一人达标即可，由 Host 自己长按。</summary>
        private IEnumerator HostExitNight()
        {
            NetworkIdentity local = NetworkClient.localPlayer;
            if (local == null)
            {
                Fail("no_local_player_exit");
                yield break;
            }

            yield return new WaitForSeconds(0.6f);
            Teleport(local.transform, NightStand);
            yield return new WaitForSeconds(0.3f);
            S4AInput.AutoInteract = true;

            float t = 0f;
            while (SceneManager.GetActiveScene().name == "Night1" && t < 15f)
            {
                t += Time.unscaledDeltaTime;
                yield return null;
            }

            S4AInput.AutoInteract = false;

            if (SceneManager.GetActiveScene().name == "Night1")
            {
                Fail("night_exit_timeout");
            }
        }

        private IEnumerator FillQuota(int leg)
        {
            int missing = account.LegQuota - account.LegProgress;

            if (missing <= 0)
            {
                yield break;
            }

            // 直接入账而不是搬货：这条用例只验「换关确认」，搬运另有 4A 用例覆盖。
            account.DepositId($"legclient-{leg}-topup", missing);
            yield return new WaitForSeconds(0.4f);
        }

        // ==================== Client ====================

        private IEnumerator RunClient()
        {
            yield return WaitFor(() => NetworkClient.localPlayer != null, 25f, "no_local_player");
            yield return WaitFor(() => TeamAccount.Instance != null, 25f, "no_team_account");

            account = TeamAccount.Instance;
            Debug.Log($"[S4C] S4C_LEGCLIENT_CLIENT_READY netId={NetworkClient.localPlayer.netId}");

            int advances = 0;

            for (int leg = 1; leg <= 3; leg++)
            {
                yield return WaitFor(() => SceneManager.GetActiveScene().name == "Safehouse", 25f, $"no_safehouse_leg{leg}");

                // GameRoot 现在跨场景常驻，但换关瞬间仍可能先销毁再复制上来：
                // 每关重新取一次，避免拿着已销毁的旧引用断言（表现为 ready 恒为 0/0）。
                if (TeamAccount.Instance != null)
                {
                    account = TeamAccount.Instance;
                }

                // 路由会在换关后 0.6s 把玩家摆到入口锚点；先等它摆完再挪，否则会被拎回出生点。
                yield return new WaitForSeconds(1.8f);

                NetworkIdentity local = NetworkClient.localPlayer;
                if (local == null)
                {
                    Fail($"no_local_player_leg{leg}");
                    Application.Quit(1);
                    yield break;
                }

                Teleport(local.transform, SafehouseStand);
                yield return new WaitForSeconds(0.3f);
                S4AInput.AutoInteract = true;

                // 「自己按住」这条是整套 C3 的地基：服务端必须真的看到远端玩家在门口按住。
                // 8s 等待里每秒打一次现场数据，一旦没 latch，日志能直接说明卡在哪一层
                // （没发命令 / 服务端位置不对 / 账本复制没到 / 场景对象不在）。
                yield return WaitReadyWithDiag(leg, 8f);

                if (account == null || account.ContinueReady < 1)
                {
                    Fail($"self_ready_missing_leg{leg}");
                }

                // C3：只有自己一个人确认，门必须保持关闭。
                float loneAt = Time.realtimeSinceStartup;
                yield return new WaitForSeconds(2f);

                bool stillSafehouse = SceneManager.GetActiveScene().name == "Safehouse";
                Debug.Log($"[S4C] S4C_LEGCLIENT_CLIENT_LONE leg={leg} ready={account.ContinueReady}/{account.ContinueTotal} " +
                          $"scene={SceneManager.GetActiveScene().name} stayed={stillSafehouse} " +
                          $"seconds={(Time.realtimeSinceStartup - loneAt):F1}");

                Check(stillSafehouse, $"client_advanced_on_one_player leg={leg}");

                // 继续按住，等 Host 也确认。
                float t = 0f;
                while (SceneManager.GetActiveScene().name == "Safehouse" && t < 20f)
                {
                    t += Time.unscaledDeltaTime;

                    NetworkIdentity pinned = NetworkClient.localPlayer;
                    if (pinned != null)
                    {
                        Teleport(pinned.transform, SafehouseStand);
                    }

                    yield return null;
                }

                S4AInput.AutoInteract = false;

                bool advanced = SceneManager.GetActiveScene().name != "Safehouse";

                if (!advanced)
                {
                    Fail($"client_advance_timeout leg={leg}");
                    Application.Quit(1);
                    yield break;
                }

                advances++;
                Debug.Log($"[S4C] S4C_LEGCLIENT_CLIENT_STEP leg={leg} advances={advances} " +
                          $"seconds={t:F1} legIndex={account.LegIndex}");
                yield return new WaitForSeconds(1.2f);
            }

            bool ok = failures == 0;

            if (ok)
            {
                Debug.Log($"[S4C] S4C_LEGCLIENT_CLIENT_OK legs=3 advances={advances} ok=True");
            }
            else
            {
                Debug.LogError($"[S4C] S4C_LEGCLIENT_FAIL legs=3 advances={advances} failures={failures}");
            }

            yield return new WaitForSeconds(0.3f);
            Application.Quit(ok ? 0 : 1);
        }

        // ==================== 工具 ====================

        /// <summary>客户端在门口按住时每秒一次现场数据：latch 失败时能直接定位到哪一层没通。</summary>
        private IEnumerator WaitReadyWithDiag(int leg, float seconds)
        {
            float t = 0f;
            float next = 0f;

            while (t < seconds)
            {
                t += Time.unscaledDeltaTime;

                // 和房主一样每帧钉住：位置由夹具保证，不交给坡度和重力。
                NetworkIdentity local = NetworkClient.localPlayer;
                if (local != null)
                {
                    Teleport(local.transform, SafehouseStand);
                }

                if (t >= next)
                {
                    next = t + 1f;
                    LogClientDiag(leg, t);
                }

                if (account != null && account.ContinueReady >= 1)
                {
                    yield break;
                }

                yield return null;
            }
        }

        private void LogClientDiag(int leg, float held)
        {
            NetworkIdentity local = NetworkClient.localPlayer;
            PlayerInteract interact = local != null ? local.GetComponent<PlayerInteract>() : null;
            LevelPortal portal = LevelPortal.Instance;
            Vector3 pos = local != null ? local.transform.position : Vector3.zero;
            float dist = portal != null && local != null ? FlatDistance(pos, portal.transform.position) : -1f;

            Debug.Log($"[S4C] S4C_DIAG side=client leg={leg} t={held:F1} scene={SceneManager.GetActiveScene().name} " +
                      $"acctNull={(account == null ? 1 : 0)} acctNetId={(account != null ? account.netId : 0)} " +
                      $"acctClient={(account != null && account.isClient ? 1 : 0)} acctServer={(account != null && account.isServer ? 1 : 0)} " +
                      $"legIndex={(account != null ? account.LegIndex : -1)} balance={(account != null ? account.Balance : -1)} " +
                      $"ready={(account != null ? account.ContinueReady : -1)}/{(account != null ? account.ContinueTotal : -1)} " +
                      $"localNetId={(local != null ? local.netId : 0)} pos=({pos.x:F1},{pos.y:F1},{pos.z:F1}) " +
                      $"portal={(portal != null ? 1 : 0)} portalPos=({(portal != null ? portal.transform.position.x : 0f):F1},{(portal != null ? portal.transform.position.z : 0f):F1}) dist={dist:F1} " +
                      $"held={(S4AInput.InteractHeld ? 1 : 0)} serverHeld={(interact != null && interact.InteractHeldServer ? 1 : 0)} " +
                      $"clientReady={(NetworkClient.ready ? 1 : 0)} inter={((interact != null && interact.isLocalPlayer) ? 1 : 0)}");
        }

        /// <summary>服务端视角的现场数据：远端玩家的位置与交互态到底有没有到服务端。</summary>
        private void LogHostDiag(int leg)
        {
            LevelPortal portal = LevelPortal.Instance;
            int portalReady = portal != null ? portal.ReadyCount : -1;
            int portalPlayers = portal != null ? portal.PlayerCount : -1;

            foreach (PlayerMotor motor in FindObjectsByType<PlayerMotor>(FindObjectsSortMode.None))
            {
                if (motor == null)
                {
                    continue;
                }

                PlayerInteract interact = motor.GetComponent<PlayerInteract>();
                float dist = portal != null ? FlatDistance(motor.transform.position, portal.transform.position) : -1f;

                Debug.Log($"[S4C] S4C_DIAG side=host leg={leg} netId={motor.netId} hostPlayer={(motor.isLocalPlayer ? 1 : 0)} " +
                          $"pos=({motor.transform.position.x:F1},{motor.transform.position.y:F1},{motor.transform.position.z:F1}) " +
                          $"dist={dist:F1} held={(S4AInput.InteractHeld ? 1 : 0)} " +
                          $"serverHeld={(interact != null && interact.InteractHeldServer ? 1 : 0)} " +
                          $"ready={account.ContinueReady}/{account.ContinueTotal} " +
                          $"portalReady={portalReady}/{portalPlayers}");
            }
        }

        private bool HasRemotePlayer()
        {
            foreach (PlayerMotor motor in FindObjectsByType<PlayerMotor>(FindObjectsSortMode.None))
            {
                if (motor != null && !motor.isLocalPlayer)
                {
                    return true;
                }
            }

            return false;
        }

        private int CountPlayers()
        {
            return FindObjectsByType<PlayerMotor>(FindObjectsSortMode.None).Length;
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

        private static float FlatDistance(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;
            return Vector3.Distance(a, b);
        }

        /// <summary>
        /// 只改本地 transform，不碰 NetworkTransformReliable。
        /// 早先「先禁用再启用 NT」的写法是给服务端权威准备的：客户端权威（syncDirection=1）下
        /// 禁用 NT 会同时关掉「把位置变化推给服务端」的通道，服务端看到的还是出生点。
        /// </summary>
        private static void Teleport(Transform target, Vector3 position)
        {
            target.position = position;
        }

        private static T FindFirst<T>() where T : Object
        {
            T[] found = Object.FindObjectsByType<T>(FindObjectsSortMode.None);
            return found.Length > 0 ? found[0] : null;
        }
    }
}
