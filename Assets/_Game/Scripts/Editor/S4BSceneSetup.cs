using System.Collections.Generic;
using Mirror;
using ScavengeNightmare.Combat;
using ScavengeNightmare.Curses;
using ScavengeNightmare.Economy;
using ScavengeNightmare.Gameplay;
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
    ///
    /// ⚠ **这不是字节幂等的，commit 之后不要再跑 Setup()。**
    /// 每次运行都会重写 Night1.unity / Safehouse.unity：对象内容等价（名字、坐标、
    /// 旋转、profileId 全部一致），但 fileID 全部重排，diff 会有几百上千行。
    /// 要装配新资源时：先确认工作区干净 → 跑 Setup() → 把场景改动**单独**提交。
    /// 反过来做（先提交再补跑 Setup）会得到一次「内容没变、但包行为变了」的诡异重建：
    /// 实测过一次，`-s4bmon` 的测速用例从 3.20 掉到 3.01 并稳定复现。
    /// 另外 RebuildAnchors() 是用 FindObjectsByType 的返回顺序把
    /// AnchorProfiles 贴到现有锚点位置上的，那个顺序未定义——所以**锚点名与坐标的
    /// 对应关系跨 Setup 运行不稳定**，不要按名字定位某个道具，要按坐标定位。
    /// </summary>
    public static class S4BSceneSetup
    {
        private const string NightScene = "Assets/_Game/Scenes/Night1.unity";
        private const string SafehouseScene = "Assets/_Game/Scenes/Safehouse.unity";
        private const string PlayerPrefab = "Assets/_Game/Prefabs/Player.prefab";
        private const string GameRootPrefab = "Assets/_Game/Prefabs/S4AGameRoot.prefab";
        private const string MonsterPrefab = "Assets/_Game/Prefabs/Monster_HollowEye.prefab";
        private const string GreeterPrefab = "Assets/_Game/Prefabs/Monster_PlasticGreeter.prefab";
        private const string ScreamPrefab = "Assets/_Game/Prefabs/Monster_ScreamCan.prefab";
        private const string MonsterMat = "Assets/_Game/Data/Monsters/HollowEyeMat.mat";
        private const string MonsterProfileDir = "Assets/_Game/Resources/Monsters";
        private const string HollowEyeProfilePath = MonsterProfileDir + "/HollowEye.asset";
        private const string GreeterProfilePath = MonsterProfileDir + "/PlasticGreeter.asset";
        private const string ScreamProfilePath = MonsterProfileDir + "/ScreamCan.asset";
        private const string ShopCatalogPath = "Assets/_Game/Resources/ShopCatalog.asset";
        private const string LegTablePath = "Assets/_Game/Resources/LegContentTable.asset";

        /// <summary>S4B-5 三关：每关锚点数与货值总额（与验收行逐字一致）。</summary>
        private static readonly int[] LegAnchorCounts = { 12, 14, 16 };
        private static readonly int[] LegTotalValues = { 3155, 4365, 4785 };

        /// <summary>
        /// L1/L2/L3 = 12/14/16 件的差值来源：
        ///   cabinet 在 L1+L3、不在 L2（mask 5）；tv/tire/statue 只在 L2+L3（mask 6）；
        ///   三个 can 中第三个只在 L3（mask 4）。
        /// </summary>
        private static readonly (string profile, int mask)[] LegMaskRules =
        {
            ("cabinet", 5),
            ("tv", 6),
            ("tire", 6),
            ("statue", 6)
        };

        /// <summary>三个 can 里只有第三个是 L3 专属（L1/L2 各少一件）。</summary>
        private const int ThirdCanLegMask = 4;

        // ---- S4B-4 两件诅咒 ----
        private const string CurseMat = "Assets/_Game/Data/Materials/PropType_Cursed.mat";
        private const string DollProfilePath = "Assets/_Game/Data/Props/Prop_curse_doll.asset";
        private const string TabletProfilePath = "Assets/_Game/Data/Props/Prop_curse_tablet.asset";
        private const string DollPrefabPath = "Assets/_Game/Prefabs/Props/curse_doll.prefab";
        private const string TabletPrefabPath = "Assets/_Game/Prefabs/Props/curse_tablet.prefab";

        private static readonly (string name, CurseKind kind, Vector3 pos)[] CurseAnchors =
        {
            ("CurseSpawn_1_Doll", CurseKind.Doll, new Vector3(5f, 0.25f, 6f)),
            ("CurseSpawn_2_Tablet", CurseKind.Tablet, new Vector3(-5f, 0.25f, 6f))
        };

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
            // 先保证 4A 基线（Night1 / Safehouse / 商店 8 件）是最新的。
            S4ASceneSetup.Setup();

            EnsurePlayerComponents();
            EnsureGameRootNoiseState();
            EnsureLegContentTable();
            EnsureMonsterAssets();
            EnsureCurseAssets();
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
            Check(player != null && player.GetComponent<CurseHud>() != null, "player_curse_hud");
            Check(player != null && player.GetComponent<BeltSlot>() != null, "player_belt_slot");
            Check(player != null && player.GetComponent<PlayerStamina>() != null, "player_stamina");

            ShopCatalog catalog = AssetDatabase.LoadAssetAtPath<ShopCatalog>(ShopCatalogPath);
            Check(catalog != null && catalog.Count >= 8, $"shop_catalog_{(catalog != null ? catalog.Count : 0)}");
            Check(catalog != null && catalog.Find("self_injector") != null, "shop_self_injector");
            Check(catalog != null && catalog.Find("medkit") != null, "shop_medkit");
            Check(catalog != null && catalog.Find("tool_belt") != null, "shop_tool_belt");
            Check(catalog != null && catalog.Find("stamina_training") != null, "shop_stamina_training");

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
            Check(AssetDatabase.LoadAssetAtPath<MonsterProfile>(GreeterProfilePath) != null, "greeter_profile");
            Check(AssetDatabase.LoadAssetAtPath<MonsterProfile>(ScreamProfilePath) != null, "scream_profile");

            GameObject greeterPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(GreeterPrefab);
            Check(greeterPrefab != null && greeterPrefab.GetComponent<PlasticGreeterBrain>() != null, "greeter_brain");
            Check(greeterPrefab != null && greeterPrefab.GetComponent<NetworkIdentity>() != null, "greeter_identity");

            GameObject screamPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ScreamPrefab);
            Check(screamPrefab != null && screamPrefab.GetComponent<ScreamCanBrain>() != null, "scream_brain");
            Check(screamPrefab != null && screamPrefab.GetComponent<NetworkIdentity>() != null, "scream_identity");

            EditorSceneManager.OpenScene(NightScene, OpenSceneMode.Single);

            int nightShops = Object.FindObjectsByType<ShopTerminal>(FindObjectsSortMode.None).Length;
            Check(nightShops == 0, $"night_shop_{nightShops}");

            NetworkManager nm = FindFirst<NetworkManager>();
            Check(nm != null, "network_manager");
            Check(nm != null && nm.GetComponent<TeamWipeDetector>() != null, "team_wipe_detector");
            Check(nm != null && nm.GetComponent<S4BVitalsTest>() != null, "vitals_test");
            Check(nm != null && nm.GetComponent<S4BWipeTest>() != null, "wipe_test");
            Check(nm != null && nm.GetComponent<S4BReviveTest>() != null, "revive_test");
            Check(nm != null && nm.GetComponent<S4BNoiseTest>() != null, "noise_test");
            Check(nm != null && nm.GetComponent<S4BMonsterTest>() != null, "monster_test");
            Check(nm != null && nm.GetComponent<S4BGreeterTest>() != null, "greeter_test");
            Check(nm != null && nm.GetComponent<S4BScreamTest>() != null, "scream_test");
            Check(nm != null && nm.GetComponent<MonsterSpawner>() != null, "monster_spawner");
            Check(nm != null && nm.GetComponent<MonsterDirector>() != null, "monster_director");
            Check(nm != null && nm.GetComponent<CurseRuntime>() != null, "curse_runtime");
            Check(nm != null && nm.GetComponent<S4BCurseTest>() != null, "curse_test");
            Check(nm != null && nm.GetComponent<S4BLegTest>() != null, "leg_test");
            Check(nm != null && nm.GetComponent<S4BBeltTest>() != null, "belt_test");
            Check(nm != null && nm.GetComponent<S4BPerfTest>() != null, "perf_test");
            Check(nm != null && nm.GetComponent<S4BShop8Test>() != null, "shop8_test");
            Check(nm != null && nm.GetComponent<S4CLegClientTest>() != null, "legclient_test");
            Check(nm != null && nm.GetComponent<S4CAudioTest>() != null, "audio_test");

            CurseRuntime curseRuntime = nm != null ? nm.GetComponent<CurseRuntime>() : null;
            if (curseRuntime != null)
            {
                SerializedObject so = new(curseRuntime);
                Check(so.FindProperty("dollPrefab")?.objectReferenceValue != null, "curse_doll_prefab");
                Check(so.FindProperty("tabletPrefab")?.objectReferenceValue != null, "curse_tablet_prefab");
            }

            GameObject dollPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(DollPrefabPath);
            GameObject tabletPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(TabletPrefabPath);
            Check(dollPrefab != null && dollPrefab.GetComponent<CursedItem>() != null, "curse_doll_item");
            Check(tabletPrefab != null && tabletPrefab.GetComponent<CursedItem>() != null, "curse_tablet_item");
            Check(dollPrefab != null && dollPrefab.GetComponent<GrabbableProp>() != null, "curse_doll_grabbable");
            Check(tabletPrefab != null && tabletPrefab.GetComponent<GrabbableProp>() != null, "curse_tablet_grabbable");
            Check(dollPrefab != null && dollPrefab.GetComponent<NetworkIdentity>() != null, "curse_doll_identity");
            Check(tabletPrefab != null && tabletPrefab.GetComponent<NetworkIdentity>() != null, "curse_tablet_identity");

            // 数值对照 §4.4：笑面偶 2kg/850（未封印 510）、牌位 15kg/1300（未封印 780）。
            CheckProfile(DollProfilePath, CursedItem.DollId, 2f, 850, 510);
            CheckProfile(TabletProfilePath, CursedItem.TabletId, 15f, 1300, 780);

            int monsterAnchors = Object.FindObjectsByType<MonsterSpawnAnchor>(FindObjectsSortMode.None).Length;
            Check(monsterAnchors == MonsterAnchors.Length, $"monster_anchors_{monsterAnchors}");
            int curseAnchors = Object.FindObjectsByType<CurseSpawnAnchor>(FindObjectsSortMode.None).Length;
            Check(curseAnchors == CurseAnchors.Length, $"curse_anchors_{curseAnchors}");
            int patrolAnchors = Object.FindObjectsByType<ScavengeNightmare.Gameplay.PatrolAnchor>(FindObjectsSortMode.None).Length;
            Check(patrolAnchors >= 5, $"patrol_anchors_{patrolAnchors}");

            bool monsterRegistered = false;
            if (nm != null && monsterPrefab != null && nm.spawnPrefabs != null)
            {
                monsterRegistered = nm.spawnPrefabs.Contains(monsterPrefab);
            }

            Check(monsterRegistered, "monster_registered");

            // S4B-5 三关表：配额/件数/怪物/诅咒/安全期必须与验收行逐字一致。
            LegContentTable legTable = AssetDatabase.LoadAssetAtPath<LegContentTable>(LegTablePath);
            Check(legTable != null, "leg_table_asset");

            if (legTable != null)
            {
                int[] quotas = { 1800, 2400, 2800 };
                int[] props = { 12, 14, 16 };
                int[] monsters = { 1, 3, 4 };
                int[] curses = { 0, 1, 2 };

                for (int leg = 1; leg <= 3; leg++)
                {
                    Check(legTable.QuotaFor(leg) == quotas[leg - 1], $"leg_quota_{leg}_{legTable.QuotaFor(leg)}");
                    Check(legTable.PropsFor(leg) == props[leg - 1], $"leg_props_{leg}_{legTable.PropsFor(leg)}");
                    Check(legTable.MonstersFor(leg) == monsters[leg - 1], $"leg_monsters_{leg}_{legTable.MonstersFor(leg)}");
                    Check(legTable.CursesFor(leg) == curses[leg - 1], $"leg_curses_{leg}_{legTable.CursesFor(leg)}");
                    Check(legTable.SafetyFor(leg) >= 60f, $"leg_safety_{leg}_{legTable.SafetyFor(leg)}");
                }
            }

            // 锚点可见性：按 legsMask 过滤后每关的件数与货值必须等于验收数字。
            PropSpawnAnchor[] propAnchors = Object.FindObjectsByType<PropSpawnAnchor>(FindObjectsSortMode.None);
            Check(propAnchors.Length == 16, $"prop_anchors_{propAnchors.Length}");

            for (int leg = 1; leg <= 3; leg++)
            {
                int bit = 1 << (leg - 1);
                int count = 0;
                int totalValue = 0;

                foreach (PropSpawnAnchor anchor in propAnchors)
                {
                    if ((anchor.legsMask & bit) == 0)
                    {
                        continue;
                    }

                    count++;
                    GrabbableProp prop = FindSpawnProp(nm, anchor.profileId);
                    totalValue += prop != null && prop.Profile != null ? prop.Profile.value : 0;
                }

                Check(count == LegAnchorCounts[leg - 1], $"leg_{leg}_anchors_{count}");
                Check(totalValue == LegTotalValues[leg - 1], $"leg_{leg}_value_{totalValue}");
            }

            // 商店终端只在安全屋。切过去数一次，再重新加载目录——
            // OpenScene 会卸载先前 LoadAssetAtPath 拿到的实例，继续复用旧引用会读到 0。
            EditorSceneManager.OpenScene(SafehouseScene, OpenSceneMode.Single);
            int safehouseShops = Object.FindObjectsByType<ShopTerminal>(FindObjectsSortMode.None).Length;
            Check(safehouseShops == 1, $"safehouse_shop_{safehouseShops}");

            // S4C-2：安全屋出口必须带「全队确认」HUD（夜路撤离门没有确认流程，不要求）。
            LevelPortal safehousePortal = FindFirst<LevelPortal>();
            Check(safehousePortal != null, "safehouse_portal");
            Check(safehousePortal != null && safehousePortal.GetComponent<LevelPortalHud>() != null, "portal_continue_hud");

            ShopCatalog catalogNow = AssetDatabase.LoadAssetAtPath<ShopCatalog>(ShopCatalogPath);

            if (failures == 0)
            {
                Debug.Log($"[S4B] S4B_VALIDATE_OK vitals=1 revive=1 wipe=1 downedHud=1 shop8=1 tests=14 portalHud=1 " +
                          $"monsters={monsterAnchors} monsterKinds=3 noiseHud=1 monsterPrefab=1 noiseState=1 " +
                          $"curseAnchors={curseAnchors} curseKinds=2 curseHud=1 " +
                          $"legs=3 legAnchors=12/14/16 legValues=3155/4365/4785 belt=1 stamina=1 " +
                          $"nightShop=0 safehouseShop={safehouseShops} shopItems={(catalogNow != null ? catalogNow.Count : 0)}");
            }
        }

        /// <summary>诅咒物件档的三项数值：质量、标价、未封印 60% 后的价值。</summary>
        private static void CheckProfile(string path, string id, float mass, int listed, int unsealed)
        {
            PropPhysicsProfile profile = AssetDatabase.LoadAssetAtPath<PropPhysicsProfile>(path);

            if (profile == null)
            {
                Debug.LogError($"[S4B] S4B_VALIDATE_FAIL check=curse_profile_missing_{id}");
                return;
            }

            if (profile.id != id)
            {
                Debug.LogError($"[S4B] S4B_VALIDATE_FAIL check=curse_profile_id want={id} actual={profile.id}");
            }

            if (Mathf.Abs(profile.mass - mass) > 0.001f)
            {
                Debug.LogError($"[S4B] S4B_VALIDATE_FAIL check=curse_profile_mass_{id} want={mass} actual={profile.mass}");
            }

            if (profile.value != listed)
            {
                Debug.LogError($"[S4B] S4B_VALIDATE_FAIL check=curse_profile_value_{id} want={listed} actual={profile.value}");
            }

            int discounted = Mathf.RoundToInt(listed * CursedItem.UnsealedValueRate);
            if (discounted != unsealed)
            {
                Debug.LogError($"[S4B] S4B_VALIDATE_FAIL check=curse_profile_unsealed_{id} want={unsealed} actual={discounted}");
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

            if (prefab.GetComponent<CurseHud>() == null)
            {
                prefab.AddComponent<CurseHud>();
                changed = true;
            }

            // S4B-5：工具带（≤7kg 小件挂腰）与体能（连续冲刺上限）。
            if (prefab.GetComponent<BeltSlot>() == null)
            {
                prefab.AddComponent<BeltSlot>();
                changed = true;
            }

            if (prefab.GetComponent<PlayerStamina>() == null)
            {
                prefab.AddComponent<PlayerStamina>();
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
            changed |= AddIfMissing<S4BGreeterTest>(nm.gameObject);
            changed |= AddIfMissing<S4BScreamTest>(nm.gameObject);
            changed |= AddIfMissing<MonsterDirector>(nm.gameObject);
            changed |= AddIfMissing<CurseRuntime>(nm.gameObject);
            changed |= AddIfMissing<S4BCurseTest>(nm.gameObject);
            changed |= AddIfMissing<S4BLegTest>(nm.gameObject);
            changed |= AddIfMissing<S4BBeltTest>(nm.gameObject);
            changed |= AddIfMissing<S4BPerfTest>(nm.gameObject);
            changed |= AddIfMissing<S4BShop8Test>(nm.gameObject);
            changed |= AddIfMissing<S4CLegClientTest>(nm.gameObject);
            changed |= AddIfMissing<S4CAudioTest>(nm.gameObject);

            GameObject dollPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(DollPrefabPath);
            GameObject tabletPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(TabletPrefabPath);

            CurseRuntime curseRuntime = nm.GetComponent<CurseRuntime>();
            if (curseRuntime != null)
            {
                SerializedObject curseSo = new(curseRuntime);
                changed |= AssignSpawnerPrefab(curseSo, "dollPrefab", dollPrefab);
                changed |= AssignSpawnerPrefab(curseSo, "tabletPrefab", tabletPrefab);
                curseSo.ApplyModifiedPropertiesWithoutUndo();
            }

            MonsterSpawner spawner = nm.GetComponent<MonsterSpawner>();
            if (spawner == null)
            {
                spawner = nm.gameObject.AddComponent<MonsterSpawner>();
                changed = true;
            }

            GameObject monsterPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(MonsterPrefab);
            GameObject greeterPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(GreeterPrefab);
            GameObject screamPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ScreamPrefab);

            if (spawner != null)
            {
                SerializedObject spawnerSo = new(spawner);
                changed |= AssignSpawnerPrefab(spawnerSo, "hollowEyePrefab", monsterPrefab);
                changed |= AssignSpawnerPrefab(spawnerSo, "plasticGreeterPrefab", greeterPrefab);
                changed |= AssignSpawnerPrefab(spawnerSo, "screamCanPrefab", screamPrefab);
                spawnerSo.ApplyModifiedPropertiesWithoutUndo();
            }

            changed |= EnsureMonsterAnchors();
            changed |= EnsureCurseAnchors();
            // 必须在 S4ASceneSetup.RebuildAnchors 之后：重建会把 legsMask 复位成默认 7。
            changed |= EnsureLegMasks();

            foreach (GameObject prefab in new[] { monsterPrefab, greeterPrefab, screamPrefab, dollPrefab, tabletPrefab })
            {
                if (prefab == null)
                {
                    continue;
                }

                bool wasRegistered = nm.spawnPrefabs != null && nm.spawnPrefabs.Contains(prefab);
                S4ASceneSetup.RegisterPrefab(scene, prefab);
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

        // ==================== S4B-5 三关梯度 / 工具带 / 体能 ====================

        /// <summary>
        /// 三关内容表资源。缺资源时 <see cref="LegContentTable.Instance"/> 会退回内置值，
        /// 但退回值不可调、也不算「表驱动」，所以装配时一定要把资产建出来。
        /// </summary>
        private static void EnsureLegContentTable()
        {
            S4ASceneSetup.EnsureFolder("Assets/_Game/Resources");

            int[] quotas = { 1800, 2400, 2800 };
            int[] props = { 12, 14, 16 };
            int[] monsters = { 1, 3, 4 };
            int[] curses = { 0, 1, 2 };
            float[] safety = { 120f, 90f, 60f };

            LegContentTable table = AssetDatabase.LoadAssetAtPath<LegContentTable>(LegTablePath);
            if (table == null)
            {
                table = ScriptableObject.CreateInstance<LegContentTable>();
                table.EditorConfigure(quotas, props, monsters, curses, safety);
                AssetDatabase.CreateAsset(table, LegTablePath);
                EditorUtility.SetDirty(table);
                AssetDatabase.SaveAssets();
                Debug.Log("[S4B] S4B_LEG_TABLE_CREATED path=" + LegTablePath);
                return;
            }

            if (SameInts(table.EditorQuotas, quotas)
                && SameInts(table.EditorProps, props)
                && SameInts(table.EditorMonsters, monsters)
                && SameInts(table.EditorCurses, curses)
                && SameFloats(table.EditorSafety, safety))
            {
                return;
            }

            table.EditorConfigure(quotas, props, monsters, curses, safety);
            EditorUtility.SetDirty(table);
            AssetDatabase.SaveAssets();
            Debug.Log("[S4B] S4B_LEG_TABLE_REPAIRED path=" + LegTablePath);
        }

        /// <summary>
        /// 关卡可见性：L1 = 12 件（3155）/ L2 = 14 件（4365）/ L3 = 全 16 件（4785）。
        /// cabinet 在 L1+L3、不在 L2；tv/tire/statue 只在 L2+L3；三个 can 中第三个只在 L3。
        /// </summary>
        private static bool EnsureLegMasks()
        {
            List<PropSpawnAnchor> anchors = new(
                Object.FindObjectsByType<PropSpawnAnchor>(FindObjectsSortMode.None));

            // RebuildAnchors 保留锚点名，按名字排序后「第几个 can」跨 Setup 运行稳定。
            anchors.Sort((a, b) => string.CompareOrdinal(a.gameObject.name, b.gameObject.name));

            bool changed = false;
            int canIndex = 0;

            foreach (PropSpawnAnchor anchor in anchors)
            {
                int mask;

                if (anchor.profileId == "can")
                {
                    canIndex++;
                    mask = canIndex == 3 ? ThirdCanLegMask : 7;
                }
                else
                {
                    mask = 7;

                    foreach ((string profile, int value) in LegMaskRules)
                    {
                        if (anchor.profileId == profile)
                        {
                            mask = value;
                            break;
                        }
                    }
                }

                if (anchor.legsMask != mask)
                {
                    anchor.legsMask = mask;
                    EditorUtility.SetDirty(anchor);
                    changed = true;
                }
            }

            return changed;
        }

        private static bool SameInts(int[] actual, int[] expected)
        {
            if (actual == null || actual.Length != expected.Length)
            {
                return false;
            }

            for (int i = 0; i < expected.Length; i++)
            {
                if (actual[i] != expected[i])
                {
                    return false;
                }
            }

            return true;
        }

        private static bool SameFloats(float[] actual, float[] expected)
        {
            if (actual == null || actual.Length != expected.Length)
            {
                return false;
            }

            for (int i = 0; i < expected.Length; i++)
            {
                if (Mathf.Abs(actual[i] - expected[i]) > 0.001f)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>与 PropSpawner.FindPrefab 同口径：按物件档 id 在 spawnPrefabs 里找。</summary>
        private static GrabbableProp FindSpawnProp(NetworkManager nm, string profileId)
        {
            if (nm == null || nm.spawnPrefabs == null || string.IsNullOrEmpty(profileId))
            {
                return null;
            }

            foreach (GameObject prefab in nm.spawnPrefabs)
            {
                if (prefab == null)
                {
                    continue;
                }

                GrabbableProp prop = prefab.GetComponent<GrabbableProp>();
                if (prop != null && prop.Profile != null && prop.Profile.id == profileId)
                {
                    return prop;
                }
            }

            return null;
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

        // ==================== S4B-4 两件诅咒 ====================

        /// <summary>
        /// 两件诅咒的物件档 + 预制体。数值逐字照抄 §4.4：
        /// 笑面偶 2kg / 850 / 未封印 510；无字牌位 15kg / 1300 / 未封印 780。
        /// 折价率不在档里写，统一在 <see cref="CursedItem.UnsealedValueRate"/>：
        /// 写两处迟早会出现「表改了代码没改」的半吊子口径。
        /// </summary>
        private static void EnsureCurseAssets()
        {
            S4ASceneSetup.EnsureFolder("Assets/_Game/Data/Props");
            S4ASceneSetup.EnsureFolder("Assets/_Game/Prefabs/Props");

            Material material = EnsureCurseMaterial();

            PropPhysicsProfile doll = EnsureCurseProfile(DollProfilePath, CursedItem.DollId, "笑面偶", 2f, 850);
            PropPhysicsProfile tablet = EnsureCurseProfile(TabletProfilePath, CursedItem.TabletId, "无字牌位", 15f, 1300);

            EnsureCursePrefab(DollPrefabPath, "curse_doll", doll, material, CurseKind.Doll,
                PrimitiveType.Capsule, new Vector3(0.16f, 0.22f, 0.16f), new Vector3(0.32f, 0.5f, 0.32f),
                0f, 0f);

            EnsureCursePrefab(TabletPrefabPath, "curse_tablet", tablet, material, CurseKind.Tablet,
                PrimitiveType.Cube, new Vector3(0.26f, 0.42f, 0.07f), new Vector3(0.3f, 0.5f, 0.12f),
                1.5f, 0.05f);

            AssetDatabase.SaveAssets();
        }

        private static Material EnsureCurseMaterial()
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(CurseMat);
            if (material != null)
            {
                return material;
            }

            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                shader = Shader.Find("Standard");
            }

            material = new Material(shader);
            Color cursed = new(0.58f, 0.30f, 0.66f, 1f);
            material.color = cursed;
            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", cursed);
            }

            AssetDatabase.CreateAsset(material, CurseMat);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static PropPhysicsProfile EnsureCurseProfile(
            string path, string id, string displayName, float mass, int value)
        {
            PropPhysicsProfile profile = AssetDatabase.LoadAssetAtPath<PropPhysicsProfile>(path);
            bool created = profile == null;

            if (created)
            {
                profile = ScriptableObject.CreateInstance<PropPhysicsProfile>();
                AssetDatabase.CreateAsset(profile, path);
            }

            bool changed = created;

            // propType 走 Normal：诅咒的「难搞」来自 CursedItem 的规则（发笑 / 长按放下），
            // 不该顺手把抓握手感也改了——那会让「诅咒到底改了什么」说不清。
            if (profile.id != id) { profile.id = id; changed = true; }
            if (profile.displayName != displayName) { profile.displayName = displayName; changed = true; }
            if (Mathf.Abs(profile.mass - mass) > 0.001f) { profile.mass = mass; changed = true; }
            if (profile.value != value) { profile.value = value; changed = true; }
            if (profile.propType != PropType.Normal) { profile.propType = PropType.Normal; changed = true; }
            if (profile.isFragile) { profile.isFragile = false; changed = true; }

            if (changed)
            {
                EditorUtility.SetDirty(profile);
            }

            return profile;
        }

        private static void EnsureCursePrefab(
            string path, string rootName, PropPhysicsProfile profile, Material material,
            CurseKind kind, PrimitiveType shape, Vector3 visualScale, Vector3 colliderSize,
            float requiredReleaseHold, float handShakeAmplitude)
        {
            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);

            if (existing != null)
            {
                RepairCursePrefab(existing, profile, material, kind, requiredReleaseHold, handShakeAmplitude);
                return;
            }

            GameObject root = new(rootName);

            BoxCollider collider = root.AddComponent<BoxCollider>();
            collider.size = colliderSize;
            collider.center = new Vector3(0f, colliderSize.y * 0.5f, 0f);

            GameObject visual = GameObject.CreatePrimitive(shape);
            visual.name = "Visual";
            visual.transform.SetParent(root.transform, false);
            visual.transform.localPosition = new Vector3(0f, colliderSize.y * 0.5f, 0f);
            visual.transform.localScale = visualScale;

            // 前兆抖动改的是 Visual 的 localPosition：本体是刚体，直接动根 Transform
            // 会和物理求解打架（症状是货物抖着抖着被弹飞）。
            Object.DestroyImmediate(visual.GetComponent<Collider>());

            MeshRenderer renderer = visual.GetComponent<MeshRenderer>();
            if (renderer != null && material != null)
            {
                renderer.sharedMaterial = material;
            }

            root.AddComponent<NetworkIdentity>();

            GrabbableProp grabbable = root.AddComponent<GrabbableProp>();
            SerializedObject grabSo = new(grabbable);
            SerializedProperty profileProp = grabSo.FindProperty("profile");
            if (profileProp != null)
            {
                profileProp.objectReferenceValue = profile;
                grabSo.ApplyModifiedPropertiesWithoutUndo();
            }

            root.AddComponent<NetworkedProp>();

            CursedItem cursed = root.AddComponent<CursedItem>();
            ConfigureCurseItem(cursed, kind, requiredReleaseHold, handShakeAmplitude);

            PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets();
            Debug.Log($"[S4B] S4B_CURSE_PREFAB_CREATED path={path}");
        }

        /// <summary>已存在的预制体就地补齐（幂等），避免「预制体早就有了、改了表却没生效」。</summary>
        private static void RepairCursePrefab(
            GameObject prefab, PropPhysicsProfile profile, Material material, CurseKind kind,
            float requiredReleaseHold, float handShakeAmplitude)
        {
            bool changed = false;

            if (prefab.GetComponent<NetworkIdentity>() == null)
            {
                prefab.AddComponent<NetworkIdentity>();
                changed = true;
            }

            GrabbableProp grabbable = prefab.GetComponent<GrabbableProp>();
            if (grabbable == null)
            {
                grabbable = prefab.AddComponent<GrabbableProp>();
                changed = true;
            }

            SerializedObject grabSo = new(grabbable);
            SerializedProperty profileProp = grabSo.FindProperty("profile");
            if (profileProp != null && profileProp.objectReferenceValue == null)
            {
                profileProp.objectReferenceValue = profile;
                grabSo.ApplyModifiedPropertiesWithoutUndo();
                changed = true;
            }

            if (prefab.GetComponent<NetworkedProp>() == null)
            {
                prefab.AddComponent<NetworkedProp>();
                changed = true;
            }

            CursedItem cursed = prefab.GetComponent<CursedItem>();
            if (cursed == null)
            {
                cursed = prefab.AddComponent<CursedItem>();
                changed = true;
            }

            if (ConfigureCurseItem(cursed, kind, requiredReleaseHold, handShakeAmplitude))
            {
                changed = true;
            }

            Transform visual = prefab.transform.Find("Visual");
            MeshRenderer renderer = visual != null ? visual.GetComponent<MeshRenderer>() : null;
            if (renderer != null && material != null && renderer.sharedMaterial != material)
            {
                renderer.sharedMaterial = material;
                changed = true;
            }

            if (!changed)
            {
                return;
            }

            EditorUtility.SetDirty(prefab);
            PrefabUtility.SavePrefabAsset(prefab);
            AssetDatabase.SaveAssets();
            Debug.Log($"[S4B] S4B_CURSE_PREFAB_REPAIRED path={AssetDatabase.GetAssetPath(prefab)}");
        }

        private static bool ConfigureCurseItem(
            CursedItem cursed, CurseKind kind, float requiredReleaseHold, float handShakeAmplitude)
        {
            string id = kind == CurseKind.Tablet ? CursedItem.TabletId : CursedItem.DollId;
            string name = kind == CurseKind.Tablet ? "无字牌位" : "笑面偶";

            SerializedObject so = new(cursed);
            bool changed = false;

            changed |= SetEnum(so, "kind", (int)kind);
            changed |= SetString(so, "curseId", id);
            changed |= SetString(so, "displayName", name);

            // 发笑参数两件都写（牌位不发笑，但字段留着，将来换规则不用改结构）。
            changed |= SetFloat(so, "laughIntervalMin", 12f);
            changed |= SetFloat(so, "laughIntervalMax", 20f);
            changed |= SetFloat(so, "laughWindup", 1.5f);
            changed |= SetFloat(so, "laughNoise", 8f);

            changed |= SetFloat(so, "requiredReleaseHold", requiredReleaseHold);
            changed |= SetFloat(so, "handShakeAmplitude", handShakeAmplitude);

            if (changed)
            {
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            return changed;
        }

        /// <summary>2 个诅咒锚点，按名字就地校正位置；数量恒为 2。</summary>
        private static bool EnsureCurseAnchors()
        {
            bool changed = false;

            foreach (CurseSpawnAnchor anchor in Object.FindObjectsByType<CurseSpawnAnchor>(FindObjectsSortMode.None))
            {
                bool wanted = false;

                foreach ((string name, CurseKind kind, Vector3 pos) in CurseAnchors)
                {
                    if (anchor.name != name)
                    {
                        continue;
                    }

                    wanted = true;

                    if ((anchor.transform.position - pos).sqrMagnitude > 0.0001f)
                    {
                        anchor.transform.position = pos;
                        EditorUtility.SetDirty(anchor);
                        changed = true;
                    }

                    SerializedObject so = new(anchor);
                    if (SetEnum(so, "curse", (int)kind))
                    {
                        so.ApplyModifiedPropertiesWithoutUndo();
                        changed = true;
                    }

                    break;
                }

                if (!wanted)
                {
                    Object.DestroyImmediate(anchor.gameObject);
                    changed = true;
                }
            }

            foreach ((string name, CurseKind kind, Vector3 pos) in CurseAnchors)
            {
                bool exists = false;
                foreach (CurseSpawnAnchor anchor in Object.FindObjectsByType<CurseSpawnAnchor>(FindObjectsSortMode.None))
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
                CurseSpawnAnchor created = go.AddComponent<CurseSpawnAnchor>();

                SerializedObject so = new(created);
                SetEnum(so, "curse", (int)kind);
                so.ApplyModifiedPropertiesWithoutUndo();

                EditorUtility.SetDirty(created);
                EditorUtility.SetDirty(go);
                changed = true;
            }

            return changed;
        }

        private static bool SetFloat(SerializedObject so, string field, float value)
        {
            SerializedProperty prop = so.FindProperty(field);
            if (prop == null || Mathf.Abs(prop.floatValue - value) < 0.0001f)
            {
                return false;
            }

            prop.floatValue = value;
            return true;
        }

        private static bool SetString(SerializedObject so, string field, string value)
        {
            SerializedProperty prop = so.FindProperty(field);
            if (prop == null || prop.stringValue == value)
            {
                return false;
            }

            prop.stringValue = value;
            return true;
        }

        private static bool SetEnum(SerializedObject so, string field, int value)
        {
            SerializedProperty prop = so.FindProperty(field);
            if (prop == null || prop.enumValueIndex == value)
            {
                return false;
            }

            prop.enumValueIndex = value;
            return true;
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

            MonsterProfile greeter = EnsureExtraProfile(GreeterProfilePath, "plastic_greeter", "塑料导购", 30,
                new Color(0.88f, 0.56f, 0.64f, 1f));
            MonsterProfile scream = EnsureExtraProfile(ScreamProfilePath, "scream_can", "尖啸罐", 0,
                new Color(0.32f, 0.78f, 0.86f, 1f));

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
            }
            else
            {
                CreateHollowEyePrefab(profile, material);
            }

            EnsureExtraMonsterPrefab(GreeterPrefab, typeof(PlasticGreeterBrain), greeter, material, "Monster_PlasticGreeter");
            EnsureExtraMonsterPrefab(ScreamPrefab, typeof(ScreamCanBrain), scream, material, "Monster_ScreamCan");
        }

        private static MonsterProfile EnsureExtraProfile(
            string path, string id, string displayName, int attackDamage, Color bodyColor)
        {
            MonsterProfile profile = AssetDatabase.LoadAssetAtPath<MonsterProfile>(path);
            if (profile != null)
            {
                return profile;
            }

            profile = ScriptableObject.CreateInstance<MonsterProfile>();
            profile.id = id;
            profile.displayName = displayName;
            profile.attackDamage = attackDamage;
            profile.bodyColor = bodyColor;
            // 导购与尖啸罐不吃手电：导购只有「撞墙僵直」，尖啸罐只会被打断前摇。
            profile.flashlightStunSeconds = 0f;
            AssetDatabase.CreateAsset(profile, path);
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            Debug.Log("[S4B] S4B_MON_PROFILE_CREATED path=" + path);
            return profile;
        }

        private static void CreateHollowEyePrefab(MonsterProfile profile, Material material)
        {
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

            PrefabUtility.SaveAsPrefabAsset(root, MonsterPrefab);
            Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets();
            Debug.Log("[S4B] S4B_MON_PREFAB_CREATED path=" + MonsterPrefab);
        }

        /// <summary>S4B-3 的两只新怪：结构与空眼一致，只换 Brain 与数值表。</summary>
        private static void EnsureExtraMonsterPrefab(
            string path, System.Type brainType, MonsterProfile profile, Material material, string rootName)
        {
            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            bool changed = existing == null;

            if (existing == null)
            {
                existing = new GameObject(rootName);
                existing.AddComponent<NetworkIdentity>();
                NetworkTransformReliable reliable = existing.AddComponent<NetworkTransformReliable>();
                reliable.syncInterval = 0.066f;

                CharacterController controller = existing.AddComponent<CharacterController>();
                controller.height = 1.8f;
                controller.radius = 0.35f;
                controller.center = new Vector3(0f, 0.9f, 0f);
                controller.stepOffset = 0.5f;
                controller.slopeLimit = 50f;
                controller.skinWidth = 0.08f;

                GameObject body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                body.name = "Body";
                body.transform.SetParent(existing.transform, false);
                body.transform.localPosition = new Vector3(0f, 0.9f, 0f);
                body.transform.localScale = new Vector3(0.7f, 0.9f, 0.7f);
                Object.DestroyImmediate(body.GetComponent<Collider>());

                MeshRenderer renderer = body.GetComponent<MeshRenderer>();
                if (renderer != null && material != null)
                {
                    renderer.sharedMaterial = material;
                }
            }

            Component brain = existing.GetComponent(brainType);
            if (brain == null)
            {
                brain = existing.AddComponent(brainType);
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

            MonsterVisual visual = existing.GetComponent<MonsterVisual>();
            if (visual == null)
            {
                visual = existing.AddComponent<MonsterVisual>();
                changed = true;
            }

            Transform bodyTransform = existing.transform.Find("Body");
            MeshRenderer bodyRendererComp = bodyTransform != null ? bodyTransform.GetComponent<MeshRenderer>() : null;
            if (AssignVisual(visual, brain as MonsterBrain, bodyRendererComp, bodyTransform))
            {
                changed = true;
            }

            if (!changed)
            {
                return;
            }

            EditorUtility.SetDirty(existing);
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
            {
                PrefabUtility.SaveAsPrefabAsset(existing, path);
                Object.DestroyImmediate(existing);
            }
            else
            {
                PrefabUtility.SavePrefabAsset(existing);
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[S4B] S4B_MON_PREFAB_READY path={path}");
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

        private static bool AssignSpawnerPrefab(SerializedObject spawnerSo, string field, GameObject prefab)
        {
            if (prefab == null)
            {
                return false;
            }

            SerializedProperty prop = spawnerSo.FindProperty(field);
            if (prop == null || prop.objectReferenceValue == prefab)
            {
                return false;
            }

            prop.objectReferenceValue = prefab;
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
