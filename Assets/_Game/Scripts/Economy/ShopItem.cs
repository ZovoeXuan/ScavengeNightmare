using UnityEngine;

namespace ScavengeNightmare.Economy
{
    /// <summary>商品效果类型。加值只往后追加，不要插队——位掩码按目录顺序算，插队会让老存档的已购列表错位。</summary>
    public enum ShopEffect
    {
        None = 0,
        GrabBreak = 1,
        CartSlip = 2,
        Flashlight = 3,
        CarryPenalty = 4
    }

    /// <summary>
    /// 一件商品。数值全部落在资源上——改表不改代码。
    /// 4A 只上 4 件（手套 / 胶带 / 手电 / 体能），4B 再补 4 件。
    /// </summary>
    [CreateAssetMenu(menuName = "拾荒惊魂/商品", fileName = "ShopItem")]
    public sealed class ShopItem : ScriptableObject
    {
        [Tooltip("存档与日志里用的稳定 id，改了等于换了一件商品")]
        public string id;

        public string displayName;

        [TextArea(2, 4)]
        public string description;

        public int price = 400;

        [Tooltip("同一局内最多拥有几件。P0 全是 1（唯一性商品）")]
        public int maxOwned = 1;

        public ShopEffect effect = ShopEffect.None;

        [Tooltip("效果倍率：手套=脱手阈值倍率、胶带=滑移阈值倍率、体能=减速量倍率")]
        public float magnitude = 1f;

        [Tooltip("商店里的数字键位（1–4）")]
        public int hotkey = 1;
    }
}
