using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Mirror;
using ScavengeNightmare.Gameplay;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ScavengeNightmare.EditorTools
{
    /// <summary>
    /// D12 · 1 号图「废弃超市」白盒。
    ///
    /// 为什么不直接改 Main.unity：那两个场景角色不同。
    ///   Main  = 20×20m 沙盒，D3/D4/D6/D8/D9 全部自动化用例的基线，动它会一次性废掉所有回归；
    ///   Supermarket = 40×40m 正式白盒图，只服务「能进图拾荒、动线合理、窄门与楼梯可用」。
    /// 所以做法是复制 Main 的配置骨架（NetworkManager / SteamLobby / TraitBootstrap / HUD），
    /// 清掉沙盒几何体，再按平面图坐标重建。
    ///
    /// 平面图坐标约定：x 向右、z 向上（+z 为入口侧），原点在地图中心，范围 ±20m。
    /// 批处理入口：-executeMethod ScavengeNightmare.EditorTools.D12SceneSetup.Setup
    /// </summary>
    public static class D12SceneSetup
    {
        private const string MainScene = "Assets/_Game/Scenes/Main.unity";
        private const string MarketScene = "Assets/_Game/Scenes/Supermarket.unity";
        private const string MaterialFolder = "Assets/_Game/Data/Materials";

        private const float HalfSize = 20f;
        private const float WallHeight = 3.5f;
        private const float WallThickness = 0.3f;

        /// <summary>从 Main 复制时保留的根对象：联机与身份配置，以及灯光/观察机位。</summary>
        private static readonly HashSet<string> KeepRoots = new()
        {
            "NetworkManager",
            "SteamLobby",
            "TraitBootstrap",
            "ConnectionStatusHUD",
            "Directional Light",
            "Main Camera",
        };

        private static Material matFloor;
        private static Material matWall;
        private static Material matProp;
        private static Material matAccent;

        [MenuItem("拾荒惊魂/D12 · 生成 1 号图白盒")]
        public static void Setup()
        {
            EnsureMaterialFolder();
            BuildMaterials();

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(MarketScene) == null)
            {
                if (!AssetDatabase.CopyAsset(MainScene, MarketScene))
                {
                    Debug.LogError("[D12] 复制 Main 场景失败");
                    return;
                }

                Debug.Log("[D12] D12_SCENE_COPIED from=Main.unity");
            }

            Scene scene = EditorSceneManager.OpenScene(MarketScene, OpenSceneMode.Single);
            CleanSandbox(scene);
            BuildMap();
            PlaceSpawnPoints();
            PlacePatrolAnchors();
            PlacePropAnchors();
            PatchSmokeTest();
            FramingCamera();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();

            // 关键：必须先关场景再改文件。场景还留在内存里时，后续 Refresh 或编辑器退出
            // 都会用内存中的旧数据再写一次盘，把文本修复覆盖掉（实测修完仍读到 0 个锚点）。
            EditorSceneManager.CloseScene(scene, true);

            int repaired = FixScriptReferences();
            Debug.Log($"[D12] D12_SCRIPT_REFS repaired={repaired}");

            AssetDatabase.ImportAsset(MarketScene, ImportAssetOptions.ForceUpdate);
            AssetDatabase.Refresh();

            // 重新打开并按“真正能加载出来”的数量统计：内存里的数量不算数，
            // 之前 SETUP_OK 报 5 个而 Validate 读到 1 个，就是被内存数量骗了。
            EditorSceneManager.OpenScene(MarketScene, OpenSceneMode.Single);
            int anchors = UnityEngine.Object.FindObjectsByType<PropSpawnAnchor>(FindObjectsSortMode.None).Length;
            int patrols = UnityEngine.Object.FindObjectsByType<PatrolAnchor>(FindObjectsSortMode.None).Length;
            int doors = UnityEngine.Object.FindObjectsByType<NarrowDoorWatcher>(FindObjectsSortMode.None).Length;

            Debug.Log($"[D12] D12_SETUP_OK scene=Supermarket propAnchors={anchors} patrolAnchors={patrols} narrowDoors={doors} repaired={repaired}");
        }

        [MenuItem("拾荒惊魂/D12 · 校验 1 号图白盒")]
        public static void Validate()
        {
            EditorSceneManager.OpenScene(MarketScene, OpenSceneMode.Single);

            int failures = 0;
            void Check(bool ok, string name)
            {
                if (!ok)
                {
                    failures++;
                    Debug.LogError($"[D12] D12_VALIDATE_FAIL check={name}");
                }
            }

            Check(UnityEngine.Object.FindAnyObjectByType<NetworkManager>() != null, "network_manager");
            Check(UnityEngine.Object.FindAnyObjectByType<NetworkStartPosition>() != null, "spawn_points");
            Check(UnityEngine.Object.FindAnyObjectByType<EvacZone>() != null, "evac_zone");

            int spawns = UnityEngine.Object.FindObjectsByType<NetworkStartPosition>(FindObjectsSortMode.None).Length;
            Check(spawns >= 4, $"spawn_count_{spawns}");

            int doors = UnityEngine.Object.FindObjectsByType<NarrowDoorWatcher>(FindObjectsSortMode.None).Length;
            Check(doors >= 2, $"narrow_doors_{doors}");

            // 先按 Include 取并打名字：排除式查询一旦少算，不打印就永远查不出是「未激活」还是「没挂上」
            PatrolAnchor[] paAll = UnityEngine.Object.FindObjectsByType<PatrolAnchor>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            PatrolAnchor[] paActive = UnityEngine.Object.FindObjectsByType<PatrolAnchor>(FindObjectsSortMode.None);
            string names = string.Empty;
            foreach (PatrolAnchor p in paAll)
            {
                names += (names.Length > 0 ? "," : string.Empty) + p.name + (p.gameObject.activeInHierarchy ? "" : "(inactive)");
            }

            Debug.Log($"[D12] D12_PATROL_DEBUG all={paAll.Length} active={paActive.Length} names={names}");
            int patrols = paAll.Length;
            Check(patrols >= 3 && patrols <= 5, $"patrol_anchors_{patrols}");

            int anchors = UnityEngine.Object.FindObjectsByType<PropSpawnAnchor>(FindObjectsSortMode.None).Length;
            Check(anchors >= 12, $"prop_anchors_{anchors}");

            // 楼梯：找名为 StairStep 的物体，判高度递进是否真的存在
            GameObject[] all = UnityEngine.Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None);
            int steps = 0;
            float maxTop = 0f;
            foreach (GameObject go in all)
            {
                if (!go.name.StartsWith("StairStep")) continue;
                steps++;
                maxTop = Mathf.Max(maxTop, go.transform.position.y + go.transform.localScale.y * 0.5f);
            }

            Check(steps >= 8, $"stair_steps_{steps}");
            Check(maxTop >= 2.5f, $"stair_height_{maxTop:F1}");

            Check(UnityEngine.Object.FindAnyObjectByType<PropSpawner>() != null, "prop_spawner");

            if (failures == 0)
            {
                Debug.Log($"[D12] D12_VALIDATE_OK spawns={spawns} doors={doors} patrols={patrols} anchors={anchors} steps={steps} stairTop={maxTop:F1}m");
            }
            else
            {
                Debug.LogError($"[D12] D12_VALIDATE_FAILED count={failures}");
            }
        }

        // ==================== 清理 ====================

        private static void CleanSandbox(Scene scene)
        {
            List<GameObject> doomed = new();
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                // Map 根是上一轮生成的，重建前先清掉，保证幂等（否则每跑一次叠一层）
                if (root.name == "Map" || !KeepRoots.Contains(root.name))
                {
                    doomed.Add(root);
                }
            }

            foreach (GameObject go in doomed)
            {
                UnityEngine.Object.DestroyImmediate(go);
            }

            Debug.Log($"[D12] D12_CLEANED removed={doomed.Count}");
        }

        // ==================== 地图主体 ====================

        private static void BuildMap()
        {
            GameObject map = new GameObject("Map");

            BuildGround(map.transform);
            BuildOuterWalls(map.transform);
            BuildInteriorWalls(map.transform);
            BuildShelves(map.transform);
            BuildCounters(map.transform);
            BuildCrates(map.transform);
            BuildStairsAndMezzanine(map.transform);
            BuildEvac(map.transform);
        }

        private static void BuildGround(Transform parent)
        {
            // 45×45 比墙体大一圈：玩家贴墙走时不该从边缘掉下去
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.SetParent(parent, false);
            ground.transform.position = Vector3.zero;
            ground.transform.localScale = new Vector3(4.5f, 1f, 4.5f);
            ground.GetComponent<Renderer>().sharedMaterial = matFloor;
        }

        private static void BuildOuterWalls(Transform parent)
        {
            GameObject root = Group(parent, "Walls_Outer");
            float h = WallHeight;
            float t = WallThickness;
            float y = h * 0.5f;
            float d = HalfSize;

            // 北侧（入口 z=+20）：中间留 6m 前门
            Box(root.transform, "Wall_N_West", new Vector3((-d - 3f) * 0.5f, y, d), new Vector3(d - 3f, h, t), matWall);
            Box(root.transform, "Wall_N_East", new Vector3((d + 3f) * 0.5f, y, d), new Vector3(d - 3f, h, t), matWall);

            // 南侧（撤离 z=-20）：中间留 4m 后门
            Box(root.transform, "Wall_S_West", new Vector3((-d - 2f) * 0.5f, y, -d), new Vector3(d - 2f, h, t), matWall);
            Box(root.transform, "Wall_S_East", new Vector3((d + 2f) * 0.5f, y, -d), new Vector3(d - 2f, h, t), matWall);

            Box(root.transform, "Wall_W", new Vector3(-d, y, 0f), new Vector3(t, h, d * 2f), matWall);
            Box(root.transform, "Wall_E", new Vector3(d, y, 0f), new Vector3(t, h, d * 2f), matWall);
        }

        private static void BuildInteriorWalls(Transform parent)
        {
            GameObject root = Group(parent, "Walls_Inner");
            float h = WallHeight;
            float t = WallThickness;
            float y = h * 0.5f;

            // 后门横墙 z=-14：中间留 1.2m 窄门 B
            Box(root.transform, "Wall_Mid_West", new Vector3((-11f - 0.6f) * 0.5f, y, -14f), new Vector3(11f - 0.6f, h, t), matWall);
            Box(root.transform, "Wall_Mid_East", new Vector3((20f + 0.6f) * 0.5f, y, -14f), new Vector3(20f - 0.6f, h, t), matWall);

            // 仓库隔墙 x=-11（z -14..-2），中间留 1.2m 窄门 A
            Box(root.transform, "Wall_Warehouse_N", new Vector3(-11f, y, -5.4f), new Vector3(t, h, 6.8f), matWall);
            Box(root.transform, "Wall_Warehouse_S", new Vector3(-11f, y, -10.3f), new Vector3(t, h, 7.4f), matWall);

            AddNarrowDoor(root.transform, "GapA", new Vector3(-11f, 1f, -6f), new Vector3(0.4f, 2f, 1.2f));
            AddNarrowDoor(root.transform, "GapB", new Vector3(0f, 1f, -14f), new Vector3(1.2f, 2f, 0.4f));
        }

        /// <summary>窄门 = 触发器窄区 + NarrowDoorWatcher，复用 D6 的卡位反馈。</summary>
        private static void AddNarrowDoor(Transform parent, string name, Vector3 pos, Vector3 size)
        {
            GameObject zone = new GameObject(name);
            zone.transform.SetParent(parent, false);
            zone.transform.position = pos;

            BoxCollider box = zone.AddComponent<BoxCollider>();
            box.size = size;
            box.isTrigger = true;

            NarrowDoorWatcher watcher = zone.AddComponent<NarrowDoorWatcher>();
            EditorUtility.SetDirty(watcher);
            EditorUtility.SetDirty(zone);
        }

        private static void BuildShelves(Transform parent)
        {
            GameObject root = Group(parent, "Shelves");
            float[] zs = { 13.5f, 10.5f, 7.5f, 4.5f };

            for (int i = 0; i < zs.Length; i++)
            {
                // 货架长 13m（x -10..3）、深 0.8m、高 1.2m，排间留 2.2m 通道
                Box(root.transform, $"Shelf_{i + 1}", new Vector3(-3.5f, 0.6f, zs[i]),
                    new Vector3(13f, 1.2f, 0.8f), matProp);
            }
        }

        private static void BuildCounters(Transform parent)
        {
            GameObject root = Group(parent, "Checkout");
            float[] xs = { 9f, 11.5f, 14f, 16.5f };

            foreach (float x in xs)
            {
                // 收银台 0.9×1.1×4，沿 z 排布，台间 2.5m 可穿行
                Box(root.transform, $"Counter_{x:F1}", new Vector3(x, 0.55f, 2f),
                    new Vector3(0.9f, 1.1f, 4f), matAccent);
            }
        }

        private static void BuildCrates(Transform parent)
        {
            GameObject root = Group(parent, "Warehouse_Crates");
            Vector3[] spots =
            {
                new Vector3(-17f, 0.75f, -4f),
                new Vector3(-14f, 0.75f, -5.5f),
                new Vector3(-17f, 0.75f, -8f),
                new Vector3(-14f, 0.75f, -10f),
                new Vector3(-16f, 0.75f, -12f),
            };

            for (int i = 0; i < spots.Length; i++)
            {
                Box(root.transform, $"Crate_{i + 1}", spots[i], new Vector3(2f, 1.5f, 2f), matProp);
            }
        }

        /// <summary>
        /// 楼梯 + 夹层。12 级台阶，每级升 0.25m、进深 0.35m → 总升高 3.0m、水平跨度 4.2m。
        /// 步高 0.25 低于 CharacterController 的 stepOffset(0.4)，可以直接走上去不需要跳跃。
        /// </summary>
        private static void BuildStairsAndMezzanine(Transform parent)
        {
            GameObject root = Group(parent, "Stairs");

            const int steps = 12;
            const float rise = 0.25f;
            const float run = 0.35f;
            const float width = 3.2f;
            const float x = 16.5f;
            const float zStart = -7f;

            for (int i = 0; i < steps; i++)
            {
                float h = (i + 1) * rise;
                float z = zStart - (i + 0.5f) * run;
                Box(root.transform, $"StairStep_{i + 1}", new Vector3(x, h * 0.5f, z), new Vector3(width, h, run), matProp);
            }

            // 夹层平台：楼梯顶端接在平台南缘（z=-11.4）
            GameObject mezz = Box(root.transform, "Mezzanine", new Vector3(15f, 2.85f, -12.7f),
                new Vector3(8f, 0.3f, 2.6f), matFloor);

            // 护栏：只在开口侧（北缘）与两端加，留出楼梯上来的口
            Box(mezz.transform.parent, "Rail_N", new Vector3(15f, 3.5f, -11.4f), new Vector3(8f, 1f, 0.15f), matAccent);
            Box(mezz.transform.parent, "Rail_W", new Vector3(11f, 3.5f, -12.7f), new Vector3(0.15f, 1f, 2.6f), matAccent);
            Box(mezz.transform.parent, "Rail_E", new Vector3(19f, 3.5f, -12.7f), new Vector3(0.15f, 1f, 2.6f), matAccent);
        }

        private static void BuildEvac(Transform parent)
        {
            GameObject evac = new GameObject("EvacZone");
            evac.transform.SetParent(parent, false);
            evac.transform.position = new Vector3(0f, 1.5f, -18.5f);

            BoxCollider box = evac.AddComponent<BoxCollider>();
            box.size = new Vector3(4f, 3f, 2.6f);
            box.isTrigger = true;

            // 同 PatrolAnchor：EvacZone 没有可序列化字段，不标脏会退化成本地 fileID 引用，
            // 重开场景后组件丢失（实测 D12_VALIDATE_FAIL check=evac_zone）
            EvacZone zone = evac.AddComponent<EvacZone>();
            EditorUtility.SetDirty(zone);
            EditorUtility.SetDirty(evac);

            // 地面标记，白盒里也能一眼看到撤离位置
            Box(parent, "EvacMarker", new Vector3(0f, 0.02f, -18.5f), new Vector3(4f, 0.04f, 2.6f), matAccent);
        }

        // ==================== 生成点与锚点 ====================

        private static void PlaceSpawnPoints()
        {
            GameObject root = Group(null, "SpawnPoints");
            float[] xs = { -4f, -1.3f, 1.3f, 4f };

            for (int i = 0; i < xs.Length; i++)
            {
                GameObject spawn = new GameObject($"SpawnPoint_{i + 1}");
                spawn.transform.SetParent(root.transform, false);
                // y=1.05：胶囊脚底（本地 -1）刚好落在地面上方（D3 的既有约定）
                spawn.transform.position = new Vector3(xs[i], 1.05f, 17f);
                spawn.transform.LookAt(new Vector3(xs[i], 1.05f, 0f));
                NetworkStartPosition np = spawn.AddComponent<NetworkStartPosition>();
                EditorUtility.SetDirty(np);
                EditorUtility.SetDirty(spawn);
            }
        }

        private static void PlacePatrolAnchors()
        {
            GameObject root = Group(null, "PatrolAnchors");
            (string name, Vector3 pos)[] spots =
            {
                ("Patrol_1_Shelves", new Vector3(0f, 0.2f, 8f)),
                ("Patrol_2_Checkout", new Vector3(12f, 0.2f, 2f)),
                ("Patrol_3_Warehouse", new Vector3(-15f, 0.2f, -8f)),
                ("Patrol_4_BackDoor", new Vector3(0f, 0.2f, -17f)),
                ("Patrol_5_Stairs", new Vector3(16f, 0.2f, -5f)),
            };

            foreach ((string name, Vector3 pos) in spots)
            {
                GameObject go = new GameObject(name);
                go.transform.SetParent(root.transform, false);
                go.transform.position = pos;

                // 必须显式赋值并 SetDirty：只 AddComponent 而不改任何字段时，
                // Unity 会把 m_Script 写成 scene 内的本地 fileID（无 guid），重开场景后解析不到，
                // 实测 5 个锚点只剩 1 个能加载。PropSpawnAnchor 之所以正常，是因为添加后改了 profileId。
                PatrolAnchor anchor = go.AddComponent<PatrolAnchor>();
                anchor.radius = 4f;
                EditorUtility.SetDirty(anchor);
                EditorUtility.SetDirty(go);
            }
        }

        /// <summary>
        /// 18 个物件锚点，覆盖 6 个物件档：轻货在货架、重货在仓库、易碎在收银区。
        /// 分布刻意做成「越值钱的越难搬」：花瓶在收银台（要经过货架迷宫），雕像在仓库深处（要过窄门 A）。
        /// </summary>
        private static void PlacePropAnchors()
        {
            GameObject root = Group(null, "PropAnchors");

            (string id, Vector3 pos)[] spots =
            {
                // 货架区 8 件（架顶 y=1.5）：罐头/花瓶/纸箱，轻但数量多
                ("can", new Vector3(-8f, 1.5f, 13.5f)),
                ("vase", new Vector3(-2f, 1.5f, 13.5f)),
                ("box", new Vector3(-8f, 1.5f, 10.5f)),
                ("can", new Vector3(-2f, 1.5f, 10.5f)),
                ("box", new Vector3(-8f, 1.5f, 7.5f)),
                ("vase", new Vector3(-2f, 1.5f, 7.5f)),
                ("box", new Vector3(-8f, 1.5f, 4.5f)),
                ("can", new Vector3(-2f, 1.5f, 4.5f)),

                // 仓库区 6 件（地面 y=0.6）：重货，且必须过窄门 A 才能运出
                ("barrel", new Vector3(-16f, 0.6f, -4.5f)),
                ("cabinet", new Vector3(-13f, 0.6f, -6f)),
                ("statue", new Vector3(-16f, 0.6f, -9f)),
                ("box", new Vector3(-13f, 0.6f, -10.5f)),
                ("barrel", new Vector3(-15f, 0.6f, -12.5f)),
                ("cabinet", new Vector3(-12.5f, 0.6f, -13.5f)),

                // 收银区 4 件（台面 y=1.3）：易碎花瓶与小额货，撤离前最后一段
                ("vase", new Vector3(9f, 1.3f, 2f)),
                ("can", new Vector3(11.5f, 1.3f, 2f)),
                ("box", new Vector3(14f, 1.3f, 2f)),
                ("vase", new Vector3(16.5f, 1.3f, 2f)),
            };

            foreach ((string id, Vector3 pos) in spots)
            {
                GameObject go = new GameObject($"Prop_{id}_{root.transform.childCount + 1}");
                go.transform.SetParent(root.transform, false);
                go.transform.position = pos;
                PropSpawnAnchor anchor = go.AddComponent<PropSpawnAnchor>();
                anchor.profileId = id;
                EditorUtility.SetDirty(anchor);
                EditorUtility.SetDirty(go);
            }
        }

        private static void PatchSmokeTest()
        {
            NetworkManager nm = UnityEngine.Object.FindAnyObjectByType<NetworkManager>();
            if (nm == null)
            {
                return;
            }

            if (nm.GetComponent<Networking.D12SmokeTest>() == null)
            {
                nm.gameObject.AddComponent<Networking.D12SmokeTest>();
            }
        }

        private static void FramingCamera()
        {
            Camera cam = UnityEngine.Object.FindAnyObjectByType<Camera>();
            if (cam == null) return;

            // 观察机位：连接前俯瞰全图，进游戏后会被本地玩家相机顶掉（PlayerMotor 的既有行为）
            cam.transform.position = new Vector3(0f, 34f, -6f);
            cam.transform.rotation = Quaternion.Euler(62f, 0f, 0f);
        }

        // ==================== 工具 ====================

        /// <summary>
        /// 确定性修复脚本引用 —— 这一步不能省，也不能靠重跑碰运气。
        ///
        /// 现象：从 Main 复制场景后 AddComponent 挂上的组件，m_Script 有时被写成
        ///   m_Script: {fileID: 1473474789}                        ← 场景内本地 fileID，无 guid
        /// 而不是标准的
        ///   m_Script: {fileID: 11500000, guid: &lt;script guid&gt;, type: 3}
        /// 前者重开场景后解析不到。实测 5 个巡逻锚点只剩 1 个能加载，
        /// 且每次运行剩下的是哪一个都不固定（Patrol_1 / Patrol_2 / Patrol_4 都出现过）——
        /// 非确定性缺陷，SetDirty 与调整调用顺序都治不了。
        ///
        /// 所以写盘后按文本把退化引用改写成 guid 形式。幂等：带 guid 的形式不会被再次匹配。
        /// </summary>
        private static int FixScriptReferences()
        {
            string[] markerClasses = { "PropSpawnAnchor", "EvacZone", "PatrolAnchor" };
            Dictionary<string, string> guids = new();

            foreach (string cls in markerClasses)
            {
                string g = ReadMetaGuid($"Assets/_Game/Scripts/Gameplay/{cls}.cs.meta");
                if (!string.IsNullOrEmpty(g))
                {
                    guids[cls] = g;
                }
            }

            if (guids.Count == 0)
            {
                Debug.LogError("[D12] 三个标记脚本的 .meta 都读不到 guid，脚本引用无法修复");
                return -1;
            }

            if (!File.Exists(MarketScene))
            {
                return -1;
            }

            string text = File.ReadAllText(MarketScene);
            const string sep = "--- !u!114 &";
            string[] parts = text.Split(new[] { sep }, StringSplitOptions.None);
            int repaired = 0;

            for (int i = 1; i < parts.Length; i++)
            {
                string cls = null;
                foreach (string candidate in guids.Keys)
                {
                    if (parts[i].Contains("ScavengeNightmare.Gameplay." + candidate))
                    {
                        cls = candidate;
                        break;
                    }
                }

                if (cls == null)
                {
                    continue;
                }

                // 只匹配无 guid 的退化形式（紧跟 } 说明后面没有 , guid:）
                Match m = Regex.Match(parts[i], @"m_Script: \{fileID: (-?\d+)\}");
                if (!m.Success)
                {
                    continue;
                }

                // 每个标记类已拆成独立文件（自己是主类），所以统一用 11500000 + 该文件 guid 即可。
                // 早前三个类挤在同一个 MapMarkers.cs 里时不能这么写：
                // 非首个类有各自的本地 fileID，统一写 11500000 会让它们全被解析成第一个类。
                parts[i] = parts[i].Replace(m.Value,
                    $"m_Script: {{fileID: 11500000, guid: {guids[cls]}, type: 3}}");
                repaired++;
            }

            if (repaired > 0)
            {
                File.WriteAllText(MarketScene, string.Join(sep, parts));
            }

            return repaired;
        }

        private static string ReadMetaGuid(string metaPath)
        {
            if (!File.Exists(metaPath))
            {
                return null;
            }

            foreach (string line in File.ReadAllLines(metaPath))
            {
                if (line.StartsWith("guid:"))
                {
                    return line.Substring("guid:".Length).Trim();
                }
            }

            return null;
        }

        private static GameObject Group(Transform parent, string name)
        {
            GameObject go = new GameObject(name);
            if (parent != null)
            {
                go.transform.SetParent(parent, false);
            }

            return go;
        }

        private static GameObject Box(Transform parent, string name, Vector3 center, Vector3 size, Material mat)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.position = center;
            go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            return go;
        }

        private static void EnsureMaterialFolder()
        {
            if (!AssetDatabase.IsValidFolder("Assets/_Game/Data"))
            {
                AssetDatabase.CreateFolder("Assets/_Game", "Data");
            }

            if (!AssetDatabase.IsValidFolder(MaterialFolder))
            {
                AssetDatabase.CreateFolder("Assets/_Game/Data", "Materials");
            }
        }

        private static void BuildMaterials()
        {
            // URP 17：优先 Lit，找不到回退 Standard，避免渲染管线升级后材质变紫
            matFloor = UpsertMaterial("Whitebox_Floor", new Color(0.42f, 0.43f, 0.45f));
            matWall = UpsertMaterial("Whitebox_Wall", new Color(0.72f, 0.72f, 0.70f));
            matProp = UpsertMaterial("Whitebox_Prop", new Color(0.55f, 0.52f, 0.48f));
            matAccent = UpsertMaterial("Whitebox_Accent", new Color(0.78f, 0.62f, 0.32f));
        }

        private static Material UpsertMaterial(string name, Color color)
        {
            string path = $"{MaterialFolder}/{name}.mat";
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);

            if (mat == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null) shader = Shader.Find("Standard");
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }

            mat.color = color;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            EditorUtility.SetDirty(mat);
            return mat;
        }
    }
}
