using ScavengeNightmare.Gameplay;
using UnityEngine;

namespace ScavengeNightmare.UI
{
    /// <summary>
    /// D6 摩擦事件的屏幕提示（右上角最近 4 条），替代还没接入的正式音效与特效，
    /// 让玩家能明确知道「是窄门卡住了」「是我被砸了」「花瓶碎了」。
    /// D17/D32 正式 HUD 接管后可删。
    /// </summary>
    public sealed class FrictionHUD : MonoBehaviour
    {
        [SerializeField] private bool showPanel = true;
        private GUIStyle style;

        private void OnGUI()
        {
            if (!showPanel)
            {
                return;
            }

            var recent = FrictionEvents.Recent;
            if (recent == null || recent.Count == 0)
            {
                return;
            }

            EnsureStyle();

            float width = 320f;
            float height = 22f * recent.Count + 12f;
            Rect box = new Rect(Screen.width - width - 12f, 12f, width, height);
            GUILayout.BeginArea(box, GUI.skin.box);
            foreach (string line in recent)
            {
                GUILayout.Label(line, style);
            }

            GUILayout.EndArea();
        }

        private void EnsureStyle()
        {
            if (style != null)
            {
                return;
            }

            style = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                richText = true
            };
        }
    }
}
