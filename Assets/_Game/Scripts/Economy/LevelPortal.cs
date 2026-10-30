using Mirror;
using ScavengeNightmare.Networking;
using UnityEngine;

namespace ScavengeNightmare.Economy
{
    /// <summary>
    /// 夜路撤离门 / 安全屋出口。服务端长按判定，客户端只负责上报交互键。
    ///
    /// 与公秤一样轮询玩家的 <c>InteractHeldServer</c>，不另开 Command：
    /// 场景对象没有 owner，Command 挂不住（商店已经踩过这个坑）。
    /// </summary>
    public sealed class LevelPortal : MonoBehaviour
    {
        public static LevelPortal Instance { get; private set; }

        [SerializeField] private float radius = 3f;

        [Tooltip("长按秒数。与公秤的 1.2s 一致，避免玩家学两套手势")]
        [SerializeField] private float holdSeconds = 1.2f;

        private float holdTime;
        private float lastRejectAt;

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

            PlayerMotor[] players = FindObjectsByType<PlayerMotor>(FindObjectsSortMode.None);
            bool holding = false;

            foreach (PlayerMotor motor in players)
            {
                if (motor == null)
                {
                    continue;
                }

                Vector3 a = motor.transform.position;
                Vector3 b = transform.position;
                a.y = 0f;
                b.y = 0f;

                if (Vector3.Distance(a, b) > radius)
                {
                    continue;
                }

                Networking.PlayerInteract interact = motor.GetComponent<Networking.PlayerInteract>();
                if (interact != null && interact.InteractHeldServer)
                {
                    holding = true;
                    break;
                }
            }

            if (!holding)
            {
                holdTime = 0f;
                return;
            }

            holdTime += Time.deltaTime;

            if (holdTime < holdSeconds)
            {
                return;
            }

            holdTime = 0f;

            LevelRouteService route = LevelRouteService.Instance;
            TeamAccount account = TeamAccount.Instance;

            if (route == null || account == null)
            {
                return;
            }

            // 夜路未达标时每 1s 只报一次，避免长按时刷屏
            if (!route.InSafehouse && !account.QuotaReached && Time.unscaledTime - lastRejectAt > 1f)
            {
                lastRejectAt = Time.unscaledTime;
                Debug.Log($"[S4A] GATE_PORTAL_REJECT reason=quota_not_reached leg={account.LegIndex} progress={account.LegProgress} quota={account.LegQuota} missing={Mathf.Max(0, account.LegQuota - account.LegProgress)}");
                return;
            }

            route.ServerAdvance(account);
        }
    }
}
