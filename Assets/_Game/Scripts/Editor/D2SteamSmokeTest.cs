using System;
using System.Threading.Tasks;
using Steamworks;
using Steamworks.Data;
using UnityEditor;
using UnityEngine;

/// <summary>
/// D2 Steam 冒烟测试：初始化 Steam → 创建 4 人好友大厅 → 写房主数据 → 离开 → 关闭。
/// 只验证 API 通路，不验证双客户端好友邀请（需第二个 Steam 账号，按 Docs/SteamLobby.md 手动验收）。
/// </summary>
public static class D2SteamSmokeTest
{
    private const uint AppId = 480;
    private const int MaxMembers = 4;

    public static void Run()
    {
        try
        {
            if (!SteamClient.IsValid)
            {
                SteamClient.Init(AppId, asyncCallbacks: false);
            }

            Debug.Log($"[D2Smoke] user={SteamClient.Name} id={SteamClient.SteamId}");

            Task<Lobby?> createTask = SteamMatchmaking.CreateLobbyAsync(MaxMembers);
            DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(20);
            while (!createTask.IsCompleted && DateTime.UtcNow < deadline)
            {
                SteamClient.RunCallbacks();
                System.Threading.Thread.Sleep(16);
            }

            SteamClient.RunCallbacks();

            if (!createTask.IsCompleted)
            {
                Debug.LogError("[D2Smoke] CreateLobbyAsync timed out.");
                SafeShutdown();
                EditorApplication.Exit(2);
                return;
            }

            Lobby? lobby = createTask.Result;
            if (!lobby.HasValue)
            {
                Debug.LogError("[D2Smoke] CreateLobbyAsync returned null.");
                SafeShutdown();
                EditorApplication.Exit(3);
                return;
            }

            Lobby value = lobby.Value;
            value.SetFriendsOnly();
            value.SetJoinable(true);
            value.SetData("host", SteamClient.SteamId.ToString());
            value.SetData("hostName", SteamClient.Name);

            Debug.Log($"[D2Smoke] lobby={value.Id} members={value.MemberCount}/{value.MaxMembers} host={value.IsOwnedBy(SteamClient.SteamId)}");

            value.Leave();
            SafeShutdown();
            Debug.Log("D2_SMOKE_OK");
        }
        catch (Exception ex)
        {
            Debug.LogError("[D2Smoke] failed: " + ex);
            SafeShutdown();
            EditorApplication.Exit(4);
        }
    }

    private static void SafeShutdown()
    {
        try
        {
            if (SteamClient.IsValid)
            {
                SteamClient.Shutdown();
            }
        }
        catch
        {
            // ignore shutdown errors in smoke test
        }
    }
}
