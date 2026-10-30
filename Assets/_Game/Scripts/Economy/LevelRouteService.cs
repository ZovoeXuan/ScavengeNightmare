using System.Collections;
using Mirror;
using ScavengeNightmare.Networking;
using ScavengeNightmare.Gameplay;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ScavengeNightmare.Economy
{
    /// <summary>
    /// 关卡路由：夜路 ⇄ 安全屋。挂在 NetworkManager 物体上（那个物体是 DontDestroyOnLoad，
    /// 换关不会被销毁），所以它能跨场景观察并用「场景名变了」来驱动重建。
    ///
    /// 不写 NetworkManager 子类：那需要把三个场景的 NM 组件整体迁移一遍，
    /// 迁移失败的表现是「联机配置全丢」，代价远高于收益——真正需要 NM 钩子的只有
    /// 「换关后玩家放哪」和「换关后重刷物件」，这两件事用场景名轮询 + 自带锚点就能做完。
    /// </summary>
    public sealed class LevelRouteService : MonoBehaviour
    {
        public static LevelRouteService Instance { get; private set; }

        [SerializeField] private string nightScene = "Night1";
        [SerializeField] private string safehouseScene = "Safehouse";

        [Tooltip("各关过关额（下标 = 关卡序号 - 1）。超出长度时取最后一个，不越界。")]
        [SerializeField] private int[] legQuotas = { 2632, 2632, 2800 };

        private string lastScene = string.Empty;

        public bool InSafehouse => SceneManager.GetActiveScene().name == safehouseScene;

        public string NightSceneName => nightScene;

        public string SafehouseSceneName => safehouseScene;

        public int QuotaFor(int legIndex)
        {
            if (legQuotas == null || legQuotas.Length == 0)
            {
                return 1800;
            }

            int index = Mathf.Clamp(legIndex - 1, 0, legQuotas.Length - 1);
            return legQuotas[index];
        }

        private void Awake()
        {
            Instance = this;
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

            // 首次进入场景不算「换关」：S4ABootstrap 与 PropSpawner 已经在 Start 里
            // 各生成过一次，这里再调 EnsureSceneObjects 会把货刷成两份（实测 32 件）。
            bool firstScene = string.IsNullOrEmpty(lastScene);
            lastScene = now;
            Debug.Log($"[S4A] S4A_ROUTE_SCENE name={now} safehouse={InSafehouse} first={firstScene}");
            StartCoroutine(AfterSceneChanged(now, firstScene));
        }

        private IEnumerator AfterSceneChanged(string sceneName, bool firstScene)
        {
            // 等 Mirror 把玩家、场景对象都重建完。这个延迟不是「保险」——
            // 实测 0.2s 时玩家还没 OnStartServer，挪位置会被随后的一次重建覆盖掉。
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
                // 进安全屋 = 撤销窗口打开
                account.OpenPurchaseWindow();
            }
            else
            {
                account.SetQuota(QuotaFor(account.LegIndex));
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
            foreach (PlayerMotor motor in players)
            {
                if (motor == null)
                {
                    continue;
                }

                // 只救「还留在原点」的玩家。这是换关时 GetStartPosition() 拿到空表的真实故障表现；
                // 而玩法代码/用例已经把人摆到别处时，这里再挪一次会把它们的定位覆盖掉
                // （实测把商店与推车的用例全打挂了：人刚站到店里，0.6s 后被拎回出生点）。
                Vector3 current = motor.transform.position;
                if (new Vector2(current.x, current.z).magnitude > 3f)
                {
                    continue;
                }

                motor.transform.SetPositionAndRotation(anchor.SpawnPosition, anchor.SpawnRotation);
                Debug.Log($"[S4A] S4A_ROUTE_PLACE netId={motor.netId} at=({anchor.SpawnPosition.x:F1},{anchor.SpawnPosition.z:F1})");
            }
        }

        /// <summary>
        /// 服务端：请求推进。夜路 → 安全屋要求过关额达标；安全屋 → 夜路推进一关。
        /// 拒绝路径必须有明确码，否则玩家只会觉得「门坏了」。
        /// </summary>
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
