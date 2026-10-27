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
    /// D5 手感参数装配：生成三套方案（轻快 / 稳重 / 沉重）+ 玩家移动参数表，
    /// 并把方案切换器接到玩家 prefab 上。
    /// 批处理入口：-executeMethod ScavengeNightmare.EditorTools.D5FeelSetup.Setup
    /// </summary>
    public static class D5FeelSetup
    {
        private const string ScenePath = "Assets/_Game/Scenes/Main.unity";
        private const string FeelFolder = "Assets/_Game/Data/Feel";
        private const string MoveSettingsPath = "Assets/_Game/Data/PlayerMove.asset";
        private const string PlayerPrefabPath = "Assets/_Game/Prefabs/Player.prefab";

        private sealed class SchemeSpec
        {
            public string Id;
            public string Name;
            public string Description;
            public string Comedy;
            public float GrabAcceleration;
            public float GrabMaxSpeed;
            public float FollowStiffness;
            public float HoldDistance;
            public float ThrowImpulse;
            public float ThrowSpin;
            public float PlayerSpeed;
            public float CarryDivisor;
        }

        private static readonly SchemeSpec[] Specs =
        {
            new SchemeSpec
            {
                Id = "light", Name = "轻快",
                Description = "跟手快、扔得远、负重几乎不拖沓，物件像「没那么重」。适合把节奏推快。",
                Comedy = "抢货互扯、边跑边甩、扔东西砸队友——快节奏的手忙脚乱",
                GrabAcceleration = 1.5f, GrabMaxSpeed = 1.25f, FollowStiffness = 1.45f, HoldDistance = 1f,
                ThrowImpulse = 1.3f, ThrowSpin = 1.3f, PlayerSpeed = 1.15f, CarryDivisor = 100f
            },
            new SchemeSpec
            {
                Id = "solid", Name = "稳重",
                Description = "标准基线：物件有惯性、有重量，操作不别扭。对照实验的参照组。",
                Comedy = "搬运翻车、窄门卡位、堆叠倒塌——D1 冻结的核心笑点主线",
                GrabAcceleration = 1f, GrabMaxSpeed = 1f, FollowStiffness = 1f, HoldDistance = 1f,
                ThrowImpulse = 1f, ThrowSpin = 1f, PlayerSpeed = 1f, CarryDivisor = 70f
            },
            new SchemeSpec
            {
                Id = "heavy", Name = "沉重",
                Description = "跟手滞后明显、扔不远、抱重物明显掉速，一切都很费劲。适合放大挣扎感。",
                Comedy = "重物拖不动、手滑砸脚、下楼梯被货带着滚——挣扎型身体喜剧",
                GrabAcceleration = 0.55f, GrabMaxSpeed = 0.65f, FollowStiffness = 0.6f, HoldDistance = 1.1f,
                ThrowImpulse = 0.6f, ThrowSpin = 0.6f, PlayerSpeed = 0.85f, CarryDivisor = 45f
            }
        };

        [MenuItem("拾荒惊魂/D5 · 装配手感方案")]
        public static void Setup()
        {
            EnsureFolders();

            FeelScheme[] schemes = new FeelScheme[Specs.Length];
            for (int i = 0; i < Specs.Length; i++)
            {
                schemes[i] = UpsertScheme(Specs[i]);
            }

            PlayerMoveSettings move = UpsertMoveSettings();
            PatchPlayerPrefab(schemes, move);

            // 场景里也存一份移动参数，方便后续替换
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"D5_SETUP_OK schemes={schemes.Length} moveSettings={MoveSettingsPath}");
        }

        [MenuItem("拾荒惊魂/D5 · 校验手感方案")]
        public static void Validate()
        {
            int failures = 0;

            void Check(bool condition, string name)
            {
                if (!condition)
                {
                    failures++;
                    Debug.LogError($"[D5] D5_VALIDATE_FAIL check={name}");
                }
            }

            foreach (SchemeSpec spec in Specs)
            {
                FeelScheme scheme = AssetDatabase.LoadAssetAtPath<FeelScheme>($"{FeelFolder}/Feel_{spec.Id}.asset");
                Check(scheme != null, $"scheme_{spec.Id}");
                if (scheme == null)
                {
                    continue;
                }

                Check(scheme.schemeId == spec.Id, $"scheme_id_{spec.Id}");
                Check(!string.IsNullOrEmpty(scheme.comedyNotes), $"scheme_comedy_{spec.Id}");
                Check(scheme.throwImpulseScale > 0f && scheme.grabAccelerationScale > 0f, $"scheme_values_{spec.Id}");
            }

            FeelScheme light = AssetDatabase.LoadAssetAtPath<FeelScheme>($"{FeelFolder}/Feel_light.asset");
            FeelScheme solid = AssetDatabase.LoadAssetAtPath<FeelScheme>($"{FeelFolder}/Feel_solid.asset");
            FeelScheme heavy = AssetDatabase.LoadAssetAtPath<FeelScheme>($"{FeelFolder}/Feel_heavy.asset");

            if (light != null && solid != null && heavy != null)
            {
                Check(light.throwImpulseScale > solid.throwImpulseScale, "light_throws_farther_than_solid");
                Check(solid.throwImpulseScale > heavy.throwImpulseScale, "solid_throws_farther_than_heavy");
                Check(light.grabAccelerationScale > solid.grabAccelerationScale, "light_grabs_tighter_than_solid");
                Check(solid.grabAccelerationScale > heavy.grabAccelerationScale, "solid_grabs_tighter_than_heavy");
                Check(light.carryDivisor > heavy.carryDivisor, "light_carry_lighter_than_heavy");
            }

            PlayerMoveSettings move = AssetDatabase.LoadAssetAtPath<PlayerMoveSettings>(MoveSettingsPath);
            Check(move != null, "move_settings_asset");

            GameObject player = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            Check(player != null, "player_prefab");
            if (player != null)
            {
                PlayerMotor motor = player.GetComponent<PlayerMotor>();
                Check(motor != null, "player_motor");

                FeelSwitchHUD hud = player.GetComponent<FeelSwitchHUD>();
                Check(hud != null, "player_feel_hud");
                if (hud != null)
                {
                    SerializedObject so = new SerializedObject(hud);
                    SerializedProperty arr = so.FindProperty("schemes");
                    Check(arr != null && arr.arraySize == Specs.Length, $"player_feel_schemes_{arr?.arraySize}");
                }
            }

            if (failures == 0)
            {
                Debug.Log($"D5_VALIDATE_OK schemes={Specs.Length} moveSettings=true playerWired=true");
            }
            else
            {
                Debug.LogError($"[D5] D5_VALIDATE_FAILED count={failures}");
            }
        }

        private static void EnsureFolders()
        {
            if (!AssetDatabase.IsValidFolder(FeelFolder))
            {
                AssetDatabase.CreateFolder("Assets/_Game/Data", "Feel");
            }
        }

        private static FeelScheme UpsertScheme(SchemeSpec spec)
        {
            string path = $"{FeelFolder}/Feel_{spec.Id}.asset";
            FeelScheme scheme = AssetDatabase.LoadAssetAtPath<FeelScheme>(path);
            if (scheme == null)
            {
                scheme = ScriptableObject.CreateInstance<FeelScheme>();
                AssetDatabase.CreateAsset(scheme, path);
            }

            scheme.schemeId = spec.Id;
            scheme.displayName = spec.Name;
            scheme.description = spec.Description;
            scheme.comedyNotes = spec.Comedy;
            scheme.grabAccelerationScale = spec.GrabAcceleration;
            scheme.grabMaxSpeedScale = spec.GrabMaxSpeed;
            scheme.followStiffnessScale = spec.FollowStiffness;
            scheme.holdDistanceScale = spec.HoldDistance;
            scheme.throwImpulseScale = spec.ThrowImpulse;
            scheme.throwSpinScale = spec.ThrowSpin;
            scheme.playerSpeedScale = spec.PlayerSpeed;
            scheme.carryDivisor = spec.CarryDivisor;

            EditorUtility.SetDirty(scheme);
            return scheme;
        }

        private static PlayerMoveSettings UpsertMoveSettings()
        {
            PlayerMoveSettings settings = AssetDatabase.LoadAssetAtPath<PlayerMoveSettings>(MoveSettingsPath);
            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<PlayerMoveSettings>();
                AssetDatabase.CreateAsset(settings, MoveSettingsPath);
            }

            // 与 D3/D4 实测一致的默认值，方案层再在其上做倍率。
            settings.walkSpeed = 3.2f;
            settings.sprintSpeed = 5.0f;
            settings.acceleration = 24f;
            settings.jumpSpeed = 4.2f;
            settings.gravity = -18f;
            settings.lookSensitivity = 0.12f;
            settings.carryDivisor = 70f;

            EditorUtility.SetDirty(settings);
            return settings;
        }

        private static void PatchPlayerPrefab(FeelScheme[] schemes, PlayerMoveSettings move)
        {
            GameObject contents = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);

            PlayerMotor motor = contents.GetComponent<PlayerMotor>();
            if (motor != null)
            {
                SerializedObject motorSo = new SerializedObject(motor);
                SerializedProperty settingsProp = motorSo.FindProperty("settings");
                if (settingsProp != null)
                {
                    settingsProp.objectReferenceValue = move;
                    motorSo.ApplyModifiedPropertiesWithoutUndo();
                }
            }

            FeelSwitchHUD hud = contents.GetComponent<FeelSwitchHUD>();
            if (hud == null)
            {
                hud = contents.AddComponent<FeelSwitchHUD>();
            }

            SerializedObject so = new SerializedObject(hud);
            SerializedProperty arr = so.FindProperty("schemes");
            if (arr != null)
            {
                arr.arraySize = schemes.Length;
                for (int i = 0; i < schemes.Length; i++)
                {
                    arr.GetArrayElementAtIndex(i).objectReferenceValue = schemes[i];
                }
            }

            SerializedProperty index = so.FindProperty("defaultIndex");
            if (index != null)
            {
                index.intValue = 1; // 稳重
            }

            SerializedProperty gate = so.FindProperty("localGate");
            if (gate != null)
            {
                gate.objectReferenceValue = contents.GetComponent<GrabController>();
            }

            so.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(contents, PlayerPrefabPath);
            PrefabUtility.UnloadPrefabContents(contents);
        }
    }
}
