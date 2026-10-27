using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// D1 工程脚手架初始化：生成 Main 场景、目录规范、基础 PlayerSettings。
/// 用法（批处理）：
/// Unity.exe -batchmode -quit -projectPath "D:\drlj\game" -executeMethod D1ProjectSetup.Run -logFile "D:\drlj\game\Logs\d1_setup.log"
/// </summary>
public static class D1ProjectSetup
{
    private const string ScenePath = "Assets/_Game/Scenes/Main.unity";

    public static void Run()
    {
        EnsureDirectories();
        ConfigurePlayerSettings();
        CreateMainScene();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("D1_SETUP_OK");
        Validate();
    }

    /// <summary>
    /// 验收检查：Main 场景可打开、相机/灯光齐全、URP 管线生效、Build Settings 已登记。
    /// </summary>
    public static void Validate()
    {
        if (!File.Exists(ScenePath))
        {
            throw new FileNotFoundException("Main scene missing: " + ScenePath);
        }

        var scene = EditorSceneManager.OpenScene(ScenePath);
        if (!scene.IsValid())
        {
            throw new IOException("Main scene failed to open: " + ScenePath);
        }

        var camera = Object.FindFirstObjectByType<Camera>();
        var light = Object.FindFirstObjectByType<Light>();
        if (camera == null || light == null)
        {
            throw new IOException("Main scene requires a Camera and a Directional Light.");
        }

        RenderPipelineAsset pipeline = GraphicsSettings.currentRenderPipeline;
        if (pipeline == null)
        {
            throw new IOException("URP pipeline asset is not assigned in Graphics Settings.");
        }

        bool sceneInBuild = false;
        foreach (var s in EditorBuildSettings.scenes)
        {
            if (s.path == ScenePath && s.enabled)
            {
                sceneInBuild = true;
                break;
            }
        }

        if (!sceneInBuild)
        {
            throw new IOException("Main scene is not enabled in Build Settings.");
        }

        Debug.Log($"D1_VALIDATE_OK pipeline={pipeline.name} scene={ScenePath}");
    }

    private static void EnsureDirectories()
    {
        string[] dirs =
        {
            "Assets/_Game/Scripts",
            "Assets/_Game/Scenes",
            "Assets/_Game/Data",
            "Assets/_Game/Art",
            "Assets/_Game/Audio",
            "Assets/_Game/UI",
            "Logs"
        };

        foreach (string dir in dirs)
        {
            Directory.CreateDirectory(dir);
        }
    }

    private static void ConfigurePlayerSettings()
    {
        PlayerSettings.companyName = "Drlj";
        PlayerSettings.productName = "拾荒惊魂";
        PlayerSettings.colorSpace = ColorSpace.Linear;
        PlayerSettings.defaultScreenWidth = 1920;
        PlayerSettings.defaultScreenHeight = 1080;
        PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
    }

    private static void CreateMainScene()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

        var camera = Object.FindFirstObjectByType<Camera>();
        if (camera != null && camera.GetComponent<UniversalAdditionalCameraData>() == null)
        {
            camera.gameObject.AddComponent<UniversalAdditionalCameraData>();
        }

        if (!EditorSceneManager.SaveScene(scene, ScenePath))
        {
            throw new IOException("Failed to save scene: " + ScenePath);
        }

        EditorBuildSettings.scenes = new[]
        {
            new EditorBuildSettingsScene(ScenePath, true)
        };
    }
}
