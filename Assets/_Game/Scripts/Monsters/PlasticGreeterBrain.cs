using ScavengeNightmare.Combat;
using ScavengeNightmare.Noise;
using UnityEngine;

namespace ScavengeNightmare.Monsters
{
    /// <summary>
    /// 塑料导购（S4B-3）：按「被注视」而不是「看见玩家」驱动。
    ///
    /// 规则（服务端权威）：
    /// - 任一存活玩家在 25° / 18m / 无遮挡内注视它 → 原地僵住，位移恒为 0；
    /// - 全部移开视线 → 0.35s 前摇 → 0.8s 内突进 &gt;2m；
    /// - 突进撞墙 → 僵直 1.5s；
    /// - 命中 30 伤害；不可击杀（4B 没有击杀系统）。
    ///
    /// 它和空眼的差别是本类完全接管 tick：基类的听觉/视觉状态机对它不生效，
    /// 但手电与推车击退仍由基类统一处理（导购可以被车撞开）。
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
        public override bool IsWindingUp => windupAt >= 0f;

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
                return;
            }

            // 走基类入口：追逐预算由 MonsterDirector 统一裁决，导购不享受例外。
            EnterChase(target, $"sight:{target.netId}", target.transform.position);
            if (State != MonsterState.Chase)
            {
                // 预算拒绝：留在原地，下一轮前摇再试一次（GATE_MON_BUDGET 已由 Director 打过）。
                windupAt = now;
                return;
            }

            targetVitals = target;

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
            EnterState(MonsterState.Patrol);
            Debug.Log($"[S4B] GATE_GREETER_DASH_END monster={DisplayId} at=({transform.position.x:F1},{transform.position.z:F1})");
        }

        /// <summary>
        /// 4 人时每端一位：按 PlayerVitals.All 的服务端顺序置位。
        /// 位掩码只用于日志与验收，不做网络同步——注视判定本身不参与任何玩法结算。
        /// </summary>
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
