using UnityEngine;

namespace ScavengeNightmare.Economy
{
    /// <summary>
    /// 4A 临时 HUD：只显示钱、交关进度和过关额。
    /// 位置放在左下，避开 D12/D9 已有的左侧与顶部面板。
    /// </summary>
    public sealed class WalletHUD : MonoBehaviour
    {
        private GUIStyle boxStyle;

        private void OnGUI()
        {
            if (boxStyle == null)
            {
                boxStyle = new GUIStyle(GUI.skin.box)
                {
                    fontSize = 16,
                    alignment = TextAnchor.UpperLeft
                };
            }

            TeamAccount account = TeamAccount.Instance;
            GUILayout.BeginArea(new Rect(12f, Screen.height - 96f, 420f, 84f), boxStyle);
            GUILayout.Label("<b>4A 公共资金（内存态）</b>", boxStyle);

            if (account == null)
            {
                GUILayout.Label("账本未就绪", boxStyle);
            }
            else
            {
                GUILayout.Label($"余额：{account.Balance}", boxStyle);
                GUILayout.Label($"交关进度：{account.LegProgress} / {account.LegQuota}（第 {account.LegIndex} 关）", boxStyle);
            }

            GUILayout.EndArea();
        }
    }
}
