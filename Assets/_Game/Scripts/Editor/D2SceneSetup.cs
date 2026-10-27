using System.IO;
using ScavengeNightmare.Networking;
using ScavengeNightmare.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// D2 场景装配：把 SteamLobbyService + LobbyDebugUI 挂到 Main 场景的 SteamLobby 物体上。
/// 用法：
/// Unity.exe -batchmode -quit -projectPath "D:\drlj\game" -executeMethod D2SceneSetup.Run -logFile "..."
/// </summary>
public static class D2SceneSetup
{
    private const string ScenePath = "Assets/_Game/Scenes/Main.unity";
    private const string RootName = "SteamLobby";

    public static void Run()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath);

        SteamLobbyService existing = Object.FindFirstObjectByType<SteamLobbyService>();
        GameObject root = existing != null ? existing.gameObject : GameObject.Find(RootName);

        if (root == null)
        {
            root = new GameObject(RootName);
        }

        if (root.GetComponent<SteamLobbyService>() == null)
        {
            root.AddComponent<SteamLobbyService>();
        }

        if (root.GetComponent<LobbyDebugUI>() == null)
        {
            root.AddComponent<LobbyDebugUI>();
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();

        Debug.Log("D2_SETUP_OK");
        Validate();
    }

    public static void Validate()
    {
        EditorSceneManager.OpenScene(ScenePath);

        SteamLobbyService service = Object.FindFirstObjectByType<SteamLobbyService>();
        LobbyDebugUI ui = Object.FindFirstObjectByType<LobbyDebugUI>();

        if (service == null)
        {
            throw new IOException("Main scene is missing SteamLobbyService.");
        }

        if (ui == null)
        {
            throw new IOException("Main scene is missing LobbyDebugUI.");
        }

        string appIdPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "steam_appid.txt"));
        if (!File.Exists(appIdPath))
        {
            throw new FileNotFoundException("steam_appid.txt missing at project root.", appIdPath);
        }

        string appId = File.ReadAllText(appIdPath).Trim();
        if (appId != SteamLobbyService.AppId.ToString())
        {
            throw new IOException($"steam_appid.txt should be {SteamLobbyService.AppId}, but was '{appId}'.");
        }

        const string managedPlugin = "Assets/Plugins/Facepunch.Steamworks/Facepunch.Steamworks.Win64.dll";
        const string nativePlugin = "Assets/Plugins/Facepunch.Steamworks/redistributable_bin/win64/steam_api64.dll";
        if (!File.Exists(managedPlugin) || !File.Exists(nativePlugin))
        {
            throw new FileNotFoundException("Facepunch Steamworks plugin files are missing.");
        }

        Debug.Log($"D2_VALIDATE_OK appId={appId} scene={ScenePath} service={service.name} ui={ui.name}");
    }
}
