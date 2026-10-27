using System;
using System.Collections;
using System.Text;
using Mirror;
using kcp2k;
using UnityEngine;

namespace ScavengeNightmare.Networking
{
    /// <summary>
    /// D3 自动化与多开验证外壳（批处理 / 多实例测试专用，正常启动不生效）。
    ///
    /// 用法（构建包或编辑器均可，参数来自命令行）：
    ///   -d3server            启动为纯服务器
    ///   -d3client            启动为客户端并连接 -address（默认 127.0.0.1）
    ///   -address=192.168.1.10
    ///   -port=7777
    ///   -duration=20         运行 N 秒后自动退出（默认 20）
    ///   -automove            客户端自动向 +X 方向移动（用于验证位置同步）
    ///
    /// 输出标记：D3_SERVER_LISTENING / D3_CLIENT_CONNECTED / D3_STATE / D3_AUTORUN_DONE，
    /// 供批处理日志比对，不参与正式玩法。
    /// </summary>
    public sealed class D3AutoRunner : MonoBehaviour
    {
        private const string ServerFlag = "-d3server";
        private const string ClientFlag = "-d3client";

        private string role;
        private string address = "127.0.0.1";
        private ushort port = 7777;
        private float duration = 20f;
        private bool autoMove;
        private bool autoMoveBack;

        private float deadline;
        private float nextStateAt;
        private bool connectLogged;
        private bool automationApplied;
        private bool finished;

        private void Start()
        {
            string[] args = Environment.GetCommandLineArgs();

            if (HasFlag(args, ServerFlag))
            {
                role = "server";
            }
            else if (HasFlag(args, ClientFlag))
            {
                role = "client";
            }

            if (role == null)
            {
                enabled = false;
                return;
            }

            address = ReadValue(args, "-address=", address);
            port = (ushort)ReadInt(args, "-port=", port);
            duration = ReadInt(args, "-duration=", Mathf.RoundToInt(duration));
            autoMove = HasFlag(args, "-automove");
            // 多开测试时必须往外跑，否则 4 个胶囊体会在中心挤成一团互相卡住。
            autoMoveBack = HasFlag(args, "-automoveback");
            autoMove = HasFlag(args, "-automove") || autoMoveBack;

            deadline = Time.realtimeSinceStartup + duration;
            nextStateAt = Time.realtimeSinceStartup + 1f;

            // 批处理环境默认不限帧（实测 2-4 万 FPS），单帧位移远小于 CharacterController 的
            // skinWidth，会被物理系统吞掉，导致自动化测出的移动速度不具代表性。这里固定帧率。
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = ReadInt(args, "-fps=", 60);

            StartCoroutine(BeginNextFrame());
        }

        private IEnumerator BeginNextFrame()
        {
            // 等一帧，确保 NetworkManager / 场景对象 Awake 完成。
            yield return null;

            NetworkManager nm = NetworkManager.singleton;
            if (nm == null)
            {
                Debug.LogError("[D3] D3_AUTORUN_FAIL reason=no_network_manager");
                Finish(1);
                yield break;
            }

            KcpTransport kcp = nm.GetComponent<KcpTransport>();
            if (kcp != null)
            {
                kcp.port = port;
            }

            if (role == "server")
            {
                nm.StartServer();
                Debug.Log($"[D3] D3_SERVER_LISTENING port={port} maxConnections={nm.maxConnections}");
            }
            else
            {
                nm.networkAddress = address;
                nm.StartClient();
                Debug.Log($"[D3] D3_CLIENT_CONNECTING address={address} port={port}");
            }
        }

        private void Update()
        {
            if (role == null || finished)
            {
                return;
            }

            if (role == "client")
            {
                HandleClient();
            }

            if (Time.realtimeSinceStartup >= nextStateAt)
            {
                nextStateAt = Time.realtimeSinceStartup + 1f;
                LogState();
            }

            if (Time.realtimeSinceStartup >= deadline)
            {
                LogState();
                Finish(0);
            }
        }

        private void HandleClient()
        {
            if (!connectLogged && NetworkClient.isConnected)
            {
                connectLogged = true;
                Debug.Log("[D3] D3_CLIENT_CONNECTED");
            }

            if (!autoMove || automationApplied)
            {
                return;
            }

            NetworkIdentity local = NetworkClient.localPlayer;
            if (local == null)
            {
                return;
            }

            PlayerMotor motor = local.GetComponent<PlayerMotor>();
            if (motor != null)
            {
                // 以 +X 方向前进，用于验证服务端能否收到客户端权威的位置同步。
                motor.ApplyAutomationMove(new Vector2(0f, autoMoveBack ? -1f : 1f), false);
                automationApplied = true;
                Debug.Log($"[D3] D3_AUTOMOVE_STARTED direction={(autoMoveBack ? "backward" : "forward")}");
            }
        }

        private void LogState()
        {
            if (role == "server")
            {
                int conns = NetworkServer.active ? NetworkServer.connections.Count : 0;
                int players = NetworkServer.active ? NetworkServer.spawned.Count : 0;
                Debug.Log($"[D3] D3_STATE role=server conns={conns} players={players} fps={Fps():F1} pos={PositionsText()}");
                return;
            }

            double rttMs = NetworkClient.active ? NetworkTime.rtt * 1000.0 : 0.0;
            NetworkIdentity local = NetworkClient.localPlayer;
            Vector3 p = local != null ? local.transform.position : Vector3.zero;
            Debug.Log($"[D3] D3_STATE role=client connected={NetworkClient.isConnected} rtt_ms={rttMs:F1} fps={Fps():F1} pos=({p.x:F2},{p.y:F2},{p.z:F2}) posCount={PositionsText()}");
        }

        private static float Fps()
        {
            float dt = Time.smoothDeltaTime;
            return dt > 0f ? 1f / dt : 0f;
        }

        private static string PositionsText()
        {
            if (!NetworkServer.active)
            {
                return "[]";
            }

            StringBuilder sb = new StringBuilder("[");
            bool first = true;
            foreach (NetworkIdentity identity in NetworkServer.spawned.Values)
            {
                if (!first)
                {
                    sb.Append(',');
                }

                Vector3 p = identity.transform.position;
                sb.Append($"({p.x:F2},{p.y:F2},{p.z:F2})");
                first = false;
            }

            sb.Append(']');
            return sb.ToString();
        }

        private void Finish(int exitCode)
        {
            finished = true;
            Debug.Log($"[D3] D3_AUTORUN_DONE role={role} exit={exitCode}");
            if (NetworkServer.active || NetworkClient.active)
            {
                NetworkManager.singleton.StopHost();
            }

            StartCoroutine(QuitSoon(exitCode));
        }

        private IEnumerator QuitSoon(int exitCode)
        {
            yield return null;
            yield return null;
            Application.Quit(exitCode);
        }

        private static bool HasFlag(string[] args, string flag)
        {
            foreach (string a in args)
            {
                if (string.Equals(a, flag, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static string ReadValue(string[] args, string prefix, string fallback)
        {
            foreach (string a in args)
            {
                if (a.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return a.Substring(prefix.Length);
                }
            }

            return fallback;
        }

        private static int ReadInt(string[] args, string prefix, int fallback)
        {
            string raw = ReadValue(args, prefix, null);
            return raw != null && int.TryParse(raw, out int v) ? v : fallback;
        }
    }
}
