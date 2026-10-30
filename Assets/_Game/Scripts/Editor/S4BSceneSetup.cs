using Mirror;
using ScavengeNightmare.Combat;
using ScavengeNightmare.Economy;
using ScavengeNightmare.Monsters;
using ScavengeNightmare.Networking;
using ScavengeNightmare.Noise;
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
        private const string GameRootPrefab = "Assets/_Game/Prefabs/S4AGameRoot.prefab";
        private const string MonsterPrefab = "Assets/_Game/Prefabs/Monster_HollowEye.prefab";
        private const string MonsterMat = "Assets/_Game/Data/Monsters/HollowEyeMat.mat";
        private const string MonsterProfileDir = "Assets/_Game/Resources/Monsters";
        private const string HollowEyeProfilePath = MonsterProfileDir + "/HollowEye.asset";
        private const string ShopCatalogPath = "Assets/_Game/Resources/ShopCatalog.asset";

        private static readonly (string name, Vector3 pos)[] MonsterAnchors =
        {
            ("MonsterSpawn_1_Shelves", new Vector3(12f, 0.2f, 2f)),
            ("MonsterSpawn_2_Warehouse", new Vector3(-15f, 0.2f, -8f)),
            ("MonsterSpawn_3_BackDoor", new Vector3(0f, 0.2f, -17f)),
            ("MonsterSpawn_4_Stairs", new Vector3(16f, 0.2f, -5f)),
            ("MonsterSpawn_5_Mid", new Vector3(0f, 0.2f, 8f)),
            ("MonsterSpawn_6_West", new Vector3(-10f, 0.2f, 10f))
        };

        [MenuItem("拾荒惊魂/S4B · 生成/校验 生存系统")]
        public static void Setup()
        {
            // 先保证 4A 基线（Night1 / Safehouse / 商店 6 件）是最新的。
            S4ASceneSetup.Setup();

            EnsurePlayerComponents();
            EnsureGameRootNoiseState();
            EnsureMonsterAssets();
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
            Check(player != null && player.GetComponent<NoiseHud>() != null, "player_noise_hud");

            ShopCatalog catalog = AssetDatabase.LoadAssetAtPath<ShopCatalog>(ShopCatalogPath);
            Check(catalog != null && catalog.Count >= 6, $"shop_catalog_{(catalog != null ? catalog.Count : 0)}");
            Check(catalog != null && catalog.Find("self_injector") != null, "shop_self_injector");
            Check(catalog != null && catalog.Find("medkit") != null, "shop_medkit");

            GameObject gameRoot = AssetDatabase.LoadAssetAtPath<GameObject>(GameRootPrefab);
            Check(gameRoot != null && gameRoot.GetComponent<NoiseNetworkState>() != null, "gameroot_noise_state");

            GameObject monsterPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(MonsterPrefab);
            Check(monsterPrefab != null, "monster_prefab");
            Check(monsterPrefab != null && monsterPrefab.GetComponent<HollowEyeBrain>() != null, "monster_brain");
            Check(monsterPrefab != null && monsterPrefab.GetComponent<CharacterController>() != null, "monster_controller");
            Check(monsterPrefab != null && monsterPrefab.GetComponent<NetworkIdentity>() != null, "monster_identity");
            Check(monsterPrefab != null && monsterPrefab.GetComponent<NetworkTransformReliable>() != null, "monster_transform");
            Check(monsterPrefab != null && monsterPrefab.GetComponent<MonsterVisual>() != null, "monster_visual");
            Check(AssetDatabase.LoadAssetAtPath<MonsterProfile>(HollowEyeProfilePath) != null, "monster_profile");

            EditorSceneManager.OpenScene(NightScene, OpenSceneMode.Single);

            NetworkManager nm = FindFirst<NetworkManager>();
            Check(nm != null, "network_manager");
            Check(nm != null && nm.GetComponent<TeamWipeDetector>() != null, "team_wipe_detector");
            Check(nm != null && nm.GetComponent<S4BVitalsTest>() != null, "vitals_test");
            Check(nm != null && nm.GetComponent<S4BWipeTest>() != null, "wipe_test");
            Check(nm != null && nm.GetComponent<S4BReviveTest>() != null, "revive_test");
            Check(nm != null && nm.GetComponent<S4BNoiseTest>() != null, "noise_test");
            Check(nm != null && nm.GetComponent<S4BMonsterTest>() != null, "monster_test");
            Check(nm != null && nm.GetComponent<MonsterSpawner>() != null, "monster_spawner");
            Check(nm != null && nm.GetComponent<MonsterDirector>() != null, "monster_director");

            int monsterAnchors = Object.FindObjectsByType<MonsterSpawnAnchor>(FindObjectsSortMode.None).Length;
            Check(monsterAnchors == MonsterAnchors.Length, $"monster_anchors_{monsterAnchors}");
            int patrolAnchors = Object.FindObjectsByType<ScavengeNightmare.Gameplay.PatrolAnchor>(FindObjectsSortMode.None).Length;
            Check(patrolAnchors >= 5, $"patrol_anchors_{patrolAnchors}");

            bool monsterRegistered = false;
            if (nm != null && monsterPrefab != null && nm.spawnPrefabs != null)
            {
                monsterRegistered = nm.spawnPrefabs.Contains(monsterPrefab);
            }

            Check(monsterRegistered, "monster_registered");

            if (failures == 0)
            {
                Debug.Log($"[S4B] S4B_VALIDATE_OK vitals=1 revive=1 wipe=1 downedHud=1 shop6=1 tests=5 " +
                          $"monsters={monsterAnchors} noiseHud=1 monsterPrefab=1 noiseState=1");
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

            if (prefab.GetComponent<NoiseHud>() == null)
            {
                prefab.AddComponent<NoiseHud>();
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
            changed |= AddIfMissing<S4BNoiseTest>(nm.gameObject);
            changed |= AddIfMissing<S4BMonsterTest>(nm.gameObject);
            changed |= AddIfMissing<MonsterDirector>(nm.gameObject);

            MonsterSpawner spawner = nm.GetComponent<MonsterSpawner>();
            if (spawner == null)
            {
                spawner = nm.gameObject.AddComponent<MonsterSpawner>();
                changed = true;
            }

            GameObject monsterPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(MonsterPrefab);
            if (monsterPrefab != null && spawner != null)
            {
                SerializedObject spawnerSo = new(spawner);
                SerializedProperty prefabProp = spawnerSo.FindProperty("hollowEyePrefab");
                if (prefabProp != null && prefabProp.objectReferenceValue != monsterPrefab)
                {
                    prefabProp.objectReferenceValue = monsterPrefab;
                    spawnerSo.ApplyModifiedPropertiesWithoutUndo();
                    changed = true;
                }
            }

            changed |= EnsureMonsterAnchors();

            if (monsterPrefab != null)
            {
                bool wasRegistered = nm.spawnPrefabs != null && nm.spawnPrefabs.Contains(monsterPrefab);
                S4ASceneSetup.RegisterPrefab(scene, monsterPrefab);
                if (!wasRegistered)
                {
                    changed = true;
                }
            }

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

        /// <summary>6 个怪物生成锚点，按名字就地校正位置；多余的旧锚点删掉，保证数量恒为 6。</summary>
        private static bool EnsureMonsterAnchors()
        {
            bool changed = false;

            foreach (MonsterSpawnAnchor anchor in Object.FindObjectsByType<MonsterSpawnAnchor>(FindObjectsSortMode.None))
            {
                bool wanted = false;
                foreach ((string name, Vector3 pos) in MonsterAnchors)
                {
                    if (anchor.name == name)
                    {
                        wanted = true;
                        if ((anchor.transform.position - pos).sqrMagnitude > 0.0001f)
                        {
                            anchor.transform.position = pos;
                            EditorUtility.SetDirty(anchor);
                            changed = true;
                        }

                        break;
                    }
                }

                if (!wanted)
                {
                    Object.DestroyImmediate(anchor.gameObject);
                    changed = true;
                }
            }

            foreach ((string name, Vector3 pos) in MonsterAnchors)
            {
                bool exists = false;
                foreach (MonsterSpawnAnchor anchor in Object.FindObjectsByType<MonsterSpawnAnchor>(FindObjectsSortMode.None))
                {
                    if (anchor.name == name)
                    {
                        exists = true;
                        break;
                    }
                }

                if (exists)
                {
                    continue;
                }

                GameObject go = new(name);
                go.transform.position = pos;
                MonsterSpawnAnchor created = go.AddComponent<MonsterSpawnAnchor>();
                created.profileId = "holloweye";
                EditorUtility.SetDirty(created);
                EditorUtility.SetDirty(go);
                changed = true;
            }

            return changed;
        }

        /// <summary>GameRoot 上加噪音真值桥：怪物 AI 与 HUD 必须读同一份数据。</summary>
        private static void EnsureGameRootNoiseState()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(GameRootPrefab);
            if (prefab == null)
            {
                Debug.LogError($"[S4B] S4B_SETUP_FAIL reason=gameroot_prefab_missing path={GameRootPrefab}");
                return;
            }

            if (prefab.GetComponent<NoiseNetworkState>() != null)
            {
                return;
            }

            prefab.AddComponent<NoiseNetworkState>();
            EditorUtility.SetDirty(prefab);
            PrefabUtility.SavePrefabAsset(prefab);
            AssetDatabase.SaveAssets();
            Debug.Log("[S4B] S4B_NOISE_STATE_ADDED");
        }

        /// <summary>空眼：数值表 + 材质 + 占位胶囊预制体。数值全部在 SO 里，预制体只负责结构。</summary>
        private static void EnsureMonsterAssets()
        {
            S4ASceneSetup.EnsureFolder("Assets/_Game/Data");
            S4ASceneSetup.EnsureFolder("Assets/_Game/Data/Monsters");
            S4ASceneSetup.EnsureFolder("Assets/_Game/Resources");
            S4ASceneSetup.EnsureFolder(MonsterProfileDir);

            MonsterProfile profile = AssetDatabase.LoadAssetAtPath<MonsterProfile>(HollowEyeProfilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<MonsterProfile>();
                profile.id = "holloweye";
                profile.displayName = "空眼";
                AssetDatabase.CreateAsset(profile, HollowEyeProfilePath);
                EditorUtility.SetDirty(profile);
                AssetDatabase.SaveAssets();
                Debug.Log("[S4B] S4B_MON_PROFILE_CREATED path=" + HollowEyeProfilePath);
            }

            Material material = AssetDatabase.LoadAssetAtPath<Material>(MonsterMat);
            if (material == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null)
                {
                    shader = Shader.Find("Standard");
                }

                material = new Material(shader);
                AssetDatabase.CreateAsset(material, MonsterMat);
            }

            material.color = profile.bodyColor;
            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", profile.bodyColor);
            }

            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssets();

            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(MonsterPrefab);
            if (existing != null)
            {
                RepairMonsterPrefab(existing, profile, material);
                return;
            }

            GameObject root = new("Monster_HollowEye");

            CharacterController controller = root.AddComponent<CharacterController>();
            controller.height = 1.8f;
            controller.radius = 0.35f;
            controller.center = new Vector3(0f, 0.9f, 0f);
            controller.stepOffset = 0.5f;
            controller.slopeLimit = 50f;
            controller.skinWidth = 0.08f;

            GameObject body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "Body";
            body.transform.SetParent(root.transform, false);
            body.transform.localPosition = new Vector3(0f, 0.9f, 0f);
            body.transform.localScale = new Vector3(0.7f, 0.9f, 0.7f);
            Object.DestroyImmediate(body.GetComponent<Collider>());

            MeshRenderer bodyRenderer = body.GetComponent<MeshRenderer>();
            if (bodyRenderer != null && material != null)
            {
                bodyRenderer.sharedMaterial = material;
            }

            root.AddComponent<NetworkIdentity>();
            NetworkTransformReliable transformReliable = root.AddComponent<NetworkTransformReliable>();
            transformReliable.syncInterval = 0.066f;

            HollowEyeBrain brain = root.AddComponent<HollowEyeBrain>();
            MonsterVisual visual = root.AddComponent<MonsterVisual>();

            SerializedObject brainSo = new(brain);
            SerializedProperty profileProp = brainSo.FindProperty("profile");
            if (profileProp != null)
            {
                profileProp.objectReferenceValue = profile;
                brainSo.ApplyModifiedPropertiesWithoutUndo();
            }

            AssignVisual(visual, brain, bodyRenderer, body.transform);

            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, MonsterPrefab);
            Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets();
            Debug.Log("[S4B] S4B_MON_PREFAB_CREATED path=" + MonsterPrefab);
        }

        /// <summary>已存在的预制体就地补齐组件与引用（幂等），避免「预制体早就在、改了表却没生效」。</summary>
        private static void RepairMonsterPrefab(GameObject prefab, MonsterProfile profile, Material material)
        {
            bool changed = false;

            CharacterController controller = prefab.GetComponent<CharacterController>();
            if (controller == null)
            {
                controller = prefab.AddComponent<CharacterController>();
                changed = true;
            }

            Vector3 center = new(0f, 0.9f, 0f);
            if (Mathf.Abs(controller.height - 1.8f) > 0.001f || Mathf.Abs(controller.radius - 0.35f) > 0.001f
                || (controller.center - center).sqrMagnitude > 0.0001f || Mathf.Abs(controller.stepOffset - 0.5f) > 0.001f)
            {
                controller.height = 1.8f;
                controller.radius = 0.35f;
                controller.center = center;
                controller.stepOffset = 0.5f;
                controller.slopeLimit = 50f;
                controller.skinWidth = 0.08f;
                changed = true;
            }

            Transform body = prefab.transform.Find("Body");
            MeshRenderer bodyRenderer = body != null ? body.GetComponent<MeshRenderer>() : null;

            if (body != null && material != null && bodyRenderer != null && bodyRenderer.sharedMaterial != material)
            {
                bodyRenderer.sharedMaterial = material;
                changed = true;
            }

            if (prefab.GetComponent<NetworkIdentity>() == null)
            {
                prefab.AddComponent<NetworkIdentity>();
                changed = true;
            }

            NetworkTransformReliable transformReliable = prefab.GetComponent<NetworkTransformReliable>();
            if (transformReliable == null)
            {
                transformReliable = prefab.AddComponent<NetworkTransformReliable>();
                changed = true;
            }

            if (Mathf.Abs(transformReliable.syncInterval - 0.066f) > 0.0001f)
            {
                transformReliable.syncInterval = 0.066f;
                changed = true;
            }

            HollowEyeBrain brain = prefab.GetComponent<HollowEyeBrain>();
            if (brain == null)
            {
                brain = prefab.AddComponent<HollowEyeBrain>();
                changed = true;
            }

            SerializedObject brainSo = new(brain);
            SerializedProperty profileProp = brainSo.FindProperty("profile");
            if (profileProp != null && profileProp.objectReferenceValue == null)
            {
                profileProp.objectReferenceValue = profile;
                brainSo.ApplyModifiedPropertiesWithoutUndo();
                changed = true;
            }

            MonsterVisual visual = prefab.GetComponent<MonsterVisual>();
            if (visual == null)
            {
                visual = prefab.AddComponent<MonsterVisual>();
                changed = true;
            }

            if (visual != null && AssignVisual(visual, brain, bodyRenderer, body))
            {
                changed = true;
            }

            if (!changed)
            {
                return;
            }

            EditorUtility.SetDirty(prefab);
            PrefabUtility.SavePrefabAsset(prefab);
            AssetDatabase.SaveAssets();
            Debug.Log("[S4B] S4B_MON_PREFAB_REPAIRED");
        }

        private static bool AssignVisual(MonsterVisual visual, MonsterBrain brain, MeshRenderer renderer, Transform body)
        {
            SerializedObject so = new(visual);
            bool changed = false;

            changed |= AssignRef(so, "brain", brain);
            changed |= AssignRef(so, "bodyRenderer", renderer);
            changed |= AssignRef(so, "body", body);

            if (changed)
            {
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            return changed;
        }

        private static bool AssignRef(SerializedObject so, string field, Object value)
        {
            SerializedProperty prop = so.FindProperty(field);
            if (prop == null || prop.objectReferenceValue == value)
            {
                return false;
            }

            prop.objectReferenceValue = value;
            return true;
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
