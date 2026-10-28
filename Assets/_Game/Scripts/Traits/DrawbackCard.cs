using UnityEngine;

namespace ScavengeNightmare.Traits
{
    /// <summary>
    /// 缺点牌（D8 灰盒）：每局每人随机 1 张，必须同时带「代价」和「补偿收益」。
    ///
    /// 三条设计约束（对应 D8 验收口径）：
    /// 1. **每张都标注与哪个角色冲突最强**，并给出冲突加权——冲突组合才是笑点来源
    ///    （大力士 + 手抖 = 力气最大的人抓不稳）。
    /// 2. **代价与补偿分处不同数值轴**：代价乘数一律 ≤ 1，补偿乘数一律 ≥ 1，
    ///    同一轴上不允许既罚又补，否则就是「叠 buff」。抽取时还会排除同轴重复惩罚。
    /// 3. 全部数值在数据表里，改表即生效（D1 手感三原则第 3 条）。
    /// </summary>
    [CreateAssetMenu(menuName = "拾荒惊魂/缺点牌", fileName = "Drawback_")]
    public sealed class DrawbackCard : ScriptableObject
    {
        [Header("身份")]
        public string id = "shaky";
        public string displayName = "手抖";

        [TextArea(2, 3)]
        [Tooltip("这张牌的笑点来源，写给策划看，也会显示在 HUD 上")]
        public string comedyNotes = "力气最大的人抓不稳最轻的东西。";

        [TextArea(1, 2)]
        [Tooltip("补偿收益的可读描述，HUD 必须显示，让玩家知道「亏了什么、赚了什么」")]
        public string compensationText = "扔得比谁都远";

        [Header("冲突与抽取")]
        [Tooltip("与哪个角色身份冲突最强（填 CharacterIdentity.id）")]
        public string strongestConflictId = "brute";
        [Tooltip("基础权重")]
        public int baseWeight = 10;
        [Tooltip("抽到冲突角色时的额外权重：值越大越容易出「冲突组合」")]
        public int conflictWeightBonus = 30;
        [Tooltip("若角色本身在该轴已有惩罚，则排除这张牌（防止叠 buff）")]
        public bool excludeIfIdentitySlower;
        [Tooltip("同上，抓握轴")]
        public bool excludeIfIdentityClumsier;

        [Header("代价 · 数值（一律 ≤ 1，负重分母例外：越大越省力）")]
        [Range(0.2f, 1f)] public float grabStiffnessScale = 1f;
        [Range(0.2f, 1f)] public float speedScale = 1f;
        [Range(0.2f, 1f)] public float interactRangeScale = 1f;
        [Tooltip("负重分母倍率：小于 1 表示更容易被压慢")]
        [Range(0.3f, 1f)] public float carryDivisorScale = 1f;
        [Tooltip("质量低于该值的物件视为「小件」。0 = 本牌不涉及小件")]
        public float smallItemMassLimit;
        [Tooltip("是否禁止小件被高亮与拾取（看不清小件）")]
        public bool blocksSmallItems;

        [Header("代价 · 行为")]
        [Tooltip("手持时准星注入随机抖动（手抖）")]
        public bool shakyHands;
        [Tooltip("进入门洞 / 窄区时移速额外惩罚（窄门恐惧）")]
        public bool narrowDoorPanic;

        [Header("补偿收益（数值一律 ≥ 1；抗揍用 stunScale ≤ 1 表达）")]
        [Range(1f, 3f)] public float compensationThrowScale = 1f;
        [Range(1f, 2f)] public float compensationSpeedScale = 1f;
        [Range(1f, 2f)] public float compensationGrabScale = 1f;
        [Range(1f, 2f)] public float compensationCarryDivisorScale = 1f;
        [Tooltip("硬直时间倍率：小于 1 = 抗揍（被砸顿得更短），是补偿而非代价")]
        [Range(0.3f, 1f)] public float stunScale = 1f;
        [Tooltip("是否给出「最近的高价值物件在哪」的屏幕提示（闻香定位）")]
        public bool valueScent;

        [Header("行为参数")]
        [Tooltip("手抖的抖动幅度（米）")]
        public float shakeAmplitude = 0.09f;
        [Tooltip("窄门恐惧在门洞内的移速倍率")]
        [Range(0.2f, 1f)] public float panicSpeedScale = 0.5f;
    }
}
