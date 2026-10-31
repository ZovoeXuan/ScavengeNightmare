using UnityEngine;

namespace ScavengeNightmare.Economy
{
    /// <summary>
    /// S4B-5 三关内容表：配额、货件数、怪物数、诅咒数、安全期。
    ///
    /// 为什么必须是资源而不是散落在各组件里的硬编码：
    /// 配额同时被 <see cref="LevelRouteService"/>（开门判据）、<see cref="TeamAccount"/>（HUD/存档）
    /// 与 <see cref="ScavengeNightmare.Gameplay.PropSpawner"/>（生成比例）读取。
    /// 三处各写一份，改一次表要改三处，迟早出现「门要 2400、货只刷 3155 的比例 57%」这种半吊子口径。
    ///
    /// 资源缺失时用与验收表一致的内置值兜底——测试进程不该因为资源没导入就整条链路拿不到配额，
    /// 但兜底值必须与 §Story S4B-5 的验收数字逐字一致。
    /// </summary>
    [CreateAssetMenu(menuName = "拾荒惊魂/LegContentTable", fileName = "LegContentTable")]
    public sealed class LegContentTable : ScriptableObject
    {
        public const string ResourceName = "LegContentTable";

        [Header("过关额（下标 = 关卡序号 - 1）")]
        [SerializeField] private int[] quotas = { 1800, 2400, 2800 };

        [Header("夜路可拾荒件数（含每关的全部锚点）")]
        [SerializeField] private int[] props = { 12, 14, 16 };

        [Header("怪物数（L1 空眼 / L2 +导购 / L3 +尖啸罐）")]
        [SerializeField] private int[] monsters = { 1, 3, 4 };

        [Header("诅咒件数（0/1/2）")]
        [SerializeField] private int[] curses = { 0, 1, 2 };

        [Header("每关怪物安全期（秒），不得低于 60")]
        [SerializeField] private float[] safetySeconds = { 120f, 90f, 60f };

        private static LegContentTable cached;

        public static LegContentTable Instance
        {
            get
            {
                if (cached != null)
                {
                    return cached;
                }

                cached = Resources.Load<LegContentTable>(ResourceName);
                if (cached == null)
                {
                    // 兜底实例不写盘、不注册，只保证数值可用。
                    cached = CreateInstance<LegContentTable>();
                    cached.name = ResourceName + "_fallback";
                    Debug.LogWarning("[S4B] S4B_LEG_TABLE_FALLBACK reason=resource_missing");
                }

                return cached;
            }
        }

        public int QuotaFor(int leg) => At(quotas, leg, 1800);
        public int PropsFor(int leg) => At(props, leg, 12);
        public int MonstersFor(int leg) => At(monsters, leg, 1);
        public int CursesFor(int leg) => At(curses, leg, 0);

        public float SafetyFor(int leg)
        {
            if (safetySeconds == null || safetySeconds.Length == 0)
            {
                return 60f;
            }

            return safetySeconds[Mathf.Clamp(leg - 1, 0, safetySeconds.Length - 1)];
        }

        private static int At(int[] table, int leg, int fallback)
        {
            if (table == null || table.Length == 0)
            {
                return fallback;
            }

            return table[Mathf.Clamp(leg - 1, 0, table.Length - 1)];
        }

        /// <summary>编辑器装配用：一次写全五张表，避免逐字段 SetDirty 漏项。</summary>
        public void EditorConfigure(int[] quotaTable, int[] propTable, int[] monsterTable, int[] curseTable, float[] safety)
        {
            quotas = quotaTable;
            props = propTable;
            monsters = monsterTable;
            curses = curseTable;
            safetySeconds = safety;
        }

        public int[] EditorQuotas => quotas;
        public int[] EditorProps => props;
        public int[] EditorMonsters => monsters;
        public int[] EditorCurses => curses;
        public float[] EditorSafety => safetySeconds;
    }
}
