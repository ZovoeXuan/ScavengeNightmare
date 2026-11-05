using ScavengeNightmare.Audio;
using ScavengeNightmare.Curses;
using ScavengeNightmare.Networking;
using UnityEngine;

namespace ScavengeNightmare.Gameplay
{
    /// <summary>
    /// D4 可抓取物件 V2 升级版：
    /// 1) 新增 IsOnCart 状态与双人协力抬重物 (AssistHolder) 支持；
    /// 2) 修复运行时创建的 PhysicsMaterial 在销毁时未释放的内存泄漏。
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
        private bool onCart;

        public PropPhysicsProfile Profile => profile;
        public Rigidbody Body => body;
        public float Mass => body != null ? body.mass : 0f;
        public bool IsHeld => held;
        public bool IsOnCart => onCart;

        /// <summary>当前主持有者（谁正抓着它）。</summary>
        public GrabController Holder { get; private set; }

        /// <summary>V2 双人协力抬重物的副持有者（协助分担 45% 重量并提升稳定性）。</summary>
        public PlayerMotor AssistMotor { get; private set; }
        public bool HasCoopAssist => AssistMotor != null && !AssistMotor.VitalsDowned;

        public int CurrentValue
        {
            get
            {
                FragileProp fragile = GetComponent<FragileProp>();
                if (fragile != null)
                {
                    return fragile.CurrentValue;
                }

                CursedItem cursed = GetComponent<CursedItem>();
                if (cursed != null)
                {
                    return cursed.CurrentValue;
                }

                return profile != null ? profile.value : 0;
            }
        }

        public void SetHolder(GrabController value)
        {
            Holder = value;
            if (value == null)
            {
                AssistMotor = null;
            }
        }

        public void SetAssistMotor(PlayerMotor assistant)
        {
            AssistMotor = assistant;
        }

        public void SetOnCart(bool value)
        {
            onCart = value;
        }

        private void OnCollisionEnter(Collision collision)
        {
            PlayHeavyDrop(collision);

            if (!FrictionEvents.Tuning.enableImpactStun || profile == null)
            {
                return;
            }

            float speed = collision.relativeVelocity.magnitude;
            if (speed < FrictionEvents.Tuning.stunSpeedThreshold || profile.mass < FrictionEvents.Tuning.stunMinMass)
            {
                return;
            }

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

            FrictionEvents.Report("手滑砸脚", $"{profile.displayName} 砸中玩家 ({speed:F1} m/s) 硬直 {seconds:F1}s", 420f);
        }

        private void PlayHeavyDrop(Collision collision)
        {
            if (profile == null || profile.mass < 30f)
            {
                return;
            }

            if (collision.relativeVelocity.magnitude < 1.5f)
            {
                return;
            }

            if (networked != null)
            {
                networked.HeavyDropAuthoritative(profile.mass);
                return;
            }

            AudioBus.Play(AudioCue.HeavyDrop, transform.position, 1f, $"mass={profile.mass:F1}");
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

            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.interpolation = RigidbodyInterpolation.Interpolate;

            ApplyProfile();
        }

        private void OnDestroy()
        {
            if (runtimeSurface != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(runtimeSurface);
                }
                else
                {
                    DestroyImmediate(runtimeSurface);
                }

                runtimeSurface = null;
            }
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

            if (!Application.isPlaying)
            {
                body.mass = Mathf.Max(0.1f, profile.mass);
                return;
            }

            body.mass = Mathf.Max(0.1f, profile.mass);
            body.linearDamping = profile.linearDamping;
            body.angularDamping = profile.angularDamping;

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
            if (!value)
            {
                AssistMotor = null;
            }
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
