using ScavengeNightmare.Gameplay;
using UnityEngine;

namespace ScavengeNightmare.UI
{
    /// <summary>
    /// D4 临时抓取 HUD：中心准星 + 当前可交互提示 + 持物重量。
    /// 正式 HUD 在 D17 / D32，这里只保证「准星指向」和「东西有多重」看得见。
    /// </summary>
    public sealed class GrabHUD : MonoBehaviour
    {
        [SerializeField] private GrabController grab;
        [SerializeField] private bool showMass = true;

        private GUIStyle style;

        private void Awake()
        {
            if (grab == null)
            {
                grab = GetComponent<GrabController>();
            }
        }

        private static bool ShouldDraw(GrabController controller)
        {
            return controller != null && controller.enabled && controller.IsLocalControl;
        }

        private void OnGUI()
        {
            if (!ShouldDraw(grab))
            {
                return;
            }

            if (style == null)
            {
                style = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 13,
                    richText = true,
                    alignment = TextAnchor.MiddleCenter
                };
            }

            float cx = Screen.width * 0.5f;
            float cy = Screen.height * 0.5f;

            // 中心准星：有目标时放大并变色
            bool hasTarget = grab.HasFocus || grab.Held != null;
            float size = hasTarget ? 6f : 4f;
            Color old = GUI.color;
            GUI.color = hasTarget ? new Color(1f, 0.85f, 0.35f) : new Color(1f, 1f, 1f, 0.75f);
            GUI.DrawTexture(new Rect(cx - size * 0.5f, cy - size * 0.5f, size, size), Texture2D.whiteTexture);
            GUI.color = old;

            string hint = BuildHint();
            if (!string.IsNullOrEmpty(hint))
            {
                Rect area = new Rect(cx - 220f, cy + 26f, 440f, 40f);
                GUI.Label(area, hint, style);
            }
        }

        private string BuildHint()
        {
            GrabbableProp held = grab.Held;
            if (held != null)
            {
                return showMass ? $"持有中 · {MassText(held)} · 松开左键投掷" : "持有中 · 松开左键投掷";
            }

            GrabbableProp focus = grab.Focused;
            if (focus != null)
            {
                return showMass ? $"<b>左键</b> 抓取 · {MassText(focus)}" : "<b>左键</b> 抓取";
            }

            return string.Empty;
        }

        private static string MassText(GrabbableProp prop)
        {
            string name = prop.Profile != null ? prop.Profile.displayName : prop.name;
            return $"{name} {prop.Mass:F1}kg";
        }
    }
}
