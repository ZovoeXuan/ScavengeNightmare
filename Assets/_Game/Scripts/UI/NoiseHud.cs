using Mirror;
using ScavengeNightmare.Noise;
using UnityEngine;

namespace ScavengeNightmare.UI
{
    /// <summary>
    /// 4B 噪音 HUD（系统基线，不是商品）：等级条 + 主要来源 + 方向。
    /// 4A 时代这些只进日志与 CSV，玩家看不到；A9 要求「可归因」必须对玩家可见。
    /// 临时 IMGUI 件，正式 UI 在 P1 替换（技术债 T10）。
    /// </summary>
    public sealed class NoiseHud : MonoBehaviour
    {
        private static readonly string[] DirectionLabels = { "前", "右前", "右", "右后", "后", "左后", "左", "左前" };

        private GUIStyle labelStyle;

        /// <summary>测试读它：HUD 上显示的主要来源。</summary>
        public string DisplayedSource { get; private set; } = "none";

        public string DisplayedDirection { get; private set; } = "-";

        public float DisplayedLevel { get; private set; }

        private void Update()
        {
            NoiseNetworkState state = NoiseNetworkState.Instance;
            NetworkIdentity local = NetworkClient.localPlayer;

            if (state == null || local == null)
            {
                DisplayedSource = "none";
                DisplayedDirection = "-";
                DisplayedLevel = 0f;
                return;
            }

            DisplayedLevel = state.Level;
            DisplayedSource = string.IsNullOrEmpty(state.Source) ? "none" : state.Source;

            if (DisplayedSource == "none")
            {
                DisplayedDirection = "-";
                return;
            }

            Vector3 delta = state.SourcePosition - local.transform.position;
            delta.y = 0f;

            if (delta.sqrMagnitude < 0.01f)
            {
                DisplayedDirection = "脚下";
                return;
            }

            Vector3 forward = local.transform.forward;
            forward.y = 0f;
            float angle = Vector3.SignedAngle(forward, delta, Vector3.up);
            int sector = Mathf.RoundToInt(Mathf.DeltaAngle(0f, angle) / 45f);
            sector = ((sector % 8) + 8) % 8;
            DisplayedDirection = DirectionLabels[sector];
        }

        private void OnGUI()
        {
            NoiseNetworkState state = NoiseNetworkState.Instance;
            if (state == null)
            {
                return;
            }

            EnsureStyles();

            Rect panel = new(16f, 16f, 300f, 76f);
            GUI.color = new Color(0f, 0f, 0f, 0.45f);
            GUI.DrawTexture(panel, Texture2D.whiteTexture);
            GUI.color = Color.white;

            string source = DisplayedSource == "none" ? "无（安静）" : DisplayedSource;
            GUI.Label(new Rect(24f, 20f, 290f, 22f), $"噪音 {state.Level:F0} / {BandName(state.Band)}", labelStyle);
            GUI.Label(new Rect(24f, 42f, 290f, 22f), $"来源 {source}　方向 {DisplayedDirection}", labelStyle);

            float ratio = Mathf.Clamp01(state.Level / NoiseBus.MaxLevel);
            Rect bar = new(24f, 70f, 284f, 10f);
            GUI.color = new Color(0.15f, 0.15f, 0.15f, 0.9f);
            GUI.DrawTexture(bar, Texture2D.whiteTexture);
            GUI.color = ratio >= 0.7f ? new Color(0.9f, 0.2f, 0.15f) : (ratio >= 0.4f ? new Color(0.95f, 0.75f, 0.2f) : new Color(0.35f, 0.75f, 0.4f));
            GUI.DrawTexture(new Rect(bar.x, bar.y, bar.width * ratio, bar.height), Texture2D.whiteTexture);
            GUI.color = Color.white;
        }

        private static string BandName(string band)
        {
            switch (band)
            {
                case "hunt":
                    return "追猎";
                case "burst":
                    return "爆发";
                case "alert":
                    return "警觉";
                default:
                    return "安静";
            }
        }

        private void EnsureStyles()
        {
            labelStyle ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 14,
                normal = { textColor = Color.white }
            };
        }
    }
}
