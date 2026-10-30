using UnityEngine;

namespace ScavengeNightmare.Cart
{
    /// <summary>
    /// 推车全部数值。不放任何硬编码常量在代码里——改表不改代码（D13 已确立的原则）。
    ///
    /// 数值来源：P0 垂直切片规格 §2.1 系统清单 + §2.4 噪音 + §2.7 脱困，
    /// 以及 Sprint 4A 技术计划 Story 3（v1.4 把 3.0 m/s 的噪音由 18/s 降到 7/s）。
    /// </summary>
    [CreateAssetMenu(menuName = "拾荒惊魂/推车配置", fileName = "CartProfile")]
    public sealed class CartProfile : ScriptableObject
    {
        [Header("尺寸（米）")]
        [Tooltip("实测：保险箱 0.81m、雕像 0.85m、电视 0.86m 宽。1.3×0.75 连两件大件都放不下"
               + "（0.81+0.85=1.66m）。1.8×0.9 是能单层并排两件大件的最小尺寸。")]
        public float deckLength = 1.8f;
        public float deckWidth = 0.9f;
        public float deckHeight = 0.12f;
        [Tooltip("折叠后宽度 0.3m，窄门 1.2m 与楼梯都靠它")]
        public float foldedWidth = 0.3f;
        [Tooltip("轮子把手高度，决定推行时玩家的持握点")]
        public float handleHeight = 0.9f;

        [Header("质量与载重")]
        public float cartMassKg = 40f;
        public float capacityKg = 120f;
        [Tooltip("超载判定容差：120.4kg 不算超载，避免物理抖动在临界点反复报警")]
        public float capacityToleranceKg = 0.5f;
        [Tooltip("件数上限 6（P0 §2.1）。与载重是两个独立闸门，先撞哪个算哪个")]
        public int maxItems = 6;
        [Tooltip("同板最重与最轻的质量比上限。1kg 配 42kg（42:1）实测必被接触求解弹飞，"
               + "与其装上去再掉，不如直接拒绝并给明确原因")]
        public float maxMassRatio = 20f;
        [Tooltip("180kg 起提示「随时可能翻」，但不禁止放货")]
        public float overloadWarnKg = 180f;

        [Header("推行速度（m/s）")]
        public float pushSpeedEmpty = 3.0f;
        [Tooltip("满载降速到 ≤ 2.0")]
        public float pushSpeedFull = 2.0f;
        [Tooltip("达到该载重时取 pushSpeedFull，之间线性插值")]
        public float fullLoadKg = 120f;

        [Header("噪音：速度 → 噪音/秒")]
        public float speedSlow = 0.5f;
        public float noiseAtSlow = 3f;
        public float speedMid = 1.5f;
        public float noiseAtMid = 4f;
        public float speedFast = 3.0f;
        [Tooltip("v1.4：由 18/s 降到 7/s，18–50 留给超载/甩落/撞击脉冲")]
        public float noiseAtFast = 7f;
        [Tooltip("噪音三档必须单调递增（±10%），由 -s4acart-noise 断言")]
        public float noiseMonotonicTolerance = 0.1f;

        [Header("脱困（§2.7）")]
        [Tooltip("1.0s，刻意与公秤提交的 1.2s 区分，避免两个长按互相触发")]
        public float unstickHoldSeconds = 1.0f;
        public float unstickCooldownSeconds = 6f;
        public float unstickNoise = 20f;
        [Tooltip("重置到玩家身后 3m 内最近的合法地面点")]
        public float unstickMaxDistance = 3f;

        [Header("卡死判定")]
        [Tooltip("0.75s 窗口内 push=true 且水平位移 < 0.08m 记一次停滞")]
        public float stallWindowSeconds = 0.75f;
        public float stallDistanceMeters = 0.08f;
        [Tooltip("连续 2 次停滞 ≈ 1.5s 判卡死。用计数不用计时，避免单帧抖动误判")]
        public int stallCountToJudge = 2;

        [Header("货物稳定")]
        public float cargoFriction = 0.9f;
        [Tooltip("单帧位移上限 0.15m，超过就钳住——防止高速推行时货物穿板")]
        public float maxFrameMove = 0.15f;
        [Tooltip("货物超出板面多少米算滑落")]
        public float cargoFallMargin = 0.15f;

        public float DeckArea => deckLength * deckWidth;

        public bool IsOverCapacity(float loadKg)
        {
            return loadKg > capacityKg + capacityToleranceKg;
        }

        /// <summary>速度 → 噪音（/秒），三档之间线性插值。</summary>
        public float NoiseAtSpeed(float speed)
        {
            if (speed <= 0f)
            {
                return 0f;
            }

            if (speed <= speedSlow)
            {
                return Mathf.Lerp(0f, noiseAtSlow, speed / Mathf.Max(0.0001f, speedSlow));
            }

            if (speed <= speedMid)
            {
                return Mathf.Lerp(noiseAtSlow, noiseAtMid, (speed - speedSlow) / Mathf.Max(0.0001f, speedMid - speedSlow));
            }

            if (speed <= speedFast)
            {
                return Mathf.Lerp(noiseAtMid, noiseAtFast, (speed - speedMid) / Mathf.Max(0.0001f, speedFast - speedMid));
            }

            // 超过最快档也不再涨：涨的部分留给「超载/甩落/撞击」这类事件脉冲，
            // 不然满速推 5 秒必进追猎，节奏直接崩。
            return noiseAtFast;
        }

        /// <summary>载重 → 推行速度上限，空车到满载线性下降。</summary>
        public float PushSpeedAtLoad(float loadKg)
        {
            float t = Mathf.Clamp01(loadKg / Mathf.Max(0.0001f, fullLoadKg));
            return Mathf.Lerp(pushSpeedEmpty, pushSpeedFull, t);
        }
    }
}
