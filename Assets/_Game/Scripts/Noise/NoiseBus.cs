using System.Collections.Generic;
using UnityEngine;

namespace ScavengeNightmare.Noise
{
    /// <summary>
    /// 一条噪音记忆。结构参考 zombie-ai 的 NPCMemory：
    /// 记「在哪、多响、什么时候听到的」，用 age 做遗忘，而不是只留一个 float。
    /// </summary>
    public sealed class NoiseMemory
    {
        public string source;
        public Vector3 position;
        /// <summary>最近一次的强度。</summary>
        public float amount;
        /// <summary>窗口内的峰值强度，供消费方排序用。</summary>
        public float score;
        public float lastHeard;

        public float Age => Time.unscaledTime - lastHeard;
    }

    /// <summary>
    /// P0 噪音总线（design §2.4）：全队共享 0–100，服务器权威，自然衰减 3/s。
    /// 区间：0–39 安静 / 40–69 警觉 / 70–99 追猎 / 100 一次爆发后回落。
    ///
    /// 4A 里它没有消费方（怪物 AI 在 4B），但**可归因是硬要求**（A9：
    /// 出现「没有来源却进入追猎」的次数必须为 0）。所以每一笔都必须带来源与位置，
    /// 不能只往一个 float 上累加——那样到了 4B 就查不出是谁弄出的噪音。
    ///
    /// 记忆模型（参考 zombie-ai 的 NPCSensoryMemory）：同一来源在窗口内刷新而非追加，
    /// 按 age 遗忘。这样 4B 的怪物可以直接问「最近谁最吵、在哪、多久之前」，
    /// 不用自己再维护一套感知表。
    /// </summary>
    public static class NoiseBus
    {
        public const float MaxLevel = 100f;
        public const float DecayPerSecond = 3f;
        public const float AlertLevel = 40f;
        public const float HuntLevel = 70f;

        /// <summary>P0 §2.2「丢失目标：脱离视线 + 噪音回落，6s」——同时作为噪音归因窗口。</summary>
        public const float AttributionWindowSeconds = 6f;

        private static readonly List<NoiseMemory> memories = new();
        private static float level;
        private static string lastZone = "quiet";

        public static float Level => level;

        /// <summary>当前未遗忘的噪音记忆，供 4B 的怪物感知直接消费。</summary>
        public static IReadOnlyList<NoiseMemory> Memories => memories;

        public static NoiseMemory TopMemory { get; private set; }

        public static string TopSource => TopMemory != null ? TopMemory.source : "none";
        public static Vector3 TopSourcePosition => TopMemory != null ? TopMemory.position : default;

        public static void Report(string source, float amount, Vector3 at)
        {
            if (amount <= 0f)
            {
                return;
            }

            string name = string.IsNullOrEmpty(source) ? "unknown" : source;
            level = Mathf.Min(MaxLevel, level + amount);

            NoiseMemory memory = FetchMemory(name);
            memory.position = at;
            memory.amount = amount;
            memory.score = Mathf.Max(memory.score, amount);
            memory.lastHeard = Time.unscaledTime;

            Debug.Log($"[S4A] NOISE_ADD source={name} amount={amount} level={level:F1} at=({at.x:F1},{at.z:F1})");
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

            ForgetOlderThan(AttributionWindowSeconds);
            level = Mathf.Max(0f, level - DecayPerSecond * deltaTime);
            CheckZone();
        }

        /// <summary>
        /// 按年龄遗忘。这是「噪音回落」的实现：没有来源继续补充时，
        /// 旧记忆到期即消失，消费方再也问不到它。
        /// </summary>
        public static void ForgetOlderThan(float seconds)
        {
            memories.RemoveAll(m => m.Age > seconds);

            if (TopMemory != null && TopMemory.Age > seconds)
            {
                TopMemory = null;
            }
        }

        public static void Reset()
        {
            memories.Clear();
            level = 0f;
            TopMemory = null;
            lastZone = "quiet";
            Debug.Log("[S4A] NOISE_RESET level=0");
        }

        private static NoiseMemory FetchMemory(string source)
        {
            foreach (NoiseMemory memory in memories)
            {
                if (memory != null && string.Equals(memory.source, source, System.StringComparison.Ordinal))
                {
                    return memory;
                }
            }

            NoiseMemory created = new() { source = source, score = 0f };
            memories.Add(created);
            return created;
        }

        private static void CheckZone()
        {
            RefreshTopMemory();

            string zone = level >= MaxLevel ? "burst" : (level >= HuntLevel ? "hunt" : (level >= AlertLevel ? "alert" : "quiet"));

            if (!string.Equals(zone, lastZone, System.StringComparison.Ordinal))
            {
                lastZone = zone;
                Debug.Log($"[S4A] NOISE_ZONE zone={zone} level={level:F1} source={TopSource}");
            }
        }

        /// <summary>
        /// 取窗口内峰值最高的记忆。
        /// score 目前只是「窗口内峰值强度」，**还没有做距离衰减**——
        /// 4B 有了怪物位置之后再按距离/朝向衰减，那时这里必须改，别把它当成最终版。
        /// </summary>
        private static void RefreshTopMemory()
        {
            NoiseMemory best = null;

            foreach (NoiseMemory memory in memories)
            {
                if (memory == null)
                {
                    continue;
                }

                if (best == null || memory.score > best.score)
                {
                    best = memory;
                }
            }

            TopMemory = best;
        }
    }
}
