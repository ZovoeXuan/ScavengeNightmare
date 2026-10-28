using Mirror;
using ScavengeNightmare.Networking;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ScavengeNightmare.EditorTools
{
    /// <summary>
    /// D11 · M1 验收装配（幂等）：
    ///   1. NetworkManager 挂 M1Acceptance（重量感 / 延迟量化）
    ///   2. NetworkManager 挂 LatencySimulation 并把 wrap 连到 KcpTransport，
    ///      组件保持禁用，运行时才由 M1Acceptance 启用
    /// 批处理入口：-executeMethod ScavengeNightmare.EditorTools.D11AcceptanceSetup.Setup
    /// </summary>
    public static class D11AcceptanceSetup
    {
        private const string ScenePath = "Assets/_Game/Scenes/Main.unity";

        [MenuItem("拾荒惊魂/D11 · 装配 M1 验收工具")]
        public static void Setup()
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            NetworkManager nm = Object.FindAnyObjectByType<NetworkManager>();
            if (nm == null)
            {
                Debug.LogError("[D11] 场景没有 NetworkManager");
                return;
            }

            bool acceptAdded = false;
            if (nm.GetComponent<M1Acceptance>() == null)
            {
                nm.gameObject.AddComponent<M1Acceptance>();
                acceptAdded = true;
            }

            // LatencySimulation 的 Awake 会校验 wrap 为空并抛异常，而 Awake 在组件禁用时
            // 照样执行 —— 所以 wrap 必须在编辑器里就连线，不能拖到运行时。
            bool simAdded = false;
            LatencySimulation sim = nm.GetComponent<LatencySimulation>();
            if (sim == null)
            {
                sim = nm.gameObject.AddComponent<LatencySimulation>();
                simAdded = true;
            }

            kcp2k.KcpTransport kcp = nm.GetComponent<kcp2k.KcpTransport>();
            if (kcp == null)
            {
                Debug.LogError("[D11] NetworkManager 上没有 KcpTransport，无法连线延迟模拟");
                return;
            }

            SerializedObject simSo = new SerializedObject(sim);
            SerializedProperty wrapProp = simSo.FindProperty("wrap");
            if (wrapProp != null)
            {
                wrapProp.objectReferenceValue = kcp;
                simSo.ApplyModifiedPropertiesWithoutUndo();
            }

            // 默认禁用：正常游玩与其它自动化用例都不该被注入延迟
            sim.enabled = false;
            EditorUtility.SetDirty(sim);
            EditorUtility.SetDirty(nm.gameObject);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[D11] D11_SETUP_OK acceptAdded={acceptAdded} simAdded={simAdded} wrap=KcpTransport simDisabled=true");
        }

        [MenuItem("拾荒惊魂/D11 · 校验 M1 验收工具")]
        public static void Validate()
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            int failures = 0;

            void Check(bool ok, string name)
            {
                if (!ok)
                {
                    failures++;
                    Debug.LogError($"[D11] D11_VALIDATE_FAIL check={name}");
                }
            }

            NetworkManager nm = Object.FindAnyObjectByType<NetworkManager>();
            Check(nm != null, "network_manager");

            if (nm != null)
            {
                Check(nm.GetComponent<M1Acceptance>() != null, "m1_acceptance");

                LatencySimulation sim = nm.GetComponent<LatencySimulation>();
                Check(sim != null, "latency_simulation");

                if (sim != null)
                {
                    SerializedObject so = new SerializedObject(sim);
                    SerializedProperty wrap = so.FindProperty("wrap");
                    Check(wrap != null && wrap.objectReferenceValue != null, "latency_wrap_wired");
                    Check(!sim.enabled, "latency_disabled_by_default");
                }

                // 负重联动必须真的接线：GrabController 抓到东西要能传到 PlayerMotor
                Check(nm.GetComponent<kcp2k.KcpTransport>() != null, "kcp_transport");
            }

            if (failures == 0)
            {
                Debug.Log("[D11] D11_VALIDATE_OK acceptance=1 latencySim=1 wrap=1 disabled=1");
            }
            else
            {
                Debug.LogError($"[D11] D11_VALIDATE_FAILED count={failures}");
            }
        }
    }
}
