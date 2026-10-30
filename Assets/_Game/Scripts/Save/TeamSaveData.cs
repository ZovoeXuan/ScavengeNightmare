using System;
using System.Collections.Generic;
using UnityEngine;

namespace ScavengeNightmare.Save
{
    /// <summary>
    /// 一笔已承认的入账事务。存档不存「最后余额」，存「事务流水」：
    /// 余额可以由流水反算，所以 HK-04 的 sumCommitted 是算出来的，不是抄内存的。
    /// </summary>
    [Serializable]
    public sealed class SaveTxnRecord
    {
        public int seq;
        public string cargoId;
        public int value;
        public int legIndex;
        public long balanceAfter;
        public long tsUnixMs;
    }

    /// <summary>
    /// 一笔购买。撤销窗口的判据源——不用「现在几点减购买时间」现算，
    /// 因为跨进程重启后只能用落盘的 ts，现算会让窗口在重开后重新打开。
    /// state 用 int 而不是 enum：JsonUtility 对 enum 的兼容性差，加值容易反序列化失败。
    /// </summary>
    [Serializable]
    public sealed class PurchaseRecord
    {
        public const int Active = 0;
        public const int Revoked = 1;

        public string purchaseId;
        public string itemId;
        public int price;
        public int state = Active;
        public int legIndex;
        public long tsUnixMs;
    }

    /// <summary>
    /// 4A 队伍存档（P0 只存钱包与交关进度；安全屋/商店/路线状态在 Story 4 接入）。
    ///
    /// 刻意不持久化 committedIds：cargoId = runId-legIndex-netId，而 netId 在进程重启后会被复用。
    /// 把旧 id 带回来会把「新局里重新生成的物件」误判成重复提交，钱就再也进不来了。
    /// 跨进程重启 = 新的一局：钱留下，去重表清空。重连（同一 Host 不重启）仍由内存去重表保证。
    ///
    /// version 1 → 2：加 purchases。缺字段的老档读出来是空列表，天然兼容。
    /// </summary>
    [Serializable]
    public sealed class TeamSaveData
    {
        public int version = 2;
        public int runId;
        public int legIndex = 1;
        public int legQuota = 1800;
        public int legProgress;
        public long balance;
        public bool quotaReached;
        public int txnCount;
        public long sumCommitted;
        public List<SaveTxnRecord> txns = new();
        public List<PurchaseRecord> purchases = new();

        public TeamSaveData Clone()
        {
            TeamSaveData copy = JsonUtility.FromJson<TeamSaveData>(JsonUtility.ToJson(this));
            if (copy == null)
            {
                return null;
            }

            // JsonUtility 走 FormatterServices，不跑字段初始化器：List 可能是 null。
            copy.txns ??= new List<SaveTxnRecord>();
            copy.purchases ??= new List<PurchaseRecord>();
            return copy;
        }

        public static TeamSaveData NewEmpty(int runId, int legQuota)
        {
            return new TeamSaveData
            {
                version = 2,
                runId = runId,
                legIndex = 1,
                legQuota = legQuota,
                legProgress = 0,
                balance = 0,
                quotaReached = false,
                txnCount = 0,
                sumCommitted = 0,
                txns = new List<SaveTxnRecord>(),
                purchases = new List<PurchaseRecord>()
            };
        }

        /// <summary>
        /// 从存档自身反算余额。**必须把负向流水算进来**：购买/撤销走的是同一张 txn 表
        /// （value = -price），过滤掉负值会让 balance == SumFromTxns() 这条恒等式在购物后破裂。
        /// </summary>
        public long SumFromTxns()
        {
            if (txns == null)
            {
                return 0;
            }

            long sum = 0;
            foreach (SaveTxnRecord t in txns)
            {
                if (t != null)
                {
                    sum += t.value;
                }
            }

            return sum;
        }

        /// <summary>本次进程内有效的购买笔数（撤销窗口的 hard_cap 判据）。</summary>
        public int ActivePurchaseCount()
        {
            if (purchases == null)
            {
                return 0;
            }

            int n = 0;
            foreach (PurchaseRecord p in purchases)
            {
                if (p != null && p.state == PurchaseRecord.Active)
                {
                    n++;
                }
            }

            return n;
        }
    }
}
