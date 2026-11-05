using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ScavengeNightmare.Gameplay
{
    /// <summary>
    /// D6 夹手 V2 升级版：
    /// 1) 修复旋转门扇使用世界 AABB bounds.extents 传入 OverlapBox 导致检测盒在 45° 时严重膨胀变形的 Bug；
    /// 2) 联机模式下仅在服务端执行物理推挤与强制脱手 (ServerForceRelease)。
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

                if (state == DoorState.Closing && (!NetworkClient.active || NetworkServer.active))
                {
                    DetectClamp();
                }

                if (Mathf.Approximately(angle, target))
                {
                    state = target > 0f ? DoorState.Open : DoorState.Closed;
                }
            }
        }

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

        private void DetectClamp()
        {
            if (leafCollider == null || Time.time - lastClampAt < clampCooldown)
            {
                return;
            }

            Vector3 worldCenter = leafCollider.transform.TransformPoint(leafCollider.center);
            Vector3 lossy = leafCollider.transform.lossyScale;
            Vector3 halfExtents = new Vector3(
                Mathf.Abs(leafCollider.size.x * lossy.x) * 0.5f + 0.03f,
                Mathf.Abs(leafCollider.size.y * lossy.y) * 0.5f + 0.03f,
                Mathf.Abs(leafCollider.size.z * lossy.z) * 0.5f + 0.03f);

            Collider[] hits = Physics.OverlapBox(worldCenter, halfExtents, leafCollider.transform.rotation);

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

                ClampProp(prop, worldCenter);
                lastClampAt = Time.time;
                break;
            }
        }

        private void ClampProp(GrabbableProp prop, Vector3 doorCenter)
        {
            if (prop.Holder != null)
            {
                if (NetworkServer.active)
                {
                    prop.Holder.ServerForceRelease();
                }
                else
                {
                    prop.Holder.Release(false);
                }
            }

            if (prop.Body == null || leaf == null)
            {
                return;
            }

            Vector3 normal = leaf.forward;
            Vector3 diff = prop.Body.worldCenterOfMass - doorCenter;
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
