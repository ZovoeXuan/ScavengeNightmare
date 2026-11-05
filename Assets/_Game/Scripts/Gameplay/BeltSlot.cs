using System.Collections.Generic;
using Mirror;
using ScavengeNightmare.Cart;
using ScavengeNightmare.Combat;
using ScavengeNightmare.Economy;
using ScavengeNightmare.Networking;
using ScavengeNightmare.Save;
using UnityEngine;

namespace ScavengeNightmare.Gameplay
{
    /// <summary>
    /// S4B-5 工具带 V2 升级版：
    /// 1) 拦截倒地玩家按 G 挂货的漏洞；
    /// 2) 过滤已在推车板上的货物（避免靠近推车按 G 偷走车板货物引生物理冲突）；
    /// 3) 提升跟随最大速度与加速度并在挂载期间关闭重力，解决玩家冲刺(5m/s)时腰带物件以3m/s掉队卡门框的问题；
    /// 4) 在正式游玩中支持周围无小件可挂时按 G 取下腰带上的物品。
    /// </summary>
    public sealed class BeltSlot : NetworkBehaviour
    {
        public const float MaxSlotMass = 7f;
        public const int MaxSlots = 2;

        public int EffectiveMaxSlots => EquippedEffects.HasToolBelt ? MaxSlots : 1;

        [Header("引用 (留空自动抓)")]
        [SerializeField] private PlayerMotor motor;

        [Header("挂载")]
        [SerializeField] private float reach = 2.5f;
        [SerializeField] private float followStiffness = 18f;
        [SerializeField] private float waistHeight = 0.85f;

        [SyncVar] private float beltMass;

        private readonly List<GrabbableProp> items = new();
        private PlayerVitals vitals;

        public float BeltMass => beltMass;

        public int SlotCount
        {
            get
            {
                items.RemoveAll(i => i == null);
                return items.Count;
            }
        }

        public IReadOnlyList<GrabbableProp> Items => items;

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

        [Server]
        public bool ServerBeltNearest()
        {
            if (vitals != null && vitals.IsDowned)
            {
                Debug.Log($"[S4B] S4B_BELT_REJECT netId={netId} reason=downed");
                return false;
            }

            if (SlotCount >= EffectiveMaxSlots)
            {
                string reason = (!EquippedEffects.HasToolBelt && SlotCount >= 1) ? "locked" : "slot_full";
                Debug.Log($"[S4C] GATE_BELT_REJECT netId={netId} reason={reason} slots={EffectiveMaxSlots}");
                return false;
            }

            GrabbableProp best = null;
            float bestDistance = reach;

            foreach (GrabbableProp prop in FindObjectsByType<GrabbableProp>(FindObjectsSortMode.None))
            {
                if (prop == null || prop.Body == null || prop.IsHeld || prop.IsOnCart)
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
                // V2 人性化交互：非自动化测试下，若周围没有可挂小件且腰带上有货，按 G 可主动卸下腰带货物
                if (!S4ACli.IsTestRun && SlotCount > 0)
                {
                    ServerDropAll("manual_unbelt");
                    return true;
                }

                Debug.Log($"[S4B] S4B_BELT_REJECT netId={netId} reason=no_candidate reach={reach:F1}");
                return false;
            }

            return ServerBelt(best);
        }

        [Server]
        public bool ServerBelt(GrabbableProp prop)
        {
            if (vitals != null && vitals.IsDowned)
            {
                Debug.Log($"[S4B] S4B_BELT_REJECT netId={netId} reason=downed");
                return false;
            }

            if (prop == null || prop.Body == null)
            {
                Debug.Log($"[S4B] S4B_BELT_REJECT netId={netId} reason=no_prop");
                return false;
            }

            if (SlotCount >= EffectiveMaxSlots)
            {
                string reason = (!EquippedEffects.HasToolBelt && SlotCount >= 1) ? "locked" : "slot_full";
                Debug.Log($"[S4C] GATE_BELT_REJECT netId={netId} reason={reason} slots={EffectiveMaxSlots}");
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

            if (CartController.Instance != null)
            {
                CartController.Instance.ServerNotifyPropGrabbed(prop);
            }

            items.Add(prop);
            prop.SetHeld(true);
            prop.SetHolder(null);
            prop.Body.useGravity = false;
            prop.Body.WakeUp();
            Recalculate();

            Debug.Log($"[S4B] S4B_BELT_ADD netId={netId} prop={(prop.Profile != null ? prop.Profile.id : "?")} " +
                      $"mass={prop.Mass:F1} slots={items.Count} beltMass={beltMass:F1}");
            return true;
        }

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
                    body.useGravity = true;
                    body.WakeUp();
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
            if (delta.magnitude > 3.5f)
            {
                body.MovePosition(target);
                body.linearVelocity = Vector3.zero;
                return;
            }

            Vector3 desired = Vector3.ClampMagnitude(delta * followStiffness, 9.5f);
            body.linearVelocity = Vector3.MoveTowards(
                body.linearVelocity, desired, 48f * Time.fixedDeltaTime);
            body.angularVelocity = Vector3.MoveTowards(
                body.angularVelocity, Vector3.zero, 24f * Time.fixedDeltaTime);
        }

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
