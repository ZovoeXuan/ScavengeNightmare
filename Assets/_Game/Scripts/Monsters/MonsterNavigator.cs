using UnityEngine;

namespace ScavengeNightmare.Monsters
{
    /// <summary>
    /// 服务端移动器：CharacterController + 简易避障 + 卡住度量。
    /// 不做 NavMesh（4B 明说不烘）；直追 + 前方球扫转向 + 2s 卡住回退锚点是兜底。
    /// 台阶靠 CharacterController 的 stepOffset 0.5 直接走上 0.25m 级楼梯，不需要跳跃。
    /// </summary>
    public sealed class MonsterNavigator
    {
        private const float Gravity = -18f;
        private const float ProbeRadius = 0.3f;
        private const float ProbeDistance = 0.8f;

        private static readonly float[] SteeringAngles = { 0f, 45f, -45f, 80f, -80f };

        private readonly Transform transform;
        private readonly CharacterController controller;
        private float verticalVelocity;

        public MonsterNavigator(Transform transform, CharacterController controller)
        {
            this.transform = transform;
            this.controller = controller;
        }

        /// <summary>本帧实际发生的水平位移。卡死判定读它，不读「有没有下达移动指令」。</summary>
        public float LastPlanarDelta { get; private set; }

        public void MoveTowards(Vector3 target, float speed)
        {
            Vector3 delta = target - transform.position;
            delta.y = 0f;

            if (delta.sqrMagnitude < 0.0001f)
            {
                Stop();
                return;
            }

            MoveDirection(delta.normalized, speed);
        }

        public void MoveDirection(Vector3 direction, float speed)
        {
            if (direction.sqrMagnitude < 0.0001f || speed <= 0f)
            {
                Stop();
                return;
            }

            Vector3 before = transform.position;
            Vector3 heading = ResolveHeading(direction.normalized);

            if (heading.sqrMagnitude > 0.001f)
            {
                Quaternion look = Quaternion.LookRotation(heading, Vector3.up);
                transform.rotation = Quaternion.Slerp(transform.rotation, look, 8f * Time.deltaTime);
            }

            if (controller.isGrounded && verticalVelocity < 0f)
            {
                verticalVelocity = -2f;
            }
            else
            {
                verticalVelocity += Gravity * Time.deltaTime;
            }

            Vector3 motion = heading * speed + Vector3.up * verticalVelocity;
            controller.Move(motion * Time.deltaTime);

            Vector3 after = transform.position;
            LastPlanarDelta = new Vector2(after.x - before.x, after.z - before.z).magnitude;
        }

        public void Stop()
        {
            LastPlanarDelta = 0f;

            if (controller.isGrounded && verticalVelocity < 0f)
            {
                verticalVelocity = -2f;
            }

            controller.Move(Vector3.up * verticalVelocity * Time.deltaTime);
        }

        /// <summary>服务端瞬移（卡住脱困 / 测试定位）。必须临时关掉 CharacterController，否则位移会被它吃掉。</summary>
        public void Warp(Vector3 position)
        {
            controller.enabled = false;
            transform.position = position;
            controller.enabled = true;
            verticalVelocity = 0f;
            LastPlanarDelta = 0f;
        }

        private Vector3 ResolveHeading(Vector3 desired)
        {
            foreach (float angle in SteeringAngles)
            {
                Vector3 candidate = Quaternion.Euler(0f, angle, 0f) * desired;

                if (!Blocked(candidate))
                {
                    return candidate;
                }
            }

            return desired;
        }

        private bool Blocked(Vector3 direction)
        {
            Vector3 origin = transform.position + Vector3.up * 0.9f;
            RaycastHit[] hits = Physics.SphereCastAll(origin, ProbeRadius, direction, ProbeDistance, ~0, QueryTriggerInteraction.Ignore);

            foreach (RaycastHit hit in hits)
            {
                if (hit.collider == null)
                {
                    continue;
                }

                if (hit.collider.transform.IsChildOf(transform))
                {
                    continue;
                }

                // 玩家不算障碍：怪物必须能贴到攻击距离，不能被「玩家自己」挡住前进。
                if (hit.collider.GetComponentInParent<Combat.PlayerVitals>() != null)
                {
                    continue;
                }

                if (hit.distance < ProbeDistance)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
