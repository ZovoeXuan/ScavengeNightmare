using UnityEngine;

namespace ScavengeNightmare.Traits
{
    /// <summary>
    /// 角色身份（D8 灰盒）：四个固定角色的基础属性差异。
    ///
    /// 设计口径：每个角色必须「有一项明显强、一项明显弱」，且弱项要能被缺点牌放大——
    /// 否则「冲突组合」无从谈起。倍率全部相对基线 1.0，与物件物理档、手感方案同属乘法层。
    ///
    /// 三层参数的合成顺序（从物件到玩家）：
    ///   物件物理档（这个物件多重多滑）
    ///   × 手感方案（这一版整体调性，D5）
    ///   × 角色身份（这个人有多大力气，D8）
    ///   × 缺点牌（这一局他有什么毛病，D8）
    /// </summary>
    [CreateAssetMenu(menuName = "拾荒惊魂/角色身份", fileName = "Role_")]
    public sealed class CharacterIdentity : ScriptableObject
    {
        [Header("身份")]
        public string id = "brute";
        public string displayName = "大力士";

        [TextArea(2, 3)]
        public string description = "搬得动，扔得远，但手笨、走得慢。";

        [Tooltip("灰盒胶囊体颜色，D8 用它区分四人（无美术）")]
        public Color bodyColor = Color.red;

        [Tooltip("胶囊体整体缩放：体型差异会影响窄门通过性，这是「身体限制可表现」的入口")]
        public Vector3 bodyScale = Vector3.one;

        [Header("移动")]
        [Tooltip("移动速度倍率")]
        public float speedScale = 1f;
        [Tooltip("是否允许冲刺（瘸子不允许）")]
        public bool canSprint = true;

        [Header("负重")]
        [Tooltip("负重减速分母覆盖：越大越不怕重。留 0 表示沿用手感方案的值")]
        public float carryDivisorOverride;

        [Header("抓握与投掷")]
        [Tooltip("抓握跟随刚度倍率：越大越跟手，越小越「手笨」")]
        public float grabStiffnessScale = 1f;
        [Tooltip("投掷冲量倍率")]
        public float throwScale = 1f;
        [Tooltip("可交互 / 高亮的距离倍率（近视眼小于 1）")]
        public float interactRangeScale = 1f;

        [Header("受击")]
        [Tooltip("硬直时间倍率：小于 1 表示抗揍")]
        public float stunScale = 1f;
    }
}
