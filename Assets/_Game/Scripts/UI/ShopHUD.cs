using ScavengeNightmare.Economy;
using UnityEngine;

namespace ScavengeNightmare.UI
{
    /// <summary>
    /// 商店 HUD（只读）。所有数字都来自 <see cref="TeamAccount"/> 的 SyncVar，
    /// 本地不维护任何账本副本——「客户端自己算一份钱」是双端不一致最经典的来源。
    ///
    /// 和钱包 HUD 分开摆：钱包在左下（y = height-116），商店在右上，两者不会互相盖住。
    /// </summary>
    public sealed class ShopHUD : MonoBehaviour
    {
        private GUIStyle boxStyle;
        private GUIStyle titleStyle;
        private GUIStyle rowStyle;
        private GUIStyle hintStyle;

        private void OnGUI()
        {
            ShopTerminal terminal = ShopTerminal.Instance;
            TeamAccount account = TeamAccount.Instance;

            if (terminal == null || account == null || !terminal.LocalPlayerInside())
            {
                return;
            }

            EnsureStyles();

            ShopCatalog catalog = ShopCatalog.Instance;
            int rows = catalog != null ? catalog.Count : 0;
            float height = 92f + rows * 22f;

            GUILayout.BeginArea(new Rect(Screen.width - 452f, 12f, 440f, height), boxStyle);
            GUILayout.Label("安全屋 · 商店", titleStyle);
            GUILayout.Label($"余额 ¥{account.Balance}　本次已购 {account.VisitPurchases}/{TeamAccount.HardCapPerVisit}", rowStyle);
            GUILayout.Space(4f);

            for (int i = 0; i < rows; i++)
            {
                ShopItem item = catalog.At(i);
                if (item == null)
                {
                    continue;
                }

                string tag;

                if (item.consumable)
                {
                    int charges = item.effect == ShopEffect.SelfInjector
                        ? account.SelfInjectorCharges
                        : (item.effect == ShopEffect.Medkit ? account.MedkitCharges : 0);
                    tag = $"¥{item.price}　库存 {charges}";
                }
                else
                {
                    tag = account.Owns(i) ? "【已拥有】" : $"¥{item.price}";
                }

                GUILayout.Label($"{item.hotkey}. {item.displayName}  {tag}", rowStyle);
            }

            GUILayout.Space(4f);
            GUILayout.Label(
                string.IsNullOrEmpty(account.LastPurchaseId)
                    ? "数字键购买　U 撤销上一笔"
                    : $"数字键购买　U 撤销 {account.LastPurchaseId}",
                hintStyle);

            GUILayout.EndArea();
        }

        private void EnsureStyles()
        {
            boxStyle ??= new GUIStyle(GUI.skin.box)
            {
                padding = new RectOffset(12, 12, 10, 10),
                alignment = TextAnchor.UpperLeft
            };

            titleStyle ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 16,
                fontStyle = FontStyle.Bold
            };

            rowStyle ??= new GUIStyle(GUI.skin.label) { fontSize = 14 };

            hintStyle ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                fontStyle = FontStyle.Italic
            };
        }
    }
}
