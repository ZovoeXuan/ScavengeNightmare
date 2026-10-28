using System.Collections;
using System.Collections.Generic;
using Mirror;
using UnityEngine;

namespace ScavengeNightmare.Networking
{
    /// <summary>
    /// D9 物件同步压测：生成 N 个联网物件（计划要求 20 / 40 两档），
    /// 让它们在服务端持续运动，同时量三类指标：
    ///   1. 带宽 —— 订阅 NetworkDiagnostics.OutMessageEvent，累加 bytes×count（count 是收件连接数，
    ///      只算 bytes 会严重低估真实上行）
    ///   2. 帧率 —— 服务端在 40 物件下的 fps，判断物理+同步是否成为瓶颈
    ///   3. 同步误差 —— 客户端与房主按统一 UTC 时钟对齐同一物件的位置差（沿用 D6.5 的方法，
    ///      这是唯一正确的跨进程对比方式：对齐墙钟，不对齐数组下标）
    ///
    /// 为什么必须让物件动起来：NetworkTransform 的 onlySyncOnChange 会跳过静止物件，
    /// 静止压测测出的是「几乎零流量」，对判断 40 物件能否扛住毫无意义。
    /// </summary>
    public sealed class D9StressTest : MonoBehaviour
    {
        private int targetCount;
        private bool host;
        private bool client;
        private readonly List<GameObject> spawned = new();
        private readonly List<Rigidbody> bodies = new();

        private double outBytesTotal;
        private double outMessagesTotal;
        private double inBytesTotal;

        /// <summary>
        /// 只累计「一份消息」的字节数，不含 count 倍增。
        /// 这才是真实网络负载的基准：上行 = singleCopy × 真实远程客户端数
        /// （Host 模式下本地连接不产生任何网络流量）。
        /// </summary>
        private double singleCopyBytes;

        private double measureStart;
        private float lastSampleTime;
        private double sampleSingleBytes;
        private double peakBps;

        private void Start()
        {
            string[] args = System.Environment.GetCommandLineArgs();
            targetCount = 0;

            foreach (string a in args)
            {
                if (a.StartsWith("-d9stress=", System.StringComparison.OrdinalIgnoreCase))
                {
                    int.TryParse(a.Substring("-d9stress=".Length), out targetCount);
                }
                else if (Eq(a, "-d9host")) host = true;
                else if (Eq(a, "-d9client")) client = true;
            }

            if (targetCount <= 0 || (!host && !client))
            {
                enabled = false;
                return;
            }

            // 压测必须锁帧：无头环境不限帧时 fps 会到几万，物理与同步的真实开销被完全掩盖（D3 起的老坑）
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 60;

            NetworkDiagnostics.OutMessageEvent += OnOutMessage;
            NetworkDiagnostics.InMessageEvent += OnInMessage;

            StartCoroutine(host ? RunHost() : RunClient());
        }

        private static bool Eq(string a, string b) => string.Equals(a, b, System.StringComparison.OrdinalIgnoreCase);

        private void OnOutMessage(NetworkDiagnostics.MessageInfo info)
        {
            // count 是这条消息发给了几个连接（含 Host 的本地连接）。
            // realNetwork = 单副本字节 × 远程客户端数；用 count 直接乘会把本地连接算进去，高估一倍。
            outBytesTotal += info.bytes * info.count;
            outMessagesTotal++;
            singleCopyBytes += info.bytes;
            sampleSingleBytes += info.bytes;
        }

        private void OnInMessage(NetworkDiagnostics.MessageInfo info)
        {
            inBytesTotal += info.bytes;
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

            if (NetworkClient.localPlayer == null)
            {
                Debug.LogError("[D9] D9_STRESS_FAIL reason=no_local_player");
                yield break;
            }

            // 等客户端进场再开始压测，否则只有一个收件人，带宽测不出多人放大的效果
            float connWait = 0f;
            while (NetworkServer.connections.Count < 2 && connWait < 25f)
            {
                connWait += Time.unscaledDeltaTime;
                yield return null;
            }

            Debug.Log($"[D9] D9_STRESS_START role=host target={targetCount} conns={NetworkServer.connections.Count}");

            SpawnProps();
            yield return new WaitForSeconds(1.5f);

            measureStart = Time.realtimeSinceStartupAsDouble;
            sampleSingleBytes = 0;
            peakBps = 0;
            lastSampleTime = Time.realtimeSinceStartup;
            outBytesTotal = 0;
            singleCopyBytes = 0;
            inBytesTotal = 0;
            outMessagesTotal = 0;

            // 持续驱动 15 秒，取稳态数据
            float end = Time.realtimeSinceStartup + 15f;
            int tick = 0;
            float nextProbeLog = 0f;
            while (Time.realtimeSinceStartup < end)
            {
                DriveProps(tick++);
                SampleBandwidth();

                // 每 0.5 秒记录锚点物件的位置与 UTC 时间戳，供与客户端按墙钟对齐比对同步误差
                if (Time.realtimeSinceStartup >= nextProbeLog)
                {
                    nextProbeLog = Time.realtimeSinceStartup + 0.5f;
                    long ms = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    // 两端必须用同一套锚点规则（最小 netId），否则对齐的是不同物件
                    Vector3 probe = ProbePos();
                    Debug.Log($"[D9] D9_HOST_PROBE t={ms} pos=({probe.x:F2},{probe.y:F2},{probe.z:F2})");
                }

                yield return new WaitForSeconds(0.1f);
            }

            double elapsed = Time.realtimeSinceStartupAsDouble - measureStart;
            float avgFps = FrameStats.AverageFps;

            // 真实上行口径：单副本字节 × 远程客户端数（Host 自己的本地连接不走网络）
            int remoteClients = Mathf.Max(0, NetworkServer.connections.Count - 1);
            double singleBps = elapsed > 0 ? singleCopyBytes / elapsed : 0;
            double realUpBps = singleBps * remoteClients;
            // 4 人局预估：同样负载下发给 3 个远程客户端
            double est4UpBps = singleBps * 3;

            Debug.Log($"[D9] D9_STRESS_HOST props={spawned.Count} conns={NetworkServer.connections.Count} " +
                      $"single_kbps={singleBps * 8 / 1024:F1} real_up_kbps={realUpBps * 8 / 1024:F1} " +
                      $"est4_up_kbps={est4UpBps * 8 / 1024:F1} peak_single_kbps={peakBps * 8 / 1024:F1} " +
                      $"msgs={outMessagesTotal:F0} fps={avgFps:F1} elapsed={elapsed:F1}s");

            // 成功线：40 物件下 60fps 目标至少要守住 45fps（75%），
            // 4 人局预估上行不超过 2500 kbps（约 2.5 Mbps，家用宽带上行与 Steam 中继都能承受）。
            bool fpsOk = avgFps >= 45f;
            bool bandwidthOk = est4UpBps * 8 / 1024 <= 2500;
            Debug.Log($"[D9] D9_STRESS_{(fpsOk && bandwidthOk ? "OK" : "FAIL")} props={spawned.Count} " +
                      $"fpsOk={fpsOk}({avgFps:F1}>=45) bandwidthOk={bandwidthOk}(est4={est4UpBps * 8 / 1024:F1}kbps<=2500)");

            yield return new WaitForSeconds(1f);
            Application.Quit(0);
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

            Debug.Log($"[D9] D9_STRESS_START role=client connected={NetworkClient.isConnected}");

            // 客户端只观察：记录可见物件数、收包带宽、以及目标物件的位置（供与房主按墙钟对齐比对）
            float end = Time.realtimeSinceStartup + 26f;
            int logged = 0;
            while (Time.realtimeSinceStartup < end && logged < 20)
            {
                yield return new WaitForSeconds(1f);
                logged++;

                int visible = 0;
                foreach (NetworkIdentity id in NetworkClient.spawned.Values)
                {
                    if (id != null && id.GetComponent<Gameplay.GrabbableProp>() != null) visible++;
                }

                long ms = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                Vector3 probe = ProbePos();
                // inBytesTotal 每秒重置一次，这里它就是「上一秒的下行字节」，直接换算 kbps
                double inKbps = inBytesTotal * 8 / 1024;
                Debug.Log($"[D9] D9_CLIENT_STATE t={ms} visibleProps={visible} in_kbps={inKbps:F1} " +
                          $"rtt_ms={NetworkTime.rtt * 1000.0:F0} quality={NetworkClient.connectionQuality} " +
                          $"probe=({probe.x:F2},{probe.y:F2},{probe.z:F2})");
                inBytesTotal = 0;
            }

            Debug.Log($"[D9] D9_STRESS_CLIENT_DONE");
            yield return new WaitForSeconds(0.5f);
            Application.Quit(0);
        }

        /// <summary>取 netId 最小的可抓物件，作为两端位置对比的锚点（房主与客户端都会记录它）。</summary>
        private Vector3 ProbePos()
        {
            uint best = uint.MaxValue;
            Vector3 pos = Vector3.zero;
            foreach (NetworkIdentity id in NetworkClient.spawned.Values)
            {
                if (id == null || id.GetComponent<Gameplay.GrabbableProp>() == null) continue;
                if (id.netId < best)
                {
                    best = id.netId;
                    pos = id.transform.position;
                }
            }

            return pos;
        }

        /// <summary>
        /// 生成 N 个联网物件，铺开在 20m×20m 区域，避免堆叠导致的物理爆炸。
        /// 复用 NetworkManager.spawnPrefabs 里已注册的 prefab（D6.5 装配过），保证 assetId 有效。
        /// </summary>
        private void SpawnProps()
        {
            NetworkManager nm = NetworkManager.singleton;
            if (nm == null || nm.spawnPrefabs == null || nm.spawnPrefabs.Count == 0)
            {
                Debug.LogError("[D9] D9_STRESS_FAIL reason=no_spawn_prefabs");
                return;
            }

            int cols = Mathf.CeilToInt(Mathf.Sqrt(targetCount));
            for (int i = 0; i < targetCount; i++)
            {
                GameObject prefab = nm.spawnPrefabs[i % nm.spawnPrefabs.Count];
                if (prefab == null) continue;

                int row = i / cols;
                int col = i % cols;
                Vector3 pos = new Vector3((col - cols * 0.5f) * 2.4f, 0.8f, (row - cols * 0.5f) * 2.4f);

                GameObject instance = Instantiate(prefab, pos, Quaternion.identity);
                instance.name = $"Stress_{i + 1}";
                NetworkServer.Spawn(instance);
                spawned.Add(instance);

                Rigidbody body = instance.GetComponent<Rigidbody>();
                if (body != null) bodies.Add(body);
            }

            Debug.Log($"[D9] D9_SPAWNED count={spawned.Count} requested={targetCount}");
        }

        /// <summary>
        /// 服务端持续驱动物件运动：交替给速度，制造真实的同步流量。
        /// 不用 AddForce 累积（会让物件飞出场地），而是直接设速度并周期性换向。
        /// </summary>
        private void DriveProps(int tick)
        {
            for (int i = 0; i < bodies.Count; i++)
            {
                Rigidbody body = bodies[i];
                if (body == null || body.isKinematic) continue;

                float phase = tick * 0.3f + i * 0.7f;
                Vector3 dir = new Vector3(Mathf.Sin(phase), 0f, Mathf.Cos(phase * 0.8f));
                body.linearVelocity = dir * 2.2f + Vector3.up * body.linearVelocity.y;

                // 拉回场地中心，防止跑出 200m 地面边界后无限下坠（D3 遇到过）
                if (body.position.magnitude > 22f)
                {
                    body.position = new Vector3(body.position.x * 0.5f, 1.2f, body.position.z * 0.5f);
                    body.linearVelocity = Vector3.zero;
                }
            }
        }

        private void SampleBandwidth()
        {
            float now = Time.realtimeSinceStartup;
            float dt = now - lastSampleTime;
            if (dt < 0.9f) return;

            double bps = sampleSingleBytes / dt;
            if (bps > peakBps) peakBps = bps;
            sampleSingleBytes = 0;
            lastSampleTime = now;
        }

        private void OnDestroy()
        {
            NetworkDiagnostics.OutMessageEvent -= OnOutMessage;
            NetworkDiagnostics.InMessageEvent -= OnInMessage;
        }

        /// <summary>帧率统计：只统计压测窗口内的平均帧率，避免把启动加载的低帧算进来。</summary>
        private static class FrameStats
        {
            private static int frames;
            private static float seconds;
            private static float lastTime;

            public static float AverageFps => seconds > 0.5f ? frames / seconds : 0f;

            [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
            private static void Hook()
            {
                lastTime = Time.realtimeSinceStartup;
            }

            public static void Tick()
            {
                float now = Time.realtimeSinceStartup;
                float dt = now - lastTime;
                lastTime = now;
                if (dt <= 0f || dt > 1f) return; // 跳过加载卡顿
                frames++;
                seconds += dt;
            }
        }

        private void Update()
        {
            if (host && spawned.Count > 0) FrameStats.Tick();
        }
    }
}
