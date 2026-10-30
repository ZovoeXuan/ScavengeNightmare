using ScavengeNightmare.Combat;
using ScavengeNightmare.Gameplay;
using ScavengeNightmare.Networking;
using ScavengeNightmare.Noise;
using UnityEngine;

namespace ScavengeNightmare.Monsters
{
    /// <summary>
    /// 尖啸罐（S4B-3）：噪音放大器，不是击杀目标。
    ///
    /// 规则（服务端权威）：
    /// - 玩家进入 6m 内，或全局噪音 ≥40 → 1.5s 前摇；
    /// - 前摇结束 → NoiseBus +45，12m 内玩家眩晕 2s 并掉下手持物；
    /// - 前摇内被任何投掷物（速度 ≥5m/s）命中 → 引爆：噪音只 +15、无眩晕无掉落；
    /// - 尖叫后只把「最近的一只空眼」推进 Investigate（source=scream_can），不做全图 Chase。
    ///
    /// 它不移动、不攻击、也不吃视觉/听觉状态机；基类的推车击退仍会把它撞歪（可接受）。
    /// </summary>
    public sealed class ScreamCanBrain : MonsterBrain
    {
        private const float PreemptSpeedThreshold = 5f;

        private float windupEndsAt = -1f;
        private float cooldownUntil;
        private string triggerReason = "none";
        private int screamCount;
        private int preemptCount;

        public int ScreamCount => screamCount;
        public int PreemptCount => preemptCount;
        public override bool IsWindingUp => windupEndsAt >= 0f;

        protected override string MonsterId => "scream_can";

        protected override bool HasCustomTick => true;

        protected override void CustomTick(float deltaTime, float now, PlayerVitals seen, AudibleNoise heard)
        {
            navigator.Stop();

            switch (State)
            {
                case MonsterState.Spawn:
                    if (now - stateEnteredAt >= 0.5f)
                    {
                        EnterState(MonsterState.Patrol);
                    }

                    return;

                case MonsterState.Stunned:
                    if (now >= stunUntil)
                    {
                        EnterState(MonsterState.Patrol);
                    }

                    return;
            }

            if (windupEndsAt >= 0f)
            {
                GrabbableProp thrown = FindThrownProp();
                if (thrown != null)
                {
                    Preempt(now, thrown);
                    return;
                }

                if (now >= windupEndsAt)
                {
                    Scream(now);
                }

                return;
            }

            if (now < cooldownUntil)
            {
                return;
            }

            float playerRange = profile != null ? profile.screamPlayerRange : 6f;
            float noiseThreshold = profile != null ? profile.screamNoiseThreshold : 40f;

            PlayerVitals near = FindNearestPlayer(playerRange);
            bool noiseTrigger = NoiseBus.Level >= noiseThreshold;

            if (near == null && !noiseTrigger)
            {
                return;
            }

            float windup = profile != null ? profile.screamWindupSeconds : 1.5f;
            windupEndsAt = now + windup;
            triggerReason = near != null ? $"player:{near.netId}" : $"noise:{NoiseBus.Level:F0}";
            EnterState(MonsterState.Attack);

            Debug.Log($"[S4B] GATE_SCREAM_TRIGGER monster={DisplayId} reason={triggerReason} windup={windup:F2} " +
                      $"at=({transform.position.x:F1},{transform.position.z:F1})");
        }

        private GrabbableProp FindThrownProp()
        {
            float radius = profile != null ? profile.screamPreemptRadius : 3.5f;
            Collider[] overlapped = Physics.OverlapSphere(
                transform.position + Vector3.up * 0.6f, radius, ~0, QueryTriggerInteraction.Ignore);

            foreach (Collider collider in overlapped)
            {
                GrabbableProp prop = collider != null ? collider.GetComponentInParent<GrabbableProp>() : null;
                if (prop == null || prop.Body == null || prop.IsHeld)
                {
                    continue;
                }

                if (prop.Body.linearVelocity.sqrMagnitude < PreemptSpeedThreshold * PreemptSpeedThreshold)
                {
                    continue;
                }

                return prop;
            }

            return null;
        }

        private void Preempt(float now, GrabbableProp thrown)
        {
            float amount = profile != null ? profile.screamPreemptNoise : 15f;
            float cooldown = profile != null ? profile.screamCooldownSeconds : 6f;

            windupEndsAt = -1f;
            cooldownUntil = now + cooldown;
            preemptCount++;

            NoiseBus.Report("scream_can_preempt", amount, transform.position);
            EnterState(MonsterState.Patrol);

            Debug.Log($"[S4B] GATE_SCREAM_PREEMPT monster={DisplayId} amount={amount:F0} " +
                      $"prop={(thrown.Profile != null ? thrown.Profile.id : "?")} " +
                      $"speed={thrown.Body.linearVelocity.magnitude:F1} dropped=0");
        }

        private void Scream(float now)
        {
            float amount = profile != null ? profile.screamNoiseAmount : 45f;
            float radius = profile != null ? profile.screamStunRadius : 12f;
            float stunSeconds = profile != null ? profile.screamStunSeconds : 2f;
            float cooldown = profile != null ? profile.screamCooldownSeconds : 6f;

            windupEndsAt = -1f;
            cooldownUntil = now + cooldown;
            screamCount++;

            NoiseBus.Report("scream_can", amount, transform.position);

            int stunned = 0;
            int dropped = 0;

            foreach (PlayerVitals player in PlayerVitals.All)
            {
                if (player == null || player.IsDowned)
                {
                    continue;
                }

                if (PlanarDistance(transform.position, player.transform.position) > radius)
                {
                    continue;
                }

                PlayerMotor motor = player.GetComponent<PlayerMotor>();
                if (motor != null)
                {
                    motor.ServerStun(stunSeconds);
                    stunned++;
                }

                GrabController grab = player.GetComponent<GrabController>();
                if (grab != null && grab.IsHolding)
                {
                    grab.Release(false);
                    dropped++;
                    Debug.Log($"[S4B] GATE_SCREAM_DROP target={player.netId} reason=scream");
                }

                Debug.Log($"[S4B] GATE_SCREAM_STUN target={player.netId} seconds={stunSeconds:F1} " +
                          $"dist={PlanarDistance(transform.position, player.transform.position):F1}");
            }

            MonsterBrain nearest = FindNearestHollowEye();
            if (nearest != null)
            {
                nearest.ServerAlert(transform.position, "scream_can");
                Debug.Log($"[S4B] GATE_SCREAM_ALERT monster={nearest.DisplayId} netId={nearest.netId} " +
                          $"source=scream_can dist={PlanarDistance(transform.position, nearest.transform.position):F1}");
            }

            EnterState(MonsterState.Patrol);

            Debug.Log($"[S4B] GATE_SCREAM monster={DisplayId} noise={amount:F0} stunned={stunned} " +
                      $"dropped={dropped} reason={triggerReason} at=({transform.position.x:F1},{transform.position.z:F1})");
        }

        private PlayerVitals FindNearestPlayer(float range)
        {
            PlayerVitals best = null;
            float bestDistance = range;

            foreach (PlayerVitals candidate in PlayerVitals.All)
            {
                if (candidate == null || candidate.IsDowned)
                {
                    continue;
                }

                float distance = PlanarDistance(transform.position, candidate.transform.position);
                if (distance <= bestDistance)
                {
                    bestDistance = distance;
                    best = candidate;
                }
            }

            return best;
        }

        private MonsterBrain FindNearestHollowEye()
        {
            MonsterBrain best = null;
            float bestDistance = float.MaxValue;

            foreach (HollowEyeBrain eye in FindObjectsByType<HollowEyeBrain>(FindObjectsSortMode.None))
            {
                if (eye == null)
                {
                    continue;
                }

                float distance = PlanarDistance(transform.position, eye.transform.position);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = eye;
                }
            }

            return best;
        }
    }
}
