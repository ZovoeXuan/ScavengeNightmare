using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Windows x64 构建：输出到 Builds/Windows，并复制开发用 steam_appid.txt。
/// 用法：Unity.exe -batchmode -quit -projectPath ... -executeMethod BuildWindows.Build -logFile ...
/// </summary>
public static class BuildWindows
{
    private const string ScenePath = "Assets/_Game/Scenes/Main.unity";
    private const string OutputPath = "Builds/Windows/ScavengeNightmare.exe";

    [MenuItem("Build/Windows x64")]
    public static void Build()
    {
        var options = new BuildPlayerOptions
        {
            scenes = new[] { ScenePath },
            locationPathName = OutputPath,
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
        string outputDir = Path.GetDirectoryName(Path.GetFullPath(OutputPath));
        string targetAppId = Path.Combine(outputDir, "steam_appid.txt");

        if (File.Exists(sourceAppId))
        {
            File.Copy(sourceAppId, targetAppId, true);
        }
        else
        {
            Debug.LogWarning("steam_appid.txt not found at project root; build will fail to init Steam.");
        }

        Debug.Log($"BUILD_OK {OutputPath} appIdFile={targetAppId}");
    }
}
