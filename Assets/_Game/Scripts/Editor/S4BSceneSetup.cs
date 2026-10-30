using Mirror;
using ScavengeNightmare.Combat;
using ScavengeNightmare.Economy;
using ScavengeNightmare.Networking;
using ScavengeNightmare.Save;
using ScavengeNightmare.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ScavengeNightmare.EditorTools
{
    /// <summary>
    /// Sprint 4B 生存系统装配：把 PlayerVitals / 救援 / 团灭 / 倒地 HUD 挂到预制体与场景。
    /// 幂等；不新建场景，不手改 YAML。
    /// </summary>
    public static class S4BSceneSetup
    {
        private const string NightScene = "Assets/_Game/Scenes/Night1.unity";
        private const string PlayerPrefab = "Assets/_Game/Prefabs/Player.prefab";
        private const string ShopCatalogPath = "Assets/_Game/Resources/ShopCatalog.asset";

        [MenuItem("拾荒惊魂/S4B · 生成/校验 生存系统")]
        public static void Setup()
        {
            // 先保证 4A 基线（Night1 / Safehouse / 商店 6 件）是最新的。
            S4ASceneSetup.Setup();

            EnsurePlayerComponents();
            EnsureNightComponents();
            Validate();
        }

        [MenuItem("拾荒惊魂/S4B · 校验 生存系统")]
        public static void Validate()
        {
            int failures = 0;

            void Check(bool ok, string name)
            {
                if (!ok)
                {
                    failures++;
                    Debug.LogError($"[S4B] S4B_VALIDATE_FAIL check={name}");
                }
            }

            GameObject player = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefab);
            Check(player != null, "player_prefab");
            Check(player != null && player.GetComponent<PlayerVitals>() != null, "player_vitals");
            Check(player != null && player.GetComponent<ReviveInteractor>() != null, "player_revive");
            Check(player != null && player.GetComponent<DownedHud>() != null, "player_downed_hud");
            Check(player != null && player.GetComponent<RevivePrompt>() != null, "player_revive_prompt");

            ShopCatalog catalog = AssetDatabase.LoadAssetAtPath<ShopCatalog>(ShopCatalogPath);
            Check(catalog != null && catalog.Count >= 6, $"shop_catalog_{(catalog != null ? catalog.Count : 0)}");
            Check(catalog != null && catalog.Find("self_injector") != null, "shop_self_injector");
            Check(catalog != null && catalog.Find("medkit") != null, "shop_medkit");

            EditorSceneManager.OpenScene(NightScene, OpenSceneMode.Single);

            NetworkManager nm = FindFirst<NetworkManager>();
            Check(nm != null, "network_manager");
            Check(nm != null && nm.GetComponent<TeamWipeDetector>() != null, "team_wipe_detector");
            Check(nm != null && nm.GetComponent<S4BVitalsTest>() != null, "vitals_test");
            Check(nm != null && nm.GetComponent<S4BWipeTest>() != null, "wipe_test");
            Check(nm != null && nm.GetComponent<S4BReviveTest>() != null, "revive_test");

            if (failures == 0)
            {
                Debug.Log("[S4B] S4B_VALIDATE_OK vitals=1 revive=1 wipe=1 downedHud=1 shop6=1 tests=3");
            }
        }

        private static void EnsurePlayerComponents()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefab);
            if (prefab == null)
            {
                Debug.LogError($"[S4B] S4B_SETUP_FAIL reason=player_prefab_missing path={PlayerPrefab}");
                return;
            }

            bool changed = false;

            if (prefab.GetComponent<PlayerVitals>() == null)
            {
                prefab.AddComponent<PlayerVitals>();
                changed = true;
            }

            if (prefab.GetComponent<ReviveInteractor>() == null)
            {
                prefab.AddComponent<ReviveInteractor>();
                changed = true;
            }

            if (prefab.GetComponent<DownedHud>() == null)
            {
                prefab.AddComponent<DownedHud>();
                changed = true;
            }

            if (prefab.GetComponent<RevivePrompt>() == null)
            {
                prefab.AddComponent<RevivePrompt>();
                changed = true;
            }

            if (!changed)
            {
                return;
            }

            EditorUtility.SetDirty(prefab);
            PrefabUtility.SaveAsPrefabAsset(prefab, PlayerPrefab);
            AssetDatabase.SaveAssets();
            Debug.Log("[S4B] S4B_PLAYER_COMPONENTS_SAVED");
        }

        private static void EnsureNightComponents()
        {
            UnityEngine.SceneManagement.Scene scene = EditorSceneManager.OpenScene(NightScene, OpenSceneMode.Single);
            NetworkManager nm = FindFirst<NetworkManager>();

            if (nm == null)
            {
                Debug.LogError("[S4B] S4B_SETUP_FAIL reason=no_network_manager");
                return;
            }

            bool changed = false;
            changed |= AddIfMissing<TeamWipeDetector>(nm.gameObject);
            changed |= AddIfMissing<S4BVitalsTest>(nm.gameObject);
            changed |= AddIfMissing<S4BWipeTest>(nm.gameObject);
            changed |= AddIfMissing<S4BReviveTest>(nm.gameObject);

            if (!changed)
            {
                return;
            }

            EditorUtility.SetDirty(nm.gameObject);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("[S4B] S4B_NIGHT_COMPONENTS_SAVED");
        }

        private static bool AddIfMissing<T>(GameObject host) where T : Component
        {
            if (host.GetComponent<T>() != null)
            {
                return false;
            }

            host.AddComponent<T>();
            return true;
        }

        private static T FindFirst<T>() where T : Object
        {
            T[] found = Object.FindObjectsByType<T>(FindObjectsSortMode.None);
            return found.Length > 0 ? found[0] : null;
        }
    }
}
