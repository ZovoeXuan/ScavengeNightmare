using UnityEngine;

namespace ScavengeNightmare.Traits
{
    /// <summary>
    /// 场景启动时把角色/缺点牌库接进全局（与 D6 的 FrictionBootstrap 同一模式），
    /// 避免每个玩家组件各自引用资产。同时重置身份分配表，防止上一局的占用残留。
    /// </summary>
    public sealed class TraitBootstrap : MonoBehaviour
    {
        [SerializeField] private TraitDatabase database;

        public static TraitDatabase Database { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnLoad()
        {
            Database = null;
        }

        private void Awake()
        {
            Database = database;
            if (database != null)
            {
                TraitSettings.Use(database);
            }

            // 上一局的身份占用必须清掉，否则重开一局第二个人拿不到 1 号身份
            TraitAllocator.Reset();
        }

        private void OnDestroy()
        {
            if (Database == database)
            {
                Database = null;
            }
        }
    }
}
