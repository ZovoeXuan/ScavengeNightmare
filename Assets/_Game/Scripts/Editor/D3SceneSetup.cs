using kcp2k;
using Mirror;
using ScavengeNightmare.Networking;
using ScavengeNightmare.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ScavengeNightmare.EditorTools
{
    /// <summary>
    /// D3 联机骨架的编辑器装配脚本（幂等，可重复执行）。
    /// 产出：Assets/_Game/Prefabs/Player.prefab + Main 场景里的 Ground / 4 个出生点 / NetworkManager。
    /// 批处理入口：-executeMethod ScavengeNightmare.EditorTools.D3SceneSetup.Setup
    /// </summary>
    public static class D3SceneSetup
    {
        private const string ScenePath = "Assets/_Game/Scenes/Main.unity";
        private const string PrefabFolder = "Assets/_Game/Prefabs";
        private const string PlayerPrefabPath = "Assets/_Game/Prefabs/Player.prefab";
        private const int MaxPlayers = 4;
        private const ushort DefaultPort = 7777;

        [MenuItem("拾荒惊魂/D3 · 装配联机骨架")]
        public static void Setup()
        {
            EnsureFolder();

            GameObject prefab = EnsurePlayerPrefab();

            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            EnsureGround(scene);
            EnsureSpawnPoints(scene);
            EnsureNetworkManager(prefab);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"D3_SETUP_OK prefab={PlayerPrefabPath} scene={ScenePath} maxPlayers={MaxPlayers} port={DefaultPort}");
        }

        [MenuItem("拾荒惊魂/D3 · 校验联机骨架")]
        public static void Validate()
        {
            int failures = 0;

            void Check(bool condition, string name)
            {
                if (!condition)
                {
                    failures++;
                    Debug.LogError($"[D3] D3_VALIDATE_FAIL check={name}");
                }
            }

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            Check(prefab != null, "prefab_exists");

            if (prefab != null)
            {
                NetworkIdentity identity = prefab.GetComponent<NetworkIdentity>();
                Check(identity != null, "prefab_network_identity");
                Check(identity != null && identity.assetId != 0u, "prefab_asset_id");
                Check(prefab.GetComponent<CharacterController>() != null, "prefab_character_controller");
                Check(prefab.GetComponent<PlayerMotor>() != null, "prefab_player_motor");
                Check(prefab.GetComponent<NetworkTransformReliable>() != null, "prefab_network_transform");
                Check(prefab.GetComponentInChildren<Camera>(true) != null, "prefab_camera");
            }

            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Check(scene.IsValid(), "scene_open");

            NetworkManager nm = Object.FindAnyObjectByType<NetworkManager>();
            Check(nm != null, "network_manager");
            if (nm != null)
            {
                Check(nm.transport is KcpTransport, "transport_kcp");
                Check(nm.playerPrefab != null, "player_prefab_assigned");
                Check(nm.maxConnections == MaxPlayers, "max_connections_4");
                Check(nm.autoCreatePlayer, "auto_create_player");
                Check(nm.playerSpawnMethod == PlayerSpawnMethod.RoundRobin, "spawn_round_robin");
                Check(nm.GetComponent<D3AutoRunner>() != null, "auto_runner");
                Check(nm.GetComponent<NetDebugHUD>() != null, "net_debug_hud");
            }

            int spawnPoints = Object.FindObjectsByType<NetworkStartPosition>(FindObjectsSortMode.None).Length;
            Check(spawnPoints == MaxPlayers, "spawn_points_4");

            Check(Object.FindAnyObjectByType<SteamLobbyService>() != null, "steam_lobby_kept");

            if (failures == 0)
            {
                Debug.Log($"D3_VALIDATE_OK transport=kcp maxConnections={MaxPlayers} spawnPoints={spawnPoints} prefab={PlayerPrefabPath}");
            }
            else
            {
                Debug.LogError($"[D3] D3_VALIDATE_FAILED count={failures}");
            }
        }

        private static void EnsureFolder()
        {
            if (!AssetDatabase.IsValidFolder(PrefabFolder))
            {
                AssetDatabase.CreateFolder("Assets/_Game", "Prefabs");
            }
        }

        private static GameObject EnsurePlayerPrefab()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath) != null)
            {
                // 已存在则只补一次 assetId，避免重复生成覆盖手改内容。
                TouchAssetId();
                return AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            }

            GameObject root = new GameObject("Player");

            CharacterController controller = root.AddComponent<CharacterController>();
            controller.height = 2f;
            controller.radius = 0.5f;
            controller.center = Vector3.zero;
            controller.slopeLimit = 50f;
            controller.stepOffset = 0.4f;
            controller.skinWidth = 0.02f;

            root.AddComponent<NetworkIdentity>();

            NetworkTransformReliable transformSync = root.AddComponent<NetworkTransformReliable>();
            // 玩家由所属客户端权威：位置从客户端同步到服务端。
            transformSync.syncDirection = SyncDirection.ClientToServer;
            transformSync.syncPosition = true;
            transformSync.syncRotation = true;

            root.AddComponent<PlayerMotor>();

            GameObject mesh = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            mesh.name = "Mesh";
            Object.DestroyImmediate(mesh.GetComponent<Collider>());
            mesh.transform.SetParent(root.transform, false);

            GameObject cameraGo = new GameObject("PlayerCamera");
            cameraGo.tag = "MainCamera";
            cameraGo.transform.SetParent(root.transform, false);
            cameraGo.transform.localPosition = new Vector3(0f, 0.6f, 0f);
            Camera cam = cameraGo.AddComponent<Camera>();
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 200f;
            cam.fieldOfView = 70f;
            cameraGo.AddComponent<AudioListener>();
            cameraGo.SetActive(false);

            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
            Object.DestroyImmediate(root);

            TouchAssetId();
            return saved != null ? saved : AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
        }

        private static void TouchAssetId()
        {
            // Mirror 的 assetId 在编辑器里由 SetupIDs 从 GUID 生成，但 LoadPrefabContents
            // 得到的是预览对象、不被识别为 prefab，赋值不会落盘（实测 _assetId 仍为 0）。
            // 这里直接用 Mirror 自带的 GUID→uint 换算写进序列化字段。
            // 玩家走 playerPrefab 直连生成，不影响 D3；但 D4/D6 用 NetworkServer.Spawn
            // 生成拾荒物时必须靠 assetId 找到 prefab，所以现在就补上。
            string guid = AssetDatabase.AssetPathToGUID(PlayerPrefabPath);
            if (string.IsNullOrEmpty(guid))
            {
                Debug.LogError("[D3] 读不到 prefab GUID，assetId 无法生成。");
                return;
            }

            uint expected = NetworkIdentity.AssetGuidToUint(new System.Guid(guid));

            GameObject contents = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
            NetworkIdentity identity = contents.GetComponent<NetworkIdentity>();
            if (identity != null)
            {
                // 注意：在 LoadPrefabContents 的上下文里读 assetId 会走 SetupIDs，把值清回 0，
                // 所以这里只写不读，校验放到保存之后按资产上下文做。
                SerializedObject so = new SerializedObject(identity);
                SerializedProperty prop = so.FindProperty("_assetId");
                if (prop != null)
                {
                    prop.uintValue = expected;
                    so.ApplyModifiedPropertiesWithoutUndo();
                }
            }

            PrefabUtility.SaveAsPrefabAsset(contents, PlayerPrefabPath);
            PrefabUtility.UnloadPrefabContents(contents);
            AssetDatabase.SaveAssets();

            // Mirror 的 assetId getter 在编辑器里会自动重算，无法用来校验「是否真的落盘」，
            // 而运行时读的是序列化值。这里直接改序列化字段并按文件内容校验。
            string full = System.IO.Path.GetFullPath(PlayerPrefabPath);
            string text = System.IO.File.ReadAllText(full);
            string marker = "_assetId: 0";
            if (text.Contains(marker))
            {
                text = text.Replace(marker, $"_assetId: {expected}");
                System.IO.File.WriteAllText(full, text);
                AssetDatabase.ImportAsset(PlayerPrefabPath, ImportAssetOptions.ForceUpdate);
            }

            string persisted = System.IO.File.ReadAllText(full);
            if (persisted.Contains(marker))
            {
                Debug.LogError($"[D3] assetId 未落盘 expected={expected}");
            }
            else
            {
                Debug.Log($"[D3] D3_ASSET_ID assetId={expected}");
            }
        }

        private static void EnsureGround(Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name == "Ground")
                {
                    // 200m × 200m：4 人多开自动化测试要跑 30m 以上，50m 会跑出边界下坠。
                    root.transform.position = Vector3.zero;
                    root.transform.localScale = new Vector3(20f, 1f, 20f);
                    return;
                }
            }

            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.position = Vector3.zero;
            ground.transform.localScale = new Vector3(20f, 1f, 20f);
        }

        private static void EnsureSpawnPoints(Scene scene)
        {
            for (int i = 0; i < MaxPlayers; i++)
            {
                string name = $"SpawnPoint_{i + 1}";
                bool exists = false;
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    if (root.name == name)
                    {
                        exists = true;
                        break;
                    }
                }

                if (exists)
                {
                    continue;
                }

                float angle = (Mathf.PI * 2f / MaxPlayers) * i;
                GameObject spawn = new GameObject(name);
                spawn.AddComponent<NetworkStartPosition>();
                // y=1.05：胶囊脚底（本地 -1）刚好落在地面上方。
                spawn.transform.position = new Vector3(Mathf.Cos(angle) * 3.5f, 1.05f, Mathf.Sin(angle) * 3.5f);
                spawn.transform.LookAt(new Vector3(0f, 1.05f, 0f));
            }
        }

        private static void EnsureNetworkManager(GameObject playerPrefab)
        {
            NetworkManager nm = Object.FindAnyObjectByType<NetworkManager>();
            if (nm == null)
            {
                GameObject go = new GameObject("NetworkManager");
                nm = go.AddComponent<NetworkManager>();
            }

            KcpTransport transport = nm.GetComponent<KcpTransport>();
            if (transport == null)
            {
                transport = nm.gameObject.AddComponent<KcpTransport>();
            }

            transport.port = DefaultPort;

            if (nm.GetComponent<D3AutoRunner>() == null)
            {
                nm.gameObject.AddComponent<D3AutoRunner>();
            }

            if (nm.GetComponent<NetDebugHUD>() == null)
            {
                nm.gameObject.AddComponent<NetDebugHUD>();
            }

            nm.transport = transport;
            nm.playerPrefab = playerPrefab;
            nm.autoCreatePlayer = true;
            nm.maxConnections = MaxPlayers;
            // 默认 Random 会把多个客户端塞进同一个出生点，4 人开局直接重叠，改轮流分配。
            nm.playerSpawnMethod = PlayerSpawnMethod.RoundRobin;
            if (string.IsNullOrEmpty(nm.networkAddress))
            {
                nm.networkAddress = "127.0.0.1";
            }

            EditorUtility.SetDirty(nm);
        }
    }
}
