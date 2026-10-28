using System.Collections.Generic;
using UnityEngine;

namespace ScavengeNightmare.Traits
{
    /// <summary>
    /// 角色与缺点牌的数据表集合（D8）。一个资产装 4 身份 + 5 张牌，改表即生效。
    /// </summary>
    [CreateAssetMenu(menuName = "拾荒惊魂/角色与缺点牌库", fileName = "TraitDatabase")]
    public sealed class TraitDatabase : ScriptableObject
    {
        [Tooltip("4 个固定角色身份，顺序对应数字键 1..4")]
        public CharacterIdentity[] identities = new CharacterIdentity[0];

        [Tooltip("5 张缺点牌，每局每人随机 1 张")]
        public DrawbackCard[] cards = new DrawbackCard[0];

        [Tooltip("随机种子；0 = 用系统时间（真随机）。自动化验证时填固定值以复现")]
        public int seed;
    }

    /// <summary>
    /// 全局访问点：抽取逻辑与身份查询都收在这里，避免各组件各自 Find 资产。
    /// 未配置数据库时返回空值，所有倍率退化为 1，游戏仍可运行（等价于「没有角色差异」）。
    /// </summary>
    public static class TraitSettings
    {
        private static TraitDatabase db;

        public static TraitDatabase Db => db;
        public static bool Ready => db != null && db.identities != null && db.identities.Length > 0;

        private static int noTraitsFlag = -1;

        /// <summary>
        /// 运行时强制禁用角色/缺点牌。基线自动化测试（D4/D6）在自己的 Start 里设 true，
        /// 声明「我要中性身份」，不依赖调用者记得传命令行参数——职责由需要中性的那一方声明。
        /// </summary>
        public static bool ForceNoTraits { get; set; }

        /// <summary>
        /// 是否禁用角色/缺点牌：运行时强制（基线测试）或命令行 <c>-notraits</c>。
        ///
        /// D4/D6 的自动化基线必须在中性身份下跑：否则测试玩家被随机分配大力士/手抖，
        /// 投掷速度、硬直时长都会随身份漂移，基线断言失效（D8 上线时已实测复现）。
        /// 命令行结果缓存，读一次即可。
        /// </summary>
        public static bool NoTraitsRequested
        {
            get
            {
                if (ForceNoTraits)
                {
                    return true;
                }

                if (noTraitsFlag < 0)
                {
                    noTraitsFlag = 0;
                    foreach (string a in System.Environment.GetCommandLineArgs())
                    {
                        if (string.Equals(a, "-notraits", System.StringComparison.OrdinalIgnoreCase))
                        {
                            noTraitsFlag = 1;
                            break;
                        }
                    }
                }

                return noTraitsFlag == 1;
            }
        }

        public static void Use(TraitDatabase value)
        {
            if (value != null)
            {
                db = value;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnLoad()
        {
            db = null;
        }

        public static CharacterIdentity Identity(int index)
        {
            if (db == null || db.identities == null || index < 0 || index >= db.identities.Length)
            {
                return null;
            }

            return db.identities[index];
        }

        public static int IdentityCount => db != null && db.identities != null ? db.identities.Length : 0;

        public static DrawbackCard Card(string id)
        {
            if (db == null || db.cards == null || string.IsNullOrEmpty(id))
            {
                return null;
            }

            foreach (DrawbackCard card in db.cards)
            {
                if (card != null && card.id == id)
                {
                    return card;
                }
            }

            return null;
        }

        /// <summary>
        /// 加权抽牌（D8 验收核心）：
        ///   1. 排除会造成「同轴重复惩罚」的牌 —— 这就是「叠 buff 组合排除」的落地点
        ///   2. 冲突角色的牌额外加权，让「大力士 + 手抖」这类组合高频出现
        /// 返回 null 表示所有牌都被排除（数据表配置有问题，调用方需容错）。
        /// </summary>
        public static DrawbackCard Draw(CharacterIdentity identity, System.Random rng, out int considered)
        {
            considered = 0;
            if (db == null || db.cards == null || db.cards.Length == 0)
            {
                return null;
            }

            List<DrawbackCard> pool = new();
            List<int> weights = new();

            foreach (DrawbackCard card in db.cards)
            {
                if (card == null)
                {
                    continue;
                }

                considered++;

                if (identity != null)
                {
                    // 角色本身已经慢，再给「跑得慢」的牌就是叠 buff，排除
                    if (card.excludeIfIdentitySlower && identity.speedScale < 1f)
                    {
                        continue;
                    }

                    // 角色本身已经手笨，再给「手抖」同理
                    if (card.excludeIfIdentityClumsier && identity.grabStiffnessScale < 1f)
                    {
                        continue;
                    }
                }

                int weight = Mathf.Max(1, card.baseWeight);
                if (identity != null && card.strongestConflictId == identity.id)
                {
                    weight += Mathf.Max(0, card.conflictWeightBonus);
                }

                pool.Add(card);
                weights.Add(weight);
            }

            if (pool.Count == 0)
            {
                return null;
            }

            int total = 0;
            foreach (int w in weights)
            {
                total += w;
            }

            int roll = rng.Next(total);
            for (int i = 0; i < pool.Count; i++)
            {
                roll -= weights[i];
                if (roll < 0)
                {
                    return pool[i];
                }
            }

            return pool[pool.Count - 1];
        }

        /// <summary>该牌与给定身份是否构成「最强冲突」，HUD 与日志用它做提示。</summary>
        public static bool IsConflict(CharacterIdentity identity, DrawbackCard card)
        {
            return identity != null && card != null && card.strongestConflictId == identity.id;
        }
    }
}
