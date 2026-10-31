using System.Collections.Generic;
using Mirror;
using ScavengeNightmare.Combat;
using ScavengeNightmare.Save;
using ScavengeNightmare.Networking;
using UnityEngine;

namespace ScavengeNightmare.Gameplay
{
    /// <summary>
    /// S4B-5 工具带：双手被 42kg 大件占住时，还能把 ≤7kg 的小件挂到腰上。
    ///
    /// 三条硬规则（P0 §2.5「工具带」）：
    /// 1) 只能挂 ≤7kg 的小件，大件一律拒绝（否则它就是「无限背包」）；
    /// 2) 腰带上的重量**照常计入负重**——挂上去不能比拿在手上更省体力；
    /// 3) 倒地时全部掉落（与手持物同一条生存规则，不做「倒地还挂着一把刀」）。
    ///
    /// 权威边界：挂/取/掉全部服务端裁决。客户端只发「按了 G」，服务端自己找最近的小件。
    /// 不自带相机射线：手中已经有 42kg 时不会有准星聚焦，按「离我最近的合规小件」更符合意图。
    /// </summary>
    public sealed class BeltSlot : NetworkBehaviour
    {
        /// <summary>单件上限（kg）。</summary>
        public const float MaxSlotMass = 7f;

        /// <summary>槽位数。</summary>
        public const int MaxSlots = 2;

        [Header("引用（留空自动抓）")]
        [SerializeField] private PlayerMotor motor;

        [Header("挂载")]
        [SerializeField] private float reach = 2.5f;
        [SerializeField] private float followStiffness = 18f;
        [SerializeField] private float waistHeight = 0.85f;

        [SyncVar] private float beltMass;

        private readonly List<GrabbableProp> items = new();
        private PlayerVitals vitals;

        /// <summary>腰带上的总重量（SyncVar，客户端与移动端都读它）。</summary>
        public float BeltMass => beltMass;

        /// <summary>已挂件数（服务端权威；自动化断言用）。</summary>
        public int SlotCount
        {
            get
            {
                items.RemoveAll(i => i == null);
                return items.Count;
            }
        }

        private void Awake()
        {
            if (motor == null)
            {
                motor = GetComponent<PlayerMotor>();
            }

            vitals = GetComponent<PlayerVitals>();
        }

        private void Update()
        {
            if (!isLocalPlayer)
            {
                return;
            }

            if (!S4BInput.BeltPressed)
            {
                return;
            }

            if (S4BInput.AutoBelt)
            {
                S4BInput.AutoBelt = false;
            }

            CmdBeltNearest();
        }

        private void FixedUpdate()
        {
            if (!isServer)
            {
                return;
            }

            items.RemoveAll(i => i == null);

            for (int i = 0; i < items.Count; i++)
            {
                ServerHoldStep(items[i], i);
            }
        }

        [Command]
        private void CmdBeltNearest()
        {
            ServerBeltNearest();
        }

        /// <summary>服务端：把离自己最近的合规小件挂上腰带。返回是否成功。</summary>
        [Server]
        public bool ServerBeltNearest()
        {
            if (SlotCount >= MaxSlots)
            {
                Debug.Log($"[S4B] S4B_BELT_REJECT netId={netId} reason=slot_full slots={MaxSlots}");
                return false;
            }

            GrabbableProp best = null;
            float bestDistance = reach;

            foreach (GrabbableProp prop in FindObjectsByType<GrabbableProp>(FindObjectsSortMode.None))
            {
                if (prop == null || prop.Body == null || prop.IsHeld)
                {
                    continue;
                }

                float distance = Vector3.Distance(transform.position, prop.transform.position);
                bool small = prop.Mass <= MaxSlotMass;

                if (!small || distance > bestDistance)
                {
                    continue;
                }

                bestDistance = distance;
                best = prop;
            }

            if (best == null)
            {
                Debug.Log($"[S4B] S4B_BELT_REJECT netId={netId} reason=no_candidate reach={reach:F1}");
                return false;
            }

            return ServerBelt(best);
        }

        /// <summary>服务端：显式把某一件挂上腰带。</summary>
        [Server]
        public bool ServerBelt(GrabbableProp prop)
        {
            if (prop == null || prop.Body == null)
            {
                Debug.Log($"[S4B] S4B_BELT_REJECT netId={netId} reason=no_prop");
                return false;
            }

            if (SlotCount >= MaxSlots)
            {
                Debug.Log($"[S4B] S4B_BELT_REJECT netId={netId} reason=slot_full slots={MaxSlots}");
                return false;
            }

            if (prop.Mass > MaxSlotMass)
            {
                Debug.Log($"[S4B] S4B_BELT_REJECT netId={netId} reason=too_heavy mass={prop.Mass:F1} limit={MaxSlotMass:F0}");
                return false;
            }

            if (prop.IsHeld)
            {
                Debug.Log($"[S4B] S4B_BELT_REJECT netId={netId} reason=already_held");
                return false;
            }

            if (Vector3.Distance(transform.position, prop.transform.position) > reach + 0.5f)
            {
                Debug.Log($"[S4B] S4B_BELT_REJECT netId={netId} reason=out_of_reach");
                return false;
            }

            items.Add(prop);
            prop.SetHeld(true);
            prop.SetHolder(null);
            prop.Body.WakeUp();
            Recalculate();

            Debug.Log($"[S4B] S4B_BELT_ADD netId={netId} prop={(prop.Profile != null ? prop.Profile.id : "?")} " +
                      $"mass={prop.Mass:F1} slots={items.Count} beltMass={beltMass:F1}");
            return true;
        }

        /// <summary>服务端：倒地/交给别人时全部掉落。</summary>
        [Server]
        public int ServerDropAll(string reason)
        {
            items.RemoveAll(i => i == null);

            int dropped = items.Count;

            foreach (GrabbableProp prop in items)
            {
                if (prop == null)
                {
                    continue;
                }

                prop.SetHeld(false);
                prop.SetHolder(null);

                Rigidbody body = prop.Body;
                if (body != null)
                {
                    body.isKinematic = false;
                    body.WakeUp();
                    // 轻轻往外掉：不设为 0，避免掉在脚下被 CharacterController 顶飞。
                    body.linearVelocity = transform.right * 0.35f + Vector3.up * 0.05f;
                    body.angularVelocity = Random.onUnitSphere * 0.5f;
                }
            }

            items.Clear();
            Recalculate();
            Debug.Log($"[S4B] S4B_BELT_DROP netId={netId} reason={reason} dropped={dropped} beltMass={beltMass:F1}");
            return dropped;
        }

        private void Recalculate()
        {
            float total = 0f;
            foreach (GrabbableProp prop in items)
            {
                if (prop != null)
                {
                    total += prop.Mass;
                }
            }

            beltMass = total;
        }

        private void ServerHoldStep(GrabbableProp prop, int index)
        {
            Rigidbody body = prop != null ? prop.Body : null;
            if (body == null)
            {
                return;
            }

            float side = (index == 0 ? -1f : 1f) * 0.16f;
            Vector3 target = transform.position
                             + Vector3.up * waistHeight
                             - transform.forward * 0.12f
                             + transform.right * side;

            Vector3 delta = target - body.worldCenterOfMass;
            Vector3 desired = Vector3.ClampMagnitude(delta * followStiffness, 3f);
            body.linearVelocity = Vector3.MoveTowards(
                body.linearVelocity, desired, 24f * Time.fixedDeltaTime);
            body.angularVelocity = Vector3.MoveTowards(
                body.angularVelocity, Vector3.zero, 18f * Time.fixedDeltaTime);
        }

        /// <summary>供 PlayerVitals 在服务端倒地时调用（同一进程内是服务端）。</summary>
        public void ServerDropOnDowned()
        {
            if (!isServer)
            {
                return;
            }

            ServerDropAll("downed");
        }
    }
}
