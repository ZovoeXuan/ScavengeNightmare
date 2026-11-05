using Mirror;
using ScavengeNightmare.Cart;
using ScavengeNightmare.Combat;
using ScavengeNightmare.Gameplay;
using ScavengeNightmare.Noise;
using UnityEngine;

namespace ScavengeNightmare.Monsters
{
    /// <summary>
    /// V2 新增怪物：搬尸者 / 偷货怪 (lifter, T2)。
    /// 设计规格来自 Docs/design/Monster_Roster_v1.md：
    /// - 行为：巡逻寻找推车上的高价值货物或散落在地面的高价值物资（>= $120），潜行靠近后将其偷走并抱在胸前，
    ///   随后以 2.4 m/s 逃向最远的巡逻锚点藏匿；
    /// - 克制：用手电筒照射其手部/身体 1.0s（触发基类 ServerStun）或用推车撞击，会迫使它立刻丢下赃物并进入僵直！
    /// </summary>
    public sealed class LifterBrain : MonsterBrain
    {
        [Header("搬尸者 (Lifter) 专属参数")]
        [SerializeField] private float stealRange = 2.0f;
        [SerializeField] private float fleeSpeed = 2.4f;
        [SerializeField] private float stealCooldown = 8f;

        [SyncVar] private uint stolenPropNetId;

        private GrabbableProp stolenProp;
        private Vector3 fleeAnchor;
        private float nextStealAllowedAt;
        private int stealCount;
        private int dropCount;

        public int StealCount => stealCount;
        public int DropCount => dropCount;
        public bool CarryingLoot => stolenProp != null;

        protected override string MonsterId => "lifter";
        protected override bool HasCustomTick => true;

        public override void OnStopServer()
        {
            DropStolenLoot("despawn");
            base.OnStopServer();
        }

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
                    if (stolenProp != null)
                    {
                        DropStolenLoot("stunned_by_player");
                    }

                    if (now >= stunUntil)
                    {
                        EnterState(MonsterState.Patrol);
                    }
                    return;
            }

            if (stolenProp != null)
            {
                TickFleeWithLoot(now);
                return;
            }

            if (now >= nextStealAllowedAt)
            {
                GrabbableProp candidate = FindBestLootTarget();
                if (candidate != null)
                {
                    float dist = PlanarDistance(transform.position, candidate.transform.position);
                    if (dist <= stealRange)
                    {
                        StealProp(candidate, now);
                        return;
                    }

                    EnterState(MonsterState.Investigate);
                    MoveTo(candidate.transform.position, profile != null ? profile.chaseSpeed * 0.85f : 2.5f);
                    return;
                }
            }

            if (State != MonsterState.Patrol)
            {
                EnterState(MonsterState.Patrol);
            }

            MoveTo(lastKnownTarget, profile != null ? profile.patrolSpeed : 1.0f);
            if (PlanarDistance(transform.position, lastKnownTarget) <= 0.8f)
            {
                PatrolAnchor next = FindNearestAnchor(transform.position + Random.insideUnitSphere * 12f);
                if (next != null)
                {
                    lastKnownTarget = next.transform.position;
                }
            }
        }

        private GrabbableProp FindBestLootTarget()
        {
            GrabbableProp best = null;
            float bestScore = 0f;

            foreach (GrabbableProp prop in FindObjectsByType<GrabbableProp>(FindObjectsSortMode.None))
            {
                if (prop == null || prop.Body == null || prop.CurrentValue <= 0)
                {
                    continue;
                }

                // 不抢玩家正抓在手里的重物，优先偷推车上的货或地面的高价值货
                if (prop.IsHeld && !prop.IsOnCart)
                {
                    continue;
                }

                float dist = PlanarDistance(transform.position, prop.transform.position);
                if (dist > 18f)
                {
                    continue;
                }

                float cartBonus = prop.IsOnCart ? 1.6f : 1.0f;
                float score = (prop.CurrentValue * cartBonus) / Mathf.Max(2f, dist);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = prop;
                }
            }

            return best;
        }

        private void StealProp(GrabbableProp prop, float now)
        {
            if (prop == null || prop.Body == null)
            {
                return;
            }

            if (CartController.Instance != null)
            {
                CartController.Instance.ServerNotifyPropGrabbed(prop);
            }

            stolenProp = prop;
            stolenPropNetId = prop.GetComponent<NetworkIdentity>()?.netId ?? 0u;
            prop.SetHeld(true);
            prop.SetHolder(null);
            prop.Body.useGravity = false;
            stealCount++;

            PatrolAnchor nest = FindFarthestAnchor(transform.position);
            fleeAnchor = nest != null ? nest.transform.position : transform.position - transform.forward * 10f;

            NoiseBus.Report("lifter_steal", 12f, transform.position);
            EnterState(MonsterState.Chase);
            Debug.Log($"[S4B] GATE_LIFTER_STEAL monster={DisplayId} prop={(prop.Profile != null ? prop.Profile.id : "?")} value={prop.CurrentValue} count={stealCount}");
        }

        private void TickFleeWithLoot(float now)
        {
            if (stolenProp == null || stolenProp.Body == null)
            {
                stolenProp = null;
                stolenPropNetId = 0u;
                EnterState(MonsterState.Patrol);
                return;
            }

            Vector3 carryPos = transform.position + transform.forward * 0.65f + Vector3.up * 1.05f;
            stolenProp.Body.MovePosition(Vector3.Lerp(stolenProp.Body.position, carryPos, 0.5f));
            stolenProp.Body.linearVelocity = Vector3.zero;

            MoveTo(fleeAnchor, fleeSpeed);

            if (PlanarDistance(transform.position, fleeAnchor) <= 1.0f)
            {
                DropStolenLoot("hidden_at_nest");
                nextStealAllowedAt = now + stealCooldown;
                EnterState(MonsterState.Patrol);
            }
        }

        [Server]
        public void DropStolenLoot(string reason)
        {
            if (stolenProp == null)
            {
                return;
            }

            GrabbableProp prop = stolenProp;
            stolenProp = null;
            stolenPropNetId = 0u;
            dropCount++;
            nextStealAllowedAt = Time.time + stealCooldown;

            prop.SetHeld(false);
            prop.SetHolder(null);
            if (prop.Body != null)
            {
                prop.Body.isKinematic = false;
                prop.Body.useGravity = true;
                prop.Body.WakeUp();
                prop.Body.linearVelocity = Vector3.up * 1.2f - transform.forward * 0.8f;
            }

            Debug.Log($"[S4B] GATE_LIFTER_DROP monster={DisplayId} reason={reason} drops={dropCount}");
        }

        private PatrolAnchor FindFarthestAnchor(Vector3 from)
        {
            PatrolAnchor farthest = null;
            float maxDist = -1f;

            foreach (PatrolAnchor anchor in FindObjectsByType<PatrolAnchor>(FindObjectsSortMode.None))
            {
                if (anchor == null)
                {
                    continue;
                }

                float d = PlanarDistance(from, anchor.transform.position);
                if (d > maxDist)
                {
                    maxDist = d;
                    farthest = anchor;
                }
            }

            return farthest;
        }
    }
}
