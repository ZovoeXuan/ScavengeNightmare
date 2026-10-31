using System.Collections.Generic;
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
    ///
    /// 两条分支的规则刻意不同（P0 §3.3 / Sprint 4C R4）：
    ///   - 夜路 → 安全屋：**任意一人**达标长按即可。撤离是个人动作，
    ///     要求全队站在门口会把「有人先跑」变成团灭。
    ///   - 安全屋 → 下一关：**全员**都要在门前长按确认。换关是队伍级动作，
    ///     一个人把全队拖进下一关，就是 4A 遗留的「房主代决」假通过。
    /// </summary>
    public sealed class LevelPortal : MonoBehaviour
    {
        public static LevelPortal Instance { get; private set; }

        [SerializeField] private float radius = 3f;

        [Tooltip("长按秒数。与公秤的 1.2s 一致，避免玩家学两套手势")]
        [SerializeField] private float holdSeconds = 1.2f;

        [Tooltip("安全屋确认：松手/离开半径多久后清空本人 ready。给一次按错键的后悔窗口，又不至于把门锁死")]
        [SerializeField] private float releaseGraceSeconds = 2f;

        /// <summary>单个玩家在本门的确认状态。按 netId 存，玩家离开场景就丢弃。</summary>
        private sealed class ReadyState
        {
            public float Held;
            public float Released;
            public bool Latched;
        }

        private readonly Dictionary<int, ReadyState> readyStates = new();

        private float holdTime;
        private float lastRejectAt;
        private float firstReadyAt;
        private float lastWaitAt;

        /// <summary>安全屋确认进度（服务端）。客户端读 <see cref="TeamAccount"/> 的复制值。</summary>
        public int ReadyCount { get; private set; }
        public int PlayerCount { get; private set; }

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

            LevelRouteService route = LevelRouteService.Instance;
            if (route == null)
            {
                return;
            }

            if (route.InSafehouse)
            {
                UpdateSafehouse(route);
            }
            else
            {
                LeaveSafehouseState();
                UpdateNight(route);
            }
        }

        // ==================== 夜路：任意一人撤离 ====================

        private void UpdateNight(LevelRouteService route)
        {
            PlayerMotor[] players = FindObjectsByType<PlayerMotor>(FindObjectsSortMode.None);
            bool holding = false;

            foreach (PlayerMotor motor in players)
            {
                if (motor == null)
                {
                    continue;
                }

                if (FlatDistance(motor.transform.position) > radius)
                {
                    continue;
                }

                PlayerInteract interact = motor.GetComponent<PlayerInteract>();
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

            TeamAccount account = TeamAccount.Instance;
            if (account == null)
            {
                return;
            }

            // 夜路未达标时每 1s 只报一次，避免长按时刷屏
            if (!account.QuotaReached && Time.unscaledTime - lastRejectAt > 1f)
            {
                lastRejectAt = Time.unscaledTime;
                Debug.Log($"[S4A] GATE_PORTAL_REJECT reason=quota_not_reached leg={account.LegIndex} progress={account.LegProgress} quota={account.LegQuota} missing={Mathf.Max(0, account.LegQuota - account.LegProgress)}");
                return;
            }

            route.ServerAdvance(account);
        }

        // ==================== 安全屋：全队确认 ====================

        private void UpdateSafehouse(LevelRouteService route)
        {
            TeamAccount account = TeamAccount.Instance;
            if (account == null)
            {
                return;
            }

            PlayerMotor[] players = FindObjectsByType<PlayerMotor>(FindObjectsSortMode.None);
            PlayerCount = players.Length;
            PruneStates(players);

            int nowReady = 0;
            bool anyLatched = false;

            foreach (PlayerMotor motor in players)
            {
                if (motor == null)
                {
                    continue;
                }

                ReadyState state = StateFor((int)motor.netId);
                PlayerInteract interact = motor.GetComponent<PlayerInteract>();
                bool holding = FlatDistance(motor.transform.position) <= radius
                                && interact != null
                                && interact.InteractHeldServer;

                if (holding)
                {
                    state.Held += Time.deltaTime;
                    state.Released = 0f;

                    if (!state.Latched && state.Held >= holdSeconds)
                    {
                        state.Latched = true;
                        int count = CountLatched();
                        Debug.Log($"[S4C] S4C_CONTINUE_READY leg={account.LegIndex} netId={motor.netId} " +
                                  $"host={(motor.isLocalPlayer ? 1 : 0)} ready=1 count={count}/{PlayerCount}");

                        if (firstReadyAt <= 0f)
                        {
                            firstReadyAt = Time.unscaledTime;
                        }
                    }
                }
                else
                {
                    state.Held = 0f;

                    if (state.Latched)
                    {
                        state.Released += Time.deltaTime;

                        if (state.Released >= releaseGraceSeconds)
                        {
                            state.Latched = false;
                            state.Released = 0f;
                            Debug.Log($"[S4C] S4C_CONTINUE_CLEAR leg={account.LegIndex} netId={motor.netId} " +
                                      $"count={CountLatched()}/{PlayerCount}");
                        }
                    }
                }

                if (state.Latched)
                {
                    nowReady++;
                    anyLatched = true;
                }
            }

            ReadyCount = nowReady;
            account.ServerSetContinueProgress(nowReady, PlayerCount);

            if (!anyLatched)
            {
                firstReadyAt = 0f;
            }

            if (PlayerCount == 0)
            {
                return;
            }

            if (nowReady >= PlayerCount)
            {
                Debug.Log($"[S4C] S4C_CONTINUE_ADVANCE leg={account.LegIndex} count={nowReady}/{PlayerCount} " +
                          $"from=Safehouse to={route.NightSceneName}");
                ResetReady();
                route.ServerAdvance(account);
                return;
            }

            // 有人在等、但没齐：每 1s 报一次，日志才有「等了多久」的证据
            if (anyLatched && Time.unscaledTime - lastWaitAt > 1f)
            {
                lastWaitAt = Time.unscaledTime;
                float seconds = firstReadyAt > 0f ? Time.unscaledTime - firstReadyAt : 0f;
                Debug.Log($"[S4C] S4C_CONTINUE_WAIT leg={account.LegIndex} reason=not_all_ready " +
                          $"count={nowReady}/{PlayerCount} seconds={seconds:F1}");
            }
        }

        /// <summary>离开安全屋：确认状态作废，复制进度归零（HUD 据此隐藏）。</summary>
        private void LeaveSafehouseState()
        {
            bool had = readyStates.Count > 0 || ReadyCount != 0 || PlayerCount != 0;
            ResetReady();
            ReadyCount = 0;
            PlayerCount = 0;

            TeamAccount account = TeamAccount.Instance;
            if (had && account != null)
            {
                account.ServerSetContinueProgress(0, 0);
            }
        }

        private void ResetReady()
        {
            readyStates.Clear();
            firstReadyAt = 0f;
            lastWaitAt = 0f;
        }

        private ReadyState StateFor(int netId)
        {
            if (!readyStates.TryGetValue(netId, out ReadyState state))
            {
                state = new ReadyState();
                readyStates[netId] = state;
            }

            return state;
        }

        private int CountLatched()
        {
            int count = 0;
            foreach (ReadyState state in readyStates.Values)
            {
                if (state.Latched)
                {
                    count++;
                }
            }

            return count;
        }

        private void PruneStates(PlayerMotor[] players)
        {
            if (readyStates.Count == 0)
            {
                return;
            }

            HashSet<int> alive = new();
            foreach (PlayerMotor motor in players)
            {
                if (motor != null)
                {
                    alive.Add((int)motor.netId);
                }
            }

            List<int> stale = null;
            foreach (int netId in readyStates.Keys)
            {
                if (!alive.Contains(netId))
                {
                    stale ??= new();
                    stale.Add(netId);
                }
            }

            if (stale == null)
            {
                return;
            }

            foreach (int netId in stale)
            {
                readyStates.Remove(netId);
            }
        }

        private float FlatDistance(Vector3 position)
        {
            Vector3 a = position;
            Vector3 b = transform.position;
            a.y = 0f;
            b.y = 0f;
            return Vector3.Distance(a, b);
        }
    }
}
