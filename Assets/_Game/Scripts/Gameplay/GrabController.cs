using Mirror;
using ScavengeNightmare.Networking;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ScavengeNightmare.Gameplay
{
    /// <summary>
    /// D6.5 服务端权威抓取。
    ///
    /// 客户端只做两件事：准星指向谁（本地射线，零延迟高亮）、按下 / 松开（发 Command）。
    /// 真正的「拖着重物走」由服务端在 FixedUpdate 里做速度控制，位置通过 NetworkTransform
    /// （固定 ServerToClient）回传给所有客户端。
    ///
    /// 为什么不再用客户端权威：Mirror 的 NetworkTransform 同步方向不能运行时翻转，
    /// 翻转会重置可靠基线，客户端只能按旧快照慢慢爬（实测同一时刻位置差 8.67m）。
    /// </summary>
    public sealed class GrabController : NetworkBehaviour
    {
        [Header("引用")]
        [SerializeField] private Camera viewCamera;
        [SerializeField] private PlayerMotor motor;

        [Header("拾取（本地射线）")]
        [SerializeField] private float grabRange = 3.2f;
        [SerializeField] private LayerMask grabMask = ~0;

        [Header("跟随（服务端执行）")]
        [SerializeField] private float followStiffness = 16f;
        [Tooltip("持物点相对玩家原点抬高多少（近似相机高度）")]
        [SerializeField] private float holdHeightOffset = 0.45f;

        [SyncVar] private bool isHolding;
        [SyncVar] private float heldMass;

        private GrabbableProp focused;
        private GrabbableProp heldProp;
        private bool inputEnabled = true;

        public bool IsHolding => isHolding;

        /// <summary>UI 用：只有本地玩家才画准星与提示。</summary>
        public bool IsLocalControl => isLocalPlayer;
        public float HeldMass => heldMass;
        public GrabbableProp Focused => focused;
        public bool HasFocus => focused != null;

        private void Awake()
        {
            if (viewCamera == null)
            {
                viewCamera = GetComponentInChildren<Camera>(true);
            }

            if (motor == null)
            {
                motor = GetComponent<PlayerMotor>();
            }
        }

        private void OnDisable()
        {
            if (isServer && isHolding)
            {
                ServerRelease(false);
            }

            SetFocus(null);
        }

        private void Update()
        {
            if (!isLocalPlayer || viewCamera == null || !inputEnabled)
            {
                return;
            }

            if (isHolding)
            {
                Mouse mouse = Mouse.current;
                if (mouse == null || !mouse.leftButton.isPressed)
                {
                    RequestRelease(true);
                }

                return;
            }

            SetFocus(Probe());

            Mouse m = Mouse.current;
            if (m != null && m.leftButton.wasPressedThisFrame && focused != null)
            {
                RequestGrab(focused);
            }
        }

        private void FixedUpdate()
        {
            if (!isServer || !isHolding)
            {
                return;
            }

            ServerHoldStep();
        }

        private GrabbableProp Probe()
        {
            Ray ray = new Ray(viewCamera.transform.position, viewCamera.transform.forward);
            if (!Physics.Raycast(ray, out RaycastHit hit, grabRange, grabMask, QueryTriggerInteraction.Ignore))
            {
                return null;
            }

            return hit.rigidbody != null ? hit.rigidbody.GetComponent<GrabbableProp>() : null;
        }

        private void SetFocus(GrabbableProp next)
        {
            if (focused == next)
            {
                return;
            }

            if (focused != null)
            {
                focused.SetHighlight(false);
            }

            focused = next;

            if (focused != null)
            {
                focused.SetHighlight(true);
            }
        }

        // ---------- 请求入口 ----------

        public void RequestGrab(GrabbableProp target)
        {
            if (target == null || isHolding)
            {
                return;
            }

            if (isServer)
            {
                ServerGrab(target);
                return;
            }

            NetworkIdentity id = target.GetComponent<NetworkIdentity>();
            if (id == null)
            {
                return;
            }

            CmdRequestGrab(id);
        }

        public void RequestRelease(bool asThrow)
        {
            if (!isHolding)
            {
                return;
            }

            if (isServer)
            {
                ServerRelease(asThrow);
            }
            else
            {
                CmdRequestRelease(asThrow);
            }
        }

        /// <summary>兼容 D4 / D6 自动化用例的旧接口。</summary>
        public bool TryGrab(GrabbableProp target)
        {
            RequestGrab(target);
            return isHolding;
        }

        public void Release(bool asThrow)
        {
            RequestRelease(asThrow);
        }

        [Command]
        private void CmdRequestGrab(NetworkIdentity target)
        {
            if (target != null)
            {
                ServerGrab(target.GetComponent<GrabbableProp>());
            }
        }

        [Command]
        private void CmdRequestRelease(bool asThrow)
        {
            ServerRelease(asThrow);
        }

        // ---------- 服务端实现 ----------

        private void ServerGrab(GrabbableProp prop)
        {
            if (isHolding || prop == null || prop.Body == null)
            {
                return;
            }

            // 距离校验：防止客户端隔空抓
            if (Vector3.Distance(transform.position, prop.transform.position) > grabRange + 1f)
            {
                return;
            }

            heldProp = prop;
            heldMass = prop.Mass;
            isHolding = true;
            prop.SetHeld(true);
            // 门夹等外部事件需要能反向让持有者脱手
            prop.SetHolder(this);
            prop.Body.WakeUp();
        }

        private void ServerRelease(bool asThrow)
        {
            GrabbableProp prop = heldProp;
            heldProp = null;
            isHolding = false;
            heldMass = 0f;

            if (prop == null)
            {
                return;
            }

            prop.SetHeld(false);
            prop.SetHolder(null);

            Rigidbody body = prop.Body;
            PropPhysicsProfile profile = prop.Profile;
            if (body == null || profile == null || !asThrow)
            {
                return;
            }

            body.WakeUp();
            body.linearVelocity = AimDirection() * FeelSettings.ThrowSpeed(profile);
            body.angularVelocity = Random.onUnitSphere * FeelSettings.ThrowSpin(profile.throwSpin);
        }

        private void ServerHoldStep()
        {
            if (heldProp == null)
            {
                isHolding = false;
                return;
            }

            Rigidbody body = heldProp.Body;
            PropPhysicsProfile profile = heldProp.Profile;
            if (body == null || profile == null)
            {
                ServerRelease(false);
                return;
            }

            Vector3 target = transform.position
                             + AimDirection() * FeelSettings.HoldDistance(profile.holdDistance)
                             + Vector3.up * holdHeightOffset;
            Vector3 delta = target - body.worldCenterOfMass;

            if (delta.magnitude > profile.breakDistance)
            {
                // 被门框 / 墙卡住：直接脱手，而不是硬拽穿模
                ServerRelease(false);
                return;
            }

            float maxSpeed = FeelSettings.GrabMaxSpeed(profile.grabMaxSpeed);
            Vector3 desired = Vector3.ClampMagnitude(delta * FeelSettings.FollowStiffness(followStiffness), maxSpeed);
            body.linearVelocity = Vector3.MoveTowards(
                body.linearVelocity,
                desired,
                FeelSettings.GrabAcceleration(profile.grabAcceleration) * Time.fixedDeltaTime);
            body.angularVelocity = Vector3.MoveTowards(body.angularVelocity, Vector3.zero, 18f * Time.fixedDeltaTime);
        }

        /// <summary>朝向 + 俯仰合成准星方向；俯仰由 PlayerMotor 同步到服务端。</summary>
        private Vector3 AimDirection()
        {
            float pitch = motor != null ? motor.ViewPitchDeg : 0f;
            return Quaternion.AngleAxis(pitch, transform.right) * transform.forward;
        }

        public void SetInputEnabled(bool value)
        {
            inputEnabled = value;
        }
    }
}
