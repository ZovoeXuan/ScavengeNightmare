using System.Collections;
using Mirror;
using ScavengeNightmare.Gameplay;
using ScavengeNightmare.Networking;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ScavengeNightmare.Economy
{
    /// <summary>
    /// 关卡路由服务 V2 升级版：
    /// 修复 MovePlayersToAnchor 直接修改 transform 导致远端客户端 (ClientToServer) 位置不刷新的 Bug (NET-03)。
    /// </summary>
    public sealed class LevelRouteService : MonoBehaviour
    {
        public static LevelRouteService Instance { get; private set; }

        [SerializeField] private string nightScene = "Night1";
        [SerializeField] private string safehouseScene = "Safehouse";

        private string lastScene = string.Empty;

        public bool InSafehouse => SceneManager.GetActiveScene().name == safehouseScene;
        public string NightSceneName => nightScene;
        public string SafehouseSceneName => safehouseScene;
        /// <summary>兼容旧拼写属性。</summary>
        public string SafehousesSceneName => safehouseScene;

        public int QuotaFor(int legIndex)
        {
            return LegContentTable.Instance.QuotaFor(legIndex);
        }

        private void Awake()
        {
            Debug.Log($"[S4A] S4A_ROUTE_BOOT scene={SceneManager.GetActiveScene().name}");

            if (Instance == null)
            {
                Instance = this;
            }
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void Update()
        {
            if (!NetworkServer.active)
            {
                return;
            }

            string now = SceneManager.GetActiveScene().name;
            if (now == lastScene)
            {
                return;
            }

            bool firstScene = string.IsNullOrEmpty(lastScene);
            lastScene = now;
            Debug.Log($"[S4A] S4A_ROUTE_SCENE name={now} safehouse={InSafehouse} first={firstScene}");
            StartCoroutine(AfterSceneChanged(now, firstScene));
        }

        private IEnumerator AfterSceneChanged(string sceneName, bool firstScene)
        {
            yield return new WaitForSeconds(0.6f);

            if (!firstScene)
            {
                MovePlayersToAnchor();

                NetworkManager manager = NetworkManager.singleton;
                S4ABootstrap boot = manager != null ? manager.GetComponent<S4ABootstrap>() : null;
                boot?.EnsureSceneObjects();
            }

            TeamAccount account = TeamAccount.Instance;
            if (account == null)
            {
                yield break;
            }

            if (InSafehouse)
            {
                account.OpenPurchaseWindow();
            }
            else
            {
                account.SetQuota(QuotaFor(account.LegIndex));
                PlaytestRecorder.Instance?.NoteLegStart(account.LegIndex, sceneName, account.LegQuota);
            }

            Debug.Log($"[S4A] S4A_ROUTE_READY scene={sceneName} leg={account.LegIndex} quota={account.LegQuota} balance={account.Balance}");
        }

        private void MovePlayersToAnchor()
        {
            SceneEntryAnchor anchor = SceneEntryAnchor.ResolveActive();
            if (anchor == null)
            {
                Debug.LogWarning("[S4A] S4A_ROUTE_WARN reason=no_entry_anchor scene=" + SceneManager.GetActiveScene().name);
                return;
            }

            PlayerMotor[] players = FindObjectsByType<PlayerMotor>(FindObjectsSortMode.None);
            int idx = 0;
            foreach (PlayerMotor motor in players)
            {
                if (motor == null)
                {
                    continue;
                }

                Vector3 current = motor.transform.position;
                if (new Vector2(current.x, current.z).magnitude > 3f)
                {
                    continue;
                }

                Vector3 offset = new Vector3((idx % 2) * 0.7f, 0f, (idx / 2) * 0.7f);
                Vector3 spawnPos = anchor.SpawnPosition + offset;
                motor.ServerTeleport(spawnPos, anchor.SpawnRotation);
                idx++;
                Debug.Log($"[S4A] S4A_ROUTE_PLACE netId={motor.netId} at=({spawnPos.x:F1},{spawnPos.z:F1})");
            }
        }

        [Server]
        public bool ServerAdvance(TeamAccount account)
        {
            if (account == null || NetworkManager.singleton == null)
            {
                return false;
            }

            if (InSafehouse)
            {
                int nextLeg = account.LegIndex + 1;
                account.ClosePurchaseWindow("leave_safehouse");
                account.NextLeg(QuotaFor(nextLeg), "table");
                Debug.Log($"[S4A] GATE_PORTAL from=Safehouse to={nightScene} leg={account.LegIndex} quota={account.LegQuota}");
                NetworkManager.singleton.ServerChangeScene(nightScene);
                return true;
            }

            if (!account.QuotaReached)
            {
                int missing = Mathf.Max(0, account.LegQuota - account.LegProgress);
                Debug.Log($"[S4A] GATE_PORTAL_REJECT reason=quota_not_reached leg={account.LegIndex} progress={account.LegProgress} quota={account.LegQuota} missing={missing}");
                return false;
            }

            account.ClosePurchaseWindow("enter_safehouse");
            Debug.Log($"[S4A] GATE_PORTAL from={nightScene} to={safehouseScene} leg={account.LegIndex} balance={account.Balance}");
            NetworkManager.singleton.ServerChangeScene(safehouseScene);
            return true;
        }
    }
}
