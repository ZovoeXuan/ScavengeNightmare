using System;
using Mirror;
using UnityEngine;

namespace ScavengeNightmare.Networking
{
    /// <summary>
    /// D9 连接健康服务：断线检测、原因归类、连接质量、房主失联预警。
    ///
    /// 为什么不直接依赖 Mirror / KCP 的超时：
    ///   KcpTransport.Timeout 默认 10000ms，意味着房主硬杀进程后，客户端最长要等 10 秒才收到断开。
    ///   而 D9 验收要求「房主掉线后客户端 10 秒内收到提示」——光检测就吃满预算，提示必然超标。
    ///   因此这里做两件事：① 把 KCP Timeout 调到 4000ms；② 不等 transport，自己按
    ///   NetworkConnection.lastMessageTime 判定（PingInterval=0.1s，正常时每秒更新约 10 次），
    ///   3 秒无响应给预警、5 秒判定失联并主动断开。实测可在 ~5 秒内给出明确提示。
    ///
    /// 连接质量直接复用 Mirror v96 内建的 onConnectionQualityChanged（基于 rtt + jitter 分档），
    /// 比自己按 rtt 单指标分档更能反映真实可玩性，因此不重复实现。
    /// </summary>
    public sealed class NetworkHealthService : MonoBehaviour
    {
        public enum Health
        {
            Offline,      // 未联网
            Normal,       // 正常
            Warning,      // 无响应预警（还没断开）
            Disconnected  // 已断开，等待用户处理
        }

        [Header("超时调优（会在 Awake 写入 transport 与 NetworkManager）")]
        [Tooltip("KCP 无响应判定毫秒数。默认 10000 太长，压到 4000 以满足「10 秒内提示」")]
        [SerializeField] private int kcpTimeoutMs = 4000;

        [Tooltip("服务端踢掉不活跃连接的秒数。默认关闭，开启后避免僵尸连接占坑")]
        [SerializeField] private float serverInactiveTimeout = 20f;

        [Header("客户端失联检测")]
        [Tooltip("多久没收到任何包就给「连接不稳」预警")]
        [SerializeField] private float warningAfterSeconds = 3f;

        [Tooltip("多久没收到任何包就判定房主失联并主动断开")]
        [SerializeField] private float disconnectAfterSeconds = 5f;

        private Health health = Health.Offline;
        private string reason = string.Empty;
        private bool warned;
        private ConnectionQuality quality = ConnectionQuality.ESTIMATING;
        private bool qualityHooked;
        private bool sessionWasHost;

        /// <summary>
        /// 主动断开的抑制窗口。StopClient 的断开链路是「Transport.Disconnect → OnClientDisconnect」
        /// 的异步链，用布尔标记可能来不及消费就吞掉下一次真断线；用时间窗自动过期更安全。
        /// </summary>
        private float suppressDisconnectUntil;

        // 服务端视角：谁掉了、什么时候掉的，供 HUD 显示「XX 掉线了」
        private string lastLostPlayer = string.Empty;
        private float lastLostTime;

        public static NetworkHealthService Instance { get; private set; }

        public Health State => health;
        public string Reason => reason;
        public ConnectionQuality Quality => quality;
        public string LastLostPlayer => lastLostPlayer;
        public float SecondsSinceLost => lastLostTime > 0f ? Time.time - lastLostTime : -1f;
        public bool IsHost => NetworkServer.active && NetworkClient.active;
        public bool IsClientOnly => NetworkClient.active && !NetworkServer.active;

        /// <summary>断线瞬间触发，参数为原因文案。HUD 与自动化都订阅它。</summary>
        public event Action<string> Disconnected;

        /// <summary>连接质量变化（含首次估算完成）。</summary>
        public event Action<ConnectionQuality, ConnectionQuality> QualityChanged;

        private void Awake()
        {
            Instance = this;
            ApplyTimeoutTuning();
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }

            UnhookEvents();
        }

        /// <summary>
        /// 把超时参数写进 transport 与 NetworkManager。
        /// 必须在 StartHost/StartClient 之前生效——KcpTransport 在 ServerStart 时才把 Timeout 传给 KcpConfig。
        /// </summary>
        private void ApplyTimeoutTuning()
        {
            Transport transport = Transport.active;
            if (transport is kcp2k.KcpTransport kcp)
            {
                kcp.Timeout = kcpTimeoutMs;
                Debug.Log($"[D9] D9_KCP_TIMEOUT applied={kcpTimeoutMs}ms");
            }
            else
            {
                Debug.LogWarning($"[D9] transport 不是 KcpTransport（实际 {transport?.GetType().Name}），未调整超时");
            }

            NetworkManager nm = NetworkManager.singleton;
            if (nm != null)
            {
                nm.disconnectInactiveConnections = true;
                nm.disconnectInactiveTimeout = serverInactiveTimeout;
                NetworkServer.disconnectInactiveConnections = true;
                NetworkServer.disconnectInactiveTimeout = serverInactiveTimeout;
                Debug.Log($"[D9] D9_INACTIVE_KICK enabled={true} timeout={serverInactiveTimeout}s");
            }
        }

        private void Update()
        {
            if (!qualityHooked && NetworkClient.active)
            {
                NetworkClient.onConnectionQualityChanged += OnQualityChanged;
                NetworkClient.OnDisconnectedEvent += OnClientDisconnected;
                NetworkClient.OnErrorEvent += OnClientError;
                NetworkServer.OnDisconnectedEvent += OnServerDisconnected;
                sessionWasHost = NetworkServer.active && NetworkClient.active;
                qualityHooked = true;
            }

            if (qualityHooked && !NetworkClient.active && !NetworkServer.active)
            {
                UnhookEvents();
            }

            TrackClientResponsiveness();
        }

        private void UnhookEvents()
        {
            if (!qualityHooked)
            {
                return;
            }

            NetworkClient.onConnectionQualityChanged -= OnQualityChanged;
            NetworkClient.OnDisconnectedEvent -= OnClientDisconnected;
            NetworkClient.OnErrorEvent -= OnClientError;
            NetworkServer.OnDisconnectedEvent -= OnServerDisconnected;
            qualityHooked = false;
        }

        /// <summary>
        /// 客户端失联检测：靠 lastMessageTime 而不是等 transport 超时。
        /// Host 模式必须跳过——本地连接不走网络，lastMessageTime 不会推进，否则会误判自己失联。
        /// </summary>
        /// <summary>
        /// 静默时长 → 健康状态。抽成纯函数是为了能确定性验证边界：
        /// 真实网络里「网线松动 / 主机冻结」不发 RST，只能靠这条判定发现，
        /// 而它无法在 loopback 上稳定复现（杀进程会立即 RST，测到的是另一条路径）。
        /// </summary>
        public static Health EvaluateSilence(float silentSeconds, float warnAt, float disconnectAt)
        {
            if (silentSeconds >= disconnectAt)
            {
                return Health.Disconnected;
            }

            return silentSeconds >= warnAt ? Health.Warning : Health.Normal;
        }

        private void TrackClientResponsiveness()
        {
            if (!NetworkClient.active || NetworkClient.connection == null)
            {
                // 关键：已判定断线后不能再被重置成 Offline，否则 StopClient 的下一帧
                // 遮罩就会消失，玩家什么提示都看不到。断线态保留到用户点「返回大厅」。
                if (health != Health.Disconnected)
                {
                    health = NetworkServer.active ? Health.Normal : Health.Offline;
                }

                return;
            }

            if (IsHost)
            {
                health = Health.Normal;
                return;
            }

            if (!NetworkClient.isConnected)
            {
                return;
            }

            float silent = Time.time - NetworkClient.connection.lastMessageTime;
            Health next = EvaluateSilence(silent, warningAfterSeconds, disconnectAfterSeconds);

            if (next == Health.Disconnected)
            {
                EnterDisconnected($"房主失联：{silent:F0} 秒未收到任何数据");
                // 主动断开，否则客户端会一直停在「已连接」假象里
                NetworkManager.singleton?.StopClient();
                return;
            }

            if (next == Health.Warning)
            {
                if (!warned)
                {
                    warned = true;
                    Debug.Log($"[D9] D9_NO_RESPONSE_WARNING silent={silent:F1}s threshold={warningAfterSeconds}s");
                }

                health = Health.Warning;
                reason = $"{silent:F0} 秒未收到房主数据";
            }
            else
            {
                warned = false;
                reason = string.Empty;
                health = Health.Normal;
            }
        }

        private void OnQualityChanged(ConnectionQuality previous, ConnectionQuality next)
        {
            quality = next;
            Debug.Log($"[D9] D9_QUALITY previous={previous} current={next} rtt_ms={NetworkTime.rtt * 1000.0:F0} jitter_ms={NetworkTime.rttVariance * 1000.0:F0}");
            QualityChanged?.Invoke(previous, next);
        }

        private void OnClientDisconnected()
        {
            // 用户主动点「返回大厅」触发的断开，不当成断线（时间窗内有效，过期自动失效）
            if (Time.time < suppressDisconnectUntil)
            {
                health = Health.Offline;
                reason = string.Empty;
                return;
            }

            // 房主主动停服/退出时，本地会收到自己的 OnClientDisconnected。
            // 这不是「与房主断开」，弹全屏遮罩等于误报——房主视角只应看到「玩家掉线」横幅。
            if (sessionWasHost)
            {
                health = Health.Offline;
                reason = string.Empty;
                Debug.Log("[D9] D9_HOST_SESSION_ENDED local client disconnected（本机是房主，不提示断线）");
                return;
            }

            if (health == Health.Disconnected)
            {
                return; // 已经由失联检测报过，不重复覆盖原因
            }

            EnterDisconnected(string.IsNullOrEmpty(reason) ? "与服务器的连接已断开" : reason);
        }

        private void OnClientError(TransportError error, string message)
        {
            Debug.LogWarning($"[D9] D9_CLIENT_ERROR error={error} message={message}");

            // 主动断开时 transport 也可能抛 ConnectionClosed 之类的错误码，同属预期行为
            if (Time.time < suppressDisconnectUntil)
            {
                return;
            }

            // Timeout 类错误直接归因，其余保留原文，便于 D10 排查
            string text = error switch
            {
                TransportError.Timeout => "连接超时（网络中断或房主已退出）",
                TransportError.DnsResolve => "地址无法解析（检查 IP 或虚拟局域网是否连通）",
                TransportError.Refused => "连接被拒绝（房主未开服或端口被占用）",
                _ => $"网络错误：{error} {message}"
            };

            EnterDisconnected(text);
        }

        private void OnServerDisconnected(NetworkConnectionToClient conn)
        {
            string who = conn != null ? conn.address : "unknown";
            lastLostPlayer = who;
            lastLostTime = Time.time;
            Debug.Log($"[D9] D9_SERVER_PLAYER_LOST conn={conn?.connectionId} address={who} remaining={NetworkServer.connections.Count}");
        }

        private void EnterDisconnected(string text)
        {
            if (health == Health.Disconnected)
            {
                return;
            }

            health = Health.Disconnected;
            reason = text;

            // UTC 毫秒戳：自动化要靠它与「杀掉房主进程的时刻」相减，算出真实检测耗时
            long ms = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            Debug.LogError($"[D9] D9_DISCONNECTED t={ms} reason={text}");
            Disconnected?.Invoke(text);
        }

        /// <summary>用户点了「返回大厅」：清状态并停掉网络。房间本身由 Steam 层保留。</summary>
        public void ReturnToLobby()
        {
            Debug.Log("[D9] D9_RETURN_TO_LOBBY requested");

            // 主动断开不应被当成「断线」再弹一次遮罩
            suppressDisconnectUntil = Time.time + 3f;

            NetworkManager nm = NetworkManager.singleton;
            if (nm != null)
            {
                if (IsHost)
                {
                    nm.StopHost();
                }
                else if (NetworkClient.active)
                {
                    nm.StopClient();
                }
                else if (NetworkServer.active)
                {
                    nm.StopServer();
                }
            }

            health = Health.Offline;
            reason = string.Empty;
            warned = false;
            sessionWasHost = false;
            lastLostPlayer = string.Empty;
            lastLostTime = 0f;
        }

        /// <summary>质量的可读文案，HUD 直接显示。</summary>
        public string QualityText()
        {
            if (!NetworkClient.active || quality == ConnectionQuality.ESTIMATING)
            {
                return "评估中";
            }

            return quality switch
            {
                ConnectionQuality.EXCELLENT => "优（rtt ≤100ms）",
                ConnectionQuality.GOOD => "良（rtt ≤200ms）",
                ConnectionQuality.FAIR => "一般（rtt ≤400ms）",
                ConnectionQuality.POOR => "差（几乎不可玩）",
                _ => "评估中"
            };
        }

        public Color QualityColor()
        {
            return quality switch
            {
                ConnectionQuality.EXCELLENT => new Color(0.45f, 0.9f, 0.45f),
                ConnectionQuality.GOOD => new Color(0.9f, 0.9f, 0.4f),
                ConnectionQuality.FAIR => new Color(1f, 0.65f, 0.2f),
                ConnectionQuality.POOR => new Color(1f, 0.4f, 0.4f),
                _ => Color.gray
            };
        }
    }
}
