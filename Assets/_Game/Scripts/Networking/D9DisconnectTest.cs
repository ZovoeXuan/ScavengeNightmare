using System.Collections;
using Mirror;
using UnityEngine;

namespace ScavengeNightmare.Networking
{
    /// <summary>
    /// D9 断线检测专项验证。
    ///
    /// 压测测不到「房主进程被硬杀」这种情况，而它恰恰是 D9 验收的核心
    /// （房主掉线后客户端 10 秒内收到明确提示并可回大厅）。
    /// 因此这里做两端配合：
    ///   -d9dhost    房主：连上客户端后保持在线，直到被外部强杀（模拟崩溃/断电）
    ///   -d9dclient  客户端：连上后每秒打一条 UTC 心跳，收到断线事件时也打一条带 UTC 戳的日志
    ///
    /// 判定方式：外部脚本记录「杀掉房主进程的时刻」，再从客户端日志里找 D9_DISCONNECTED 的 UTC 戳，
    /// 两者相减就是真实检测耗时。不靠进程内计时，因为房主被杀后不可能自己上报死亡时刻。
    /// </summary>
    public sealed class D9DisconnectTest : MonoBehaviour
    {
        private bool host;
        private bool client;
        private bool logicOnly;

        [Tooltip("房主冻结秒数：主线程阻塞，模拟网线松动/主机冻结（进程存活但不发包）")]
        private float freezeSeconds;

        private void Start()
        {
            foreach (string a in System.Environment.GetCommandLineArgs())
            {
                if (Eq(a, "-d9dhost")) host = true;
                else if (Eq(a, "-d9dclient")) client = true;
                else if (Eq(a, "-d9dlogic")) logicOnly = true;
                else if (a.StartsWith("-d9dfreeze=", System.StringComparison.OrdinalIgnoreCase))
                {
                    float.TryParse(a.Substring("-d9dfreeze=".Length), out freezeSeconds);
                }
            }

            if (logicOnly)
            {
                RunLogicChecks();
                Application.Quit(0);
                return;
            }

            if (!host && !client)
            {
                enabled = false;
                return;
            }

            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 60;

            StartCoroutine(host ? RunHost() : RunClient());
        }

        private static bool Eq(string a, string b) => string.Equals(a, b, System.StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// 阈值判定的确定性验证。真实网络里「网线松动 / 主机冻结」不会发 RST，
        /// 客户端只能靠静默时长发现，而这条路径在 loopback 上无法复现
        /// （杀进程会立即 RST，走的是 transport 报错那条路）。所以单独断言边界。
        /// </summary>
        private static void RunLogicChecks()
        {
            const float warn = 3f;
            const float disconnect = 5f;
            int fail = 0;
            int passed = 0;

            void Check(bool ok, string name)
            {
                if (ok) { passed++; Debug.Log($"[D9] D9_LOGIC_OK {name}"); }
                else { fail++; Debug.LogError($"[D9] D9_LOGIC_FAIL {name}"); }
            }

            Check(NetworkHealthService.EvaluateSilence(0f, warn, disconnect) == NetworkHealthService.Health.Normal, "silent_0_normal");
            Check(NetworkHealthService.EvaluateSilence(2.99f, warn, disconnect) == NetworkHealthService.Health.Normal, "silent_2.99_normal");
            Check(NetworkHealthService.EvaluateSilence(3f, warn, disconnect) == NetworkHealthService.Health.Warning, "silent_3_warning");
            Check(NetworkHealthService.EvaluateSilence(4.99f, warn, disconnect) == NetworkHealthService.Health.Warning, "silent_4.99_warning");
            Check(NetworkHealthService.EvaluateSilence(5f, warn, disconnect) == NetworkHealthService.Health.Disconnected, "silent_5_disconnected");
            Check(NetworkHealthService.EvaluateSilence(60f, warn, disconnect) == NetworkHealthService.Health.Disconnected, "silent_60_disconnected");

            // 验收口径：检测阈值必须 < 10 秒，否则「10 秒内提示」不可能成立
            Check(disconnect < 10f, "disconnect_threshold_under_10s");

            Debug.Log(fail == 0
                ? $"[D9] D9_LOGIC_DONE all_passed={passed}"
                : $"[D9] D9_LOGIC_FAILED passed={passed} failed={fail}");
        }

        private IEnumerator RunHost()
        {
            NetworkManager.singleton.StartHost();

            float wait = 0f;
            while (NetworkClient.localPlayer == null && wait < 15f)
            {
                wait += Time.unscaledDeltaTime;
                yield return null;
            }

            Debug.Log($"[D9] D9_DHOST_READY t={NowMs()} conns={NetworkServer.connections.Count}");

            // 等客户端进场，然后什么都不做，等着被外部强杀
            float connWait = 0f;
            while (NetworkServer.connections.Count < 2 && connWait < 25f)
            {
                connWait += Time.unscaledDeltaTime;
                yield return null;
            }

            Debug.Log($"[D9] D9_DHOST_CLIENT_PRESENT t={NowMs()} conns={NetworkServer.connections.Count}");

            // 冻结模拟：主线程阻塞 → NetworkServer 停止 Tick → 一个包都不发，
            // 但进程与套接字都活着（不发 RST）。这正是「网线松动 / 主机假死」的样子，
            // 也是 watchdog 唯一能覆盖、而 transport RST 覆盖不到的路径。
            // 先等 3 秒让客户端完成生成握手，否则测的是「握手期断线」，不是稳态断线。
            if (freezeSeconds > 0f)
            {
                yield return new WaitForSeconds(3f);
                Debug.Log($"[D9] D9_DHOST_FREEZE_BEGIN t={NowMs()} seconds={freezeSeconds}");
                System.Threading.Thread.Sleep((int)(freezeSeconds * 1000f));
                Debug.Log($"[D9] D9_DHOST_FREEZE_END t={NowMs()}");
            }

            float end = Time.realtimeSinceStartup + 180f;
            while (Time.realtimeSinceStartup < end)
            {
                yield return new WaitForSeconds(1f);
            }
        }

        private IEnumerator RunClient()
        {
            NetworkManager.singleton.networkAddress = "127.0.0.1";
            NetworkManager.singleton.StartClient();

            float wait = 0f;
            while (NetworkClient.localPlayer == null && wait < 15f)
            {
                wait += Time.unscaledDeltaTime;
                yield return null;
            }

            if (NetworkClient.localPlayer == null)
            {
                Debug.LogError("[D9] D9_DCLIENT_FAIL reason=no_local_player");
                yield break;
            }

            Debug.Log($"[D9] D9_DCLIENT_CONNECTED t={NowMs()}");

            NetworkHealthService health = NetworkHealthService.Instance;
            if (health != null)
            {
                health.Disconnected += OnDisconnected;
            }

            // 每秒一条 UTC 心跳，便于外部脚本判断客户端在断线前一直活着
            float end = Time.realtimeSinceStartup + 120f;
            while (Time.realtimeSinceStartup < end)
            {
                string state = health != null ? health.State.ToString() : "no_service";
                Debug.Log($"[D9] D9_DCLIENT_ALIVE t={NowMs()} health={state} connected={NetworkClient.isConnected} rtt_ms={NetworkTime.rtt * 1000.0:F0}");
                yield return new WaitForSeconds(1f);

                // 断线后自行退出，避免外部脚本一直等
                if (health != null && health.State == NetworkHealthService.Health.Disconnected)
                {
                    yield return new WaitForSeconds(2f);
                    Application.Quit(0);
                    yield break;
                }
            }

            Application.Quit(0);
        }

        private void OnDisconnected(string reason)
        {
            Debug.LogError($"[D9] D9_DCLIENT_DISCONNECTED t={NowMs()} reason={reason}");
        }

        private static long NowMs() => System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }
}
