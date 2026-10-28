using UnityEngine;

namespace ScavengeNightmare.Gameplay
{
    /// <summary>
    /// 物件类型（D13）。四类决定「玩家一眼能认出该怎么处理它」：
    ///   易碎 = 别摔；滚动 = 会跑；重型 = 搬不动；普通 = 随手拿。
    /// 类型同时驱动白盒配色与行为组件挂载，改这一个字段即可切换整条链路。
    /// </summary>
    public enum PropType
    {
        Normal,
        Fragile,
        Rolling,
        Heavy,
    }

    /// <summary>
    /// 物件物理档（D4 起）：质量、摩擦、抓握与投掷参数的数据表条目。
    /// 手感三原则之一「一切手感参数进数据表，改表即生效」的落地点。
    /// </summary>
    [CreateAssetMenu(menuName = "拾荒惊魂/物件物理档", fileName = "Prop_")]
    public sealed class PropPhysicsProfile : ScriptableObject
    {
        [Header("身份")]
        public string id = "prop";
        public string displayName = "物件";

        [Header("物件类型（D13）")]
        public PropType propType = PropType.Normal;

        [Tooltip("抓握点：物件本地坐标偏移。持物时会把这个点对准准星，"
               + "让长条物件（如电视）横着拿、大件（如冰箱）抓侧面。")]
        public Vector3 gripOffset = Vector3.zero;

        [Header("刚体")]
        [Tooltip("质量（kg）。目测 1-3kg 的罐头、8-12kg 的纸箱、20-30kg 的圆桶、40kg 以上的雕像")]
        public float mass = 8f;
        [Range(0f, 5f)] public float linearDamping = 0.05f;
        [Range(0f, 5f)] public float angularDamping = 0.4f;

        [Header("表面")]
        [Tooltip("动摩擦：越大越难推、越不容易滑")]
        [Range(0f, 1f)] public float dynamicFriction = 0.45f;
        [Tooltip("静摩擦：越大越不容易从静止被推动")]
        [Range(0f, 1f)] public float staticFriction = 0.5f;
        [Tooltip("弹性：0 = 落地不弹，1 = 几乎不损失速度")]
        [Range(0f, 1f)] public float bounciness = 0.05f;

        [Header("抓握（速度控制）")]
        [Tooltip("跟随目标速度时的加速度上限，越大越「黏手」")]
        public float grabAcceleration = 55f;
        [Tooltip("抓握中允许的最大跟随速度，防止瞬移穿墙")]
        public float grabMaxSpeed = 9f;
        [Tooltip("物件停在准星前方多远")]
        public float holdDistance = 1.6f;
        [Tooltip("物件离目标点超过该距离即自动脱手（被墙或队友卡住）")]
        public float breakDistance = 2.6f;

        [Header("投掷")]
        [Tooltip("出手冲量（N·s）。除以质量得到出手速度，因此越重扔得越慢")]
        public float throwImpulse = 36f;
        [Range(0f, 3f)] public float throwSpin = 0.6f;
        public float maxThrowSpeed = 16f;

        [Header("价值与易碎（D6）")]
        public bool isFragile;
        [Tooltip("标价，仅供后续结算读取，D6 只做到碎后归零")]
        public int value;
        [Tooltip("碰撞相对速度超过该值就碎（m/s）")]
        public float breakSpeed = 3f;

        [Header("表现")]
        public Color highlightTint = new Color(0.55f, 0.85f, 1f, 1f);

        /// <summary>出手速度 = 冲量 / 质量，物件的「重量感」主要来自这里。</summary>
        public float ThrowSpeed => Mathf.Clamp(throwImpulse / Mathf.Max(mass, 0.1f), 1f, maxThrowSpeed);

        /// <summary>负重带来的移动速度系数（D1 冻结：跑要「会被拖累」）。</summary>
        public float CarrySpeedMultiplier => Mathf.Clamp(1f - mass / 70f, 0.45f, 1f);
    }
}
