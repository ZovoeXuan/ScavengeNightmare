using System.Collections.Generic;
using Mirror;
using ScavengeNightmare.Cart;
using ScavengeNightmare.Economy;
using ScavengeNightmare.Gameplay;
using ScavengeNightmare.Networking;
using ScavengeNightmare.Save;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ScavengeNightmare.EditorTools
{
    /// <summary>
    /// 4A 场景：从 Supermarket 复制出 Night1，删掉撤离区与 TraitBootstrap，
    /// 重配 16 个 4A 临时锚点，加公秤、启动器与钱包 HUD，并注册 GameRoot prefab。
    /// </summary>
    public static class S4ASceneSetup
    {
        private const string MarketScene = "Assets/_Game/Scenes/Supermarket.unity";
        private const string NightScene = "Assets/_Game/Scenes/Night1.unity";
        private const string GameRootPrefab = "Assets/_Game/Prefabs/S4AGameRoot.prefab";
        private const string PlayerPrefab = "Assets/_Game/Prefabs/Player.prefab";
        private const string CartPrefabPath = "Assets/_Game/Prefabs/Cart.prefab";
        private const string CartProfilePath = "Assets/_Game/Data/Cart_Default.asset";

        private static readonly string[] AnchorProfiles =
        {
            "can", "can", "can", "box", "box", "toolbox",
            "vase", "tv", "glass", "glass",
            "barrel", "tire", "watermelon",
            "statue", "cabinet", "safe"
        };

        [MenuItem("拾荒惊魂/S4A · 生成 Night1 夜路场景")]
        public static void Setup()
        {
            GameObject prefab =             EnsureGameRootPrefab();
            EnsurePlayerInteract();
            GameObject cartPrefab = EnsureCartPrefab();

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(NightScene) == null)
            {
                if (!AssetDatabase.CopyAsset(MarketScene, NightScene))
                {
                    Debug.LogError("[S4A] S4A_SETUP_FAIL reason=copy_failed from=Supermarket");
                    return;
                }

                Debug.Log("[S4A] S4A_SCENE_COPIED from=Supermarket");
            }

            Scene scene = EditorSceneManager.OpenScene(NightScene, OpenSceneMode.Single);

            RemoveRoots(scene, "EvacZone", "EvacSettlement", "TraitBootstrap");
            RebuildAnchors(scene);
            EnsureQuotaTerminal(scene);
            EnsureShop(scene);
            EnsureRoute(scene);
            EnsurePlayerEquipment();
            EnsureCartInScene(scene);
            EnsureBootstrap(scene, prefab, cartPrefab);
            RegisterPrefab(scene, prefab);
            RegisterPrefab(scene, cartPrefab);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            EditorSceneManager.CloseScene(scene, true);
            AssetDatabase.Refresh();

            // 安全屋必须在夜路存完盘之后再建：NewScene(Single) 会卸载当前场景。
            EnsureSafehouseScene();
            RegisterBuildScene(NightScene);

            EditorSceneManager.OpenScene(NightScene, OpenSceneMode.Single);
            int anchors = UnityEngine.Object.FindObjectsByType<PropSpawnAnchor>(FindObjectsSortMode.None).Length;
            int terminals = UnityEngine.Object.FindObjectsByType<QuotaTerminal>(FindObjectsSortMode.None).Length;
            int traits = UnityEngine.Object.FindObjectsByType<Traits.TraitBootstrap>(FindObjectsSortMode.None).Length;
            int shops = UnityEngine.Object.FindObjectsByType<ShopTerminal>(FindObjectsSortMode.None).Length;
            ShopCatalog catalog = AssetDatabase.LoadAssetAtPath<ShopCatalog>(ShopCatalogPath);
            Debug.Log($"[S4A] S4A_SETUP_OK scene=Night1 anchors={anchors} quotaTerminal={terminals} traits={traits} shop={shops} shopItems={(catalog != null ? catalog.Count : 0)}");
        }

        [MenuItem("拾荒惊魂/S4A · 校验 Night1 夜路场景")]
        public static void Validate()
        {
            EditorSceneManager.OpenScene(NightScene, OpenSceneMode.Single);

            int failures = 0;
            void Check(bool ok, string name)
            {
                if (!ok)
                {
                    failures++;
                    Debug.LogError($"[S4A] S4A_VALIDATE_FAIL check={name}");
                }
            }

            int anchors = UnityEngine.Object.FindObjectsByType<PropSpawnAnchor>(FindObjectsSortMode.None).Length;
            Check(anchors == 16, $"anchors_{anchors}");

            int terminals = UnityEngine.Object.FindObjectsByType<QuotaTerminal>(FindObjectsSortMode.None).Length;
            Check(terminals == 1, $"quota_terminal_{terminals}");

            int shops = UnityEngine.Object.FindObjectsByType<ShopTerminal>(FindObjectsSortMode.None).Length;
            Check(shops == 1, $"shop_terminal_{shops}");

            ShopCatalog catalog = AssetDatabase.LoadAssetAtPath<ShopCatalog>(ShopCatalogPath);
            Check(catalog != null && catalog.Count >= 4, $"shop_catalog_{(catalog != null ? catalog.Count : 0)}");

            int traits = UnityEngine.Object.FindObjectsByType<Traits.TraitBootstrap>(FindObjectsSortMode.None).Length;
            Check(traits == 0, $"traits_none_{traits}");

            int evac = UnityEngine.Object.FindObjectsByType<EvacZone>(FindObjectsSortMode.None).Length;
            Check(evac == 0, $"evac_removed_{evac}");

            int settle = UnityEngine.Object.FindObjectsByType<EvacSettlement>(FindObjectsSortMode.None).Length;
            Check(settle == 0, $"evac_settlement_removed_{settle}");

            NetworkManager nm = UnityEngine.Object.FindObjectsByType<NetworkManager>(FindObjectsSortMode.None).Length > 0
                ? UnityEngine.Object.FindObjectsByType<NetworkManager>(FindObjectsSortMode.None)[0]
                : null;
            Check(nm != null, "network_manager");

            S4ABootstrap boot = nm != null ? nm.GetComponent<S4ABootstrap>() : null;
            Check(boot != null, "bootstrap");

            bool registered = false;
            if (nm != null)
            {
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(GameRootPrefab);
                if (prefab != null && nm.spawnPrefabs != null)
                {
                    registered = nm.spawnPrefabs.Contains(prefab);
                }
            }

            Check(registered, "gameroot_registered");

            bool saveService = false;
            if (nm != null)
            {
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(GameRootPrefab);
                saveService = prefab != null && prefab.GetComponent<P0SaveService>() != null;
            }

            Check(saveService, "gameroot_save_service");
            Check(nm != null && nm.GetComponent<S4ASaveTest>() != null, "save_test_attached");
            Check(nm != null && nm.GetComponent<S4ACartTest>() != null, "cart_test_attached");

            GameObject cart = AssetDatabase.LoadAssetAtPath<GameObject>(CartPrefabPath);
            Check(cart != null, "cart_prefab_exists");
            Check(cart != null && cart.GetComponent<CartController>() != null, "cart_controller");
            Check(cart != null && cart.GetComponent<CartCargo>() != null, "cart_cargo");
            Check(AssetDatabase.LoadAssetAtPath<CartProfile>(CartProfilePath) != null, "cart_profile_asset");

            int cartAnchors = UnityEngine.Object.FindObjectsByType<CartSpawnAnchor>(FindObjectsSortMode.None).Length;
            Check(cartAnchors == 1, $"cart_anchor_{cartAnchors}");

            int parking = UnityEngine.Object.FindObjectsByType<CartParkingZone>(FindObjectsSortMode.None).Length;
            Check(parking == 1, $"cart_parking_{parking}");

            bool cartRegistered = false;
            if (nm != null && cart != null && nm.spawnPrefabs != null)
            {
                cartRegistered = nm.spawnPrefabs.Contains(cart);
            }

            Check(cartRegistered, "cart_registered");

            GameObject player = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefab);
            Check(player != null, "player_prefab_exists");
            Check(player != null && player.GetComponent<PlayerInteract>() != null, "player_interact_attached");

            if (failures == 0)
            {
                Debug.Log($"[S4A] S4A_VALIDATE_OK night1=1 quotaTerminal=1 anchors=16 evacRemoved=1 traits=none gameroot=1 saveService=1 saveTest=1");
            }
        }

        private static GameObject EnsureGameRootPrefab()
        {
            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(GameRootPrefab);
            if (existing != null)
            {
                // Story 2 起 GameRoot 必须带 P0SaveService。老预制体要就地补齐——
                // 只写「不存在才新建」的话，已经存在的 GameRoot 永远不会拿到存档组件。
                if (existing.GetComponent<P0SaveService>() == null)
                {
                    existing.AddComponent<P0SaveService>();
                    EditorUtility.SetDirty(existing);
                    PrefabUtility.SavePrefabAsset(existing);
                    AssetDatabase.SaveAssets();
                    Debug.Log("[S4A] S4A_PREFAB_SAVE_SERVICE_ADDED path=" + GameRootPrefab);
                }

                return existing;
            }

            GameObject go = new("S4AGameRoot");
            go.AddComponent<NetworkIdentity>();
            go.AddComponent<TeamAccount>();
            go.AddComponent<P0SaveService>();

            GameObject saved = PrefabUtility.SaveAsPrefabAsset(go, GameRootPrefab);
            UnityEngine.Object.DestroyImmediate(go);
            AssetDatabase.SaveAssets();
            return saved;
        }

        /// <summary>
        /// Player 预制体必须挂 PlayerInteract：公秤的交互意图要由客户端上报。
        /// 不挂的话主机单机能提交、远端客户端永远按不动，是典型的假联机。
        /// 该组件在场景里没有 QuotaTerminal 时完全不发消息，对 Main / Supermarket 零网络增量。
        /// </summary>
        private static void EnsurePlayerInteract()
        {
            GameObject player = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefab);
            if (player == null)
            {
                Debug.LogError($"[S4A] S4A_SETUP_FAIL reason=player_prefab_missing path={PlayerPrefab}");
                return;
            }

            if (player.GetComponent<PlayerInteract>() != null)
            {
                return;
            }

            player.AddComponent<PlayerInteract>();
            EditorUtility.SetDirty(player);
            PrefabUtility.SavePrefabAsset(player);
            AssetDatabase.SaveAssets();
            Debug.Log($"[S4A] S4A_PLAYER_INTERACT_ADDED path={PlayerPrefab}");
        }

        private static void RemoveRoots(Scene scene, params string[] names)
        {
            HashSet<string> targets = new(names);
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (targets.Contains(root.name))
                {
                    UnityEngine.Object.DestroyImmediate(root);
                }
            }

            // 不能只按名字删：EvacZone 可能挂在别的根下，EvacSettlement 可能挂在 NetworkManager 上。
            // 前者连物体一起删，后者只删组件，绝不能把 NetworkManager 干掉。
            foreach (EvacZone zone in UnityEngine.Object.FindObjectsByType<EvacZone>(FindObjectsSortMode.None))
            {
                UnityEngine.Object.DestroyImmediate(zone.gameObject);
            }

            foreach (EvacSettlement settlement in UnityEngine.Object.FindObjectsByType<EvacSettlement>(FindObjectsSortMode.None))
            {
                bool onNetworkManager = settlement.GetComponent<NetworkManager>() != null;
                if (onNetworkManager)
                {
                    UnityEngine.Object.DestroyImmediate(settlement);
                }
                else
                {
                    UnityEngine.Object.DestroyImmediate(settlement.gameObject);
                }
            }
        }

        private static void RebuildAnchors(Scene scene)
        {
            List<Vector3> positions = new();
            foreach (PropSpawnAnchor anchor in UnityEngine.Object.FindObjectsByType<PropSpawnAnchor>(FindObjectsSortMode.None))
            {
                positions.Add(anchor.transform.position);
                UnityEngine.Object.DestroyImmediate(anchor.gameObject);
            }

            for (int i = 0; i < AnchorProfiles.Length; i++)
            {
                Vector3 pos = i < positions.Count
                    ? positions[i]
                    : new Vector3(-16f + (i % 4) * 8f, 0.6f, -16f + (i / 4) * 8f);

                GameObject go = new($"Anchor_4A_{i + 1}_{AnchorProfiles[i]}");
                go.transform.position = pos;
                PropSpawnAnchor anchor = go.AddComponent<PropSpawnAnchor>();
                anchor.profileId = AnchorProfiles[i];
                EditorUtility.SetDirty(anchor);
                EditorUtility.SetDirty(go);
            }
        }

        private static void EnsureQuotaTerminal(Scene scene)        {
            if (UnityEngine.Object.FindObjectsByType<QuotaTerminal>(FindObjectsSortMode.None).Length > 0)
            {
                return;
            }

            GameObject go = new("QuotaTerminal");
            go.transform.position = new Vector3(0f, 1f, -16.5f);

            BoxCollider box = go.AddComponent<BoxCollider>();
            box.size = new Vector3(4f, 3f, 4f);
            box.isTrigger = true;

            go.AddComponent<NetworkIdentity>();
            go.AddComponent<QuotaTerminal>();
            EditorUtility.SetDirty(go);
        }

        private const string ShopItemDir = "Assets/_Game/Data/Shop";
        private const string ShopCatalogPath = "Assets/_Game/Resources/ShopCatalog.asset";

        /// <summary>
        /// 商店：4 件 4A 商品 + 目录资源 + 场景里的终端与 HUD。
        /// 目录放 Resources 是有意的——账本是 DontDestroyOnLoad 的 GameRoot，换关后场景引用会丢，
        /// Resources 加载不受换关影响。
        /// </summary>
        private static void EnsureShop(Scene scene)
        {
            EnsureFolder("Assets/_Game/Data");
            EnsureFolder(ShopItemDir);
            EnsureFolder("Assets/_Game/Resources");

            ShopItem[] items =
            {
                EnsureShopItem("grip_gloves", "防滑手套", "抓握更牢：脱手阈值 ×1.5，跟随更快", 400, ShopEffect.GrabBreak, 1.5f, 1),
                EnsureShopItem("packing_tape", "打包胶带", "把货捆在板上：横向滑移阈值 ×2", 500, ShopEffect.CartSlip, 2f, 2),
                EnsureShopItem("flashlight", "手电筒", "F 键开关，射程 18m", 350, ShopEffect.Flashlight, 18f, 3),
                EnsureShopItem("grip_training", "握力训练", "负重性能 +15%：负重减速量与负重硬上限同时提高", 900, ShopEffect.CarryPenalty, 1.15f, 4)
            };

            ShopCatalog catalog = AssetDatabase.LoadAssetAtPath<ShopCatalog>(ShopCatalogPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<ShopCatalog>();
                AssetDatabase.CreateAsset(catalog, ShopCatalogPath);
                Debug.Log("[S4A] S4A_SHOP_CATALOG_CREATED path=" + ShopCatalogPath);
            }

            catalog.EditorSetItems(items);
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();

            ShopTerminal[] existing = UnityEngine.Object.FindObjectsByType<ShopTerminal>(FindObjectsSortMode.None);

            if (existing.Length > 0)
            {
                // 终端现在只是普通 MonoBehaviour（Command 挂在玩家身上，场景对象没有 owner），
                // 早先挂上的 NetworkIdentity 要摘掉，否则 Mirror 会为一个没有任何行为、
                // 却带 Identity 的场景对象分配 sceneId。
                foreach (ShopTerminal t in existing)
                {
                    NetworkIdentity id = t.GetComponent<NetworkIdentity>();
                    if (id != null)
                    {
                        UnityEngine.Object.DestroyImmediate(id, true);
                    }

                    if (t.GetComponent<ScavengeNightmare.UI.ShopHUD>() == null)
                    {
                        t.gameObject.AddComponent<ScavengeNightmare.UI.ShopHUD>();
                    }

                    EditorUtility.SetDirty(t.gameObject);
                }

                return;
            }

            GameObject go = new("ShopTerminal");
            go.transform.position = new Vector3(5f, 0.6f, -16.5f);

            BoxCollider box = go.AddComponent<BoxCollider>();
            box.size = new Vector3(3f, 2.4f, 3f);
            box.isTrigger = true;

            go.AddComponent<ShopTerminal>();
            go.AddComponent<ScavengeNightmare.UI.ShopHUD>();
            EditorUtility.SetDirty(go);
        }

        private static ShopItem EnsureShopItem(string id, string display, string desc, int price, ShopEffect effect, float magnitude, int hotkey)        {
            string path = $"{ShopItemDir}/{id}.asset";
            ShopItem item = AssetDatabase.LoadAssetAtPath<ShopItem>(path);

            if (item == null)
            {
                item = ScriptableObject.CreateInstance<ShopItem>();
                AssetDatabase.CreateAsset(item, path);
            }

            item.id = id;
            item.displayName = display;
            item.description = desc;
            item.price = price;
            item.maxOwned = 1;
            item.effect = effect;
            item.magnitude = magnitude;
            item.hotkey = hotkey;

            EditorUtility.SetDirty(item);
            return item;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            int slash = path.LastIndexOf('/');
            string parent = path.Substring(0, slash);
            string leaf = path.Substring(slash + 1);

            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }

        private const string PlayerPrefabPath = "Assets/_Game/Prefabs/Player.prefab";

        private const string SafehouseScenePath = "Assets/_Game/Scenes/Safehouse.unity";

        /// <summary>夜路侧的路由物件：撤离门 + 出生锚点 + 路由服务（挂在 NM 上，DDOL）。</summary>
        private static void EnsureRoute(Scene scene)
        {
            NetworkManager[] managers = UnityEngine.Object.FindObjectsByType<NetworkManager>(FindObjectsSortMode.None);
            if (managers.Length > 0 && managers[0].GetComponent<LevelRouteService>() == null)
            {
                managers[0].gameObject.AddComponent<LevelRouteService>();
                EditorUtility.SetDirty(managers[0].gameObject);
            }

            if (UnityEngine.Object.FindObjectsByType<LevelPortal>(FindObjectsSortMode.None).Length == 0)
            {
                GameObject portal = new("LevelPortal_Exit");
                // 与公秤终端（x∈[-2,2]）错开，否则同一次长按会被两边同时处理
                portal.transform.position = new Vector3(-6f, 0.5f, -16.5f);
                BoxCollider box = portal.AddComponent<BoxCollider>();
                box.size = new Vector3(2f, 3f, 2f);
                box.isTrigger = true;
                portal.AddComponent<LevelPortal>();
                EditorUtility.SetDirty(portal);
            }

            if (UnityEngine.Object.FindObjectsByType<SceneEntryAnchor>(FindObjectsSortMode.None).Length == 0)
            {
                GameObject anchor = new("SceneEntryAnchor_Night1");
                anchor.transform.position = new Vector3(0f, 0.05f, 15f);
                SceneEntryAnchor entry = anchor.AddComponent<SceneEntryAnchor>();
                entry.yawDegrees = 180f;
                EditorUtility.SetDirty(anchor);
            }
        }

        /// <summary>
        /// 安全屋场景：一个封闭房间 + 商店终端 + 返回门 + 出生锚点。
        /// 刻意从零搭而不是复制夜路再删——复制会把货架、推车锚点、公秤全带过来，
        /// 删漏一个就是「安全屋里也能刷货」这种没人会发现的问题。
        /// </summary>
        private static void EnsureSafehouseScene()
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "Floor";
            floor.transform.position = new Vector3(0f, -0.25f, 0f);
            floor.transform.localScale = new Vector3(26f, 0.5f, 18f);

            CreateWall("Wall_North", new Vector3(0f, 2f, 9f), new Vector3(26f, 4f, 0.5f));
            CreateWall("Wall_South", new Vector3(0f, 2f, -9f), new Vector3(26f, 4f, 0.5f));
            CreateWall("Wall_West", new Vector3(-13f, 2f, 0f), new Vector3(0.5f, 4f, 18f));
            CreateWall("Wall_East", new Vector3(13f, 2f, 0f), new Vector3(0.5f, 4f, 18f));

            GameObject light = new("KeyLight");
            light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            Light lamp = light.AddComponent<Light>();
            lamp.type = LightType.Directional;
            lamp.intensity = 1.1f;

            GameObject shop = new("ShopTerminal");
            shop.transform.position = new Vector3(0f, 0.6f, 3f);
            BoxCollider shopBox = shop.AddComponent<BoxCollider>();
            shopBox.size = new Vector3(3f, 2.4f, 3f);
            shopBox.isTrigger = true;
            shop.AddComponent<ShopTerminal>();
            shop.AddComponent<ScavengeNightmare.UI.ShopHUD>();

            GameObject portal = new("LevelPortal_Safehouse");
            portal.transform.position = new Vector3(0f, 0.5f, -6f);
            BoxCollider portalBox = portal.AddComponent<BoxCollider>();
            portalBox.size = new Vector3(2f, 3f, 2f);
            portalBox.isTrigger = true;
            portal.AddComponent<LevelPortal>();

            GameObject anchor = new("SceneEntryAnchor_Safehouse");
            anchor.transform.position = new Vector3(0f, 0.05f, 5.5f);
            SceneEntryAnchor entry = anchor.AddComponent<SceneEntryAnchor>();
            entry.yawDegrees = 180f;

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, SafehouseScenePath);

            RegisterBuildScene(SafehouseScenePath);
            Debug.Log($"[S4A] S4A_SAFEHOUSE_BUILT path={SafehouseScenePath}");
        }

        private static void CreateWall(string name, Vector3 position, Vector3 scale)
        {
            GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = name;
            wall.transform.position = position;
            wall.transform.localScale = scale;
        }

        private static void RegisterBuildScene(string path)
        {
            List<EditorBuildSettingsScene> scenes = new(EditorBuildSettings.scenes);

            foreach (EditorBuildSettingsScene s in scenes)
            {
                if (s.path == path)
                {
                    return;
                }
            }

            scenes.Add(new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            Debug.Log($"[S4A] S4A_BUILD_SETTINGS_ADD path={path} total={scenes.Count}");
        }

        /// <summary>
        /// 手电筒组件挂在玩家预制体上（手持装备属于玩家，不属于场景）。
        /// 只增不改：已有就跳过，避免每次 Setup 都重写预制体、把别人手改的东西冲掉。
        /// </summary>
        private static void EnsurePlayerEquipment()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            if (prefab == null)
            {
                Debug.LogError($"[S4A] S4A_SETUP_FAIL reason=player_prefab_missing path={PlayerPrefabPath}");
                return;
            }

            if (prefab.GetComponent<PlayerEquipment>() != null)
            {
                return;
            }

            prefab.AddComponent<PlayerEquipment>();
            PrefabUtility.SaveAsPrefabAsset(prefab, PlayerPrefabPath);
            AssetDatabase.SaveAssets();
            Debug.Log("[S4A] S4A_PLAYER_EQUIPMENT_ADDED");
        }

        /// <summary>
        /// 推车 prefab + 配置资源。数值全部落在 CartProfile 资源里，prefab 只负责结构（D13 原则：改表不改代码）。
        /// </summary>
        private static GameObject EnsureCartPrefab()
        {
            CartProfile profile = AssetDatabase.LoadAssetAtPath<CartProfile>(CartProfilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<CartProfile>();
                AssetDatabase.CreateAsset(profile, CartProfilePath);
                AssetDatabase.SaveAssets();
                Debug.Log("[S4A] S4A_CART_PROFILE_CREATED path=" + CartProfilePath);
            }

            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(CartPrefabPath);
            if (existing != null)
            {
                RepairCartPrefab(existing, profile);
                return existing;
            }

            GameObject root = new("Cart");

            Rigidbody rb = root.AddComponent<Rigidbody>();
            rb.mass = profile.cartMassKg;
            rb.linearDamping = 0.2f;
            // 角阻尼给大一点：P0 不做轮胎物理，车靠速度驱动，自旋只会让推行手感发飘
            rb.angularDamping = 4f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;

            // 碰撞体上表面必须等于板视觉上表面（都取 topY），否则货物会被放进碰撞体内部
            // 然后被物理弹飞——表现是「装车成功、一推行全掉」。
            float topY = profile.deckHeight + 0.28f;
            BoxCollider hull = root.AddComponent<BoxCollider>();
            hull.size = new Vector3(profile.deckLength, topY, profile.deckWidth);
            hull.center = new Vector3(0f, topY * 0.5f, 0f);

            // 板面摩擦：货物能不能待在板上全靠它（配合 CartCargo 的平台带动）
            PhysicsMaterial mat = new("CartDeck")
            {
                dynamicFriction = profile.cargoFriction,
                staticFriction = profile.cargoFriction,
                bounciness = 0f,
                frictionCombine = PhysicsMaterialCombine.Maximum,
                bounceCombine = PhysicsMaterialCombine.Minimum
            };

            const string matPath = "Assets/_Game/Data/CartDeck.physicMaterial";
            if (AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(matPath) == null)
            {
                AssetDatabase.CreateAsset(mat, matPath);
            }

            hull.material = mat;

            // 板视觉顶部与 hull 顶部对齐，货物才会真的「躺在板上」而不是浮空或被弹飞
            GameObject deck = GameObject.CreatePrimitive(PrimitiveType.Cube);
            deck.name = "Deck";
            deck.transform.SetParent(root.transform, false);
            deck.transform.localScale = new Vector3(profile.deckLength, profile.deckHeight, profile.deckWidth);
            deck.transform.localPosition = new Vector3(0f, topY - profile.deckHeight * 0.5f, 0f);
            UnityEngine.Object.DestroyImmediate(deck.GetComponent<Collider>());

            GameObject handle = GameObject.CreatePrimitive(PrimitiveType.Cube);
            handle.name = "Handle";
            handle.transform.SetParent(root.transform, false);
            handle.transform.localScale = new Vector3(0.08f, 0.08f, profile.deckWidth);
            handle.transform.localPosition = new Vector3(-(profile.deckLength * 0.5f - 0.1f), profile.handleHeight, 0f);
            UnityEngine.Object.DestroyImmediate(handle.GetComponent<Collider>());

            root.AddComponent<NetworkIdentity>();
            // 不设 syncDirection：Mirror 默认就是 ServerToClient，而且该字段不允许运行时翻转
            root.AddComponent<NetworkTransformReliable>();

            CartController controller = root.AddComponent<CartController>();
            root.AddComponent<CartCargo>();

            SerializedObject so = new(controller);
            SerializedProperty prop = so.FindProperty("profile");
            if (prop != null)
            {
                prop.objectReferenceValue = profile;
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, CartPrefabPath);
            UnityEngine.Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets();
            Debug.Log("[S4A] S4A_CART_PREFAB_CREATED path=" + CartPrefabPath);
            return saved;
        }

        /// <summary>
        /// 已存在的推车预制体就地校正尺寸，而不是「存在就跳过」。
        /// 板面从 1.3×0.75 改到 1.8×0.9 那次就是这么漏的：预制体早就在，改表之后
        /// 碰撞体还是旧尺寸，货放上去直接穿板。把手也一直飘在车侧外面
        /// （把手原本挂在 local +Z = 0.8，而车体 Z 半宽只有 0.45）——车头是 local +X。
        /// 所以这里每次都按 profile 重算一遍，幂等。
        /// </summary>
        private static void RepairCartPrefab(GameObject prefab, CartProfile profile)
        {
            bool changed = false;
            float topY = profile.deckHeight + 0.28f;

            BoxCollider hull = prefab.GetComponent<BoxCollider>();
            if (hull != null)
            {
                Vector3 size = new(profile.deckLength, topY, profile.deckWidth);
                Vector3 center = new(0f, topY * 0.5f, 0f);

                if ((hull.size - size).sqrMagnitude > 0.0001f || (hull.center - center).sqrMagnitude > 0.0001f)
                {
                    hull.size = size;
                    hull.center = center;
                    changed = true;
                }
            }

            Transform deck = prefab.transform.Find("Deck");
            if (deck != null)
            {
                deck.localScale = new Vector3(profile.deckLength, profile.deckHeight, profile.deckWidth);
                deck.localPosition = new Vector3(0f, topY - profile.deckHeight * 0.5f, 0f);
                changed = true;
            }

            Transform handle = prefab.transform.Find("Handle");
            if (handle != null)
            {
                // 把手在车尾：车头 +X，所以车尾是 -X。
                handle.localScale = new Vector3(0.08f, 0.08f, profile.deckWidth);
                handle.localPosition = new Vector3(-(profile.deckLength * 0.5f - 0.1f), profile.handleHeight, 0f);
                changed = true;
            }

            if (!changed)
            {
                return;
            }

            PrefabUtility.SaveAsPrefabAsset(prefab, CartPrefabPath);
            AssetDatabase.SaveAssets();
            Debug.Log($"[S4A] S4A_CART_PREFAB_REPAIRED hull={profile.deckLength}x{profile.deckWidth} handle=-X");
        }

        /// <summary>场景里只摆锚点与停车位，推车本体由服务端运行时 Spawn（D6.5：场景实例拿不到 sceneId）。</summary>
        private static void EnsureCartInScene(Scene scene)
        {
            if (UnityEngine.Object.FindObjectsByType<CartSpawnAnchor>(FindObjectsSortMode.None).Length == 0)
            {
                GameObject anchor = new("CartSpawnAnchor_Entry");
                anchor.transform.position = new Vector3(0f, 0.05f, 17f);
                anchor.AddComponent<CartSpawnAnchor>();
                EditorUtility.SetDirty(anchor);
            }

            CartParkingZone[] zones = UnityEngine.Object.FindObjectsByType<CartParkingZone>(FindObjectsSortMode.None);
            Vector3 parkingPos = new(0f, 1f, -16.5f);

            if (zones.Length == 0)
            {
                GameObject zone = new("CartParkingZone");
                zone.transform.position = parkingPos;
                BoxCollider box = zone.AddComponent<BoxCollider>();
                box.size = new Vector3(1.8f, 2f, 2.6f);
                box.isTrigger = true;
                zone.AddComponent<CartParkingZone>();
                EditorUtility.SetDirty(zone);
            }
            else
            {
                // 位置错了要就地纠正，不能只在「不存在时创建」——
                // 已经存在的旧停车位会一直停在公秤外，整车提交永远按不下去。
                zones[0].transform.position = parkingPos;
                EditorUtility.SetDirty(zones[0]);
            }
        }

        private static void EnsureBootstrap(Scene scene, GameObject prefab, GameObject cartPrefab)
        {
            NetworkManager[] managers = UnityEngine.Object.FindObjectsByType<NetworkManager>(FindObjectsSortMode.None);
            if (managers.Length == 0)
            {
                return;
            }

            GameObject host = managers[0].gameObject;

            S4ABootstrap boot = host.GetComponent<S4ABootstrap>();
            if (boot == null)
            {
                boot = host.AddComponent<S4ABootstrap>();
            }

            SerializedObject so = new(boot);
            SerializedProperty prop = so.FindProperty("gameRootPrefab");
            if (prop != null)
            {
                prop.objectReferenceValue = prefab;
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            SerializedProperty cartProp = so.FindProperty("cartPrefab");
            if (cartProp != null)
            {
                cartProp.objectReferenceValue = cartPrefab;
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            if (host.GetComponent<WalletHUD>() == null)
            {
                host.AddComponent<WalletHUD>();
            }

            if (host.GetComponent<S4ADepositTest>() == null)
            {
                host.AddComponent<S4ADepositTest>();
            }

            if (host.GetComponent<S4ASaveTest>() == null)
            {
                host.AddComponent<S4ASaveTest>();
            }

            if (host.GetComponent<S4ACartTest>() == null)
            {
                host.AddComponent<S4ACartTest>();
            }

            if (host.GetComponent<S4AShopTest>() == null)
            {
                host.AddComponent<S4AShopTest>();
            }

            if (host.GetComponent<S4ACarryCapTest>() == null)
            {
                host.AddComponent<S4ACarryCapTest>();
            }

            if (host.GetComponent<S4ALoopTest>() == null)
            {
                host.AddComponent<S4ALoopTest>();
            }

            if (host.GetComponent<S4ATestGuard>() == null)
            {
                host.AddComponent<S4ATestGuard>();
            }

            EditorUtility.SetDirty(host);
        }

        private static void RegisterPrefab(Scene scene, GameObject prefab)
        {
            NetworkManager[] managers = UnityEngine.Object.FindObjectsByType<NetworkManager>(FindObjectsSortMode.None);
            if (managers.Length == 0 || prefab == null)
            {
                return;
            }

            SerializedObject so = new(managers[0]);
            SerializedProperty list = so.FindProperty("spawnPrefabs");
            if (list == null)
            {
                Debug.LogError("[S4A] S4A_SETUP_FAIL reason=no_spawnPrefabs_field");
                return;
            }

            for (int i = 0; i < list.arraySize; i++)
            {
                if (list.GetArrayElementAtIndex(i).objectReferenceValue == prefab)
                {
                    return;
                }
            }

            list.InsertArrayElementAtIndex(list.arraySize);
            list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = prefab;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(managers[0]);
        }
    }
}
