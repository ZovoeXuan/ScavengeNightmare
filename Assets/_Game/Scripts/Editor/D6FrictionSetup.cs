using ScavengeNightmare.Gameplay;
using ScavengeNightmare.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ScavengeNightmare.EditorTools
{
    /// <summary>
    /// D6 摩擦沙盒装配：新增大衣柜 / 玻璃花瓶两类物件，圆桶碰撞体改为 Convex MeshCollider，
    /// 场景补窄门与可开合门，并挂摩擦事件 HUD。
    /// 批处理入口：-executeMethod ScavengeNightmare.EditorTools.D6FrictionSetup.Setup
    /// </summary>
    public static class D6FrictionSetup
    {
        private const string ScenePath = "Assets/_Game/Scenes/Main.unity";
        private const string ProfileFolder = "Assets/_Game/Data/Props";
        private const string PrefabFolder = "Assets/_Game/Prefabs/Props";
        private const string MaterialFolder = "Assets/_Game/Art/Props";
        private const string TuningPath = "Assets/_Game/Data/FrictionTuning.asset";
        private const string BarrelPrefabPath = PrefabFolder + "/barrel.prefab";

        [MenuItem("拾荒惊魂/D6 · 装配摩擦沙盒")]
        public static void Setup()
        {
            FrictionTuning tuning = UpsertTuning();

            PropPhysicsProfile cabinet = UpsertProfile(
                "cabinet", "大衣柜", 35f, 100f, 0.6f, 0.65f, 0f, 0.7f, false, 0, 3f);
            PropPhysicsProfile vase = UpsertProfile(
                "vase", "玻璃花瓶", 3f, 22f, 0.4f, 0.45f, 0.05f, 0.3f, true, 800, 3f);

            UpsertPropPrefab("cabinet", cabinet, PrimitiveType.Cube, new Vector3(1.6f, 1.2f, 0.5f),
                new Color(0.48f, 0.36f, 0.28f), false, false);
            UpsertPropPrefab("vase", vase, PrimitiveType.Cylinder, new Vector3(0.3f, 0.3f, 0.3f),
                new Color(0.62f, 0.78f, 0.82f), false, true);

            PatchBarrelPrefab();

            // 先开场景再挂组件，否则找不到场景里的 NetworkManager
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            HookSceneBootstrap(tuning);
            BuildNarrowDoor(scene);
            BuildSwingDoor(scene);
            PlaceNewProps(scene);
            WireAutoTestProfiles();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("D6_SETUP_OK tuning=1 newProps=2 barrel=convex narrowDoor=1 swingDoor=1");
        }

        [MenuItem("拾荒惊魂/D6 · 校验摩擦沙盒")]
        public static void Validate()
        {
            int failures = 0;

            void Check(bool condition, string name)
            {
                if (!condition)
                {
                    failures++;
                    Debug.LogError($"[D6] D6_VALIDATE_FAIL check={name}");
                }
            }

            Check(AssetDatabase.LoadAssetAtPath<FrictionTuning>(TuningPath) != null, "friction_tuning_asset");

            GameObject barrel = AssetDatabase.LoadAssetAtPath<GameObject>(BarrelPrefabPath);
            Check(barrel != null, "barrel_prefab");
            if (barrel != null)
            {
                MeshCollider mesh = barrel.GetComponent<MeshCollider>();
                Check(mesh != null && mesh.convex, "barrel_convex_collider");
                Check(barrel.GetComponent<RollingProp>() != null, "barrel_rolling_prop");
                Check(barrel.GetComponent<CapsuleCollider>() == null, "barrel_no_capsule_collider");
            }

            GameObject vase = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabFolder + "/vase.prefab");
            Check(vase != null, "vase_prefab");
            if (vase != null)
            {
                Check(vase.GetComponent<FragileProp>() != null, "vase_fragile_prop");
                GrabbableProp grab = vase.GetComponent<GrabbableProp>();
                Check(grab != null && grab.Profile != null && grab.Profile.isFragile, "vase_is_fragile");
                Check(grab != null && grab.Profile != null && grab.Profile.value > 0, "vase_has_value");
            }

            GameObject cabinet = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabFolder + "/cabinet.prefab");
            Check(cabinet != null, "cabinet_prefab");

            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Check(scene.IsValid(), "scene_open");
            Check(Object.FindAnyObjectByType<NarrowDoorWatcher>() != null, "scene_narrow_door");
            Check(Object.FindAnyObjectByType<DoorTrap>() != null, "scene_door_trap");
            Check(Object.FindAnyObjectByType<FrictionHUD>() != null, "scene_friction_hud");

            if (failures == 0)
            {
                Debug.Log("D6_VALIDATE_OK tuning=1 barrel=convex vase=fragile cabinet=1 narrowDoor=1 swingDoor=1");
            }
            else
            {
                Debug.LogError($"[D6] D6_VALIDATE_FAILED count={failures}");
            }
        }

        private static void WireAutoTestProfiles()
        {
            D6AutoTest auto = Object.FindAnyObjectByType<D6AutoTest>();
            if (auto == null)
            {
                return;
            }

            SerializedObject so = new SerializedObject(auto);
            so.FindProperty("barrelProfile").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<PropPhysicsProfile>($"{ProfileFolder}/Prop_barrel.asset");
            so.FindProperty("cabinetProfile").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<PropPhysicsProfile>($"{ProfileFolder}/Prop_cabinet.asset");
            so.FindProperty("vaseProfile").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<PropPhysicsProfile>($"{ProfileFolder}/Prop_vase.asset");
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static FrictionTuning UpsertTuning()        {
            FrictionTuning tuning = AssetDatabase.LoadAssetAtPath<FrictionTuning>(TuningPath);
            if (tuning == null)
            {
                tuning = ScriptableObject.CreateInstance<FrictionTuning>();
                AssetDatabase.CreateAsset(tuning, TuningPath);
            }

            EditorUtility.SetDirty(tuning);
            return tuning;
        }

        private static PropPhysicsProfile UpsertProfile(
            string id, string displayName, float mass, float impulse,
            float dynFriction, float staticFriction, float bounce, float angularDamping,
            bool fragile, int value, float breakSpeed)
        {
            string path = $"{ProfileFolder}/Prop_{id}.asset";
            PropPhysicsProfile profile = AssetDatabase.LoadAssetAtPath<PropPhysicsProfile>(path);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<PropPhysicsProfile>();
                AssetDatabase.CreateAsset(profile, path);
            }

            profile.id = id;
            profile.displayName = displayName;
            profile.mass = mass;
            profile.throwImpulse = impulse;
            profile.dynamicFriction = dynFriction;
            profile.staticFriction = staticFriction;
            profile.bounciness = bounce;
            profile.angularDamping = angularDamping;
            profile.isFragile = fragile;
            profile.value = value;
            profile.breakSpeed = breakSpeed;
            EditorUtility.SetDirty(profile);
            return profile;
        }

        private static void UpsertPropPrefab(
            string id, PropPhysicsProfile profile, PrimitiveType shape, Vector3 scale,
            Color color, bool rolling, bool fragile)
        {
            string materialPath = $"{MaterialFolder}/M_{id}.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(material, materialPath);
            }

            material.SetColor("_BaseColor", color);
            material.SetFloat("_Smoothness", 0.15f);
            EditorUtility.SetDirty(material);

            GameObject go = GameObject.CreatePrimitive(shape);
            go.name = id;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = material;

            Rigidbody body = go.AddComponent<Rigidbody>();
            body.mass = profile.mass;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.interpolation = RigidbodyInterpolation.Interpolate;

            GrabbableProp grabbable = go.AddComponent<GrabbableProp>();
            SerializedObject so = new SerializedObject(grabbable);
            so.FindProperty("profile").objectReferenceValue = profile;
            so.ApplyModifiedPropertiesWithoutUndo();

            if (rolling)
            {
                go.AddComponent<RollingProp>();
            }

            if (fragile)
            {
                go.AddComponent<FragileProp>();
            }

            PrefabUtility.SaveAsPrefabAsset(go, $"{PrefabFolder}/{id}.prefab");
            Object.DestroyImmediate(go);
        }

        private static void PatchBarrelPrefab()
        {
            GameObject contents = PrefabUtility.LoadPrefabContents(BarrelPrefabPath);

            // CapsuleCollider 会让圆桶滚成胶囊（自己拐回来），必须换成凸包网格碰撞体
            CapsuleCollider capsule = contents.GetComponent<CapsuleCollider>();
            if (capsule != null)
            {
                Object.DestroyImmediate(capsule);
            }

            MeshCollider mesh = contents.GetComponent<MeshCollider>();
            if (mesh == null)
            {
                mesh = contents.AddComponent<MeshCollider>();
            }

            mesh.sharedMesh = contents.GetComponent<MeshFilter>().sharedMesh;
            mesh.convex = true;

            if (contents.GetComponent<RollingProp>() == null)
            {
                contents.AddComponent<RollingProp>();
            }

            PrefabUtility.SaveAsPrefabAsset(contents, BarrelPrefabPath);
            PrefabUtility.UnloadPrefabContents(contents);
        }

        private static void HookSceneBootstrap(FrictionTuning tuning)
        {
            Mirror.NetworkManager nm = Object.FindAnyObjectByType<Mirror.NetworkManager>();
            if (nm == null)
            {
                Debug.LogWarning("[D6] 场景没有 NetworkManager，摩擦 HUD / 引导未挂载");
                return;
            }

            if (nm.GetComponent<FrictionBootstrap>() == null)
            {
                FrictionBootstrap bootstrap = nm.gameObject.AddComponent<FrictionBootstrap>();
                SerializedObject so = new SerializedObject(bootstrap);
                so.FindProperty("tuning").objectReferenceValue = tuning;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            else
            {
                FrictionBootstrap bootstrap = nm.GetComponent<FrictionBootstrap>();
                SerializedObject so = new SerializedObject(bootstrap);
                so.FindProperty("tuning").objectReferenceValue = tuning;
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            if (nm.GetComponent<FrictionHUD>() == null)
            {
                nm.gameObject.AddComponent<FrictionHUD>();
            }

            if (nm.GetComponent<D6AutoTest>() == null)
            {
                nm.gameObject.AddComponent<D6AutoTest>();
            }
        }

        private static void BuildNarrowDoor(Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name == "NarrowDoor")
                {
                    Object.DestroyImmediate(root);
                }
            }

            GameObject door = new GameObject("NarrowDoor");
            door.transform.position = new Vector3(0f, 0f, 8f);

            // 两根立柱 + 门楣：门洞净宽 1.2m，玩家（直径 1.0m）能过，大衣柜（1.6m）过不去
            CreateBox(door.transform, "LeftPillar", new Vector3(-1.1f, 1.1f, 0f), new Vector3(1f, 2.2f, 0.3f));
            CreateBox(door.transform, "RightPillar", new Vector3(1.1f, 1.1f, 0f), new Vector3(1f, 2.2f, 0.3f));
            CreateBox(door.transform, "Lintel", new Vector3(0f, 2.35f, 0f), new Vector3(3.2f, 0.3f, 0.3f));

            GameObject zone = new GameObject("GapZone");
            zone.transform.SetParent(door.transform, false);
            zone.transform.localPosition = new Vector3(0f, 1f, 0f);
            BoxCollider box = zone.AddComponent<BoxCollider>();
            box.size = new Vector3(1.2f, 2f, 0.4f);
            box.isTrigger = true;
            zone.AddComponent<NarrowDoorWatcher>();
        }

        private static void BuildSwingDoor(Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name == "SwingDoor")
                {
                    Object.DestroyImmediate(root);
                }
            }

            GameObject door = new GameObject("SwingDoor");
            door.transform.position = new Vector3(6f, 0f, 8f);

            CreateBox(door.transform, "FrameL", new Vector3(-0.6f, 1.1f, 0f), new Vector3(0.2f, 2.2f, 0.3f));
            CreateBox(door.transform, "FrameR", new Vector3(0.6f, 1.1f, 0f), new Vector3(0.2f, 2.2f, 0.3f));
            CreateBox(door.transform, "FrameTop", new Vector3(0f, 2.35f, 0f), new Vector3(1.4f, 0.3f, 0.3f));

            GameObject pivot = new GameObject("LeafPivot");
            pivot.transform.SetParent(door.transform, false);
            pivot.transform.localPosition = new Vector3(-0.5f, 0f, 0f);

            GameObject leaf = CreateBox(pivot.transform, "Leaf", new Vector3(0.5f, 1.1f, 0f), new Vector3(1f, 2.2f, 0.12f));

            DoorTrap trap = door.AddComponent<DoorTrap>();
            SerializedObject so = new SerializedObject(trap);
            so.FindProperty("leaf").objectReferenceValue = pivot.transform;
            so.FindProperty("leafCollider").objectReferenceValue = leaf.GetComponent<BoxCollider>();
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void PlaceNewProps(Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name == "FrictionProps")
                {
                    Object.DestroyImmediate(root);
                }
            }

            GameObject group = new GameObject("FrictionProps");

            PlaceProp(group.transform, "cabinet", new Vector3(-2.5f, 0.65f, 5.5f));
            PlaceProp(group.transform, "vase", new Vector3(2.5f, 0.35f, 5.5f));
            PlaceProp(group.transform, "vase", new Vector3(3.2f, 0.35f, 5.5f));
        }

        private static void PlaceProp(Transform parent, string id, Vector3 position)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabFolder}/{id}.prefab");
            if (prefab == null)
            {
                Debug.LogError($"[D6] 缺 prefab：{id}");
                return;
            }

            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            instance.transform.SetParent(parent, false);
            instance.transform.position = position;
        }

        private static GameObject CreateBox(Transform parent, string name, Vector3 localPos, Vector3 scale)
        {
            GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = name;
            box.transform.SetParent(parent, false);
            box.transform.localPosition = localPos;
            box.transform.localScale = scale;
            return box;
        }
    }
}
