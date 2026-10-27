using Mirror;
using ScavengeNightmare.Networking;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ScavengeNightmare.Gameplay
{
    /// <summary>
    /// D4 抓取控制器：准星指向高亮 → 按住抓取（速度控制，**不 SetParent**）→ 松手投掷（继承玩家速度）。
    ///
    /// 三个设计约束（对应 D1 冻结的「手感三原则」）：
    /// 1. 物件全程保持物理模拟与碰撞，靠速度控制跟随，不做刚性挂接，所以会撞墙、会被卡住、会掉；
    /// 2. 抓握加速度与最高速度来自数据表，重物更「黏不住」；
    /// 3. 出手速度 = 投掷冲量 / 质量，越重扔得越慢。
    ///
    /// D4 范围内不做：多人所有权（谁能抓同一件）、共持、摩擦事件（D6）。
    /// </summary>
    public sealed class GrabController : MonoBehaviour
    {
        [Header("引用")]
        [SerializeField] private Camera viewCamera;
        [SerializeField] private PlayerMotor motor;

        [Header("拾取")]
        [SerializeField] private float grabRange = 3.2f;
        [SerializeField] private LayerMask grabMask = ~0;

        [Header("跟随")]
        [Tooltip("目标点跟随的刚度（1/s），越大越紧跟准星")]
        [SerializeField] private float followStiffness = 16f;

        private GrabbableProp focused;
        private GrabbableProp held;
        private NetworkIdentity identity;
        private bool inputEnabled = true;

        public GrabbableProp Held => held;
        public GrabbableProp Focused => focused;
        public bool HasFocus => focused != null;

        /// <summary>只有本地玩家能抓：远端玩家的复制体不读键鼠。</summary>
        public bool IsLocalControl => identity == null || identity.isLocalPlayer;

        private void Awake()
        {
            identity = GetComponent<NetworkIdentity>();

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
            if (held != null)
            {
                Release(false);
            }

            SetFocus(null);
        }

        private void Update()
        {
            if (viewCamera == null || !inputEnabled || !IsLocalControl)
            {
                return;
            }

            if (held != null)
            {
                Mouse mouse = Mouse.current;
                if (mouse == null || !mouse.leftButton.isPressed)
                {
                    Release(true);
                }

                return;
            }

            SetFocus(Probe());

            Mouse m = Mouse.current;
            if (m != null && m.leftButton.wasPressedThisFrame && focused != null)
            {
                TryGrab(focused);
            }
        }

        private void FixedUpdate()
        {
            if (held == null)
            {
                return;
            }

            Rigidbody body = held.Body;
            PropPhysicsProfile profile = held.Profile;
            if (body == null || profile == null)
            {
                Release(false);
                return;
            }

            Vector3 target = viewCamera.transform.position + viewCamera.transform.forward * FeelSettings.HoldDistance(profile.holdDistance);
            Vector3 delta = target - body.worldCenterOfMass;

            if (delta.magnitude > profile.breakDistance)
            {
                // 被门框 / 墙 / 队友卡住：直接脱手，而不是硬拽穿模。
                Release(false);
                return;
            }

            float maxSpeed = FeelSettings.GrabMaxSpeed(profile.grabMaxSpeed);
            Vector3 desiredVelocity = Vector3.ClampMagnitude(delta * FeelSettings.FollowStiffness(followStiffness), maxSpeed);
            body.linearVelocity = Vector3.MoveTowards(
                body.linearVelocity,
                desiredVelocity,
                FeelSettings.GrabAcceleration(profile.grabAcceleration) * Time.fixedDeltaTime);

            body.angularVelocity = Vector3.MoveTowards(body.angularVelocity, Vector3.zero, 18f * Time.fixedDeltaTime);

            // 兜底：任何情况下都不让被抓物件飞出物理合理区间。
            held.ClampSpeed(maxSpeed * 1.5f);
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

        public bool TryGrab(GrabbableProp target)
        {
            if (held != null || target == null)
            {
                return false;
            }

            held = target;
            held.SetHeld(true);
            held.SetHighlight(false);
            SetFocus(null);

            // 静止的刚体会进入 sleep，直接改速度不生效，必须先唤醒，否则「抓不动」。
            if (held.Body != null)
            {
                held.Body.WakeUp();
            }

            if (motor != null && held.Profile != null)
            {
                motor.SetCarryMass(held.Mass);
            }

            return true;
        }

        public void Release(bool asThrow)
        {
            GrabbableProp prop = held;
            held = null;

            if (motor != null)
            {
                motor.SetCarryMass(0f);
            }

            if (prop == null)
            {
                return;
            }

            prop.SetHeld(false);

            Rigidbody body = prop.Body;
            PropPhysicsProfile profile = prop.Profile;
            if (body == null || profile == null)
            {
                return;
            }

            if (!asThrow)
            {
                return;
            }

            Vector3 playerVelocity = motor != null ? motor.CurrentVelocity : Vector3.zero;
            Vector3 velocity = playerVelocity + viewCamera.transform.forward * FeelSettings.ThrowSpeed(profile);

            // 玩家自身速度 + 出手速度的合成值也要设上限，避免贴脸扔出穿模。
            float limit = profile.maxThrowSpeed + playerVelocity.magnitude;
            body.WakeUp();
            body.linearVelocity = Vector3.ClampMagnitude(velocity, limit);
            body.angularVelocity = Random.onUnitSphere * FeelSettings.ThrowSpin(profile.throwSpin);
        }

        /// <summary>自动化验证用：关掉键鼠输入，改由测试脚本直接调用 TryGrab / Release。</summary>
        public void SetInputEnabled(bool value)
        {
            inputEnabled = value;
        }
    }
}
