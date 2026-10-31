using UnityEngine;

namespace ScavengeNightmare.Economy
{
    /// <summary>
    /// 已购商品的效果查询。效果倍率**不缓存在玩家身上**——玩家跨关重建、商品又被撤销过，
    /// 缓存一定会漂。这里每次都从 <see cref="TeamAccount.OwnedMask"/> 现算，权威且不会过期。
    ///
    /// 目录很小（P0 8 件），线性查找的成本远低于一次同步出错的代价。
    /// </summary>
    public static class EquippedEffects
    {
        private static float MagnitudeOf(ShopEffect effect, float fallback)
        {
            TeamAccount account = TeamAccount.Instance;
            ShopCatalog catalog = ShopCatalog.Instance;

            if (account == null || catalog == null)
            {
                return fallback;
            }

            for (int i = 0; i < catalog.Count; i++)
            {
                ShopItem item = catalog.At(i);
                if (item != null && item.effect == effect && account.Owns(i))
                {
                    return item.magnitude;
                }
            }

            return fallback;
        }

        /// <summary>防滑手套：脱手阈值倍率。</summary>
        public static float GrabBreakScale => MagnitudeOf(ShopEffect.GrabBreak, 1f);

        /// <summary>打包胶带：横向滑移阈值倍率。</summary>
        public static float CartSlipScale => MagnitudeOf(ShopEffect.CartSlip, 1f);

        /// <summary>
        /// 握力训练：负重性能倍率（&gt;1 更好）。**同时**作用于负重减速量与负重硬上限。
        /// 只缩放减速量是不够的——42kg 的基准速度 3.335 已经在硬上限 3.135 之上，
        /// 缩小减速量会被上限一起钳住，实测比值只有 1.017（等于没效果）。
        /// 想让「练过的人能抱着保险箱走得更快」成立，上限本身也必须跟着抬。
        /// </summary>
        public static float CarryBoost => MagnitudeOf(ShopEffect.CarryPenalty, 1f);

        public static bool HasFlashlight => MagnitudeOf(ShopEffect.Flashlight, 0f) > 0f;

        public static float FlashlightRange => MagnitudeOf(ShopEffect.Flashlight, 0f);

        /// <summary>S4C-1：工具带把腰带槽 1→2。</summary>
        public static bool HasToolBelt => MagnitudeOf(ShopEffect.ToolBelt, 0f) > 0f;

        /// <summary>S4C-1：体能训练把连续冲刺 5.0s→7.5s。</summary>
        public static bool HasStaminaTraining => MagnitudeOf(ShopEffect.StaminaTraining, 0f) > 0f;
    }
}
