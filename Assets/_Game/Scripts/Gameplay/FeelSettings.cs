using UnityEngine;

namespace ScavengeNightmare.Gameplay
{
    /// <summary>
    /// 当前生效的手感方案（D5）。全局单点，抓握与移动都从这里取倍率。
    /// 未指定方案时所有倍率取 1（等价于「稳重」基线），因此没接方案的场景仍可正常跑。
    /// </summary>
    public static class FeelSettings
    {
        private static FeelScheme active;

        public static FeelScheme Active => active;
        public static string ActiveId => active != null ? active.schemeId : "default";

        public static event System.Action<FeelScheme> Changed;

        public static void Set(FeelScheme scheme)
        {
            active = scheme;
            Changed?.Invoke(scheme);
        }

        // 静态字段会跨 Play 会话残留，进入播放前重置。
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnLoad()
        {
            active = null;
            Changed = null;
        }

        private static float Scale(float value, float fallback)
        {
            return active != null ? value : fallback;
        }

        public static float GrabAcceleration(float baseValue)
        {
            return baseValue * Scale(active != null ? active.grabAccelerationScale : 1f, 1f);
        }

        public static float GrabMaxSpeed(float baseValue)
        {
            return baseValue * Scale(active != null ? active.grabMaxSpeedScale : 1f, 1f);
        }

        public static float FollowStiffness(float baseValue)
        {
            return baseValue * Scale(active != null ? active.followStiffnessScale : 1f, 1f);
        }

        public static float HoldDistance(float baseValue)
        {
            return baseValue * Scale(active != null ? active.holdDistanceScale : 1f, 1f);
        }

        public static float PlayerSpeed(float baseValue)
        {
            return baseValue * Scale(active != null ? active.playerSpeedScale : 1f, 1f);
        }

        public static float CarryMultiplier(float mass)
        {
            return active != null ? active.CarryMultiplier(mass) : Mathf.Clamp(1f - mass / 70f, 0.45f, 1f);
        }

        /// <summary>
        /// 出手速度 = 冲量 × 方案倍率 ÷ 质量，再按物件上限截断。
        /// 「越重扔得越慢」这条关系在任何方案下都成立，方案只整体缩放力度。
        /// </summary>
        public static float ThrowSpeed(PropPhysicsProfile profile)
        {
            if (profile == null)
            {
                return 0f;
            }

            float impulse = profile.throwImpulse * Scale(active != null ? active.throwImpulseScale : 1f, 1f);
            return Mathf.Clamp(impulse / Mathf.Max(profile.mass, 0.1f), 1f, profile.maxThrowSpeed);
        }

        public static float ThrowSpin(float baseValue)
        {
            return baseValue * Scale(active != null ? active.throwSpinScale : 1f, 1f);
        }
    }
}
