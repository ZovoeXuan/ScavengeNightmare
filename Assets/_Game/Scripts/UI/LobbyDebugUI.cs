using Mirror;
using ScavengeNightmare.Networking;
using Steamworks;
using UnityEngine;

namespace ScavengeNightmare.UI
{
    /// <summary>
    /// D2 临时大厅调试面板（IMGUI）。正式 UI 在 D32 替换。
    /// </summary>
    [RequireComponent(typeof(SteamLobbyService))]
    public sealed class LobbyDebugUI : MonoBehaviour
    {
        private SteamLobbyService service;
        private string joinLobbyId = string.Empty;
        private Vector2 memberScroll;
        private static Font uiFont;
        private static GUIStyle hintStyle;

        private static GUIStyle HintStyle
        {
            get
            {
                if (hintStyle == null)
                {
                    hintStyle = new GUIStyle(GUI.skin.label)
                    {
                        fontSize = 12,
                        richText = true
                    };

                    if (uiFont != null)
                    {
                        hintStyle.font = uiFont;
                    }
                }

                return hintStyle;
            }
        }

        // D9：联机进行中大厅面板已无用，且它 520x560 会盖住身份/联机面板。
        // 默认自动隐藏，F2 强制唤出（例如断线后想手动查大厅状态）。
        private bool forceVisible;

        private void Awake()
        {
            service = GetComponent<SteamLobbyService>();
        }

        private void Update()
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && kb.f2Key.wasPressedThisFrame)
            {
                forceVisible = !forceVisible;
            }
        }

        private void OnGUI()
        {
            if (service == null)
            {
                return;
            }

            EnsureFont();

            bool inSession = NetworkServer.active || NetworkClient.active;
            if (inSession && !forceVisible)
            {
                // 只留一行提示，告诉玩家面板去哪了
                GUI.Label(new Rect(12f, Screen.height - 150f, 420f, 20f),
                    "<color=#AAAAAA>已在联机中，大厅面板已隐藏（F2 唤出）</color>", HintStyle);
                return;
            }

            GUILayout.BeginArea(new Rect(12, 12, 520, 560), GUI.skin.box);
            GUILayout.BeginVertical();

            GUILayout.Label("拾荒惊魂 · D2 Steam 大厅（临时面板）");
            GUILayout.Space(4);

            if (!service.Initialized)
            {
                GUILayout.Label("状态：" + service.Status);
                if (!string.IsNullOrEmpty(service.LastError))
                {
                    GUILayout.Label("错误：" + service.LastError);
                }

                if (GUILayout.Button("重试 Steam 初始化", GUILayout.Height(28)))
                {
                    service.Initialize();
                }

                GUILayout.EndVertical();
                GUILayout.EndArea();
                return;
            }

            GUILayout.Label($"用户：{SteamClient.Name}  |  SteamID：{SteamClient.SteamId}");
            GUILayout.Label("状态：" + service.Status);
            GUILayout.Space(6);

            if (!service.IsInLobby)
            {
                GUI.enabled = !service.Busy;
                if (GUILayout.Button("创建大厅（4 人）", GUILayout.Height(30)))
                {
                    _ = service.CreateLobbyAsync();
                }

                GUILayout.Space(6);
                GUILayout.BeginHorizontal();
                GUILayout.Label("加入大厅 ID：", GUILayout.Width(100));
                joinLobbyId = GUILayout.TextField(joinLobbyId, GUILayout.Width(280));
                if (GUILayout.Button("加入", GUILayout.Width(80)))
                {
                    _ = service.JoinLobbyAsync(joinLobbyId);
                }
                GUILayout.EndHorizontal();
                GUI.enabled = true;

                GUILayout.Space(8);
                GUILayout.Label("提示：创建后点“邀请好友”打开 Steam 好友面板；好友接受邀请会自动加入。");
            }
            else
            {
                GUILayout.Label($"大厅 ID：{service.LobbyIdText}  |  {service.MemberCount}/{service.MaxLobbyMembers}");
                GUILayout.Label(service.IsHost ? "角色：房主" : "角色：成员");

                GUILayout.Space(6);
                GUILayout.BeginHorizontal();
                GUI.enabled = !service.Busy;
                if (GUILayout.Button("邀请好友", GUILayout.Height(28)))
                {
                    service.InviteFriend();
                }

                if (GUILayout.Button("复制大厅 ID", GUILayout.Height(28)))
                {
                    GUIUtility.systemCopyBuffer = service.LobbyIdText;
                    Debug.Log("[SteamLobby] 大厅 ID 已复制到剪贴板：" + service.LobbyIdText);
                }

                if (GUILayout.Button("离开大厅", GUILayout.Height(28)))
                {
                    service.LeaveLobby();
                }
                GUI.enabled = true;
                GUILayout.EndHorizontal();

                GUILayout.Space(8);
                GUILayout.Label($"成员（{service.MemberCount}/{service.MaxLobbyMembers}）：");
                memberScroll = GUILayout.BeginScrollView(memberScroll, GUILayout.Height(180));
                foreach (LobbyMemberInfo member in service.Members)
                {
                    string tags = (member.IsOwner ? " [房主]" : string.Empty) + (member.IsMe ? " [我]" : string.Empty);
                    GUILayout.Label($"{member.Name}{tags}  ({member.Id})");
                }
                GUILayout.EndScrollView();
            }

            GUILayout.Space(8);
            if (service.Busy)
            {
                GUILayout.Label("处理中…");
            }

            GUILayout.EndVertical();
            GUILayout.EndArea();
        }

        private static void EnsureFont()
        {
            if (uiFont != null)
            {
                GUI.skin.font = uiFont;
                return;
            }

            uiFont = Font.CreateDynamicFontFromOSFont(
                new[] { "Microsoft YaHei", "SimHei", "PingFang SC", "Arial Unicode MS", "Arial" }, 14);

            if (uiFont != null)
            {
                GUI.skin.font = uiFont;
            }
        }
    }
}
