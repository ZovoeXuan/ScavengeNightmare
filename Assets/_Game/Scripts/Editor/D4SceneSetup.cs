using System.Collections.Generic;
using ScavengeNightmare.Gameplay;
using ScavengeNightmare.Networking;
using ScavengeNightmare.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ScavengeNightmare.EditorTools
{
    /// <summary>
    /// D4 抓取沙盒的编辑器装配脚本（幂等，可重复执行；prefab 与试验场每次重建）。
    /// 产出：4 个物件物理档、4 个物件 prefab、Player 挂 GrabController + GrabHUD、
    /// 场景里的 8 个可抓物件与一面投掷墙。
    /// 批处理入口：-executeMethod ScavengeNightmare.EditorTools.D4SceneSetup.Setup
    /// </summary>
    public static class D4SceneSetup
    {
        private const string ScenePath = "Assets/_Game/Scenes/Main.unity";
        private const string ProfileFolder = "Assets/_Game/Data/Props";
        private const string PrefabFolder = "Assets/_Game/Prefabs/Props";
        private const string MaterialFolder = "Assets/_Game/Art/Props";
        private const string PlayerPrefabPath = "Assets/_Game/Prefabs/Player.prefab";

        private sealed class PropSpec
        {
            public string Id;
            public string DisplayName;
            public float Mass;
            public float ThrowImpulse;
            public float DynamicFriction;
            public float StaticFriction;
            public float Bounciness;
            public float AngularDamping;
            public PrimitiveType Shape;
            public Vector3 Scale;
            public Color Color;
        }

        private static readonly PropSpec[] Specs =
        {
            new PropSpec
            {
                Id = "can", DisplayName = "罐头", Mass = 2f, ThrowImpulse = 26f,
                DynamicFriction = 0.45f, StaticFriction = 0.5f, Bounciness = 0.1f, AngularDamping = 0.4f,
                Shape = PrimitiveType.Cylinder, Scale = new Vector3(0.22f, 0.31f, 0.22f),
                Color = new Color(0.72f, 0.33f, 0.28f)
            },
            new PropSpec
            {
                Id = "box", DisplayName = "纸箱", Mass = 9f, ThrowImpulse = 42f,
                DynamicFriction = 0.6f, StaticFriction = 0.65f, Bounciness = 0.02f, AngularDamping = 0.6f,
                Shape = PrimitiveType.Cube, Scale = new Vector3(0.7f, 0.7f, 0.7f),
                Color = new Color(0.62f, 0.47f, 0.28f)
            },
            new PropSpec
            {
                Id = "barrel", DisplayName = "圆桶", Mass = 22f, ThrowImpulse = 70f,
                DynamicFriction = 0.32f, StaticFriction = 0.35f, Bounciness = 0.05f, AngularDamping = 0.15f,
                Shape = PrimitiveType.Cylinder, Scale = new Vector3(0.5f, 0.9f, 0.5f),
                Color = new Color(0.28f, 0.44f, 0.62f)
            },
            new PropSpec
            {
                Id = "statue", DisplayName = "雕像", Mass = 42f, ThrowImpulse = 120f,
                DynamicFriction = 0.7f, StaticFriction = 0.75f, Bounciness = 0f, AngularDamping = 0.8f,
                Shape = PrimitiveType.Cube, Scale = new Vector3(0.4f, 1.4f, 0.4f),
                Color = new Color(0.55f, 0.55f, 0.5f)
            }
        };

        [MenuItem("拾荒惊魂/D4 · 装配抓取沙盒")]
        public static void Setup()
        {
            EnsureFolders();

            Dictionary<string, PropPhysicsProfile> profiles = new();
            foreach (PropSpec spec in Specs)
            {
                profiles[spec.Id] = UpsertProfile(spec);
                UpsertPropPrefab(spec, profiles[spec.Id]);
            }

            PatchPlayerPrefab();

            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            BuildPropPen(scene);
            HookAutoTest(profiles["barrel"]);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"D4_SETUP_OK profiles={profiles.Count} prefabs={Specs.Length} scene={ScenePath}");
        }

        [MenuItem("拾荒惊魂/D4 · 校验抓取沙盒")]
        public static void Validate()
        {
            int failures = 0;

            void Check(bool condition, string name)
            {
                if (!condition)
                {
                    failures++;
                    Debug.LogError($"[D4] D4_VALIDATE_FAIL check={name}");
                }
            }

            foreach (PropSpec spec in Specs)
            {
                string profilePath = $"{ProfileFolder}/Prop_{spec.Id}.asset";
                string prefabPath = $"{PrefabFolder}/{spec.Id}.prefab";

                PropPhysicsProfile profile = AssetDatabase.LoadAssetAtPath<PropPhysicsProfile>(profilePath);
                Check(profile != null, $"profile_{spec.Id}");
                if (profile != null)
                {
                    Check(profile.throwImpulse > 0f && profile.mass > 0f, $"profile_values_{spec.Id}");
                    Check(profile.breakDistance > profile.holdDistance, $"profile_break_{spec.Id}");
                }

                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                Check(prefab != null, $"prefab_{spec.Id}");
                if (prefab != null)
                {
                    Rigidbody body = prefab.GetComponent<Rigidbody>();
                    Check(body != null, $"prefab_rigidbody_{spec.Id}");
                    Check(body != null && body.collisionDetectionMode == CollisionDetectionMode.ContinuousDynamic,
                        $"prefab_continuous_{spec.Id}");
                    Check(prefab.GetComponent<Collider>() != null, $"prefab_collider_{spec.Id}");
                    GrabbableProp grab = prefab.GetComponent<GrabbableProp>();
                    Check(grab != null && grab.Profile != null, $"prefab_grabbable_{spec.Id}");
                }
            }

            GameObject player = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            Check(player != null, "player_prefab");
            if (player != null)
            {
                Check(player.GetComponent<GrabController>() != null, "player_grab_controller");
                Check(player.GetComponent<GrabHUD>() != null, "player_grab_hud");
                Check(player.GetComponentInChildren<Camera>(true) != null, "player_camera_kept");
            }

            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Check(scene.IsValid(), "scene_open");
            int props = 0;
            bool wall = false;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name == "PropPen")
                {
                    props = root.GetComponentsInChildren<GrabbableProp>().Length;
                }

                if (root.name == "ThrowWall")
                {
                    wall = true;
                }
            }

            Check(props >= 8, $"scene_props_{props}");
            Check(wall, "scene_throw_wall");

            D4AutoTest auto = Object.FindAnyObjectByType<D4AutoTest>();
            Check(auto != null, "auto_test_present");

            if (failures == 0)
            {
                Debug.Log($"D4_VALIDATE_OK profiles={Specs.Length} props={props} wall={wall}");
            }
            else
            {
                Debug.LogError($"[D4] D4_VALIDATE_FAILED count={failures}");
            }
        }

        private static void EnsureFolders()
        {
            CreateFolderIfMissing("Assets/_Game/Data", "Props");
            CreateFolderIfMissing("Assets/_Game/Art", "Props");
            CreateFolderIfMissing("Assets/_Game/Prefabs", "Props");
        }

        private static void CreateFolderIfMissing(string parent, string child)
        {
            if (!AssetDatabase.IsValidFolder($"{parent}/{child}"))
            {
                AssetDatabase.CreateFolder(parent, child);
            }
        }

        private static PropPhysicsProfile UpsertProfile(PropSpec spec)
        {
            string path = $"{ProfileFolder}/Prop_{spec.Id}.asset";
            PropPhysicsProfile profile = AssetDatabase.LoadAssetAtPath<PropPhysicsProfile>(path);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<PropPhysicsProfile>();
                AssetDatabase.CreateAsset(profile, path);
            }

            profile.id = spec.Id;
            profile.displayName = spec.DisplayName;
            profile.mass = spec.Mass;
            profile.throwImpulse = spec.ThrowImpulse;
            profile.dynamicFriction = spec.DynamicFriction;
            profile.staticFriction = spec.StaticFriction;
            profile.bounciness = spec.Bounciness;
            profile.angularDamping = spec.AngularDamping;
            profile.highlightTint = new Color(0.55f, 0.85f, 1f);
            EditorUtility.SetDirty(profile);
            return profile;
        }

        private static void UpsertPropPrefab(PropSpec spec, PropPhysicsProfile profile)
        {
            Material material = UpsertMaterial(spec);

            GameObject go = GameObject.CreatePrimitive(spec.Shape);
            go.name = spec.Id;
            go.transform.localScale = spec.Scale;

            Renderer renderer = go.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = material;
            }

            Rigidbody body = go.AddComponent<Rigidbody>();
            body.mass = spec.Mass;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.interpolation = RigidbodyInterpolation.Interpolate;

            GrabbableProp grabbable = go.AddComponent<GrabbableProp>();
            SerializedObject so = new SerializedObject(grabbable);
            SerializedProperty prop = so.FindProperty("profile");
            if (prop != null)
            {
                prop.objectReferenceValue = profile;
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            string path = $"{PrefabFolder}/{spec.Id}.prefab";
            PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
        }

        private static Material UpsertMaterial(PropSpec spec)
        {
            string path = $"{MaterialFolder}/M_{spec.Id}.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit");
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }

            material.SetColor("_BaseColor", spec.Color);
            material.SetFloat("_Smoothness", 0.15f);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void PatchPlayerPrefab()
        {
            GameObject contents = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);

            if (contents.GetComponent<GrabController>() == null)
            {
                contents.AddComponent<GrabController>();
            }

            if (contents.GetComponent<GrabHUD>() == null)
            {
                contents.AddComponent<GrabHUD>();
            }

            PrefabUtility.SaveAsPrefabAsset(contents, PlayerPrefabPath);
            PrefabUtility.UnloadPrefabContents(contents);
        }

        private static void BuildPropPen(Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name == "PropPen" || root.name == "ThrowWall")
                {
                    Object.DestroyImmediate(root);
                }
            }

            GameObject pen = new GameObject("PropPen");

            Vector3[] slots =
            {
                new Vector3(-2.4f, 0f, -1.4f), new Vector3(-0.8f, 0f, -1.4f),
                new Vector3(0.8f, 0f, -1.4f), new Vector3(2.4f, 0f, -1.4f),
                new Vector3(-2.4f, 0f, -3.0f), new Vector3(-0.8f, 0f, -3.0f),
                new Vector3(0.8f, 0f, -3.0f), new Vector3(2.4f, 0f, -3.0f)
            };

            for (int i = 0; i < slots.Length; i++)
            {
                PropSpec spec = Specs[i % Specs.Length];
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabFolder}/{spec.Id}.prefab");
                if (prefab == null)
                {
                    continue;
                }

                GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                instance.transform.SetParent(pen.transform, false);
                instance.name = $"{spec.Id}_{i + 1}";
                instance.transform.position = slots[i] + Vector3.up * (spec.Scale.y * 0.5f + 0.05f);
            }

            GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = "ThrowWall";
            wall.transform.position = new Vector3(0f, 1.5f, -6f);
            wall.transform.localScale = new Vector3(8f, 3f, 0.3f);
        }

        private static void HookAutoTest(PropPhysicsProfile barrel)
        {
            D4AutoTest auto = Object.FindAnyObjectByType<D4AutoTest>();
            if (auto == null)
            {
                Mirror.NetworkManager nm = Object.FindAnyObjectByType<Mirror.NetworkManager>();
                if (nm != null)
                {
                    auto = nm.gameObject.AddComponent<D4AutoTest>();
                }
            }

            if (auto == null)
            {
                Debug.LogWarning("[D4] 场景里没有 NetworkManager，D4AutoTest 未挂载。");
                return;
            }

            SerializedObject so = new SerializedObject(auto);
            SerializedProperty prop = so.FindProperty("testProfile");
            if (prop != null)
            {
                prop.objectReferenceValue = barrel;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }
    }
}
