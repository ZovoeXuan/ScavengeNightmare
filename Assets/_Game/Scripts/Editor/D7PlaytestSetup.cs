using Mirror;
using ScavengeNightmare.Gameplay;
using ScavengeNightmare.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ScavengeNightmare.EditorTools
{
    /// <summary>
    /// D7 实测件装配（幂等）：
    ///   1. 场景里的 NetworkManager 挂 PlaytestRecorder（服务端落 CSV）
    ///   2. Player 预制体挂 PlaytestMarker（F5/F6/F7 本地标记 + Command 上报）
    /// 批处理入口：-executeMethod ScavengeNightmare.EditorTools.D7PlaytestSetup.Setup
    /// </summary>
    public static class D7PlaytestSetup
    {
        private const string ScenePath = "Assets/_Game/Scenes/Main.unity";
        private const string PlayerPrefabPath = "Assets/_Game/Prefabs/Player.prefab";

        [MenuItem("拾荒惊魂/D7 · 装配实测记录器")]
        public static void Setup()
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            NetworkManager nm = Object.FindAnyObjectByType<NetworkManager>();
            if (nm == null)
            {
                Debug.LogError("[D7] 场景没有 NetworkManager");
                return;
            }

            bool recorderAdded = false;
            if (nm.GetComponent<PlaytestRecorder>() == null)
            {
                nm.gameObject.AddComponent<PlaytestRecorder>();
                recorderAdded = true;
            }

            EditorUtility.SetDirty(nm.gameObject);

            bool markerAdded = false;
            GameObject contents = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
            if (contents.GetComponent<PlaytestMarker>() == null)
            {
                contents.AddComponent<PlaytestMarker>();
                markerAdded = true;
            }

            PrefabUtility.SaveAsPrefabAsset(contents, PlayerPrefabPath);
            PrefabUtility.UnloadPrefabContents(contents);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[D7] D7_SETUP_OK recorderAdded={recorderAdded} markerAdded={markerAdded}");
        }

        [MenuItem("拾荒惊魂/D7 · 校验实测件")]
        public static void Validate()
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            bool ok = true;
            PlaytestRecorder recorder = Object.FindAnyObjectByType<PlaytestRecorder>();
            ok &= Check(recorder != null, "recorder_in_scene");

            GameObject player = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            ok &= Check(player != null, "player_prefab");

            if (player != null)
            {
                ok &= Check(player.GetComponent<PlaytestMarker>() != null, "marker_on_player");
                ok &= Check(player.GetComponent<FeelSwitchHUD>() != null, "feel_hud_on_player");
            }

            Debug.Log(ok
                ? "[D7] D7_VALIDATE_OK recorder=1 marker=1 feelHud=1"
                : "[D7] D7_VALIDATE_FAIL");
        }

        private static bool Check(bool pass, string name)
        {
            if (!pass)
            {
                Debug.LogError($"[D7] D7_VALIDATE_FAIL check={name}");
            }

            return pass;
        }
    }
}
