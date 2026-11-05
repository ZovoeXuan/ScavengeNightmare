using ScavengeNightmare.Combat;
using ScavengeNightmare.Networking;
using ScavengeNightmare.Noise;
using UnityEngine;

namespace ScavengeNightmare.Monsters
{
    /// <summary>
    /// 服务端感知集 V2 升级版：
    /// 修复 IsBeamLit() 未过滤倒地玩家 (candidate.IsDowned) 导致倒地玩家仍能用手电筒把怪物照僵直的 Bug (NET-02)。
    /// </summary>
    public sealed class MonsterSenses
    {
        private const float BeamHalfAngle = 20f;
        private const float BeamRange = 18f;

        private readonly MonsterBrain owner;
        private readonly MonsterProfile profile;
        private readonly CharacterController controller;

        public MonsterSenses(MonsterBrain owner, MonsterProfile profile, CharacterController controller)
        {
            this.owner = owner;
            this.profile = profile;
            this.controller = controller;
        }

        private Vector3 Eye => owner.transform.position + Vector3.up * 1.55f;

        public PlayerVitals FindVisiblePlayer()
        {
            PlayerVitals best = null;
            float bestDistance = float.MaxValue;

            foreach (PlayerVitals candidate in PlayerVitals.All)
            {
                if (candidate == null || candidate.IsDowned)
                {
                    continue;
                }

                Vector3 target = candidate.transform.position + Vector3.up * 1.0f;
                Vector3 delta = target - owner.transform.position;
                float distance = delta.magnitude;

                if (distance > profile.sightRange || distance < 0.01f)
                {
                    continue;
                }

                Vector3 flatForward = new(owner.transform.forward.x, 0f, owner.transform.forward.z);
                Vector3 flatToTarget = new(delta.x, 0f, delta.z);
                if (flatForward.sqrMagnitude < 0.001f || flatToTarget.sqrMagnitude < 0.001f)
                {
                    continue;
                }

                float angle = Vector3.Angle(flatForward, flatToTarget);
                if (angle > profile.sightAngle * 0.5f)
                {
                    continue;
                }

                if (!HasLineOfSight(Eye, target))
                {
                    continue;
                }

                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = candidate;
                }
            }

            return best;
        }

        public AudibleNoise HearNoise()
        {
            return NoiseBus.AudibleAt(owner.transform.position, NoiseBus.HearingRadiusForLevel(NoiseBus.Level));
        }

        public bool IsBeamLit()
        {
            foreach (PlayerVitals candidate in PlayerVitals.All)
            {
                if (candidate == null || candidate.IsDowned)
                {
                    continue;
                }

                PlayerEquipment equipment = candidate.GetComponent<PlayerEquipment>();
                if (equipment == null || !equipment.LightOn)
                {
                    continue;
                }

                PlayerMotor motor = candidate.GetComponent<PlayerMotor>();
                if (motor == null)
                {
                    continue;
                }

                Vector3 eye = candidate.transform.position + Vector3.up * 1.4f;
                Vector3 toTarget = Eye - eye;
                float distance = toTarget.magnitude;
                if (distance > BeamRange || distance < 0.01f)
                {
                    continue;
                }

                Vector3 beam = candidate.transform.rotation
                    * (Quaternion.Euler(motor.ViewPitchDeg, 0f, 0f) * Vector3.forward);

                float angle = Vector3.Angle(beam, toTarget);
                if (angle > BeamHalfAngle)
                {
                    continue;
                }

                if (!HasLineOfSight(eye, Eye))
                {
                    continue;
                }

                return true;
            }

            return false;
        }

        private bool HasLineOfSight(Vector3 from, Vector3 to)
        {
            Vector3 delta = to - from;
            float distance = delta.magnitude;
            if (distance < 0.05f)
            {
                return true;
            }

            RaycastHit[] hits = Physics.RaycastAll(from, delta / distance, distance, ~0, QueryTriggerInteraction.Ignore);

            foreach (RaycastHit hit in hits)
            {
                if (hit.collider == controller || hit.collider.transform.IsChildOf(owner.transform))
                {
                    continue;
                }

                if (hit.collider.GetComponentInParent<PlayerVitals>() != null)
                {
                    continue;
                }

                if (hit.distance < distance - 0.05f)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
