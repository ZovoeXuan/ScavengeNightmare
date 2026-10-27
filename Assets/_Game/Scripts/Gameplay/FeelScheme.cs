using UnityEngine;

namespace ScavengeNightmare.Gameplay
{
    /// <summary>
    /// 手感方案（D5）：一套**全局倍率**，叠加在物件物理档之上。
    ///
    /// 为什么不把三套数值直接写进每个物件：物件档描述的是「这个物件有多重、多滑」，
    /// 方案描述的是「这一版游戏的手感调性」。分开以后换方案不用动 20 个物件的数据，
    /// 而 D7 实测后要微调单个物件也不会打翻整套调性。
    ///
    /// 每个方案都必须写清「适合制造哪种笑点」——这是 D5 的验收口径，
    /// 手感参数不是调着玩，是为「局均笑点」服务的。
    /// </summary>
    [CreateAssetMenu(menuName = "拾荒惊魂/手感方案", fileName = "Feel_")]
    public sealed class FeelScheme : ScriptableObject
    {
        [Header("身份")]
        public string schemeId = "solid";
        public string displayName = "稳重";

        [TextArea(2, 4)]
        public string description = "标准手感：有惯性、有重量，但操作不别扭。";

        [TextArea(2, 4)]
        [Tooltip("这套参数最适合制造哪类笑点（D5 验收口径）")]
        public string comedyNotes = "搬运翻车、窄门卡位、堆叠倒塌。";

        [Header("抓握")]
        [Tooltip("跟随加速度倍率：越大越跟手，越小越「拖沓」")]
        public float grabAccelerationScale = 1f;
        [Tooltip("抓握最高速度倍率：影响能否快速把物件甩到面前")]
        public float grabMaxSpeedScale = 1f;
        [Tooltip("跟随刚度倍率：越小物件离准星越远，越容易撞到环境")]
        public float followStiffnessScale = 1f;
        [Tooltip("持物距离倍率：越大物件离身体越远，视野占用越多")]
        public float holdDistanceScale = 1f;

        [Header("投掷")]
        [Tooltip("投掷冲量倍率：同时影响出手速度与射程")]
        public float throwImpulseScale = 1f;
        public float throwSpinScale = 1f;

        [Header("移动与负重")]
        [Tooltip("玩家移动速度倍率")]
        public float playerSpeedScale = 1f;
        [Tooltip("负重减速的分母：越大惩罚越轻（1 - 质量/分母）")]
        public float carryDivisor = 70f;

        /// <summary>负重减速系数，下限 0.45 保证再重也走得动。</summary>
        public float CarryMultiplier(float mass)
        {
            return Mathf.Clamp(1f - mass / Mathf.Max(carryDivisor, 1f), 0.45f, 1f);
        }
    }
}
