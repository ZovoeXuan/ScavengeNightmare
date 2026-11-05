using ScavengeNightmare.Combat;
using ScavengeNightmare.Gameplay;
using ScavengeNightmare.Networking;
using ScavengeNightmare.Noise;
using UnityEngine;

namespace ScavengeNightmare.Monsters
{
    /// <summary>
    /// 尖叫罐 V2 升级版：
    /// 1) 同步 syncWindingUp 到远端客户端；
    /// 2) 尖叫震落手持物时调用 grab.ServerForceRelease()，确保手持诅咒物/重物也能正常脱手。
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
        public override bool IsWindingUp => syncWindingUp || windupEndsAt >= 0f;

        protected override string MonsterId => "scream_can";
        protected override bool HasCustomTick => true;

        protected override void CustomTick(float deltaTime, float now, PlayerVitals seen, AudibleNoise heard)
        {
            switch (State)
            {
                case MonsterState.Spawn:
                    navigator.Stop();
                    if (now - stateEnteredAt >= 0.5f)
                    {
                        EnterState(MonsterState.Patrol);
                    }

                    return;

                case MonsterState.Stunned:
                    navigator.Stop();
                    syncWindingUp = false;
                    if (now >= stunUntil)
                    {
                        EnterState(MonsterState.Patrol);
                    }

                    return;
            }

            navigator.Stop();

            if (now < cooldownUntil)
            {
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

            float playerRadius = profile != null ? profile.screamPlayerRange : 6f;
            PlayerVitals nearby = FindNearestPlayer(playerRadius);
            string reason = null;

            if (nearby != null)
            {
                reason = $"player:{nearby.netId}";
            }
            else
            {
                float noiseThreshold = profile != null ? profile.screamNoiseThreshold : 40f;
                if (NoiseBus.Level >= noiseThreshold)
                {
                    reason = $"noise:{NoiseBus.Level:F0}";
                }
            }

            if (reason != null)
            {
                float windup = profile != null ? profile.screamWindupSeconds : 1.5f;
                windupEndsAt = now + windup;
                syncWindingUp = true;
                triggerReason = reason;
                EnterState(MonsterState.Attack);
                Debug.Log($"[S4B] GATE_SCREAM_WINDUP monster={DisplayId} reason={reason} windup={windup:F1}s");
            }
        }

        private GrabbableProp FindThrownProp()
        {
            float radius = profile != null ? profile.screamPreemptRadius : 3.5f;

            foreach (GrabbableProp prop in FindObjectsByType<GrabbableProp>(FindObjectsSortMode.None))
            {
                if (prop == null || prop.Body == null || prop.IsHeld)
                {
                    continue;
                }

                if (Vector3.Distance(transform.position, prop.transform.position) > radius)
                {
                    continue;
                }

                if (prop.Body.linearVelocity.magnitude >= PreemptSpeedThreshold)
                {
                    return prop;
                }
            }

            return null;
        }

        private void Preempt(float now, GrabbableProp thrown)
        {
            float cooldown = profile != null ? profile.screamCooldownSeconds : 6f;
            float amount = profile != null ? profile.screamPreemptNoise : 15f;
            float speed = thrown != null && thrown.Body != null ? thrown.Body.linearVelocity.magnitude : 0f;

            windupEndsAt = -1f;
            syncWindingUp = false;
            cooldownUntil = now + cooldown;
            preemptCount++;

            NoiseBus.Report("scream_can_preempt", amount, transform.position);
            EnterState(MonsterState.Patrol);

            Debug.Log($"[S4B] GATE_SCREAM_PREEMPT monster={DisplayId} amount={amount:F0} " +
                      $"prop={(thrown != null && thrown.Profile != null ? thrown.Profile.id : "?")} " +
                      $"speed={speed:F1} dropped=0");
        }

        private void Scream(float now)
        {
            float amount = profile != null ? profile.screamNoiseAmount : 45f;
            float radius = profile != null ? profile.screamStunRadius : 12f;
            float stunSeconds = profile != null ? profile.screamStunSeconds : 2f;
            float cooldown = profile != null ? profile.screamCooldownSeconds : 6f;

            windupEndsAt = -1f;
            syncWindingUp = false;
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
                    grab.ServerForceRelease();
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
