using System.Collections.Generic;
using Mirror;
using ScavengeNightmare.Gameplay;
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

        /// <summary>Story 1 是纯内存态：没有落盘，重开进程余额归零。Story 2 才会变成 true。</summary>
        public static bool Persisted => false;

        [SyncVar] private long balance;
        [SyncVar] private int legProgress;
        [SyncVar] private int legQuota = 1800;
        [SyncVar] private int legIndex = 1;
        [SyncVar] private int runId;
        [SyncVar] private bool quotaReached;

        private readonly HashSet<string> committed = new();

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

            if (runId == 0)
            {
                runId = (int)(System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() % int.MaxValue);
            }

            if (legIndex <= 0)
            {
                legIndex = 1;
            }

            if (legQuota <= 0)
            {
                legQuota = 1800;
            }

            Debug.Log($"[S4A] S4A_ACCOUNT_READY runId={runId} legIndex={legIndex} quota={legQuota} balance={balance}");
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

            committed.Add(cargoId);
            balance += value;
            legProgress += value;
            CommittedTotal += value;
            AcceptedCount++;
            LastCommittedId = cargoId;
            LastCommittedValue = value;

            Debug.Log($"[S4A] S4A_DEPOSIT cargoId={cargoId} value={value} state=Committed reason=- balance={balance} legProgress={legProgress} totalAfter={balance}");

            // 达标开门的判据源（QA 的 Q1/Q2）：超额必须照常入账、不截断，
            // carry_over 就是「交超了没有吃亏」的证据。
            if (!quotaReached && legProgress >= legQuota)
            {
                quotaReached = true;
            }

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

        [Server]
        public void SetQuota(int quota)
        {
            legQuota = quota;
        }

        [Server]
        public void NextLeg(int quota)
        {
            legIndex++;
            legProgress = 0;
            legQuota = quota;
            quotaReached = false;
            Debug.Log($"[S4A] GATE_LEVEL n={legIndex} quota={legQuota} source=table before={balance} after={balance}");
        }
    }
}
