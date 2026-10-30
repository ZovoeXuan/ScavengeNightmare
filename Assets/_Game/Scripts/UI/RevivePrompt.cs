using ScavengeNightmare.Combat;
using UnityEngine;

namespace ScavengeNightmare.UI
{
    /// <summary>
    /// 救援提示：身边有倒地队友时显示「长按 E 救援」。
    /// 只读 PlayerVitals 的 SyncVar，不自己算进度。
    /// </summary>
    public sealed class RevivePrompt : MonoBehaviour
    {
        private const float RangeMeters = 3f;

        private GUIStyle promptStyle;
        private PlayerVitals nearest;
        private float nextScan;

        private void OnGUI()
        {
            PlayerVitals local = PlayerVitals.Local;
            if (local == null || local.IsDowned)
            {
                return;
            }

            if (Time.unscaledTime >= nextScan)
            {
                nextScan = Time.unscaledTime + 0.5f;
                nearest = FindNearest(local);
            }

            if (nearest == null)
            {
                return;
            }

            EnsureStyles();

            int percent = Mathf.RoundToInt(nearest.ReviveProgress * 100f);
            string text = percent > 0
                ? $"队友倒地　救援中 {percent}%　继续长按 E"
                : "队友倒地　长按 E 救援";

            GUI.Label(
                new Rect(0f, Screen.height - 120f, Screen.width, 30f),
                text,
                promptStyle);
        }

        private static PlayerVitals FindNearest(PlayerVitals local)
        {
            PlayerVitals best = null;
            float bestDistance = RangeMeters;

            foreach (PlayerVitals candidate in PlayerVitals.All)
            {
                if (candidate == null || candidate == local || !candidate.IsDowned)
                {
                    continue;
                }

                float distance = Vector3.Distance(local.transform.position, candidate.transform.position);
                if (distance <= bestDistance)
                {
                    bestDistance = distance;
                    best = candidate;
                }
            }

            return best;
        }

        private void EnsureStyles()
        {
            promptStyle ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 17,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(1f, 0.85f, 0.4f) }
            };
        }
    }
}
