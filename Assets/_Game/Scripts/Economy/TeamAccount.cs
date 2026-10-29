using System.Collections.Generic;
using Mirror;
using ScavengeNightmare.Gameplay;
using ScavengeNightmare.Save;
using UnityEngine;

namespace ScavengeNightmare.Economy
{
    /// <summary>
    /// 4A 全队公共账本（内存态）。挂在运行时 Spawn + DontDestroyOnLoad 的 GameRoot 上：
    /// 挂场景对象会被 Mirror 在 ServerChangeScene 时 unspawn 掉，跨场景余额一定丢。
    ///
    /// 职责只有一件：把「一件货的当前价值」同时计入永久资金与本关交关进度，且同一货物只能入账一次。
    /// </summary>
    public sealed class TeamAccount : NetworkBehaviour
    {
        public static TeamAccount Instance { get; private set; }

        /// <summary>
        /// Story 2 起为 true，但只在「存档服务已加载」时成立：
        /// 没加载成功的进程必须诚实地报 false，不能让下游以为钱已经在盘上了。
        /// </summary>
        public static bool Persisted
        {
            get
            {
                P0SaveService svc = P0SaveService.Instance;
                return svc != null && svc.Loaded && svc.State != SaveState.Locked && svc.State != SaveState.Failed;
            }
        }

        [SyncVar] private long balance;
        [SyncVar] private int legProgress;
        [SyncVar] private int legQuota = 1800;
        [SyncVar] private int legIndex = 1;
        [SyncVar] private int runId;
        [SyncVar] private bool quotaReached;

        private readonly HashSet<string> committed = new();
        private bool warnedNoSave;

        public long Balance => balance;
        public int LegProgress => legProgress;
        public int LegQuota => legQuota;
        public int LegIndex => legIndex;

        /// <summary>达标即解锁下一关入口。超额不截断（carry_over），交超了不能反而吃亏。</summary>
        public bool QuotaReached => quotaReached;

        /// <summary>服务端统计，供自动化对账。</summary>
        public int AcceptedCount { get; private set; }
        public int UniqueCargoCount => committed.Count;
        public int CommittedTotal { get; private set; }
        public int DupBlocked { get; private set; }
        public string LastCommittedId { get; private set; } = string.Empty;
        public int LastCommittedValue { get; private set; }

        private void Awake()
        {
            // 跨场景常驻的唯一风险：同一进程里出现第二份账本。旧实例被顶掉时余额会静默归零，
            // 而所有断言读的都是新实例、会自洽通过——必须报错，不能只是覆盖。
            if (Instance != null && Instance != this)
            {
                Debug.LogError($"[S4A] S4A_ACCOUNT_DUPLICATE existing_balance={Instance.balance} new_balance={balance}");
                return;
            }

            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        public override void OnStartServer()
        {
            base.OnStartServer();

            P0SaveService save = P0SaveService.Instance;
            bool restored = false;
            int persistedRunId = 0;

            if (save != null && save.Loaded)
            {
                TeamSaveData d = save.Data;
                persistedRunId = d.runId;

                balance = d.balance;
                legProgress = d.legProgress;
                legQuota = d.legQuota > 0 ? d.legQuota : 1800;
                legIndex = d.legIndex > 0 ? d.legIndex : 1;
                quotaReached = d.quotaReached;
                CommittedTotal = (int)d.sumCommitted;
                AcceptedCount = d.txnCount;
                restored = true;

                // 跨进程重启 = 新的一局：netId 会被复用，沿用旧 runId 会让新物件撞上旧 cargoId，
                // 表现是「这货怎么交都进不了账」。所以钱留下来，runId 换新，去重表从空开始。
                // 同进程重连（Host 不重启）不受影响，内存去重表仍然有效。
                runId = NewRunId();
            }

            if (runId == 0)
            {
                runId = NewRunId();
            }

            if (legIndex <= 0)
            {
                legIndex = 1;
            }

            if (legQuota <= 0)
            {
                legQuota = 1800;
            }

            if (restored)
            {
                Debug.Log($"[S4A] S4A_ACCOUNT_RESTORED balance={balance} legProgress={legProgress} legIndex={legIndex} quota={legQuota} txn={AcceptedCount} persistedRunId={persistedRunId} runId={runId}");
            }

            Debug.Log($"[S4A] S4A_ACCOUNT_READY runId={runId} legIndex={legIndex} quota={legQuota} balance={balance} persisted={Persisted}");
        }

        private static int NewRunId()
        {
            return new System.Random(System.Guid.NewGuid().GetHashCode()).Next(1, int.MaxValue);
        }

        public static string CargoId(GrabbableProp prop)
        {
            if (prop == null)
            {
                return string.Empty;
            }

            NetworkIdentity id = prop.GetComponent<NetworkIdentity>();
            uint netId = id != null ? id.netId : 0;
            int run = Instance != null ? Instance.runId : 0;
            int leg = Instance != null ? Instance.legIndex : 0;
            return $"{run}-{leg}-{netId}";
        }

        /// <summary>服务端：提交一件货。返回是否入账成功。</summary>
        [Server]
        public bool Deposit(GrabbableProp prop)
        {
            if (prop == null)
            {
                return false;
            }

            return DepositId(CargoId(prop), prop.CurrentValue, prop);
        }

        /// <summary>
        /// 服务端：按 cargoId 入账。故意与物件销毁解耦 —— 重复提交必须能被「按 id 主动触发」，
        /// 不能只能靠再抓一次物件；否则结算后 Destroy 会让 A6 变成假通过。
        /// </summary>
        [Server]
        public bool DepositId(string cargoId, int value, GrabbableProp prop = null)
        {
            long before = balance;

            if (string.IsNullOrEmpty(cargoId))
            {
                Debug.Log($"[S4A] S4A_DEPOSIT cargoId=- value=0 state=Rejected reason=empty balance={before} legProgress={legProgress} totalAfter={balance}");
                return false;
            }

            Debug.Log($"[S4A] S4A_DEPOSIT cargoId={cargoId} value={value} state=Pending reason=- balance={before} legProgress={legProgress} totalAfter={balance}");

            if (committed.Contains(cargoId))
            {
                DupBlocked++;
                Debug.Log($"[S4A] S4A_DEPOSIT_DUP cargoId={cargoId} balanceBefore={before} balanceAfter={balance}");
                Debug.Log($"[S4A] S4A_DEPOSIT cargoId={cargoId} value={value} state=Rejected reason=duplicate balance={balance} legProgress={legProgress} totalAfter={balance}");
                return false;
            }

            if (value <= 0)
            {
                Debug.Log($"[S4A] S4A_DEPOSIT_ZERO cargoId={cargoId} balance={balance} legProgress={legProgress}");
                Debug.Log($"[S4A] S4A_DEPOSIT cargoId={cargoId} value={value} state=Rejected reason=zero_value balance={balance} legProgress={legProgress} totalAfter={balance}");
                return false;
            }

            long newBalance = balance + value;
            int newProgress = legProgress + value;

            // 达标开门的判据源（QA 的 Q1/Q2）：超额必须照常入账、不截断，
            // carry_over 就是「交超了没有吃亏」的证据。
            bool newReached = quotaReached || newProgress >= legQuota;

            P0SaveService save = P0SaveService.Instance;
            if (save != null)
            {
                if (!save.Loaded)
                {
                    // 存档服务在、但没加载成功（锁被占 / 目录不可写）：必须整笔拒绝。
                    // 这里绝不能退回内存路径——那会在只读盘上显示「入账成功」，是最坏的一种假账。
                    Debug.Log($"[S4A] S4A_DEPOSIT cargoId={cargoId} value={value} state=Rejected reason=save_unavailable save_state={save.State} balance={balance} legProgress={legProgress} totalAfter={balance}");
                    Debug.LogError($"[S4A] S4A_SAVE_REJECT cargoId={cargoId} value={value} reason=save_unavailable state={save.State}");
                    return false;
                }

                TeamSaveData next = save.Data.Clone();
                if (next == null)
                {
                    Debug.LogError($"[S4A] S4A_SAVE_REJECT cargoId={cargoId} value={value} reason=clone_failed");
                    return false;
                }

                next.runId = runId;
                next.legIndex = legIndex;
                next.legQuota = legQuota;
                next.balance = newBalance;
                next.legProgress = newProgress;
                next.quotaReached = newReached;

                SaveTxnRecord record = new()
                {
                    cargoId = cargoId,
                    value = value,
                    legIndex = legIndex,
                    balanceAfter = newBalance,
                    tsUnixMs = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                };

                // 先落盘、再动内存。写失败就整笔拒绝：宁可让玩家重交一次，
                // 也不能出现「HUD 上有钱、盘上没有」的假账。
                if (!save.TryCommit(next, record, out string saveError))
                {
                    Debug.Log($"[S4A] S4A_DEPOSIT cargoId={cargoId} value={value} state=Rejected reason=save_failed detail={saveError} balance={balance} legProgress={legProgress} totalAfter={balance}");
                    Debug.LogError($"[S4A] S4A_SAVE_REJECT cargoId={cargoId} value={value} reason={saveError}");
                    return false;
                }
            }
            else
            {
                WarnSaveAbsentOnce();
            }

            committed.Add(cargoId);
            balance = newBalance;
            legProgress = newProgress;
            quotaReached = newReached;
            CommittedTotal += value;
            AcceptedCount++;
            LastCommittedId = cargoId;
            LastCommittedValue = value;

            Debug.Log($"[S4A] S4A_DEPOSIT cargoId={cargoId} value={value} state=Committed reason=- balance={balance} legProgress={legProgress} totalAfter={balance}");

            Debug.Log($"[S4A] GATE_QUOTA_GATE leg={legIndex} progress={legProgress} quota={legQuota} carry_over={Mathf.Max(0, legProgress - legQuota)} portal={(quotaReached ? "unlocked" : "locked")}");

            if (prop != null)
            {
                GrabController holder = prop.Holder;
                if (holder != null)
                {
                    holder.Release(false);
                }

                NetworkServer.Destroy(prop.gameObject);
            }

            return true;
        }

        /// <summary>
        /// 没有存档服务时（Main / Supermarket 等 D3–D13 基线场景）退回 Story 1 的纯内存行为。
        /// 这条降级路径必须存在，否则 4A 的改动会把回归基线一起改掉；但也必须喊一声，
        /// 不能让人误以为钱已经落盘。
        /// </summary>
        private void WarnSaveAbsentOnce()
        {
            if (warnedNoSave)
            {
                return;
            }

            warnedNoSave = true;
            Debug.LogWarning("[S4A] S4A_SAVE_ABSENT path=memory reason=no_save_service");
        }

        [Server]
        public void SetQuota(int quota)
        {
            P0SaveService save = P0SaveService.Instance;
            if (save != null)
            {
                if (!save.Loaded)
                {
                    Debug.LogError($"[S4A] S4A_SAVE_REJECT phase=set_quota quota={quota} reason=save_unavailable state={save.State}");
                    return;
                }

                TeamSaveData next = save.Data.Clone();
                if (next != null)
                {
                    next.legQuota = quota;
                    next.balance = balance;
                    next.legProgress = legProgress;
                    next.legIndex = legIndex;
                    next.quotaReached = quotaReached;
                    if (!save.TryCommitState(next, "set_quota", out string err))
                    {
                        Debug.LogError($"[S4A] S4A_SAVE_REJECT phase=set_quota quota={quota} reason={err}");
                        return;
                    }
                }
            }

            legQuota = quota;
        }

        [Server]
        public void NextLeg(int quota)
        {
            int nextIndex = legIndex + 1;
            int nextQuota = quota;

            P0SaveService save = P0SaveService.Instance;
            if (save != null)
            {
                if (!save.Loaded)
                {
                    Debug.LogError($"[S4A] S4A_SAVE_REJECT phase=next_leg target={nextIndex} reason=save_unavailable state={save.State}");
                    return;
                }

                TeamSaveData next = save.Data.Clone();
                if (next != null)
                {
                    next.legIndex = nextIndex;
                    next.legProgress = 0;
                    next.legQuota = nextQuota;
                    next.quotaReached = false;
                    next.balance = balance;
                    if (!save.TryCommitState(next, "next_leg", out string err))
                    {
                        Debug.LogError($"[S4A] S4A_SAVE_REJECT phase=next_leg target={nextIndex} reason={err}");
                        return;
                    }
                }
            }

            legIndex = nextIndex;
            legProgress = 0;
            legQuota = nextQuota;
            quotaReached = false;
            Debug.Log($"[S4A] GATE_LEVEL n={legIndex} quota={legQuota} source=table before={balance} after={balance}");
        }
    }
}
