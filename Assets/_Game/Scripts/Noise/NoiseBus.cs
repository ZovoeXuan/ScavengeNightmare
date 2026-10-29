using System.Collections.Generic;
using UnityEngine;

namespace ScavengeNightmare.Noise
{
    /// <summary>
    /// P0 噪音总线（design §2.4）：全队共享 0–100，服务器权威，自然衰减 3/s。
    /// 区间：0–39 安静 / 40–69 警觉 / 70–99 追猎 / 100 一次爆发后回落。
    ///
    /// 4A 里它没有消费方（怪物 AI 在 4B），但**可归因是硬要求**（A9：
    /// 出现「没有来源却进入追猎」的次数必须为 0）。所以每一笔都必须带来源与位置，
    /// 不能只往一个 float 上累加——那样到了 4B 就查不出是谁弄出的噪音。
    /// </summary>
    public static class NoiseBus
    {
        public const float MaxLevel = 100f;
        public const float DecayPerSecond = 3f;
        public const float AlertLevel = 40f;
        public const float HuntLevel = 70f;
        public const float AttributionWindowSeconds = 3f;

        private sealed class NoiseEvent
        {
            public string source;
            public float amount;
            public Vector3 position;
            public float time;
        }

        private static readonly List<NoiseEvent> recent = new();
        private static float level;
        private static string lastZone = "quiet";

        public static float Level => level;

        /// <summary>最近 3 秒里贡献最大的来源。HUD 的可归因显示直接读它。</summary>
        public static string TopSource { get; private set; } = "none";
        public static Vector3 TopSourcePosition { get; private set; }

        public static void Report(string source, float amount, Vector3 at)
        {
            if (amount <= 0f)
            {
                return;
            }

            level = Mathf.Min(MaxLevel, level + amount);
            recent.Add(new NoiseEvent
            {
                source = string.IsNullOrEmpty(source) ? "unknown" : source,
                amount = amount,
                position = at,
                time = Time.unscaledTime
            });

            Debug.Log($"[S4A] NOISE_ADD source={source} amount={amount} level={level:F1} at=({at.x:F1},{at.z:F1})");
            CheckZone();
        }

        /// <summary>持续型噪音（推车推行、拖动重物）每帧调用，amount 单位是「每秒」。</summary>
        public static void ReportRate(string source, float perSecond, float deltaTime, Vector3 at)
        {
            if (perSecond <= 0f || deltaTime <= 0f)
            {
                return;
            }

            Report(source, perSecond * deltaTime, at);
        }

        public static void Decay(float deltaTime)
        {
            if (deltaTime <= 0f)
            {
                return;
            }

            float now = Time.unscaledTime;

            for (int i = recent.Count - 1; i >= 0; i--)
            {
                if (now - recent[i].time > AttributionWindowSeconds)
                {
                    recent.RemoveAt(i);
                }
            }

            level = Mathf.Max(0f, level - DecayPerSecond * deltaTime);
            CheckZone();
        }

        public static void Reset()
        {
            recent.Clear();
            level = 0f;
            TopSource = "none";
            TopSourcePosition = default;
            lastZone = "quiet";
            Debug.Log("[S4A] NOISE_RESET level=0");
        }

        private static void CheckZone()
        {
            RefreshTopSource();

            string zone = level >= MaxLevel ? "burst" : (level >= HuntLevel ? "hunt" : (level >= AlertLevel ? "alert" : "quiet"));

            if (!string.Equals(zone, lastZone, System.StringComparison.Ordinal))
            {
                lastZone = zone;
                Debug.Log($"[S4A] NOISE_ZONE zone={zone} level={level:F1} source={TopSource}");
            }
        }

        private static void RefreshTopSource()
        {
            NoiseEvent best = null;
            foreach (NoiseEvent e in recent)
            {
                if (best == null || e.amount > best.amount)
                {
                    best = e;
                }
            }

            if (best == null)
            {
                TopSource = "none";
                return;
            }

            TopSource = best.source;
            TopSourcePosition = best.position;
        }
    }
}
