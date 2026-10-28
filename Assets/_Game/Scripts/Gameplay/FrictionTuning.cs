using UnityEngine;

namespace ScavengeNightmare.Gameplay
{
    /// <summary>
    /// 搬运摩擦表（D6）：五条摩擦的独立开关与阈值。
    /// 每条都可单独关闭，验收时逐条开关以确认「关掉就完全无副作用」。
    /// </summary>
    [CreateAssetMenu(menuName = "拾荒惊魂/搬运摩擦表", fileName = "FrictionTuning")]
    public sealed class FrictionTuning : ScriptableObject
    {
        [Header("1. 窄门卡位")]
        public bool enableNarrowDoor = true;
        [Tooltip("物件在门洞内持续被卡住超过该秒数才给反馈，避免路过就误报")]
        public float jamContactSeconds = 0.3f;

        [Header("2. 手滑砸脚")]
        public bool enableImpactStun = true;
        [Tooltip("相对速度低于该值不触发（避免轻碰就硬直）")]
        public float stunSpeedThreshold = 4f;
        [Tooltip("低于该质量不触发，避免 2kg 罐头砸人也硬直")]
        public float stunMinMass = 5f;
        [Tooltip("普通重物硬直秒数")]
        public float stunDuration = 0.6f;
        [Tooltip("达到该质量则判为重物，硬直更久")]
        public float stunHeavyMass = 20f;
        public float stunHeavyDuration = 1.0f;

        [Header("3. 滚动体")]
        public bool enableRolling = true;
        [Tooltip("水平速度超过该值才认为进入滚动状态")]
        public float rollingStartSpeed = 1.5f;

        [Header("4. 夹手")]
        public bool enableDoorTrap = true;
        [Tooltip("物件被门夹住后向外推出的额外余量，防止穿模")]
        public float doorPushMargin = 0.08f;

        [Header("5. 易碎")]
        public bool enableFragile = true;
        [Tooltip("碎块生成数量（占位）")]
        public int fragmentCount = 4;
        [Tooltip("碎块存活秒数，避免碎块越堆越多影响性能")]
        public float fragmentLifetime = 3f;

        [Header("表现")]
        [Tooltip("占位音效开关（无音频资产，用合成短音）")]
        public bool enablePlaceholderAudio = true;
    }
}
