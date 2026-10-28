using System.Collections.Generic;
using System.IO;
using Mirror;
using ScavengeNightmare.Gameplay;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ScavengeNightmare.EditorTools
{
    /// <summary>
    /// D6.5 物件联网装配：
    ///   1. 6 个物件 prefab 加 NetworkIdentity + NetworkTransformReliable(ClientToServer) + NetworkedProp
    ///   2. 写入并落盘 assetId（运行时 Spawn 必需）
    ///   3. 清掉场景里「摆好的物件实例」，改由 PropSpawner 开局生成并 Spawn
    ///   4. 把 6 个 prefab 注册进 NetworkManager.spawnPrefabs
    /// 批处理入口：-executeMethod ScavengeNightmare.EditorTools.D65NetworkSetup.Setup
    /// </summary>
    public static class D65NetworkSetup
    {
        private const string ScenePath = "Assets/_Game/Scenes/Main.unity";
        private const string PrefabFolder = "Assets/_Game/Prefabs/Props";

        private static readonly string[] Props = { "can", "box", "barrel", "statue", "cabinet", "vase" };

        private struct SpawnEntry
        {
            public string Id;
            public Vector3 Position;

            public SpawnEntry(string id, float x, float y, float z)
            {
                Id = id;
                Position = new Vector3(x, y, z);
            }
        }

        private static readonly SpawnEntry[] Layout =
        {
            new SpawnEntry("can", -2.4f, 0.36f, -1.4f), new SpawnEntry("box", -0.8f, 0.40f, -1.4f),
            new SpawnEntry("barrel", 0.8f, 0.95f, -1.4f), new SpawnEntry("statue", 2.4f, 0.75f, -1.4f),
            new SpawnEntry("can", -2.4f, 0.36f, -3.0f), new SpawnEntry("box", -0.8f, 0.40f, -3.0f),
            new SpawnEntry("barrel", 0.8f, 0.95f, -3.0f), new SpawnEntry("statue", 2.4f, 0.75f, -3.0f),
            new SpawnEntry("cabinet", -2.5f, 0.65f, 5.5f),
            new SpawnEntry("vase", 2.5f, 0.35f, 5.5f),
            new SpawnEntry("vase", 3.2f, 0.35f, 5.5f)
        };

        [MenuItem("拾荒惊魂/D6.5 · 装配物件联网")]
        public static void Setup()
        {
            foreach (string id in Props)
            {
                PatchPrefab($"{PrefabFolder}/{id}.prefab");
                PersistAssetId($"{PrefabFolder}/{id}.prefab");
            }

            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            RemoveScenePropHolders(scene);

            NetworkManager nm = Object.FindAnyObjectByType<NetworkManager>();
            if (nm == null)
            {
                Debug.LogError("[D65] 场景没有 NetworkManager");
                return;
            }

            RegisterSpawnPrefabs(nm);
            ConfigureSpawner(nm);

            if (nm.GetComponent<D65AutoTest>() == null)
            {
                nm.gameObject.AddComponent<D65AutoTest>();
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"D65_SETUP_OK prefabs={Props.Length} spawnEntries={Layout.Length}");
        }

        [MenuItem("拾荒惊魂/D6.5 · 校验物件联网")]
        public static void Validate()
        {
            int failures = 0;

            void Check(bool ok, string name)
            {
                if (!ok)
                {
                    failures++;
                    Debug.LogError($"[D65] D65_VALIDATE_FAIL check={name}");
                }
            }

            foreach (string id in Props)
            {
                string path = $"{PrefabFolder}/{id}.prefab";
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                Check(prefab != null, $"prefab_{id}");
                if (prefab == null)
                {
                    continue;
                }

                NetworkIdentity identity = prefab.GetComponent<NetworkIdentity>();
                Check(identity != null, $"identity_{id}");
                Check(identity != null && identity.assetId != 0u, $"asset_id_{id}");

                NetworkTransformReliable nt = prefab.GetComponent<NetworkTransformReliable>();
                Check(nt != null && nt.syncDirection == SyncDirection.ServerToClient, $"sync_direction_{id}");
                Check(prefab.GetComponent<NetworkedProp>() != null, $"networked_prop_{id}");
                Check(File.ReadAllText(path).Contains("_assetId: 0") == false, $"asset_id_persisted_{id}");
            }

            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Check(scene.IsValid(), "scene_open");

            NetworkManager nm = Object.FindAnyObjectByType<NetworkManager>();
            Check(nm != null, "network_manager");
            if (nm != null)
            {
                Check(nm.spawnPrefabs != null && nm.spawnPrefabs.Count >= Props.Length, $"spawn_prefabs_{nm.spawnPrefabs?.Count}");
                Check(nm.GetComponent<PropSpawner>() != null, "prop_spawner");
            }

            int stray = 0;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                stray += root.GetComponentsInChildren<GrabbableProp>(true).Length;
            }

            Check(stray == 0, $"no_stray_scene_props_{stray}");

            if (failures == 0)
            {
                Debug.Log($"D65_VALIDATE_OK prefabs={Props.Length} spawnPrefabs={nm.spawnPrefabs.Count} sceneStrayProps=0");
            }
            else
            {
                Debug.LogError($"[D65] D65_VALIDATE_FAILED count={failures}");
            }
        }

        private static void PatchPrefab(string path)
        {
            GameObject contents = PrefabUtility.LoadPrefabContents(path);

            if (contents.GetComponent<NetworkIdentity>() == null)
            {
                contents.AddComponent<NetworkIdentity>();
            }

            NetworkTransformReliable nt = contents.GetComponent<NetworkTransformReliable>();
            if (nt == null)
            {
                nt = contents.AddComponent<NetworkTransformReliable>();
            }

            // 服务端权威：固定 ServerToClient，运行时不再翻转
            nt.syncDirection = SyncDirection.ServerToClient;
            nt.syncPosition = true;
            nt.syncRotation = true;
            // 插值保持开启：同刻位置差实测 0.30m（约 80ms），插值能让远程端更平滑
            nt.interpolatePosition = true;
            nt.interpolateRotation = true;

            if (contents.GetComponent<NetworkedProp>() == null)
            {
                contents.AddComponent<NetworkedProp>();
            }

            PrefabUtility.SaveAsPrefabAsset(contents, path);
            PrefabUtility.UnloadPrefabContents(contents);
        }

        /// <summary>
        /// Mirror 的 assetId 只在「资产上下文」下可写，LoadPrefabContents 的预览对象写了会丢
        /// （getter 会重算，看着有值，文件里仍是 0）。直接改序列化字段并按文件内容校验。
        /// </summary>
        private static void PersistAssetId(string path)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            NetworkIdentity identity = prefab != null ? prefab.GetComponent<NetworkIdentity>() : null;
            if (identity == null)
            {
                return;
            }

            string guid = AssetDatabase.AssetPathToGUID(path);
            if (string.IsNullOrEmpty(guid))
            {
                return;
            }

            uint expected = NetworkIdentity.AssetGuidToUint(new System.Guid(guid));
            string full = Path.GetFullPath(path);
            string text = File.ReadAllText(full);
            const string marker = "_assetId: 0";

            if (text.Contains(marker))
            {
                text = text.Replace(marker, $"_assetId: {expected}");
                File.WriteAllText(full, text);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            }
        }

        private static void RemoveScenePropHolders(Scene scene)
        {
            List<GameObject> toKill = new();
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name == "PropPen" || root.name == "FrictionProps")
                {
                    toKill.Add(root);
                }
            }

            foreach (GameObject go in toKill)
            {
                Object.DestroyImmediate(go);
            }
        }

        private static void RegisterSpawnPrefabs(NetworkManager nm)
        {
            SerializedObject so = new SerializedObject(nm);
            SerializedProperty list = so.FindProperty("spawnPrefabs");
            if (list == null)
            {
                Debug.LogError("[D65] 找不到 spawnPrefabs 字段");
                return;
            }

            List<GameObject> prefabs = new();
            foreach (string id in Props)
            {
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabFolder}/{id}.prefab");
                if (prefab != null && !prefabs.Contains(prefab))
                {
                    prefabs.Add(prefab);
                }
            }

            list.arraySize = prefabs.Count;
            for (int i = 0; i < prefabs.Count; i++)
            {
                list.GetArrayElementAtIndex(i).objectReferenceValue = prefabs[i];
            }

            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void ConfigureSpawner(NetworkManager nm)
        {
            PropSpawner spawner = nm.GetComponent<PropSpawner>();
            if (spawner == null)
            {
                spawner = nm.gameObject.AddComponent<PropSpawner>();
            }

            SerializedObject so = new SerializedObject(spawner);
            SerializedProperty entries = so.FindProperty("entries");
            entries.arraySize = Layout.Length;

            for (int i = 0; i < Layout.Length; i++)
            {
                SerializedProperty e = entries.GetArrayElementAtIndex(i);
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabFolder}/{Layout[i].Id}.prefab");
                e.FindPropertyRelative("Prefab").objectReferenceValue = prefab;
                e.FindPropertyRelative("Position").vector3Value = Layout[i].Position;
                e.FindPropertyRelative("Euler").vector3Value = Vector3.zero;
            }

            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
