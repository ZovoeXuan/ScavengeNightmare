using UnityEngine;
using UnityEngine.InputSystem;

namespace ScavengeNightmare.Gameplay
{
    /// <summary>
    /// D6 夹手：一扇可开合的门。闭合过程中如果物件（或持有物）在门缝里，
    /// 强制脱手并把物件沿门面法线推出，保证不穿模、不消失。
    ///
    /// 开合由 Toggle() 驱动（D6 用 E 键或测试脚本调用，D12 再决定交互方式）。
    /// </summary>
    public sealed class DoorTrap : MonoBehaviour
    {
        public enum DoorState { Closed, Opening, Open, Closing }

        [Header("门扇")]
        [SerializeField] private Transform leaf;
        [SerializeField] private BoxCollider leafCollider;

        [Header("开合")]
        [SerializeField] private float openAngle = 95f;
        [SerializeField] private float degreesPerSecond = 140f;
        [SerializeField] private Vector3 hingeAxis = Vector3.up;

        [SerializeField] private float clampCooldown = 0.5f;

        private DoorState state = DoorState.Closed;
        private float angle;
        private Quaternion closedRotation;
        private float lastClampAt = -999f;

        public DoorState State => state;
        public bool IsClosing => state == DoorState.Closing;

        /// <summary>运行时装配（自动化测试动态搭门时用）。</summary>
        public void Configure(Transform leafTransform, BoxCollider collider)
        {
            leaf = leafTransform;
            leafCollider = collider;
            if (leaf != null)
            {
                closedRotation = leaf.localRotation;
            }
        }

        private void Awake()
        {
            if (leafCollider == null)
            {
                leafCollider = GetComponentInChildren<BoxCollider>();
            }

            if (leaf == null && leafCollider != null)
            {
                leaf = leafCollider.transform;
            }

            if (leaf != null)
            {
                closedRotation = leaf.localRotation;
            }
        }

        public void Toggle()
        {
            if (state == DoorState.Closed || state == DoorState.Closing)
            {
                state = DoorState.Opening;
            }
            else
            {
                state = DoorState.Closing;
            }
        }

        public void Open()
        {
            state = DoorState.Opening;
        }

        public void Close()
        {
            state = DoorState.Closing;
        }

        private void Update()
        {
            if (!FrictionEvents.Tuning.enableDoorTrap || leaf == null)
            {
                return;
            }

            HandleInteract();

            float target = (state == DoorState.Open || state == DoorState.Opening) ? openAngle : 0f;
            if (state == DoorState.Opening || state == DoorState.Closing)
            {
                angle = Mathf.MoveTowards(angle, target, degreesPerSecond * Time.deltaTime);
                leaf.localRotation = closedRotation * Quaternion.AngleAxis(angle, hingeAxis);

                if (state == DoorState.Closing)
                {
                    DetectClamp();
                }

                if (Mathf.Approximately(angle, target))
                {
                    state = target > 0f ? DoorState.Open : DoorState.Closed;
                }
            }
        }

        /// <summary>D6 临时交互：玩家走到门 3m 内按 E 开合。D12 再换成正式交互提示。</summary>
        private void HandleInteract()
        {
            if (Application.isBatchMode)
            {
                return;
            }

            Keyboard kb = Keyboard.current;
            if (kb == null || !kb.eKey.wasPressedThisFrame)
            {
                return;
            }

            Collider[] hits = Physics.OverlapSphere(transform.position + Vector3.up, 3f);
            foreach (Collider hit in hits)
            {
                if (hit.GetComponentInParent<CharacterController>() != null)
                {
                    Toggle();
                    return;
                }
            }
        }

        private void DetectClamp()        {
            if (leafCollider == null || Time.time - lastClampAt < clampCooldown)
            {
                return;
            }

            Bounds bounds = leafCollider.bounds;
            Collider[] hits = Physics.OverlapBox(bounds.center, bounds.extents + Vector3.one * 0.03f, leaf.rotation);

            foreach (Collider hit in hits)
            {
                if (hit.attachedRigidbody == null)
                {
                    continue;
                }

                GrabbableProp prop = hit.attachedRigidbody.GetComponent<GrabbableProp>();
                if (prop == null)
                {
                    continue;
                }

                ClampProp(prop);
                lastClampAt = Time.time;
                break;
            }
        }

        private void ClampProp(GrabbableProp prop)
        {
            if (prop.Holder != null)
            {
                prop.Holder.Release(false);
            }

            if (prop.Body == null || leaf == null)
            {
                return;
            }

            // 沿门面法线推出，而不是沿玩家朝向，否则可能把物件推进门里
            Vector3 normal = leaf.forward;
            Vector3 center = leafCollider.bounds.center;
            Vector3 diff = prop.Body.worldCenterOfMass - center;
            float sign = Vector3.Dot(diff, normal) >= 0f ? 1f : -1f;

            float radius = 0.2f;
            Collider propCollider = prop.GetComponent<Collider>();
            if (propCollider != null)
            {
                radius = propCollider.bounds.extents.magnitude;
            }

            float push = radius + FrictionEvents.Tuning.doorPushMargin;
            prop.Body.position += normal * sign * push;
            prop.Body.WakeUp();

            FrictionEvents.Report("夹手", $"{prop.name} 被门夹住，强制脱手", 300f);
        }
    }
}
