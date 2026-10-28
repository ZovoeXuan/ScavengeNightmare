using Mirror;
using ScavengeNightmare.Networking;
using UnityEngine;

namespace ScavengeNightmare.UI
{
    /// <summary>
    /// D9 断线与连接状态提示。
    ///
    /// 设计要点：断线时玩家最需要知道的三件事是「发生了什么」「为什么」「我现在能做什么」，
    /// 所以提示必须带原因文案和可点按钮，而不是一句「连接断开」。
    ///
    /// 「返回大厅」是计划要求的简易房主迁移：停掉当前网络会话，让 Steam 大厅面板重新可用，
    /// 由玩家重新开房或加入。**真正的房主迁移（保留游戏内状态）标注为 LOCKED**，
    /// 需要物件状态快照与所有权重建，未接入，不做假实现。
    /// </summary>
    public sealed class ConnectionStatusHUD : MonoBehaviour
    {
        [SerializeField] private NetworkHealthService health;

        private GUIStyle overlayStyle;
        private GUIStyle boxStyle;
        private GUIStyle titleStyle;
        private GUIStyle bodyStyle;
        private GUIStyle buttonStyle;
        private GUIStyle smallStyle;
        private bool wasDisconnected;

        private void Awake()
        {
            if (health == null)
            {
                health = FindAnyObjectByType<NetworkHealthService>();
            }
        }

        private void OnGUI()
        {
            EnsureStyles();

            if (health == null)
            {
                return;
            }

            switch (health.State)
            {
                case NetworkHealthService.Health.Disconnected:
                    DrawDisconnected();
                    break;

                case NetworkHealthService.Health.Warning:
                    DrawWarning();
                    break;
            }

            DrawServerLostBanner();
        }

        /// <summary>断线：全屏遮罩 + 原因 + 两个出口（返回大厅 / 退出程序）。</summary>
        private void DrawDisconnected()
        {
            GUI.Box(new Rect(0f, 0f, Screen.width, Screen.height), string.Empty, overlayStyle);

            float w = 460f;
            float h = 246f;
            Rect box = new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);
            GUILayout.BeginArea(box, boxStyle);

            GUILayout.Label("与房主的连接已断开", titleStyle);
            GUILayout.Space(6f);
            GUILayout.Label($"原因：{health.Reason}", bodyStyle);
            GUILayout.Space(4f);
            GUILayout.Label("当前这一局的进度不会保留。", bodyStyle);
            GUILayout.Space(14f);

            if (GUILayout.Button("返回大厅", buttonStyle, GUILayout.Height(34f)))
            {
                health.ReturnToLobby();
            }

            GUILayout.Space(6f);

            if (GUILayout.Button("退出游戏", buttonStyle, GUILayout.Height(30f)))
            {
                Application.Quit();
            }

            GUILayout.Space(10f);
            GUILayout.Label("房主迁移（保留本局进度）：LOCKED — 未实现", smallStyle);

            GUILayout.EndArea();
        }

        /// <summary>预警：不断线，只提示网络不稳，避免玩家以为卡死。</summary>
        private void DrawWarning()
        {
            float w = 380f;
            Rect box = new Rect((Screen.width - w) * 0.5f, Screen.height * 0.28f, w, 66f);
            GUILayout.BeginArea(box, boxStyle);
            GUILayout.Label("<b><color=#FFD27A>连接不稳定</color></b>", titleStyle);
            GUILayout.Label($"{health.Reason}，正在尝试恢复…", bodyStyle);
            GUILayout.EndArea();
        }

        /// <summary>房主视角：有玩家掉线时提示，便于 D9 验收「掉线有明确反馈」。</summary>
        private void DrawServerLostBanner()
        {
            if (!NetworkServer.active || string.IsNullOrEmpty(health.LastLostPlayer))
            {
                return;
            }

            if (health.SecondsSinceLost < 0f || health.SecondsSinceLost > 8f)
            {
                return;
            }

            float w = 360f;
            Rect box = new Rect((Screen.width - w) * 0.5f, 96f, w, 40f);
            GUILayout.BeginArea(box, boxStyle);
            GUILayout.Label($"<b><color=#FF9A9A>玩家掉线</color></b>  {health.LastLostPlayer}", bodyStyle);
            GUILayout.EndArea();
        }

        private void EnsureStyles()
        {
            if (overlayStyle != null)
            {
                return;
            }

            overlayStyle = new GUIStyle(GUI.skin.box)
            {
                normal = { background = MakeSolid(new Color(0f, 0f, 0f, 0.62f)) }
            };

            boxStyle = new GUIStyle(GUI.skin.box)
            {
                padding = new RectOffset(18, 18, 16, 16),
                alignment = TextAnchor.UpperLeft,
                normal = { background = MakeSolid(new Color(0.10f, 0.11f, 0.14f, 0.97f)) }
            };

            titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 17,
                fontStyle = FontStyle.Bold,
                richText = true,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(1f, 1f, 1f, 0.96f) }
            };

            bodyStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                richText = true,
                wordWrap = true,
                normal = { textColor = new Color(0.92f, 0.92f, 0.94f, 0.92f) }
            };

            smallStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                wordWrap = true,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(0.7f, 0.7f, 0.72f, 0.8f) }
            };

            buttonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 14,
                fontStyle = FontStyle.Bold
            };
        }

        private static Texture2D MakeSolid(Color color)
        {
            Texture2D tex = new Texture2D(1, 1);
            tex.SetPixel(0, 0, color);
            tex.Apply();
            return tex;
        }
    }
}
