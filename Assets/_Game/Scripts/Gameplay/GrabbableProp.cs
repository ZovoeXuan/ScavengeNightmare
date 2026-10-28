using ScavengeNightmare.Networking;
using UnityEngine;

namespace ScavengeNightmare.Gameplay
{
    /// <summary>
    /// D4 可抓取物件：把物理档应用到 Rigidbody，并负责准星高亮。
    /// 物件始终保持完整物理模拟（有碰撞、会被撞飞），不做「磁吸」。
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public sealed class GrabbableProp : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        [Header("数据")]
        [SerializeField] private PropPhysicsProfile profile;

        [Header("引用（留空自动抓子物体）")]
        [SerializeField] private Renderer[] renderers;

        private Rigidbody body;
        private NetworkedProp networked;
        private MaterialPropertyBlock block;
        private Color[] baseColors;
        private PhysicsMaterial runtimeSurface;
        private bool highlighted;
        private bool held;

        public PropPhysicsProfile Profile => profile;
        public Rigidbody Body => body;
        public float Mass => body != null ? body.mass : 0f;
        public bool IsHeld => held;

        /// <summary>当前持有者（谁正抓着它）。门夹等事件需要能反向让持有者脱手。</summary>
        public GrabController Holder { get; private set; }

        public void SetHolder(GrabController value)
        {
            Holder = value;
        }

        /// <summary>
        /// D6 手滑砸脚：被足够快、足够重的物件撞到玩家时触发硬直。
        /// 判定放在物件侧，因为 CharacterController 自身不会收到刚体碰撞回调。
        /// </summary>
        private void OnCollisionEnter(Collision collision)
        {
            if (!FrictionEvents.Tuning.enableImpactStun || profile == null)
            {
                return;
            }

            float speed = collision.relativeVelocity.magnitude;
            if (speed < FrictionEvents.Tuning.stunSpeedThreshold || profile.mass < FrictionEvents.Tuning.stunMinMass)
            {
                return;
            }

            // 服务端权威：只有服务端判定硬直，再广播给真正要顿住的那个客户端
            if (networked != null && !networked.isServer)
            {
                return;
            }

            PlayerMotor motor = collision.collider.GetComponentInParent<PlayerMotor>();
            if (motor == null)
            {
                return;
            }

            bool heavy = profile.mass >= FrictionEvents.Tuning.stunHeavyMass;
            float seconds = heavy ? FrictionEvents.Tuning.stunHeavyDuration : FrictionEvents.Tuning.stunDuration;
            if (motor.isServer)
            {
                motor.ServerStun(seconds);
            }
            else
            {
                motor.Stun(seconds);
            }

            FrictionEvents.Report("手滑砸脚", $"{profile.displayName} 砸中玩家（{speed:F1} m/s）硬直 {seconds:F1}s", 420f);
        }

        private void Awake()
        {
            body = GetComponent<Rigidbody>();
            networked = GetComponent<NetworkedProp>();

            if (renderers == null || renderers.Length == 0)
            {
                renderers = GetComponentsInChildren<Renderer>(true);
            }

            block = new MaterialPropertyBlock();
            baseColors = new Color[renderers.Length];
            for (int i = 0; i < renderers.Length; i++)
            {
                Material shared = renderers[i].sharedMaterial;
                if (shared != null)
                {
                    baseColors[i] = shared.HasProperty(BaseColorId) ? shared.GetColor(BaseColorId) : shared.color;
                }
            }

            // 高速投掷不穿墙：连续检测 + 插值。
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.interpolation = RigidbodyInterpolation.Interpolate;

            ApplyProfile();
        }

        public void SetProfile(PropPhysicsProfile value)
        {
            profile = value;
            if (body == null)
            {
                body = GetComponent<Rigidbody>();
            }

            ApplyProfile();
        }

        public void ApplyProfile()
        {
            if (profile == null || body == null)
            {
                return;
            }

            // 编辑期不写运行时物理材质（它不是资产，落盘会丢），运行时 Awake 再统一应用。
            if (!Application.isPlaying)
            {
                body.mass = Mathf.Max(0.1f, profile.mass);
                return;
            }

            body.mass = Mathf.Max(0.1f, profile.mass);
            body.linearDamping = profile.linearDamping;
            body.angularDamping = profile.angularDamping;

            // 摩擦/弹性用运行时物理材质承载：数据表里改数值即生效，不需要维护额外资产。
            if (runtimeSurface == null)
            {
                runtimeSurface = new PhysicsMaterial($"{profile.id}_surface");
            }

            runtimeSurface.dynamicFriction = profile.dynamicFriction;
            runtimeSurface.staticFriction = profile.staticFriction;
            runtimeSurface.bounciness = profile.bounciness;

            foreach (Collider c in GetComponentsInChildren<Collider>())
            {
                c.sharedMaterial = runtimeSurface;
            }
        }

        public void SetHeld(bool value)
        {
            held = value;
        }

        public void SetHighlight(bool on)
        {
            if (highlighted == on || renderers == null)
            {
                return;
            }

            highlighted = on;
            Color tint = profile != null ? profile.highlightTint : new Color(0.55f, 0.85f, 1f);

            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer r = renderers[i];
                if (r == null)
                {
                    continue;
                }

                r.GetPropertyBlock(block);
                if (on)
                {
                    block.SetColor(BaseColorId, tint);
                    block.SetColor(ColorId, tint);
                    block.SetColor(EmissionColorId, tint * 0.35f);
                }
                else
                {
                    block.SetColor(BaseColorId, baseColors[i]);
                    block.SetColor(ColorId, baseColors[i]);
                    block.SetColor(EmissionColorId, Color.black);
                }

                r.SetPropertyBlock(block);
            }
        }

        /// <summary>速度上限，作为最后一道穿模保险。</summary>
        public void ClampSpeed(float maxSpeed)
        {
            if (body == null)
            {
                return;
            }

            if (body.linearVelocity.sqrMagnitude > maxSpeed * maxSpeed)
            {
                body.linearVelocity = body.linearVelocity.normalized * maxSpeed;
            }
        }
    }
}
