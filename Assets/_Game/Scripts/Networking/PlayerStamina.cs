using ScavengeNightmare.Economy;
using UnityEngine;

namespace ScavengeNightmare.Networking
{
    /// <summary>
    /// S4C-1 体能：默认连续冲刺 5.0s（20s 一局的 25%），买过 `stamina_training`
    /// 后升到 7.5s（37.5%，4B 的验收值）。
    /// 停跑后按 1.5/s 恢复，恢复到一半才能再次冲刺。
    ///
    /// 为什么是本地组件而不是服务端权威：玩家移动本身就是客户端权威
    /// （NetworkTransform ClientToServer），冲刺在客户端被拦下才有意义；
    /// 服务端再判一遍只会得到「本地早跑过去了、服务端说你没有体能」的错位。
    /// </summary>
    public sealed class PlayerStamina : MonoBehaviour
    {
        /// <summary>未购买体能训练的连续冲刺秒数。</summary>
        public const float BaseCapacitySeconds = 5f;

        /// <summary>购买体能训练后的连续冲刺秒数（= 20s × 37.5%）。</summary>
        public const float TrainedCapacitySeconds = 7.5f;

        /// <summary>恢复速度（每秒）。5.0s 满体力约 3.3s 回满。</summary>
        public const float RecoveryPerSecond = 1.5f;

        /// <summary>力竭后至少恢复到这一比例才允许再次冲刺，避免「点一下回一丝、又立刻冲」。</summary>
        public const float ResumeFraction = 0.5f;

        /// <summary>当前档位的连续冲刺容量：买过体能训练 7.5s，否则 5.0s。</summary>
        public float CapacitySeconds => EquippedEffects.HasStaminaTraining
            ? TrainedCapacitySeconds
            : BaseCapacitySeconds;

        [SerializeField] private PlayerMotor motor;

        private float remaining = BaseCapacitySeconds;
        private bool exhausted;

        public bool CanSprint => !exhausted && remaining > 0f;

        /// <summary>0–1，HUD 与自动化都用它。</summary>
        public float Normalized => Mathf.Clamp01(remaining / CapacitySeconds);

        /// <summary>本次力竭前的连续冲刺秒数（自动化判据写日志用）。</summary>
        public float LastContinuousSprint { get; private set; }

        private float sprintElapsed;

        private void Awake()
        {
            if (motor == null)
            {
                motor = GetComponent<PlayerMotor>();
            }

            remaining = CapacitySeconds;
            Debug.Log($"[S4C] GATE_STAMINA_CAP base={BaseCapacitySeconds:F1} capacity={CapacitySeconds:F1} " +
                      $"trained={(EquippedEffects.HasStaminaTraining ? 1 : 0)}");
        }

        private void Update()
        {
            if (motor == null || !motor.isLocalPlayer)
            {
                return;
            }

            bool sprinting = motor.SprintInput && !motor.VitalsDowned;

            if (sprinting)
            {
                sprintElapsed += Time.deltaTime;
                remaining -= Time.deltaTime;

                if (remaining <= 0f)
                {
                    remaining = 0f;
                    exhausted = true;
                    LastContinuousSprint = sprintElapsed;
                    sprintElapsed = 0f;
                    Debug.Log($"[S4B] S4B_STAMINA_DEPLETED seconds={LastContinuousSprint:F2} " +
                              $"pct={(LastContinuousSprint / 20f * 100f):F1}");
                }

                return;
            }

            // 停跑即开始恢复；点了方向但没按冲刺也算停跑，恢复不打折。
            if (sprintElapsed > 0f)
            {
                sprintElapsed = 0f;
            }

            if (remaining >= CapacitySeconds)
            {
                remaining = CapacitySeconds;
                return;
            }

            remaining = Mathf.Min(CapacitySeconds, remaining + RecoveryPerSecond * Time.deltaTime);

            if (exhausted && remaining >= CapacitySeconds * ResumeFraction)
            {
                exhausted = false;
                Debug.Log($"[S4B] S4B_STAMINA_RESUME normalized={Normalized:F2}");
            }
        }

        /// <summary>测试钩子：把体力打满（不影响正式路径）。</summary>
        public void ResetStamina()
        {
            remaining = CapacitySeconds;
            exhausted = false;
            sprintElapsed = 0f;
        }
    }
}
