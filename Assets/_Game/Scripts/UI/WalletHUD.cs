using ScavengeNightmare.Save;
using UnityEngine;

namespace ScavengeNightmare.Economy
{
    /// <summary>
    /// 4A 临时 HUD：钱、交关进度、过关额，以及 Story 2 起必须露出来的存档状态。
    /// 位置放在左下，避开 D12/D9 已有的左侧与顶部面板。
    ///
    /// 存档状态是给玩家看的，不是给日志看的：LOCKED / CORRUPT 一旦出现必须肉眼可见，
    /// 否则会出现「钱显示在 HUD 上、其实一分没落盘」的最坏体验。
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
            P0SaveService save = P0SaveService.Instance;

            string token = P0SaveService.HudToken;
            string saveLine = token switch
            {
                "SAVED" => $"存档：已落盘（txn {(save != null ? save.TxnCount : 0)}）",
                "RECOVERED" => $"存档：已从备份恢复（from {(save != null ? save.LoadedFrom : "?")}）",
                "CORRUPT" => "存档：全部损坏，已留证并新建",
                "LOCKED" => "存档：被另一进程占用（只读）",
                "FAILED" => "存档：不可用，本局不落盘",
                _ => "存档：未就绪"
            };

            bool alarming = token == "CORRUPT" || token == "LOCKED" || token == "FAILED";
            Color prev = GUI.color;
            if (alarming)
            {
                GUI.color = new Color(1f, 0.45f, 0.45f);
            }

            GUILayout.BeginArea(new Rect(12f, Screen.height - 116f, 460f, 104f), boxStyle);
            GUILayout.Label("<b>4A 公共资金</b>", boxStyle);

            if (account == null)
            {
                GUILayout.Label("账本未就绪", boxStyle);
            }
            else
            {
                GUILayout.Label($"余额：{account.Balance}", boxStyle);
                GUILayout.Label($"交关进度：{account.LegProgress} / {account.LegQuota}（第 {account.LegIndex} 关）", boxStyle);
            }

            GUILayout.Label(saveLine, boxStyle);
            GUILayout.EndArea();

            GUI.color = prev;
        }
    }
}
