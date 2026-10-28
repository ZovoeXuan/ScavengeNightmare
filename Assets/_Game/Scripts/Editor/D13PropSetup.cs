using System.Collections.Generic;
using Mirror;
using ScavengeNightmare.Gameplay;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ScavengeNightmare.EditorTools
{
    /// <summary>
    /// D13 · 物件系统化：把物件参数表扩成完整的「类型 / 价值 / 质量 / 摩擦 / 抓握点」结构，
    /// 并创建 20 种拾荒物白盒占位。
    ///
    /// 一条硬约束：**现有 6 件（can / box / barrel / statue / cabinet / vase）的物理数值只能补类型，
    /// 不能改。** D4 投掷基线（4.14 m/s）依赖 barrel.throwImpulse=70，D6 摩擦用例也依赖既有参数。
    /// 所以已存在的档只写 propType / gripOffset，新增的 14 件才写完整数值。
    ///
    /// 批处理入口：-executeMethod ScavengeNightmare.EditorTools.D13PropSetup.Setup
    /// </summary>
    public static class D13PropSetup
    {
        private const string ProfileFolder = "Assets/_Game/Data/Props";
        private const string PrefabFolder = "Assets/_Game/Prefabs/Props";
        private const string MaterialFolder = "Assets/_Game/Data/Materials";
        private const string MarketScene = "Assets/_Game/Scenes/Supermarket.unity";

        private sealed class PropSpec
        {
            public string id;
            public string name;
            public PropType type;
            public float mass;
            public int value;
            public PrimitiveType shape = PrimitiveType.Cube;
            public Vector3 scale = Vector3.one;
            public PrimitiveType? shape2;
            public Vector3 scale2 = Vector3.one;
            public Vector3 offset2 = Vector3.zero;
            public float breakSpeed = 3f;
            public float friction = 0.45f;
            public Vector3 gripOffset = Vector3.zero;
        }

        /// <summary>20 种拾荒物，四类各 5 件。前 6 个 id 是既有物件，数值以「只补类型」方式更新。</summary>
        private static readonly PropSpec[] Specs =
        {
            // ---- 易碎：别摔 ----
            new PropSpec { id = "vase", name = "玻璃花瓶", type = PropType.Fragile, mass = 3f, value = 800,
                shape = PrimitiveType.Cylinder, scale = new Vector3(0.22f, 0.35f, 0.22f), breakSpeed = 2.2f, friction = 0.35f },
            new PropSpec { id = "glass", name = "玻璃杯", type = PropType.Fragile, mass = 1f, value = 120,
                shape = PrimitiveType.Cylinder, scale = new Vector3(0.10f, 0.13f, 0.10f), breakSpeed = 1.8f, friction = 0.3f },
            new PropSpec { id = "bottle", name = "酒瓶", type = PropType.Fragile, mass = 1.5f, value = 200,
                shape = PrimitiveType.Capsule, scale = new Vector3(0.12f, 0.20f, 0.12f), breakSpeed = 2.4f, friction = 0.3f },
            new PropSpec { id = "tv", name = "老电视", type = PropType.Fragile, mass = 12f, value = 600,
                shape = PrimitiveType.Cube, scale = new Vector3(0.9f, 0.6f, 0.15f), breakSpeed = 2.6f, friction = 0.5f,
                gripOffset = new Vector3(0f, -0.1f, 0.1f) },
            new PropSpec { id = "bulbbox", name = "灯泡箱", type = PropType.Fragile, mass = 1f, value = 90,
                shape = PrimitiveType.Cube, scale = new Vector3(0.28f, 0.28f, 0.28f), breakSpeed = 1.6f, friction = 0.4f },

            // ---- 滚动：会跑 ----
            new PropSpec { id = "barrel", name = "圆桶", type = PropType.Rolling, mass = 22f, value = 150,
                shape = PrimitiveType.Cylinder, scale = new Vector3(0.5f, 0.45f, 0.5f), friction = 0.12f },
            new PropSpec { id = "watermelon", name = "西瓜", type = PropType.Rolling, mass = 5f, value = 45,
                shape = PrimitiveType.Sphere, scale = new Vector3(0.5f, 0.5f, 0.5f), friction = 0.15f },
            new PropSpec { id = "basketball", name = "篮球", type = PropType.Rolling, mass = 0.6f, value = 80,
                shape = PrimitiveType.Sphere, scale = new Vector3(0.36f, 0.36f, 0.36f), friction = 0.2f },
            new PropSpec { id = "tire", name = "轮胎", type = PropType.Rolling, mass = 10f, value = 110,
                shape = PrimitiveType.Cylinder, scale = new Vector3(0.7f, 0.25f, 0.7f), friction = 0.18f },
            new PropSpec { id = "oildrum", name = "油桶", type = PropType.Rolling, mass = 18f, value = 180,
                shape = PrimitiveType.Cylinder, scale = new Vector3(0.45f, 0.40f, 0.45f), friction = 0.13f },

            // ---- 重型：搬不动 ----
            new PropSpec { id = "statue", name = "石雕像", type = PropType.Heavy, mass = 42f, value = 900,
                shape = PrimitiveType.Cube, scale = new Vector3(0.4f, 0.8f, 0.4f), friction = 0.8f,
                shape2 = PrimitiveType.Sphere, scale2 = new Vector3(0.32f, 0.32f, 0.32f), offset2 = new Vector3(0f, 0.55f, 0f) },
            new PropSpec { id = "cabinet", name = "大衣柜", type = PropType.Heavy, mass = 35f, value = 400,
                shape = PrimitiveType.Cube, scale = new Vector3(0.8f, 1.7f, 0.5f), friction = 0.7f,
                gripOffset = new Vector3(0.35f, 0f, 0f) },
            new PropSpec { id = "safe", name = "保险箱", type = PropType.Heavy, mass = 60f, value = 1200,
                shape = PrimitiveType.Cube, scale = new Vector3(0.7f, 0.7f, 0.6f), friction = 0.85f },
            new PropSpec { id = "washer", name = "洗衣机", type = PropType.Heavy, mass = 55f, value = 700,
                shape = PrimitiveType.Cube, scale = new Vector3(0.65f, 0.9f, 0.65f), friction = 0.75f,
                gripOffset = new Vector3(0.3f, 0f, 0f) },
            new PropSpec { id = "fridge", name = "冰箱", type = PropType.Heavy, mass = 70f, value = 850,
                shape = PrimitiveType.Cube, scale = new Vector3(0.7f, 1.8f, 0.65f), friction = 0.8f,
                gripOffset = new Vector3(0.32f, 0f, 0f) },

            // ---- 普通：随手拿 ----
            new PropSpec { id = "can", name = "罐头", type = PropType.Normal, mass = 2f, value = 20,
                shape = PrimitiveType.Cylinder, scale = new Vector3(0.14f, 0.10f, 0.14f), friction = 0.4f },
            new PropSpec { id = "box", name = "纸箱", type = PropType.Normal, mass = 9f, value = 60,
                shape = PrimitiveType.Cube, scale = new Vector3(0.5f, 0.4f, 0.4f), friction = 0.5f },
            new PropSpec { id = "ricebag", name = "米袋", type = PropType.Normal, mass = 15f, value = 130,
                shape = PrimitiveType.Cube, scale = new Vector3(0.5f, 0.35f, 0.3f), friction = 0.6f },
            new PropSpec { id = "toolbox", name = "工具箱", type = PropType.Normal, mass = 7f, value = 160,
                shape = PrimitiveType.Cube, scale = new Vector3(0.5f, 0.25f, 0.3f), friction = 0.45f },
            new PropSpec { id = "microwave", name = "微波炉", type = PropType.Normal, mass = 14f, value = 320,
                shape = PrimitiveType.Cube, scale = new Vector3(0.6f, 0.35f, 0.4f), friction = 0.5f },
        };

        /// <summary>既有物件：只补类型，绝不改物理数值（否则 D4 投掷基线会漂）。</summary>
        private static readonly HashSet<string> LegacyIds = new() { "can", "box", "barrel", "statue", "cabinet", "vase" };

        private static readonly Dictionary<PropType, Color> TypeColors = new()
        {
            { PropType.Normal, new Color(0.72f, 0.68f, 0.52f) },
            { PropType.Fragile, new Color(0.62f, 0.82f, 0.92f) },
            { PropType.Rolling, new Color(0.85f, 0.62f, 0.45f) },
            { PropType.Heavy, new Color(0.55f, 0.56f, 0.60f) },
        };

        [MenuItem("拾荒惊魂/D13 · 生成物件系统")]
        public static void Setup()
        {
            Dictionary<PropType, Material> mats = BuildTypeMaterials();

            int created = 0, updated = 0;
            List<GameObject> prefabs = new();

            foreach (PropSpec spec in Specs)
            {
                string profilePath = $"{ProfileFolder}/Prop_{spec.id}.asset";
                bool existed = AssetDatabase.LoadAssetAtPath<PropPhysicsProfile>(profilePath) != null;
                PropPhysicsProfile profile = UpsertProfile(spec, existing: existed);

                GameObject prefab = BuildPrefab(spec, profile, mats[spec.type]);
                prefabs.Add(prefab);

                if (existed) updated++; else created++;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            // 只把 20 件注册进 Supermarket。Main 是冻结的测试基线沙盒，
            // 往它里面塞 14 个新 prefab 会改变 D9 压测的物件构成，风险不值得冒。
            RegisterToMarket(prefabs);

            Debug.Log($"[D13] D13_SETUP_OK profiles={Specs.Length} created={created} updated={updated} prefabs={prefabs.Count}");
        }

        [MenuItem("拾荒惊魂/D13 · 校验物件系统")]
        public static void Validate()
        {
            int failures = 0;
            void Check(bool ok, string name)
            {
                if (!ok)
                {
                    failures++;
                    Debug.LogError($"[D13] D13_VALIDATE_FAIL check={name}");
                }
            }

            PropPhysicsProfile[] all = LoadAll<PropPhysicsProfile>(ProfileFolder, "Prop_");
            Check(all.Length >= 20, $"profile_count_{all.Length}");

            Dictionary<PropType, int> byType = new();
            foreach (PropPhysicsProfile p in all)
            {
                if (p == null) continue;
                if (!byType.ContainsKey(p.propType)) byType[p.propType] = 0;
                byType[p.propType]++;
            }

            foreach (PropType t in System.Enum.GetValues(typeof(PropType)))
            {
                int n = byType.TryGetValue(t, out int v) ? v : 0;
                Check(n >= 4, $"type_{t}_{n}");
            }

            // prefab 与 profile 一一对应，且都挂了 GrabbableProp + NetworkedProp
            int prefabOk = 0;
            foreach (PropSpec spec in Specs)
            {
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabFolder}/{spec.id}.prefab");
                if (prefab == null)
                {
                    Debug.LogError($"[D13] D13_VALIDATE_FAIL check=prefab_missing_{spec.id}");
                    failures++;
                    continue;
                }

                bool hasGrab = prefab.GetComponent<GrabbableProp>() != null;
                bool hasNet = prefab.GetComponent<NetworkedProp>() != null;
                bool hasIdentity = prefab.GetComponent<NetworkIdentity>() != null;
                if (!hasGrab || !hasNet || !hasIdentity)
                {
                    Debug.LogError($"[D13] D13_VALIDATE_FAIL check=prefab_components_{spec.id} grab={hasGrab} net={hasNet} id={hasIdentity}");
                    failures++;
                    continue;
                }

                // 类型与行为组件要一致：易碎件必须带 FragileProp，滚动件必须带 RollingProp
                PropPhysicsProfile profile = prefab.GetComponent<GrabbableProp>().Profile;
                if (profile == null || profile.propType != spec.type)
                {
                    Debug.LogError($"[D13] D13_VALIDATE_FAIL check=prefab_type_mismatch_{spec.id}");
                    failures++;
                    continue;
                }

                if (spec.type == PropType.Fragile && prefab.GetComponent<FragileProp>() == null)
                {
                    Debug.LogError($"[D13] D13_VALIDATE_FAIL check=fragile_component_{spec.id}");
                    failures++;
                    continue;
                }

                if (spec.type == PropType.Rolling && prefab.GetComponent<RollingProp>() == null)
                {
                    Debug.LogError($"[D13] D13_VALIDATE_FAIL check=rolling_component_{spec.id}");
                    failures++;
                    continue;
                }

                prefabOk++;
            }

            Check(prefabOk == Specs.Length, $"prefab_ok_{prefabOk}");

            // 结算组件
            Scene market = EditorSceneManager.OpenScene(MarketScene, OpenSceneMode.Single);
            Check(Object.FindAnyObjectByType<EvacSettlement>() != null, "evac_settlement");

            int registered = CountRegistered(market);
            Check(registered >= 20, $"market_registered_{registered}");

            if (failures == 0)
            {
                string dist = string.Empty;
                foreach (var kv in byType) dist += $"{kv.Key}={kv.Value} ";
                Debug.Log($"[D13] D13_VALIDATE_OK profiles={all.Length} prefabs={prefabOk} registered={registered} types[{dist.Trim()}]");
            }
            else
            {
                Debug.LogError($"[D13] D13_VALIDATE_FAILED count={failures}");
            }
        }

        // ==================== 构建 ====================

        private static PropPhysicsProfile UpsertProfile(PropSpec spec, bool existing)
        {
            string path = $"{ProfileFolder}/Prop_{spec.id}.asset";
            PropPhysicsProfile profile = AssetDatabase.LoadAssetAtPath<PropPhysicsProfile>(path);

            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<PropPhysicsProfile>();
                AssetDatabase.CreateAsset(profile, path);
            }

            profile.id = spec.id;
            profile.displayName = spec.name;
            profile.propType = spec.type;
            profile.gripOffset = spec.gripOffset;

            // value 与 isFragile 不是物理参数（D4 投掷、D6 摩擦都不依赖），
            // 所以既有 6 件也要写：它们是结算的输入，留 0 会让「改表即生效」无从验证。
            profile.value = spec.value;
            profile.isFragile = spec.type == PropType.Fragile;
            profile.highlightTint = TypeColors[spec.type];

            if (!existing)
            {
                // 新物件才写完整数值。既有 6 件的物理参数被 D4/D6 用例依赖，一律不动。
                profile.mass = spec.mass;
                profile.value = spec.value;
                profile.dynamicFriction = spec.friction;
                profile.staticFriction = Mathf.Min(1f, spec.friction + 0.08f);
                profile.breakSpeed = spec.breakSpeed;

                // 投掷冲量按质量档给默认值：重的东西本来就该扔得慢
                profile.throwImpulse = Mathf.Clamp(spec.mass * 3.2f, 30f, 95f);

                // 重型件抓握更迟钝、更容易被卡住脱手，是「搬不动」的手感来源
                if (spec.type == PropType.Heavy)
                {
                    profile.grabAcceleration = 32f;
                    profile.grabMaxSpeed = 5.5f;
                    profile.holdDistance = 1.9f;
                    profile.breakDistance = 2.2f;
                }

                // 滚动件低摩擦低角阻尼，让它真的滚得起来
                if (spec.type == PropType.Rolling)
                {
                    profile.angularDamping = 0.08f;
                    profile.linearDamping = 0.02f;
                    profile.bounciness = 0.25f;
                }
            }
            else if (spec.type == PropType.Fragile)
            {
                // 既有易碎件的 isFragile 本来就开着，这里只确保一致
                profile.isFragile = true;
            }

            EditorUtility.SetDirty(profile);
            return profile;
        }

        private static GameObject BuildPrefab(PropSpec spec, PropPhysicsProfile profile, Material mat)
        {
            GameObject root = GameObject.CreatePrimitive(spec.shape);
            root.name = spec.id;
            root.transform.localScale = spec.scale;
            root.GetComponent<Renderer>().sharedMaterial = mat;

            if (spec.shape2.HasValue)
            {
                GameObject part = GameObject.CreatePrimitive(spec.shape2.Value);
                part.name = spec.id + "_part";
                part.transform.SetParent(root.transform, false);
                part.transform.localPosition = spec.offset2;
                part.transform.localScale = spec.scale2;
                part.GetComponent<Renderer>().sharedMaterial = mat;
            }

            root.AddComponent<NetworkIdentity>();

            GrabbableProp grabbable = root.AddComponent<GrabbableProp>();
            SerializedObject so = new SerializedObject(grabbable);
            SerializedProperty profileProp = so.FindProperty("profile");
            if (profileProp != null)
            {
                profileProp.objectReferenceValue = profile;
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            root.AddComponent<NetworkedProp>();

            // 类型驱动行为组件：改 profile 的 propType 就要同步改这里，
            // 所以 Validate 里专门校验两者一致，避免表改了组件没跟上
            if (spec.type == PropType.Fragile)
            {
                root.AddComponent<FragileProp>();
            }

            if (spec.type == PropType.Rolling)
            {
                root.AddComponent<RollingProp>();
            }

            string path = $"{PrefabFolder}/{spec.id}.prefab";
            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);

            return AssetDatabase.LoadAssetAtPath<GameObject>(path) ?? saved;
        }

        private static Dictionary<PropType, Material> BuildTypeMaterials()
        {
            Dictionary<PropType, Material> result = new();

            if (!AssetDatabase.IsValidFolder(MaterialFolder))
            {
                AssetDatabase.CreateFolder("Assets/_Game/Data", "Materials");
            }

            foreach (var kv in TypeColors)
            {
                string path = $"{MaterialFolder}/PropType_{kv.Key}.mat";
                Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);

                if (mat == null)
                {
                    Shader shader = Shader.Find("Universal Render Pipeline/Lit");
                    if (shader == null) shader = Shader.Find("Standard");
                    mat = new Material(shader);
                    AssetDatabase.CreateAsset(mat, path);
                }

                mat.color = kv.Value;
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", kv.Value);
                EditorUtility.SetDirty(mat);
                result[kv.Key] = mat;
            }

            return result;
        }

        private static void RegisterToMarket(List<GameObject> prefabs)
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(MarketScene) == null)
            {
                Debug.LogWarning("[D13] Supermarket 场景不存在，跳过注册（先跑 D12SceneSetup.Setup）");
                return;
            }

            Scene scene = EditorSceneManager.OpenScene(MarketScene, OpenSceneMode.Single);
            NetworkManager nm = Object.FindAnyObjectByType<NetworkManager>();
            if (nm == null)
            {
                Debug.LogError("[D13] Supermarket 里没有 NetworkManager");
                return;
            }

            SerializedObject so = new SerializedObject(nm);
            SerializedProperty list = so.FindProperty("spawnPrefabs");
            if (list == null)
            {
                Debug.LogError("[D13] 找不到 spawnPrefabs 字段");
                return;
            }

            // 全量覆盖：避免重复注册或残留旧引用导致压测物件构成不可控
            list.ClearArray();
            for (int i = 0; i < prefabs.Count; i++)
            {
                list.InsertArrayElementAtIndex(i);
                list.GetArrayElementAtIndex(i).objectReferenceValue = prefabs[i];
            }

            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(nm);

            if (nm.GetComponent<Networking.D13Acceptance>() == null)
            {
                nm.gameObject.AddComponent<Networking.D13Acceptance>();
            }

            EnsureSettlement();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[D13] D13_REGISTERED scene=Supermarket count={prefabs.Count}");
        }

        private static void EnsureSettlement()
        {
            if (Object.FindAnyObjectByType<EvacSettlement>() != null)
            {
                return;
            }

            GameObject go = new GameObject("EvacSettlement");
            go.AddComponent<NetworkIdentity>();
            EvacSettlement settlement = go.AddComponent<EvacSettlement>();
            EditorUtility.SetDirty(settlement);
            EditorUtility.SetDirty(go);
        }

        private static int CountRegistered(Scene scene)
        {
            NetworkManager nm = Object.FindAnyObjectByType<NetworkManager>();
            if (nm == null) return 0;

            SerializedObject so = new SerializedObject(nm);
            SerializedProperty list = so.FindProperty("spawnPrefabs");
            return list != null ? list.arraySize : 0;
        }

        private static T[] LoadAll<T>(string folder, string prefix) where T : Object
        {
            List<T> found = new();
            string[] guids = AssetDatabase.FindAssets("t:" + typeof(T).Name, new[] { folder });
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!System.IO.Path.GetFileName(path).StartsWith(prefix)) continue;
                T asset = AssetDatabase.LoadAssetAtPath<T>(path);
                if (asset != null) found.Add(asset);
            }

            return found.ToArray();
        }
    }
}
