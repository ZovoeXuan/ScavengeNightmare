namespace ScavengeNightmare.Monsters
{
    /// <summary>
    /// 4B 七状态（+1）：Spawn → Patrol → Investigate → Chase → Attack → Search → Patrol，
    /// 另有 Stunned。尖啸罐的「引爆即消失」在 S4B-3 用独立状态处理，本枚举不含死亡态。
    /// </summary>
    public enum MonsterState : byte
    {
        Spawn = 0,
        Patrol = 1,
        Investigate = 2,
        Chase = 3,
        Attack = 4,
        Search = 5,
        Stunned = 6
    }
}
