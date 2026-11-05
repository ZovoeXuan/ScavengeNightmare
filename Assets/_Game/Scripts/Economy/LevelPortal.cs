using System.Collections.Generic;
using Mirror;
using ScavengeNightmare.Gameplay;
using ScavengeNightmare.Networking;
using UnityEngine;

namespace ScavengeNightmare.Economy
{
    /// <summary>
    /// 关卡传送门 V2 升级版：
    /// 修复倒地玩家 (VitalsDowned) 依然能触发夜间撤离读条或阻碍安全屋就绪人数判定的 Bug (ECO-04)。
    /// </summary>
    public sealed class LevelPortal : MonoBehaviour
    {
        public static LevelPortal Instance { get; private set; }

        [SerializeField] private float radius = 3f;
        [SerializeField] private float holdSeconds = 1.2f;
        [SerializeField] private float releaseGraceSeconds = 2f;

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

        private void UpdateNight(LevelRouteService route)
        {
            PlayerMotor[] players = FindObjectsByType<PlayerMotor>(FindObjectsSortMode.None);
            bool holding = false;

            foreach (PlayerMotor motor in players)
            {
                if (motor == null || motor.VitalsDowned)
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

            if (!account.QuotaReached && Time.unscaledTime - lastRejectAt > 1f)
            {
                lastRejectAt = Time.unscaledTime;
                Debug.Log($"[S4A] GATE_PORTAL_REJECT reason=quota_not_reached leg={account.LegIndex} progress={account.LegProgress} quota={account.LegQuota} missing={Mathf.Max(0, account.LegQuota - account.LegProgress)}");
                return;
            }

            route.ServerAdvance(account);
        }

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
                bool holding = !motor.VitalsDowned
                               && FlatDistance(motor.transform.position) <= radius
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
                        PlaytestRecorder.Instance?.NoteContinueVote(account.LegIndex, motor.netId, count, PlayerCount);

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

            if (anyLatched && Time.unscaledTime - lastWaitAt > 1f)
            {
                lastWaitAt = Time.unscaledTime;
                float seconds = firstReadyAt > 0f ? Time.unscaledTime - firstReadyAt : 0f;
                Debug.Log($"[S4C] S4C_CONTINUE_WAIT leg={account.LegIndex} reason=not_all_ready " +
                          $"count={nowReady}/{PlayerCount} seconds={seconds:F1}");
            }
        }

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
