using Mirror;
using ScavengeNightmare.Combat;
using ScavengeNightmare.Economy;
using ScavengeNightmare.Networking;
using ScavengeNightmare.Noise;
using UnityEngine;

namespace ScavengeNightmare.Monsters
{
    /// <summary>
    /// V2 新增高阶怪：夜秤 (night_scale, T3)。
    /// 设计规格来自 Docs/design/Monster_Roster_v1.md：
    /// - 行为：在公秤 (QuotaTerminal) 附近或高噪音区域缓步巡游，展开 6.5m「重力失衡场」，
    ///   令场内未受保护的搬运者移动上限降低（压迫玩家交货节奏）；
    /// - 克制：用手电筒照射夜秤 1.5s 使其进入 Stunned，失衡场立即消散 10 秒！
    /// </summary>
    public sealed class NightScaleBrain : MonsterBrain
    {
        [Header("夜秤 (NightScale) 专属参数")]
        [SerializeField] private float auraRadius = 6.5f;
        [SerializeField] private float auraSpeedCap = 2.15f;

        [SyncVar] private bool auraActive = true;

        private float auraSuppressedUntil;

        public bool AuraActive => auraActive;

        protected override string MonsterId => "night_scale";
        protected override bool HasCustomTick => true;

        public override void OnStopServer()
        {
            ClearAuraEffects();
            base.OnStopServer();
        }

        protected override void CustomTick(float deltaTime, float now, PlayerVitals seen, AudibleNoise heard)
        {
            if (State == MonsterState.Stunned)
            {
                if (auraActive)
                {
                    auraActive = false;
                    auraSuppressedUntil = now + 10f;
                    ClearAuraEffects();
                    Debug.Log($"[S4B] GATE_NIGHTSCALE_SUPPRESSED monster={DisplayId} seconds=10.0");
                }

                navigator.Stop();
                if (now >= stunUntil)
                {
                    EnterState(MonsterState.Patrol);
                }
                return;
            }

            if (!auraActive && now >= auraSuppressedUntil)
            {
                auraActive = true;
            }

            Vector3 targetPos = lastKnownTarget;
            if (QuotaTerminal.Instance != null && NoiseBus.Level >= NoiseBus.AlertLevel)
            {
                targetPos = QuotaTerminal.Instance.transform.position;
            }
            else if (heard.HasSource)
            {
                targetPos = heard.Position;
            }

            MoveTo(targetPos, profile != null ? profile.patrolSpeed : 0.7f);
            if (PlanarDistance(transform.position, targetPos) <= 1.2f)
            {
                NextPatrolPoint();
            }

            ApplyAuraToNearbyPlayers();
        }

        private void ApplyAuraToNearbyPlayers()
        {
            if (!auraActive)
            {
                return;
            }

            foreach (PlayerVitals vitals in PlayerVitals.All)
            {
                if (vitals == null || vitals.IsDowned)
                {
                    continue;
                }

                PlayerMotor motor = vitals.GetComponent<PlayerMotor>();
                if (motor == null)
                {
                    continue;
                }

                float d = PlanarDistance(transform.position, vitals.transform.position);
                if (d <= auraRadius && motor.CarryMassKg > 0.1f)
                {
                    motor.SetExternalSpeedCap(auraSpeedCap);
                }
            }
        }

        private void ClearAuraEffects()
        {
            foreach (PlayerVitals vitals in PlayerVitals.All)
            {
                if (vitals == null)
                {
                    continue;
                }

                PlayerMotor motor = vitals.GetComponent<PlayerMotor>();
                if (motor != null && ScavengeNightmare.Cart.CartController.Instance?.CurrentPusher != motor)
                {
                    motor.SetExternalSpeedCap(-1f);
                }
            }
        }
    }
}
