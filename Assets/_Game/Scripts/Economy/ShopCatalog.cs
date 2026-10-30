using UnityEngine;

namespace ScavengeNightmare.Economy
{
    /// <summary>
    /// 商品目录。放在 <c>Resources/ShopCatalog.asset</c>，运行时 <see cref="Instance"/> 直接加载——
    /// 不用在场景里挂引用：账本本体是 DontDestroyOnLoad 的 GameRoot，跨场景重建时场景引用会丢，
    /// 而 Resources 加载不受换关影响。
    ///
    /// 目录顺序就是 <c>ownedMask</c> 的位序，**只许追加、不许重排**。
    /// </summary>
    [CreateAssetMenu(menuName = "拾荒惊魂/商品目录", fileName = "ShopCatalog")]
    public sealed class ShopCatalog : ScriptableObject
    {
        public const string ResourcePath = "ShopCatalog";

        [SerializeField] private ShopItem[] items;

        private static ShopCatalog instance;

        public static ShopCatalog Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = Resources.Load<ShopCatalog>(ResourcePath);
                }

                return instance;
            }
        }

        public int Count => items != null ? items.Length : 0;

        public ShopItem At(int index)
        {
            if (items == null || index < 0 || index >= items.Length)
            {
                return null;
            }

            return items[index];
        }

        public int IndexOf(string itemId)
        {
            if (items == null || string.IsNullOrEmpty(itemId))
            {
                return -1;
            }

            for (int i = 0; i < items.Length; i++)
            {
                if (items[i] != null && string.Equals(items[i].id, itemId, System.StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return -1;
        }

        public ShopItem Find(string itemId)
        {
            int index = IndexOf(itemId);
            return index >= 0 ? items[index] : null;
        }

        /// <summary>编辑器生成用：一次性把表塞进来。</summary>
        public void EditorSetItems(ShopItem[] value)
        {
            items = value;
        }
    }
}
