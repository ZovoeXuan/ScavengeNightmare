using UnityEngine;

namespace ScavengeNightmare.Gameplay
{
    /// <summary>
    /// D6 滚动体：圆桶类物件进入/退出滚动状态的判定与反馈。
    /// 滚动行为本身由物理（Convex MeshCollider + 低摩擦 + 低角阻尼）负责，本组件只负责让状态可感知。
    /// </summary>
    [RequireComponent(typeof(GrabbableProp))]
    public sealed class RollingProp : MonoBehaviour
    {
        private Rigidbody body;
        private bool rolling;

        public bool IsRolling => rolling;
        public float PlanarSpeed { get; private set; }

        private void Awake()
        {
            body = GetComponent<Rigidbody>();
        }

        private void FixedUpdate()
        {
            if (body == null)
            {
                return;
            }

            Vector3 planar = body.linearVelocity;
            planar.y = 0f;
            PlanarSpeed = planar.magnitude;

            bool shouldRoll = FrictionEvents.Tuning.enableRolling && PlanarSpeed > FrictionEvents.Tuning.rollingStartSpeed;

            if (shouldRoll && !rolling)
            {
                rolling = true;
                FrictionEvents.Report("滚动体", $"{name} 开始滚动 {PlanarSpeed:F1} m/s", 190f);
            }
            else if (!shouldRoll && rolling)
            {
                rolling = false;
                FrictionEvents.Report("滚动体", $"{name} 停滚");
            }
        }
    }
}
