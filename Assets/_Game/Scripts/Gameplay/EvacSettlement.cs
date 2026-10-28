using Mirror;
using UnityEngine;

namespace ScavengeNightmare.Gameplay
{
    /// <summary>
    /// 撤离结算（D13）：玩家把物件带进撤离区即计入结算。
    ///
    /// 核心约束：**结算自己不持有任何物件数值**。价值一律读 <see cref="GrabbableProp.CurrentValue"/>
    /// （易碎件碎后为 0），类型读 Profile.propType。改数据表里的 value 立刻反映到结算结果上，
    /// 不需要动这边的代码——这就是「改表即生效」的验收点。
    ///
    /// 挂在带 NetworkIdentity 的场景物体上，用 SyncVar 把总额同步给所有客户端。
    /// </summary>
    public sealed class EvacSettlement : NetworkBehaviour
    {
        public static EvacSettlement Instance { get; private set; }

        [SyncVar] private int totalValue;
        [SyncVar] private int deliveredCount;

        public int TotalValue => totalValue;
        public int DeliveredCount => deliveredCount;

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

        /// <summary>服务端调用：把一件货计入结算并回收。</summary>
        [Server]
        public void Deliver(GrabbableProp prop)
        {
            if (prop == null)
            {
                return;
            }

            // 先让持有者脱手，再销毁，否则 GrabController 会继续追一个已销毁的刚体
            GrabController holder = prop.Holder;
            if (holder != null)
            {
                holder.Release(false);
            }

            int value = prop.CurrentValue;
            string id = prop.Profile != null ? prop.Profile.id : "unknown";
            string type = prop.Profile != null ? prop.Profile.propType.ToString() : "?";

            totalValue += value;
            deliveredCount++;

            Debug.Log($"[D13] D13_DELIVER id={id} type={type} value={value} total={totalValue} count={deliveredCount}");

            NetworkServer.Destroy(prop.gameObject);
        }

        /// <summary>结算摘要，供 HUD 与自动化读取。</summary>
        public string Summary() => $"已交付 {deliveredCount} 件，总价值 {totalValue}";

        [Server]
        public void LogSettlement()
        {
            Debug.Log($"[D13] D13_SETTLE count={deliveredCount} total={totalValue}");
        }

        [Server]
        public void ResetForTest()
        {
            totalValue = 0;
            deliveredCount = 0;
            Debug.Log("[D13] D13_SETTLE_RESET");
        }
    }
}
