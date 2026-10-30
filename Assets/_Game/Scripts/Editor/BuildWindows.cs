using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Windows x64 构建：输出到 Builds/Windows，并复制开发用 steam_appid.txt。
/// 用法：Unity.exe -batchmode -quit -projectPath ... -executeMethod BuildWindows.Build -logFile ...
///
/// 地图选择（D12 起）：默认打 Main 沙盒（全部自动化用例的基线），
/// 传 <c>-map=supermarket</c> 打 1 号图白盒到独立目录 Builds/Supermarket，
/// 两者输出互不覆盖——否则验完白盒会把测试基线包冲掉。
/// </summary>
public static class BuildWindows
{
    private const string MainScene = "Assets/_Game/Scenes/Main.unity";
    private const string MarketScene = "Assets/_Game/Scenes/Supermarket.unity";
    private const string NightScene = "Assets/_Game/Scenes/Night1.unity";
    private const string SafehouseScene = "Assets/_Game/Scenes/Safehouse.unity";

    [MenuItem("Build/Windows x64")]
    public static void Build()
    {
        string map = ReadArg("-map=");
        bool market = string.Equals(map, "supermarket", StringComparison.OrdinalIgnoreCase);
        bool sprint4a = string.Equals(map, "sprint4a", StringComparison.OrdinalIgnoreCase);

        string scene = market ? MarketScene : (sprint4a ? NightScene : MainScene);
        string output = market
            ? "Builds/Supermarket/ScavengeNightmare.exe"
            : (sprint4a ? "Builds/Sprint4A/ScavengeNightmare.exe" : "Builds/Windows/ScavengeNightmare.exe");

        if (sprint4a && AssetDatabase.LoadAssetAtPath<SceneAsset>(NightScene) == null)
        {
            Debug.LogError($"BUILD_FAILED reason=scene_missing path={NightScene}（先跑 S4ASceneSetup.Setup）");
            if (Application.isBatchMode)
            {
                EditorApplication.Exit(1);
            }

            return;
        }

        if (market && AssetDatabase.LoadAssetAtPath<SceneAsset>(MarketScene) == null)
        {
            Debug.LogError($"BUILD_FAILED reason=scene_missing path={MarketScene}（先跑 D12SceneSetup.Setup）");
            if (Application.isBatchMode)
            {
                EditorApplication.Exit(1);
            }

            return;
        }

        var options = new BuildPlayerOptions
        {
            // sprint4a 包里夜路与安全屋必须都在：换关靠 ServerChangeScene 按名字加载，
            // 场景没进 Build Settings 会在运行时直接报「scene not found」，而且只在换关那一刻才炸。
            scenes = sprint4a ? new[] { NightScene, SafehouseScene } : new[] { scene },
            locationPathName = output,
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.None
        };

        BuildReport report = BuildPipeline.BuildPlayer(options);
        if (report.summary.result != BuildResult.Succeeded)
        {
            Debug.LogError($"BUILD_FAILED result={report.summary.result} errors={report.summary.totalErrors}");
            if (Application.isBatchMode)
            {
                EditorApplication.Exit(1);
            }

            return;
        }

        string sourceAppId = Path.GetFullPath("steam_appid.txt");
        string outputDir = Path.GetDirectoryName(Path.GetFullPath(output));
        string targetAppId = Path.Combine(outputDir, "steam_appid.txt");

        if (File.Exists(sourceAppId))
        {
            File.Copy(sourceAppId, targetAppId, true);
        }
        else
        {
            Debug.LogWarning("steam_appid.txt not found at project root; build will fail to init Steam.");
        }

        Debug.Log($"BUILD_OK {output} scene={(market ? "Supermarket" : (sprint4a ? "Night1" : "Main"))} appIdFile={targetAppId}");
    }

    private static string ReadArg(string prefix)
    {
        foreach (string a in Environment.GetCommandLineArgs())
        {
            if (a.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return a.Substring(prefix.Length);
            }
        }

        return null;
    }
}
