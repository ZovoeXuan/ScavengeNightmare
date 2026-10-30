using ScavengeNightmare.Combat;
using ScavengeNightmare.Economy;
using UnityEngine;

namespace ScavengeNightmare.UI
{
    /// <summary>
    /// 倒地遮罩：本地玩家倒地时显示，只读 PlayerVitals 的 SyncVar。
    /// 临时 IMGUI 件，与 4A 的 HUD 同一套风格；正式 UI 在 D32 / P1 替换。
    /// </summary>
    public sealed class DownedHud : MonoBehaviour
    {
        private GUIStyle titleStyle;
        private GUIStyle bodyStyle;
        private GUIStyle smallStyle;

        private void OnGUI()
        {
            PlayerVitals vitals = PlayerVitals.Local;
            if (vitals == null || !vitals.IsDowned)
            {
                return;
            }

            EnsureStyles();

            Color previous = GUI.color;
            GUI.color = new Color(0.35f, 0f, 0f, 0.45f);
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = previous;

            float top = Screen.height * 0.32f;
            GUI.Label(new Rect(0f, top, Screen.width, 44f), "倒地", titleStyle);

            TeamAccount account = TeamAccount.Instance;
            int bought = account != null ? account.SelfInjectorCharges : 0;

            string line;
            if (vitals.SelfReviveReady)
            {
                line = $"按 R 自救（免费 {vitals.FreeInjectors} / 已购 {bought}）";
            }
            else if (vitals.DownedSeconds < PlayerVitals.SelfReviveDelaySeconds)
            {
                float remaining = PlayerVitals.SelfReviveDelaySeconds - vitals.DownedSeconds;
                line = $"自救窗口 {remaining:F1}s　等待队友长按 E 救援";
            }
            else
            {
                line = "没有自救针　等待队友长按 E 救援";
            }

            GUI.Label(new Rect(0f, top + 48f, Screen.width, 32f), line, bodyStyle);

            if (vitals.ReviveProgress > 0.01f)
            {
                GUI.Label(
                    new Rect(0f, top + 82f, Screen.width, 28f),
                    $"队友救援中 {Mathf.RoundToInt(vitals.ReviveProgress * 100f)}%",
                    bodyStyle);
            }

            GUI.Label(
                new Rect(0f, top + 120f, Screen.width, 24f),
                "未提交的货物会在团灭时丢失",
                smallStyle);
        }

        private void EnsureStyles()
        {
            titleStyle ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 30,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Color.white }
            };

            bodyStyle ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 17,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(1f, 0.88f, 0.88f) }
            };

            smallStyle ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                fontStyle = FontStyle.Italic,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(0.9f, 0.75f, 0.75f) }
            };
        }
    }
}
