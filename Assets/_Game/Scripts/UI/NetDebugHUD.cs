using Mirror;
using ScavengeNightmare.Networking;
using UnityEngine;

namespace ScavengeNightmare.UI
{
    /// <summary>
    /// D3 联机调试 HUD（临时，D32 替换正式 UI）。
    /// 提供 Host / Client / Stop 入口、地址输入，并显示延迟（RTT）、连接数与本地玩家坐标，
    /// 用于验证「4 客户端同步移动 + 延迟可见」这条 D3 验收标准。
    /// </summary>
    public sealed class NetDebugHUD : MonoBehaviour
    {
        [SerializeField] private bool showOnStart = true;
        [SerializeField] private string address = "127.0.0.1";

        private bool visible;
        private string addressInput;
        private GUIStyle boxStyle;
        private GUIStyle labelStyle;

        private void Awake()
        {
            visible = showOnStart;
            addressInput = address;

            if (NetworkManager.singleton != null && !string.IsNullOrEmpty(NetworkManager.singleton.networkAddress))
            {
                addressInput = NetworkManager.singleton.networkAddress;
            }
        }

        private void Update()
        {
            // F1 收起/展开，避免挡住验收录像。
            KeyboardShortcut();
        }

        private void KeyboardShortcut()
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && kb.f1Key.wasPressedThisFrame)
            {
                visible = !visible;
            }
        }

        private void OnGUI()
        {
            if (!visible)
            {
                return;
            }

            EnsureStyles();

            // 位置让给 TraitHUD（左上角第一格），本面板在其下方。
            // D9 前三个面板都挤在 (12,12) 完全重叠，身份面板会被联机面板盖住。
            GUILayout.BeginArea(new Rect(12f, 130f, 320f, 268f), boxStyle);
            GUILayout.Label("<b>联机调试</b>（F1 收起）", labelStyle);
            GUILayout.Label(ModeText(), labelStyle);
            GUILayout.Label($"RTT：{RttText()}", labelStyle);
            GUILayout.Label($"质量：{QualityText()}", labelStyle);
            GUILayout.Label($"连接数：{ConnectionText()}", labelStyle);
            GUILayout.Label($"本地玩家：{LocalPlayerText()}", labelStyle);
            GUILayout.Space(6f);

            NetworkManager nm = NetworkManager.singleton;
            bool running = NetworkServer.active || NetworkClient.active;

            if (!running)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("地址", labelStyle, GUILayout.Width(32f));
                addressInput = GUILayout.TextField(string.IsNullOrEmpty(addressInput) ? "127.0.0.1" : addressInput, GUILayout.Width(170f));
                GUILayout.EndHorizontal();

                if (GUILayout.Button("Host（服务器+客户端）"))
                {
                    nm.networkAddress = addressInput;
                    nm.StartHost();
                }

                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Client"))
                {
                    nm.networkAddress = addressInput;
                    nm.StartClient();
                }

                if (GUILayout.Button("Server Only"))
                {
                    nm.StartServer();
                }

                GUILayout.EndHorizontal();
            }
            else
            {
                if (GUILayout.Button("停止"))
                {
                    nm.StopHost();
                }
            }

            GUILayout.Label("F1 收起面板", labelStyle);
            GUILayout.EndArea();
        }

        private void EnsureStyles()
        {
            if (boxStyle != null)
            {
                return;
            }

            boxStyle = new GUIStyle(GUI.skin.box)
            {
                padding = new RectOffset(10, 10, 8, 8),
                alignment = TextAnchor.UpperLeft
            };

            labelStyle = new GUIStyle(GUI.skin.label)
            {
                richText = true,
                fontSize = 12
            };
        }

        private static string ModeText()
        {
            if (NetworkServer.active && NetworkClient.active)
            {
                return "模式：Host（房主权威）";
            }

            if (NetworkServer.active)
            {
                return "模式：Server Only";
            }

            if (NetworkClient.active)
            {
                return NetworkClient.isConnected ? "模式：Client（已连接）" : "模式：Client（连接中）";
            }

            return "模式：未启动";
        }

        private static string RttText()
        {
            if (!NetworkClient.active)
            {
                return "— （仅客户端有值）";
            }

            // NetworkTime.rtt 是本地测得的往返时延（秒）。
            double rtt = NetworkTime.rtt;
            if (rtt <= 0)
            {
                return "测量中";
            }

            return $"{Mathf.RoundToInt((float)(rtt * 1000.0))} ms（抖动 {Mathf.RoundToInt((float)(NetworkTime.rttVariance * 1000.0))} ms）";
        }

        /// <summary>D9：连接质量取自 NetworkHealthService（底层是 Mirror 内建的 rtt+jitter 分档）。</summary>
        private static string QualityText()
        {
            NetworkHealthService health = NetworkHealthService.Instance;
            if (health == null)
            {
                return "— （未挂载健康服务）";
            }

            Color c = health.QualityColor();
            string hex = ColorUtility.ToHtmlStringRGB(c);
            string text = health.QualityText();

            // Host 模式本地连接没有 rtt，质量分档无意义，直接说明
            if (health.IsHost)
            {
                return "本地房主（无网络延迟）";
            }

            return $"<color=#{hex}>{text}</color>";
        }

        private static string ConnectionText()
        {
            if (NetworkServer.active)
            {
                return $"{NetworkServer.connections.Count} / {NetworkManager.singleton.maxConnections}";
            }

            if (NetworkClient.active)
            {
                return NetworkClient.isConnected ? "1 / 1（已连接）" : "0 / 1（连接中）";
            }

            return "0";
        }

        private static string LocalPlayerText()
        {
            NetworkIdentity local = NetworkClient.localPlayer;
            if (local == null)
            {
                return "未生成";
            }

            Vector3 p = local.transform.position;
            return $"({p.x:F2}, {p.y:F2}, {p.z:F2})";
        }
    }
}
