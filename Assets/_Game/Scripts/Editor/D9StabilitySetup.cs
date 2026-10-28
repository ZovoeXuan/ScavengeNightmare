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
    /// D9 联机稳定性装配（幂等）：
    ///   1. NetworkManager 挂 NetworkHealthService（超时调优 + 断线检测）
    ///   2. 场景挂 ConnectionStatusHUD（断线提示 + 返回大厅）
    ///   3. NetworkManager 挂 D9StressTest（压测入口）
    /// 批处理入口：-executeMethod ScavengeNightmare.EditorTools.D9StabilitySetup.Setup
    /// </summary>
    public static class D9StabilitySetup
    {
        private const string ScenePath = "Assets/_Game/Scenes/Main.unity";

        [MenuItem("拾荒惊魂/D9 · 装配联机稳定性")]
        public static void Setup()
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            NetworkManager nm = Object.FindAnyObjectByType<NetworkManager>();
            if (nm == null)
            {
                Debug.LogError("[D9] 场景没有 NetworkManager");
                return;
            }

            // NetworkHealthService 必须在 StartHost/StartClient 之前 Awake 才能改到 KCP Timeout，
            // 因此挂在与 NetworkManager 同一对象上（它的 Awake 早于任何点击 Host 的时机）。
            bool healthAdded = false;
            if (nm.GetComponent<NetworkHealthService>() == null)
            {
                nm.gameObject.AddComponent<NetworkHealthService>();
                healthAdded = true;
            }

            bool stressAdded = false;
            if (nm.GetComponent<D9StressTest>() == null)
            {
                nm.gameObject.AddComponent<D9StressTest>();
                stressAdded = true;
            }

            bool discAdded = false;
            if (nm.GetComponent<D9DisconnectTest>() == null)
            {
                nm.gameObject.AddComponent<D9DisconnectTest>();
                discAdded = true;
            }

            EditorUtility.SetDirty(nm.gameObject);

            // 服务端不活跃连接清理持久化：默认关闭，开启后僵尸连接不会一直占着 4 人上限的坑位。
            // 写进场景资产，Validate 才能校验真实值（编辑器非运行态下运行时赋值是看不到的）。
            nm.disconnectInactiveConnections = true;
            nm.disconnectInactiveTimeout = 20f;
            EditorUtility.SetDirty(nm);

            // KCP 超时持久化：默认 10000ms 会让「房主掉线 10 秒内提示」在光检测阶段就吃满预算。
            // 写进场景资产而非只靠运行时改，Validate 才能校验真实值，也不依赖服务组件的 Awake 时序。
            Transport transport = nm.GetComponent<Transport>();
            if (transport == null)
            {
                transport = Object.FindAnyObjectByType<Transport>();
            }

            if (transport is kcp2k.KcpTransport kcp)
            {
                kcp.Timeout = 4000;
                EditorUtility.SetDirty(kcp);
            }

            bool hudAdded = false;
            GameObject canvas = null;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name == "ConnectionStatusHUD")
                {
                    canvas = root;
                    break;
                }
            }

            if (canvas == null)
            {
                canvas = new GameObject("ConnectionStatusHUD");
                hudAdded = true;
            }

            if (canvas.GetComponent<ConnectionStatusHUD>() == null)
            {
                canvas.AddComponent<ConnectionStatusHUD>();
            }

            EditorUtility.SetDirty(canvas);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[D9] D9_SETUP_OK healthAdded={healthAdded} stressAdded={stressAdded} hudAdded={hudAdded}");
        }

        [MenuItem("拾荒惊魂/D9 · 校验联机稳定性")]
        public static void Validate()
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            int failures = 0;

            void Check(bool ok, string name)
            {
                if (!ok)
                {
                    failures++;
                    Debug.LogError($"[D9] D9_VALIDATE_FAIL check={name}");
                }
            }

            NetworkManager nm = Object.FindAnyObjectByType<NetworkManager>();
            Check(nm != null, "network_manager");

            if (nm != null)
            {
                NetworkHealthService health = nm.GetComponent<NetworkHealthService>();
                Check(health != null, "health_service");
                Check(nm.GetComponent<D9StressTest>() != null, "stress_test");
                Check(nm.GetComponent<D9DisconnectTest>() != null, "disconnect_test");

                // 服务端应开启不活跃连接清理，避免僵尸连接占坑
                Check(nm.disconnectInactiveConnections, "inactive_kick_enabled");
            }

            Check(Object.FindAnyObjectByType<ConnectionStatusHUD>() != null, "status_hud");

            // KCP 超时必须被调低，否则「10 秒内提示」这条验收在默认 10000ms 下不可能达成
            Transport transport = nm != null ? nm.GetComponent<Transport>() : null;
            if (transport == null)
            {
                transport = Object.FindAnyObjectByType<Transport>();
            }

            if (transport is kcp2k.KcpTransport kcp)
            {
                Check(kcp.Timeout <= 5000, $"kcp_timeout_{kcp.Timeout}");
            }

            if (failures == 0)
            {
                Debug.Log($"[D9] D9_VALIDATE_OK health=1 stress=1 hud=1 kcpTimeout={(transport is kcp2k.KcpTransport k ? k.Timeout : -1)}ms");
            }
            else
            {
                Debug.LogError($"[D9] D9_VALIDATE_FAILED count={failures}");
            }
        }
    }
}
