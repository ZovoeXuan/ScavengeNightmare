namespace ScavengeNightmare.Monsters
{
    /// <summary>
    /// 空眼（T1 噪音追猎）。4B 第一只完整怪物。
    ///
    /// 它的独有行为集中在 MonsterBrain 的噪音路径上：警觉档走向最近可听声源、
    /// 追猎档才允许 Chase（且必须有 <c>source=noise:&lt;id&gt;</c>）；
    /// 手电直射 1.5s → 僵直 2s；被推车撞 → 后退 ≥1m。
    /// 这些判定全部在服务端，本类只固定身份，S4B-3 的导购/尖啸罐用各自子类覆盖感知策略。
    /// </summary>
    public sealed class HollowEyeBrain : MonsterBrain
    {
        protected override string MonsterId => "holloweye";
    }
}
