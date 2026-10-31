using UnityEngine;
using UnityEngine.SceneManagement;

namespace ScavengeNightmare.Economy
{
    /// <summary>
    /// 安全屋商店终端（服务端权威）。
    ///
    /// 交互方式定案：**站在终端 3m 内 + 数字键 1/2/3/4 购买、U 撤销**。
    /// 不用 IMGUI 按钮是因为正式游玩时光标被 Locked，鼠标点不到屏幕上的按钮；
    /// 「客户端上报意图、服务端裁决」这套与公秤同构，双人验证时两边行为一致。
    ///
    /// **本类刻意不发 Command**：场景对象没有 owner，`connectionToClient` 恒为 null，
    /// 服务端复核距离会永远失败。Command 挂在 <see cref="ScavengeNightmare.Networking.PlayerInteract"/>
    /// 上（玩家自己拥有），再由它调到这里做裁决。
    /// </summary>
    public sealed class ShopTerminal : MonoBehaviour
    {
        public static ShopTerminal Instance { get; private set; }

        [SerializeField] private float zoneRadius = 3f;

        public float ZoneRadius => zoneRadius;

        private void Awake()
        {
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        /// <summary>服务端复核距离。客户端可以被改，服务端不能凭它信。
        /// 宁可给 0.5m 余量：宽一点也不该把「明明站在店里买不了」变成玩家侧的问题。</summary>
        public bool ServerPlayerInZone(Transform player)
        {
            return player != null && DistanceTo(player.position) <= zoneRadius + 0.5f;
        }

        public float DistanceTo(Vector3 position)
        {
            Vector3 a = position;
            Vector3 b = transform.position;
            a.y = 0f;
            b.y = 0f;
            return Vector3.Distance(a, b);
        }

        /// <summary>给 HUD 用：本地玩家是否在店内。</summary>
        public bool LocalPlayerInside()
        {
            Mirror.NetworkIdentity local = Mirror.NetworkClient.localPlayer;
            return local != null && DistanceTo(local.transform.position) <= zoneRadius;
        }

        /// <summary>服务端：按数字键买。找不到键位对应商品就报错，不静默吞掉。</summary>
        public void ServerHandleBuy(Transform player, int hotkey)
        {
            TeamAccount account = TeamAccount.Instance;
            ShopCatalog catalog = ShopCatalog.Instance;

            if (account == null || catalog == null)
            {
                return;
            }

            // S4C-3（T12 收口）：商店只在安全屋开张，夜路里不再有任何终端。
            // 旧版本把终端留在 Night1、只靠本闸门拒绝，并给 `-s4a*` 留了场景豁免——
            // 豁免一开，B8 的「夜路拒买」就永远验不到真实路径。豁免随终端一起删除。
            string sceneName = SceneManager.GetActiveScene().name;
            if (sceneName != "Safehouse")
            {
                Debug.Log($"[S4A] GATE_BUY_REJECT itemId=hotkey{hotkey} reason=wrong_scene balance={account.Balance} " +
                          $"visit={account.VisitPurchases} dist={DistanceTo(player.position):F2} scene={sceneName}");
                return;
            }

            if (!ServerPlayerInZone(player))
            {
                // 「不在店里」和「只读」是两种完全不同的失败，不能共用一个码
                Debug.Log($"[S4A] GATE_BUY_REJECT itemId=hotkey{hotkey} reason=out_of_zone balance={account.Balance} visit={account.VisitPurchases} dist={DistanceTo(player.position):F2}");
                return;
            }

            for (int i = 0; i < catalog.Count; i++)
            {
                ShopItem item = catalog.At(i);
                if (item != null && item.hotkey == hotkey)
                {
                    account.TryPurchase(item.id);
                    return;
                }
            }

            Debug.LogError($"[S4A] S4A_SHOP_FAIL reason=no_item_for_hotkey hotkey={hotkey}");
        }

        /// <summary>服务端：撤销最近一笔。没有可撤的就按 already_revoked 报，不假装成功。</summary>
        public void ServerHandleUndo(Transform player)
        {
            TeamAccount account = TeamAccount.Instance;
            if (account == null)
            {
                return;
            }

            if (!ServerPlayerInZone(player))
            {
                Debug.Log($"[S4A] GATE_BUY_UNDO_REJECT purchaseId={account.LastPurchaseId} state=active reason=window_closed balance={account.Balance}");
                return;
            }

            if (string.IsNullOrEmpty(account.LastPurchaseId))
            {
                Debug.Log($"[S4A] GATE_BUY_UNDO_REJECT purchaseId=- state=unknown reason=already_revoked balance={account.Balance}");
                return;
            }

            account.TryUndoPurchase(account.LastPurchaseId);
        }
    }
}
