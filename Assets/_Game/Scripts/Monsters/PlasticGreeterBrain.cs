using ScavengeNightmare.Combat;
using ScavengeNightmare.Networking;
using ScavengeNightmare.Noise;
using UnityEngine;

namespace ScavengeNightmare.Monsters
{
    /// <summary>
    /// 塑料导购 V2 升级版：
    /// 1) 修复 IsWatchedBy 忽略玩家相机俯仰角 (ViewPitchDeg) 导致低头看地板/抬头看天花板仍算注视的 Bug (AI-01)；
    /// 2) 同步 syncWindingUp 到远端客户端，使所有客户端都能看到突进前摇的高频脉冲预警 (AI-02)。
    /// </summary>
    public sealed class PlasticGreeterBrain : MonsterBrain
    {
        private const float WallStallSeconds = 0.15f;

        private uint watchedMask;
        private uint lastLoggedMask;
        private float windupAt = -1f;
        private float dashUntil;
        private Vector3 dashTarget;
        private bool dashing;
        private bool dashHit;
        private float wallStall;

        private int dashCount;
        private int wallStunCount;

        public uint WatchedMask => watchedMask;
        public int DashCount => dashCount;
        public int WallStunCount => wallStunCount;
        public override bool IsWindingUp => syncWindingUp || windupAt >= 0f;

        protected override string MonsterId => "plastic_greeter";
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

            uint mask = ComputeWatchedMask();
            if (mask != lastLoggedMask)
            {
                lastLoggedMask = mask;
                Debug.Log($"[S4B] GATE_GREETER_WATCH monster={DisplayId} mask={mask} players={PlayerVitals.All.Count}");
            }

            watchedMask = mask;

            if (mask != 0)
            {
                Freeze();
                return;
            }

            if (dashing)
            {
                TickDash(deltaTime, now);
                return;
            }

            if (windupAt < 0f)
            {
                windupAt = now;
                syncWindingUp = true;
            }

            float windup = profile != null ? profile.greeterWindupSeconds : 0.35f;
            if (now - windupAt < windup)
            {
                navigator.Stop();
                return;
            }

            StartDash(now);
        }

        private void Freeze()
        {
            navigator.Stop();
            dashing = false;
            wallStall = 0f;
            windupAt = -1f;
            syncWindingUp = false;

            if (State != MonsterState.Patrol)
            {
                EnterState(MonsterState.Patrol);
            }
        }

        private void StartDash(float now)
        {
            float range = profile != null ? profile.greeterWatchRange : 18f;
            PlayerVitals target = FindNearestPlayer(range);

            if (target == null)
            {
                windupAt = -1f;
                syncWindingUp = false;
                return;
            }

            EnterChase(target, $"sight:{target.netId}", target.transform.position);
            if (State != MonsterState.Chase)
            {
                windupAt = now;
                return;
            }

            targetVitals = target;
            syncWindingUp = false;
            dashing = true;
            dashHit = false;
            wallStall = 0f;
            dashUntil = now + (profile != null ? profile.greeterDashSeconds : 0.8f);
            dashTarget = target.transform.position;
            dashCount++;

            Debug.Log($"[S4B] GATE_GREETER_DASH monster={DisplayId} target={target.netId} " +
                $"from=({transform.position.x:F1},{transform.position.z:F1}) " +
                $"to=({dashTarget.x:F1},{dashTarget.z:F1}) seconds={(dashUntil - now):F2}");
        }

        private void TickDash(float deltaTime, float now)
        {
            float attackRange = profile != null ? profile.attackRange : 1.8f;

            if (!dashHit && targetVitals != null && !targetVitals.IsDowned)
            {
                if (PlanarDistance(transform.position, targetVitals.transform.position) <= attackRange)
                {
                    dashHit = true;
                    int damage = profile != null ? profile.attackDamage : 30;
                    targetVitals.ServerApplyDamage(damage, DisplayId);
                    Debug.Log($"[S4B] GATE_GREETER_ATTACK monster={DisplayId} target={targetVitals.netId} damage={damage}");
                    EndDash();
                    return;
                }
            }

            if (now >= dashUntil)
            {
                EndDash();
                return;
            }

            MoveTo(dashTarget, profile != null ? profile.chaseSpeed : 3.2f);

            if (navigator.LastPlanarDelta < 0.05f)
            {
                wallStall += deltaTime;

                if (wallStall >= WallStallSeconds)
                {
                    HitWall();
                }
            }
            else
            {
                wallStall = 0f;
            }
        }

        private void HitWall()
        {
            dashing = false;
            wallStall = 0f;
            windupAt = -1f;
            syncWindingUp = false;
            wallStunCount++;

            float seconds = profile != null ? profile.greeterWallStunSeconds : 1.5f;
            Debug.Log($"[S4B] GATE_GREETER_WALL monster={DisplayId} seconds={seconds:F1} " +
                $"at=({transform.position.x:F1},{transform.position.z:F1})");

            ServerStun(seconds, "wall");
        }

        private void EndDash()
        {
            dashing = false;
            wallStall = 0f;
            windupAt = -1f;
            syncWindingUp = false;
            EnterState(MonsterState.Patrol);
            Debug.Log($"[S4B] GATE_GREETER_DASH_END monster={DisplayId} at=({transform.position.x:F1},{transform.position.z:F1})");
        }

        private uint ComputeWatchedMask()
        {
            uint mask = 0;
            int index = 0;

            foreach (PlayerVitals candidate in PlayerVitals.All)
            {
                if (candidate == null || candidate.IsDowned)
                {
                    index++;
                    continue;
                }

                if (IsWatchedBy(candidate))
                {
                    mask |= 1u << index;
                }

                index++;
            }

            return mask;
        }

        private bool IsWatchedBy(PlayerVitals player)
        {
            float range = profile != null ? profile.greeterWatchRange : 18f;
            float angleLimit = (profile != null ? profile.greeterWatchAngle : 25f) * 0.5f;

            Vector3 playerEye = player.transform.position + Vector3.up * 1.4f;
            Vector3 monsterChest = transform.position + Vector3.up * 1.0f;
            Vector3 delta = monsterChest - playerEye;

            if (delta.sqrMagnitude < 0.0001f || delta.magnitude > range)
            {
                return false;
            }

            // 修复 AI-01：校验玩家相机俯仰角，低头看地板或抬头看天花板超过 50° 时不再算注视导购
            PlayerMotor motor = player.GetComponent<PlayerMotor>();
            if (motor != null && Mathf.Abs(motor.ViewPitchDeg) > 50f)
            {
                return false;
            }

            Vector3 flatForward = new(player.transform.forward.x, 0f, player.transform.forward.z);
            Vector3 flatToMonster = new(delta.x, 0f, delta.z);

            if (flatForward.sqrMagnitude < 0.001f || flatToMonster.sqrMagnitude < 0.001f)
            {
                return false;
            }

            if (Vector3.Angle(flatForward, flatToMonster) > angleLimit)
            {
                return false;
            }

            return HasLineOfSight(playerEye, monsterChest, player);
        }

        private bool HasLineOfSight(Vector3 from, Vector3 to, PlayerVitals viewer)
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
                if (hit.collider == null)
                {
                    continue;
                }

                if (hit.collider.transform.IsChildOf(transform) || hit.collider.transform.IsChildOf(viewer.transform))
                {
                    continue;
                }

                if (hit.collider.GetComponentInParent<PlayerVitals>() == viewer)
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
    }
}
