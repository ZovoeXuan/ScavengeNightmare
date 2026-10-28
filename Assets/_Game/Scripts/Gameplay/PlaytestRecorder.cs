using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Mirror;
using ScavengeNightmare.Networking;
using UnityEngine;

namespace ScavengeNightmare.Gameplay
{
    /// <summary>
    /// D7 朋友局实测记录器（仅服务端 / 房主生效）。
    ///
    /// 存在的理由：D7 的交付物是「≥10 条记录」，靠事后回忆一定写成印象流。
    /// 这个类把三类事实同时落盘，让复盘能对着录屏时间轴逐条核对：
    ///   1. 人工标记 —— 玩家按 F5/F6/F7（笑点 / 问题 / 金句），附当时的上下文快照
    ///   2. 摩擦事件 —— 订阅 FrictionEvents，五条摩擦各自自动记一行
    ///   3. 动作计数 —— 抓取 / 投掷 / 在线人数心跳，用于算「局均笑点」与事件密度
    ///
    /// 落盘位置：可执行文件同级 PlaytestLogs/session_yyyyMMdd_HHmmss.csv。
    /// 每 45 秒整表重写一次（数据量很小），避免测试中途崩溃丢数据。
    /// </summary>
    public sealed class PlaytestRecorder : MonoBehaviour
    {
        public const int CatLaugh = 0;
        public const int CatIssue = 1;
        public const int CatQuote = 2;

        private const string Header = "seq,local_time,epoch_ms,elapsed_s,type,player,detail,x,y,z";

        public static PlaytestRecorder Instance { get; private set; }

        [Header("节奏")]
        [Tooltip("心跳间隔（秒）：定期记录在线人数与帧率，便于判断局面冷热")]
        [SerializeField] private float heartbeatSeconds = 30f;

        [Tooltip("整表落盘间隔（秒）")]
        [SerializeField] private float flushSeconds = 45f;

        private readonly List<string> rows = new();
        private readonly Dictionary<uint, string> labels = new();
        private int nextGuestIndex = 1;
        private readonly Dictionary<string, int> laughByPlayer = new();

        private bool started;
        private string filePath;
        private DateTime startLocal;
        private float startRealtime;
        private float nextHeartbeatAt;
        private float nextFlushAt;
        private int seq;
        private int laughCount;
        private int issueCount;
        private int quoteCount;
        private int grabCount;
        private int throwCount;

        public bool Active => started;
        public string FilePath => filePath;
        public int LaughCount => laughCount;
        public int IssueCount => issueCount;
        public int QuoteCount => quoteCount;

        private void Awake()
        {
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void Update()
        {
            if (!started)
            {
                // 房主点 Host 之后 NetworkServer 才 active；每帧探测一次，不用回调是因为
                // 这个组件刻意不挂 NetworkBehaviour（NetworkManager 对象上没有 NetworkIdentity）。
                if (NetworkServer.active && NetworkServer.localConnection != null)
                {
                    Begin();
                }

                return;
            }

            float now = Time.realtimeSinceStartup;

            if (now >= nextHeartbeatAt)
            {
                nextHeartbeatAt = now + heartbeatSeconds;
                Heartbeat();
            }

            if (now >= nextFlushAt)
            {
                nextFlushAt = now + flushSeconds;
                Flush();
            }
        }

        private void OnApplicationQuit()
        {
            End();
        }

        private void Begin()
        {
            started = true;
            startLocal = DateTime.Now;
            startRealtime = Time.realtimeSinceStartup;
            nextHeartbeatAt = startRealtime + heartbeatSeconds;
            nextFlushAt = startRealtime + flushSeconds;

            string dir = Path.Combine(ExecutableRoot(), "PlaytestLogs");
            Directory.CreateDirectory(dir);
            filePath = Path.Combine(dir, $"session_{startLocal:yyyyMMdd_HHmmss}.csv");

            FeelSettings.Changed += OnFeelChanged;
            FrictionEvents.Reported += OnFriction;
            NetworkServer.OnConnectedEvent += OnPlayerJoin;
            NetworkServer.OnDisconnectedEvent += OnPlayerLeave;

            Add("session_start", "-",
                $"feelscheme={FeelSettings.ActiveId} version={Application.version} unity={Application.unityVersion} screen={Screen.width}x{Screen.height}",
                Vector3.zero);

            // 记录器先于玩家生成启动，此时方案还没应用：等 FeelSwitchHUD 选完，这里补一条
            if (FeelSettings.Active != null)
            {
                Add("feel", "-", $"scheme={FeelSettings.ActiveId}", Vector3.zero);
            }

            Debug.Log($"[PT] D7_RECORDER_START file={filePath} feelscheme={FeelSettings.ActiveId}");
            Flush();
        }

        private void End()
        {
            if (!started)
            {
                return;
            }

            started = false;

            FeelSettings.Changed -= OnFeelChanged;
            FrictionEvents.Reported -= OnFriction;
            NetworkServer.OnConnectedEvent -= OnPlayerJoin;
            NetworkServer.OnDisconnectedEvent -= OnPlayerLeave;

            float duration = Time.realtimeSinceStartup - startRealtime;
            string perPlayer = laughByPlayer.Count == 0 ? "-" : string.Join("/", laughByPlayer);
            Add("session_end", "-",
                $"duration={duration:F0}s feelscheme={FeelSettings.ActiveId} laughs={laughCount} issues={issueCount} quotes={quoteCount} grabs={grabCount} throws={throwCount} laughsByPlayer={perPlayer}",
                Vector3.zero);

            Debug.Log($"[PT] D7_RECORDER_END duration={duration:F0}s laughs={laughCount} issues={issueCount} quotes={quoteCount} grabs={grabCount} throws={throwCount}");

            Flush();
        }

        // ---------- 对外接口 ----------

        /// <summary>玩家标记入口（由 PlaytestMarker 的 Command 调到服务端）。</summary>
        public void Mark(int category, NetworkIdentity player, string teamNote)
        {
            if (!started)
            {
                return;
            }

            string label = LabelOf(player);
            Vector3 pos = player != null ? player.transform.position : Vector3.zero;

            string type;
            switch (category)
            {
                case CatLaugh:
                    type = "laugh";
                    laughCount++;
                    laughByPlayer.TryGetValue(label, out int n);
                    laughByPlayer[label] = n + 1;
                    break;
                case CatIssue:
                    type = "issue";
                    issueCount++;
                    break;
                default:
                    type = "quote";
                    quoteCount++;
                    break;
            }

            string detail = string.IsNullOrEmpty(teamNote)
                ? ContextOf(player)
                : $"{teamNote} | {ContextOf(player)}";
            Add(type, label, detail, pos);
            Flush();
        }

        public void NoteGrab(string label, float mass)
        {
            if (!started)
            {
                return;
            }

            grabCount++;
            Add("grab", label, $"mass={mass:F1}kg", Vector3.zero);
        }

        public void NoteThrow(string label, float mass, float speed)
        {
            if (!started)
            {
                return;
            }

            throwCount++;
            Add("throw", label, $"mass={mass:F1}kg speed={speed:F1}m/s", Vector3.zero);
        }

        /// <summary>把 netId 映射为稳定代号 P1..P4，方便复盘时口头对应「P2 就是老三」。</summary>
        public string LabelOf(NetworkIdentity id)
        {
            if (id == null)
            {
                return "-";
            }

            // 房主固定 P1：复盘时「P1 就是我」比按首次动作排序直观得多
            if (NetworkServer.localConnection != null && id.connectionToClient == NetworkServer.localConnection)
            {
                return "P1";
            }

            if (labels.TryGetValue(id.netId, out string label))
            {
                return label;
            }

            label = $"P{++nextGuestIndex}";
            labels[id.netId] = label;
            return label;
        }

        // ---------- 内部 ----------

        private void OnFeelChanged(FeelScheme scheme)
        {
            Add("feel", "-", $"scheme={(scheme != null ? scheme.schemeId : "null")}", Vector3.zero);
        }

        private void OnFriction(string code, string message)
        {
            Add("friction", "-", $"{code}: {message}", Vector3.zero);
        }

        private void OnPlayerJoin(NetworkConnectionToClient conn)
        {
            Add("player_join", "-", $"conn={conn.connectionId} players={NetworkServer.connections.Count}", Vector3.zero);
        }

        private void OnPlayerLeave(NetworkConnectionToClient conn)
        {
            Add("player_leave", "-", $"conn={conn.connectionId} players={NetworkServer.connections.Count}", Vector3.zero);
        }

        private void Heartbeat()
        {
            int props = UnityEngine.Object.FindObjectsByType<GrabbableProp>(FindObjectsSortMode.None).Length;
            float fps = Time.smoothDeltaTime > 0f ? 1f / Time.smoothDeltaTime : 0f;
            Add("heartbeat", "-",
                $"players={NetworkServer.connections.Count} props={props} fps={fps:F0} hold={HoldingCount()} laughs={laughCount} issues={issueCount}",
                Vector3.zero);
        }

        private int HoldingCount()
        {
            int n = 0;
            foreach (GrabController g in UnityEngine.Object.FindObjectsByType<GrabController>(FindObjectsSortMode.None))
            {
                if (g.IsHolding)
                {
                    n++;
                }
            }

            return n;
        }

        /// <summary>标记时的现场快照：手里多重、跑多快、三米内几个物件、刚发生的摩擦。</summary>
        private string ContextOf(NetworkIdentity player)
        {
            if (player == null)
            {
                return "player=null";
            }

            GrabController grab = player.GetComponent<GrabController>();
            string held = grab != null && grab.IsHolding ? $"{grab.HeldMass:F1}kg" : "none";

            PlayerMotor motion = player.GetComponent<PlayerMotor>();
            float speed = motion != null ? motion.CurrentPlanarSpeed : 0f;

            int nearby = 0;
            foreach (GrabbableProp prop in UnityEngine.Object.FindObjectsByType<GrabbableProp>(FindObjectsSortMode.None))
            {
                if (Vector3.Distance(prop.transform.position, player.transform.position) <= 3f)
                {
                    nearby++;
                }
            }

            string recent = FrictionEvents.Recent.Count > 0
                ? FrictionEvents.Recent[FrictionEvents.Recent.Count - 1]
                : "none";

            return $"held={held} speed={speed:F1}m/s nearby={nearby} last={recent}";
        }

        private void PlayerRefHolder()
        {
        }

        private void Add(string type, string player, string detail, Vector3 pos)
        {
            float elapsed = Time.realtimeSinceStartup - startRealtime;
            long epochMs = DateTimeOffset.Now.ToUnixTimeMilliseconds();
            string time = DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);

            rows.Add(string.Join(",",
                (++seq).ToString(CultureInfo.InvariantCulture),
                time,
                epochMs.ToString(CultureInfo.InvariantCulture),
                elapsed.ToString("F1", CultureInfo.InvariantCulture),
                Escape(type),
                Escape(player),
                Escape(detail),
                pos.x.ToString("F2", CultureInfo.InvariantCulture),
                pos.y.ToString("F2", CultureInfo.InvariantCulture),
                pos.z.ToString("F2", CultureInfo.InvariantCulture)));
        }

        private void Flush()
        {
            if (string.IsNullOrEmpty(filePath))
            {
                return;
            }

            StringBuilder sb = new(Header.Length + rows.Count * 96);
            sb.AppendLine(Header);
            foreach (string row in rows)
            {
                sb.AppendLine(row);
            }

            try
            {
                // UTF-8 带 BOM，Excel 直接双击打开不乱码
                File.WriteAllText(filePath, sb.ToString(), new UTF8Encoding(true));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[PT] 写入实测日志失败：{e.Message}");
            }
        }

        private static string ExecutableRoot()
        {
            // 编辑器下 dataPath = <project>/Assets，取上一级；构建包下 = <exe>_Data，同样取上一级
            return Path.GetDirectoryName(Application.dataPath) ?? Application.dataPath;
        }

        private static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            string v = value.Replace(',', ';').Replace('\n', ' ').Replace('\r', ' ');
            if (v.Contains("\""))
            {
                v = v.Replace("\"", "'");
            }

            return v;
        }
    }
}
