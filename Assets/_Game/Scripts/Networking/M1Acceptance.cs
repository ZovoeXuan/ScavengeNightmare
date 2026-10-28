using System.Collections;
using System.Collections.Generic;
using Mirror;
using ScavengeNightmare.Gameplay;
using UnityEngine;

namespace ScavengeNightmare.Networking
{
    /// <summary>
    /// D11 · M1 验收量化工具。
    ///
    /// M1 的五条标准里，三条可以量化、两条必须人判。这个工具只做能量化的部分，
    /// 并且把「能量化到什么程度」写清楚，不做「跑完就算通过」的糊弄：
    ///
    ///   1. 抓扔有重量感  → -m1weight：测 4 档质量的投掷速度与负重减速，验证单调性与区分度
    ///   2. 延迟不致命    → -m1latency=200：注入 200ms RTT，测同步误差与抓取可用性
    ///   3. 4 人同步无崩溃 → 复用 D3 四人用例（本工具不重复实现）
    ///   4. 摩擦 ≥5 条     → 复用 D6 用例（本工具不重复实现）
    ///   5. 缺点牌有笑点   → 无法量化，必须真人（D7），本工具明确不覆盖
    /// </summary>
    public sealed class M1Acceptance : MonoBehaviour
    {
        private bool weightMode;
        private bool latencyHost;
        private bool latencyClient;
        private float latencyMs;

        private void Start()
        {
            foreach (string a in System.Environment.GetCommandLineArgs())
            {
                if (Eq(a, "-m1weight")) weightMode = true;
                else if (a.StartsWith("-m1lathost=", System.StringComparison.OrdinalIgnoreCase))
                {
                    latencyHost = true;
                    float.TryParse(a.Substring("-m1lathost=".Length), out latencyMs);
                }
                else if (a.StartsWith("-m1latclient=", System.StringComparison.OrdinalIgnoreCase))
                {
                    latencyClient = true;
                    float.TryParse(a.Substring("-m1latclient=".Length), out latencyMs);
                }
            }

            if (!weightMode && !latencyHost && !latencyClient)
            {
                enabled = false;
                return;
            }

            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 60;

            // 重量感与延迟都必须在中性身份下测，否则大力士/灵敏会污染对比（D8 踩过）
            Traits.TraitSettings.ForceNoTraits = true;

            if (latencyHost || latencyClient)
            {
                ApplyLatencySimulation();
            }

            if (weightMode)
            {
                StartCoroutine(RunWeightProbe());
            }
            else if (latencyHost)
            {
                StartCoroutine(RunLatencyHost());
            }
            else
            {
                StartCoroutine(RunLatencyClient());
            }
        }

        private static bool Eq(string a, string b) => string.Equals(a, b, System.StringComparison.OrdinalIgnoreCase);

        // ==================== 重量感 ====================

        private IEnumerator RunWeightProbe()
        {
            NetworkManager.singleton.StartHost();

            float wait = 0f;
            while (NetworkClient.localPlayer == null && wait < 15f)
            {
                wait += Time.unscaledDeltaTime;
                yield return null;
            }

            NetworkIdentity player = NetworkClient.localPlayer;
            if (player == null)
            {
                Debug.LogError("[M1] M1_WEIGHT_FAIL reason=no_local_player");
                yield break;
            }

            PlayerMotor motor = player.GetComponent<PlayerMotor>();
            GrabController grab = player.GetComponent<GrabController>();
            grab.SetInputEnabled(false);

            Debug.Log("[M1] M1_WEIGHT_START 测量 4 档质量：投掷速度 + 负重减速");

            // 空手基准速度
            yield return MeasureCarry(motor, 0f, 0f);
            float emptySpeed = lastMeasuredSpeed;

            // 4 档质量：2kg 罐头 / 9kg 纸箱 / 22kg 圆桶 / 42kg 雕像
            float[] masses = { 2f, 9f, 22f, 42f };
            List<float> throwSpeeds = new();
            List<float> carrySpeeds = new();

            foreach (float mass in masses)
            {
                GrabbableProp target = FindPropByMass(mass);
                if (target == null)
                {
                    Debug.LogWarning($"[M1] M1_WEIGHT_SKIP mass={mass} 没有该质量的物件");
                    continue;
                }

                // 把物件瞬移到持物点，而不是把玩家挪过去。
                // 原因：服务端的持物目标 = 玩家前方 holdDistance + 抬高，若物件还在原地，
                // 首帧 delta 就超过 breakDistance 立即脱手（首版探针就是这么测出 throw=0 的）。
                // D4 当年也是「在玩家前方生成物件」，同一个道理。
                Vector3 holdPoint = player.transform.position
                                    + player.transform.forward * 1.5f
                                    + Vector3.up * 0.5f;
                target.Body.position = holdPoint;
                target.Body.linearVelocity = Vector3.zero;
                target.Body.angularVelocity = Vector3.zero;
                target.Body.WakeUp();
                yield return new WaitForFixedUpdate();
                yield return null;

                bool grabbed = grab.TryGrab(target);
                yield return new WaitForSeconds(0.35f);

                if (!grabbed || !grab.IsHolding)
                {
                    Debug.LogWarning($"[M1] M1_WEIGHT_SKIP mass={mass} 抓取未保持（grabbed={grabbed} holding={grab.IsHolding}）");
                    continue;
                }

                // 负重下的移动速度
                yield return MeasureCarry(motor, mass, target.Mass);
                float carrySpeed = lastMeasuredSpeed;
                carrySpeeds.Add(carrySpeed);

                // 投掷速度：松手瞬间读刚体速度（服务端在 ServerRelease 里设 linearVelocity）
                grab.Release(true);
                yield return new WaitForFixedUpdate();
                float throwSpeed = target.Body != null ? target.Body.linearVelocity.magnitude : 0f;
                throwSpeeds.Add(throwSpeed);

                float slowdown = emptySpeed > 0.01f ? (1f - carrySpeed / emptySpeed) * 100f : 0f;
                Debug.Log($"[M1] M1_WEIGHT mass={mass:F0}kg throw={throwSpeed:F2}m/s carry={carrySpeed:F2}m/s slowdown={slowdown:F1}%");

                // 把扔出去的物件收走：否则它会砸到自己触发「手滑砸脚」硬直，
                // 污染下一档的移动速度测量（这条是实测踩出来的，不是预防性编码）
                target.Body.position = new Vector3(0f, 0.5f, -40f);
                target.Body.linearVelocity = Vector3.zero;
                target.Body.angularVelocity = Vector3.zero;

                yield return new WaitForSeconds(0.5f);
                motor.ClearAutomationMove();
            }

            // 判定
            bool throwMonotonic = IsMonotonicDecreasing(throwSpeeds);
            bool carryMonotonic = IsMonotonicDecreasing(carrySpeeds);
            float throwSpread = throwSpeeds.Count > 1 && throwSpeeds[^1] > 0.01f
                ? throwSpeeds[0] / throwSpeeds[^1] : 0f;
            float maxSlowdown = emptySpeed > 0.01f && carrySpeeds.Count > 0
                ? (1f - carrySpeeds[^1] / emptySpeed) * 100f : 0f;

            Debug.Log($"[M1] M1_WEIGHT_SUMMARY empty={emptySpeed:F2}m/s throwSpread={throwSpread:F2}x maxSlowdown={maxSlowdown:F1}% " +
                      $"throwMonotonic={throwMonotonic} carryMonotonic={carryMonotonic}");

            // 判定阈值：越重扔得越慢（单调）；最重比最轻慢 3 倍以上（区分度）；负重减速至少 25%
            bool spreadOk = throwSpread >= 3f;
            bool slowdownOk = maxSlowdown >= 25f;
            bool pass = throwMonotonic && carryMonotonic && spreadOk && slowdownOk;

            Debug.Log($"[M1] M1_WEIGHT_{(pass ? "OK" : "FAIL")} throwMonotonic={throwMonotonic} carryMonotonic={carryMonotonic} " +
                      $"spreadOk={spreadOk}({throwSpread:F2}x>=3) slowdownOk={slowdownOk}({maxSlowdown:F1}%>=25)");

            yield return new WaitForSeconds(0.5f);
            Application.Quit(0);
        }

        private float lastMeasuredSpeed;

        private IEnumerator MeasureCarry(PlayerMotor motor, float declaredMass, float actualMass)
        {
            motor.ApplyAutomationMove(new Vector2(0f, 1f), false);
            yield return new WaitForSeconds(1.0f);
            lastMeasuredSpeed = motor.CurrentPlanarSpeed;
            motor.ClearAutomationMove();
            yield return new WaitForSeconds(0.15f);
        }

        private static bool IsMonotonicDecreasing(List<float> values)
        {
            for (int i = 1; i < values.Count; i++)
            {
                // 允许 2% 抖动，避免物理噪声把正确的递减判成失败
                if (values[i] > values[i - 1] * 1.02f)
                {
                    return false;
                }
            }

            return values.Count >= 2;
        }

        private static GrabbableProp FindPropByMass(float mass)
        {
            GrabbableProp best = null;
            float bestDiff = float.MaxValue;
            foreach (GrabbableProp p in Object.FindObjectsByType<GrabbableProp>(FindObjectsSortMode.None))
            {
                if (p.Profile == null) continue;
                float diff = Mathf.Abs(p.Mass - mass);
                if (diff < bestDiff)
                {
                    bestDiff = diff;
                    best = p;
                }
            }

            return bestDiff <= 1.5f ? best : null;
        }

        // ==================== 延迟 ====================

        /// <summary>
        /// 注入延迟：用 Mirror 自带的 LatencySimulation 包住 KcpTransport。
        /// 必须在 StartHost 之前替换 NetworkManager.transport —— Transport.active 是在
        /// SetupTransport（StartHost 内部）赋值的，晚于这个时机会完全不生效。
        /// </summary>
        private void ApplyLatencySimulation()
        {
            NetworkManager nm = NetworkManager.singleton;
            if (nm == null) return;

            Transport inner = nm.transport;
            if (inner == null)
            {
                Debug.LogError("[M1] M1_LATENCY_FAIL reason=no_transport");
                return;
            }

            LatencySimulation sim = nm.GetComponent<LatencySimulation>();
            if (sim == null)
            {
                Debug.LogError("[M1] M1_LATENCY_FAIL reason=no_latency_sim_component（需要先在编辑器里挂上并连线 wrap）");
                return;
            }

            // latency 是单向值，双向各加一次 —— 报 RTT 时必须 ×2，否则对外低估一半
            sim.latency = latencyMs * 0.5f;
            sim.enabled = true;
            nm.transport = sim;

            // 关键：Transport.active 早在 NetworkManager.Awake 里就指向了 KcpTransport，
            // 只改 nm.transport 不会生效（Mirror 的收发全部走 Transport.active）。
            // 首版就是这么测出「注入了 200ms 但 rtt=24ms」的假通过。
            Transport.active = sim;

            Debug.Log($"[M1] M1_LATENCY_APPLIED rtt={latencyMs}ms oneway={latencyMs * 0.5f}ms " +
                      $"wrap={inner.GetType().Name} active={Transport.active.GetType().Name}");
        }

        private IEnumerator RunLatencyHost()
        {
            NetworkManager.singleton.StartHost();

            float wait = 0f;
            while (NetworkClient.localPlayer == null && wait < 20f)
            {
                wait += Time.unscaledDeltaTime;
                yield return null;
            }

            if (NetworkClient.localPlayer == null)
            {
                Debug.LogError("[M1] M1_LATENCY_FAIL reason=no_local_player");
                yield break;
            }

            Debug.Log($"[M1] M1_LAT_HOST_START rtt={latencyMs}ms");

            float connWait = 0f;
            while (NetworkServer.connections.Count < 2 && connWait < 25f)
            {
                connWait += Time.unscaledDeltaTime;
                yield return null;
            }

            if (NetworkServer.connections.Count < 2)
            {
                Debug.LogError("[M1] M1_LATENCY_FAIL reason=no_remote_client（需要另起 -m1latclient 进程）");
                yield break;
            }

            // 让一个物件持续运动，客户端据此量同步误差
            GrabbableProp target = FindPropByMass(22f);
            float end = Time.realtimeSinceStartup + 16f;
            int tick = 0;
            while (Time.realtimeSinceStartup < end)
            {
                if (target != null && target.Body != null)
                {
                    float phase = tick * 0.35f;
                    target.Body.linearVelocity = new Vector3(Mathf.Sin(phase) * 2f, 0f, Mathf.Cos(phase) * 2f);
                    long ms = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    if (tick % 5 == 0)
                    {
                        Vector3 bp = target.Body.position;
                        Debug.Log($"[M1] M1_LAT_HOST_PROBE t={ms} pos=({bp.x:F2},{bp.y:F2},{bp.z:F2})");
                    }
                }

                tick++;
                yield return new WaitForSeconds(0.1f);
            }

            Debug.Log($"[M1] M1_LAT_HOST_DONE rtt={latencyMs}ms conns={NetworkServer.connections.Count}");
            yield return new WaitForSeconds(0.5f);
            Application.Quit(0);
        }

        /// <summary>
        /// 客户端侧：量「按下抓取 → 手里真的有东西」的往返时长。
        /// 这是玩家能直接感觉到的输入延迟，比单纯报 rtt 更接近「延迟是否致命」这个问题。
        /// </summary>
        private IEnumerator RunLatencyClient()
        {
            NetworkManager.singleton.networkAddress = "127.0.0.1";
            NetworkManager.singleton.StartClient();

            float wait = 0f;
            while (NetworkClient.localPlayer == null && wait < 20f)
            {
                wait += Time.unscaledDeltaTime;
                yield return null;
            }

            if (NetworkClient.localPlayer == null)
            {
                Debug.LogError("[M1] M1_LATENCY_FAIL reason=no_local_player");
                yield break;
            }

            NetworkIdentity player = NetworkClient.localPlayer;
            PlayerMotor motor = player.GetComponent<PlayerMotor>();
            GrabController grab = player.GetComponent<GrabController>();
            grab.SetInputEnabled(false);

            Debug.Log($"[M1] M1_LAT_CLIENT_START rtt={latencyMs}ms");

            // 等物件同步到本地
            float propWait = 0f;
            while (propWait < 12f)
            {
                propWait += Time.unscaledDeltaTime;
                if (Object.FindObjectsByType<GrabbableProp>(FindObjectsSortMode.None).Length > 0) break;
                yield return null;
            }

            yield return new WaitForSeconds(1.5f);

            // 把玩家挪到某个物件旁边并面向它（客户端权威，本地改位置即可）
            GrabbableProp target = FindPropByMass(22f);
            if (target == null)
            {
                Debug.LogError("[M1] M1_LATENCY_FAIL reason=no_prop");
                yield break;
            }

            Vector3 tp = target.transform.position;
            motor.transform.position = new Vector3(tp.x, tp.y + 1.0f, tp.z - 1.4f);
            motor.transform.LookAt(new Vector3(tp.x, tp.y + 1.0f, tp.z));
            yield return new WaitForSeconds(0.6f);

            // 抓取请求 → 本地看到 IsHolding，量这个往返
            long t0 = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            grab.TryGrab(target);

            float grabLatency = -1f;
            float deadline = Time.realtimeSinceStartup + 3f;
            while (Time.realtimeSinceStartup < deadline)
            {
                if (grab.IsHolding)
                {
                    grabLatency = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - t0;
                    break;
                }

                yield return null;
            }

            Debug.Log($"[M1] M1_LAT_GRAB latency_ms={grabLatency:F0} holding={grab.IsHolding} rtt_ms={NetworkTime.rtt * 1000.0:F0}");

            // 松手投掷 → 本地看到脱手，量第二个往返（覆盖 Command 往返的稳定性）
            long t1 = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            grab.Release(true);
            float releaseLatency = -1f;
            float deadline2 = Time.realtimeSinceStartup + 3f;
            while (Time.realtimeSinceStartup < deadline2)
            {
                if (!grab.IsHolding)
                {
                    releaseLatency = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - t1;
                    break;
                }

                yield return null;
            }

            Debug.Log($"[M1] M1_LAT_RELEASE latency_ms={releaseLatency:F0}");

            // 同步误差：把服务端探针位置与本地位置按墙钟对齐（由 RunLatencyHost 输出 M1_LAT_HOST_PROBE）
            long t2 = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            Vector3 local = target.transform.position;
            Debug.Log($"[M1] M1_LAT_CLIENT_PROBE t={t2} pos=({local.x:F2},{local.y:F2},{local.z:F2})");

            // 判定：200ms RTT 下，抓取往返应 ≤ 600ms（3 次 RTT 余量），否则操作会「按了没反应」
            bool grabOk = grabLatency > 0f && grabLatency <= 600f;
            bool releaseOk = releaseLatency > 0f && releaseLatency <= 600f;
            Debug.Log($"[M1] M1_LATENCY_{(grabOk && releaseOk ? "OK" : "FAIL")} rtt={latencyMs}ms " +
                      $"grab={grabLatency:F0}ms release={releaseLatency:F0}ms threshold=600ms");

            yield return new WaitForSeconds(0.5f);
            Application.Quit(0);
        }
    }
}
