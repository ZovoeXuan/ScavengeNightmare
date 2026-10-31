using Mirror;
using ScavengeNightmare.Economy;
using UnityEngine;

namespace ScavengeNightmare.UI
{
    /// <summary>
    /// 安全屋「继续」全队确认 HUD（只读）。
    ///
    /// 数字全部来自 <see cref="TeamAccount"/> 的 SyncVar：客户端不自己算 ready——
    /// 远端玩家松手这件事只有服务端看得见，本地算一份必然分叉。
    /// 只在本地面向安全屋出口时显示；夜路撤离门没有确认流程，不会出现。
    /// </summary>
    public sealed class LevelPortalHud : MonoBehaviour
    {
        [SerializeField] private float showRadius = 8f;

        private GUIStyle boxStyle;
        private GUIStyle titleStyle;
        private GUIStyle rowStyle;
        private GUIStyle hintStyle;

        private void OnGUI()
        {
            TeamAccount account = TeamAccount.Instance;
            LevelPortal portal = LevelPortal.Instance;
            NetworkIdentity local = NetworkClient.localPlayer;

            if (account == null || portal == null || local == null || account.ContinueTotal <= 0)
            {
                return;
            }

            Vector3 a = local.transform.position;
            Vector3 b = portal.transform.position;
            a.y = 0f;
            b.y = 0f;

            if (Vector3.Distance(a, b) > showRadius)
            {
                return;
            }

            EnsureStyles();

            int ready = account.ContinueReady;
            int total = account.ContinueTotal;
            int missing = Mathf.Max(0, total - ready);

            GUILayout.BeginArea(new Rect(Screen.width * 0.5f - 170f, Screen.height - 108f, 340f, 96f), boxStyle);
            GUILayout.Label("继续下一关", titleStyle);
            GUILayout.Label($"全队确认 {ready}/{total}　还差 {missing} 人", rowStyle);
            GUILayout.Label(
                missing > 0 ? "站到门口按住 E 确认（松开 2 秒作废）" : "全队已确认，正在进入下一关…",
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
