using System.Collections.Generic;
using Mirror;
using ScavengeNightmare.Cart;
using ScavengeNightmare.Combat;
using ScavengeNightmare.Gameplay;
using ScavengeNightmare.Noise;
using UnityEngine;

namespace ScavengeNightmare.Monsters
{
    /// <summary>
    /// 4B 怪物状态机骨架（服务端权威）。客户端只看到 15Hz 位置与 SyncVar 状态。
    ///
    /// 七状态：Spawn → Patrol → Investigate → Chase → Attack → Search → Patrol，另有 Stunned。
    /// 关键纪律：
    /// - 进入 Chase 必须有可归因来源（noise:&lt;id&gt; 或 sight:&lt;player&gt;），否则记 S4B_A9_FAIL；
    /// - 全局噪音 70 不等于全图追猎，本地听觉半径由 NoiseBus.AudibleAt 决定；
    /// - 不允许永久卡墙：连续 2s 位移 &lt;0.05m 就回退最近巡逻锚点并打 GATE_MON_UNSTICK。
    /// </summary>
    public class MonsterBrain : NetworkBehaviour
    {
        [Header("数值表（留空则按 MonsterId 从 Resources/Monsters 加载）")]
        [SerializeField] protected MonsterProfile profile;

        [SyncVar(hook = nameof(OnStateSynced))] private byte stateByte;
        [SyncVar] private uint targetNetId;

        private CharacterController controller;
        protected MonsterSenses senses;
        protected MonsterNavigator navigator;
        private MonsterDirector director;

        private readonly List<PatrolAnchor> anchors = new();
        private Vector3 spawnPosition;
        private Vector3 patrolTarget;
        protected Vector3 lastKnownTarget;
        private bool forcedTarget;

        protected float stateEnteredAt;
        private float lastSightAt;
        private float lastNoiseAt;
        private float attackHitAt;
        private float attackEndsAt;
        private bool attackLanded;
        protected float stunUntil;
        private float flashlightExposure;
        private float stalledSeconds;
        private string lastChaseSource = "none";
        private string lastUnstickRescued = "none";

        private Vector3 knockbackVelocity;
        private Vector3 lastCartPosition;
        private bool hasLastCartPosition;
        private float lastCartHitAt = -10f;

        protected PlayerVitals targetVitals;

        /// <summary>子类是否接管整个状态机（S4B-3 的导购 / 尖啸罐）。</summary>
        protected virtual bool HasCustomTick => false;

        /// <summary>前兆表现（导购前摇 / 尖啸罐蓄力），MonsterVisual 用它加快脉冲。</summary>
        public virtual bool IsWindingUp => false;

        public MonsterState State => (MonsterState)stateByte;
        public uint TargetNetId => targetNetId;
        public MonsterProfile Profile => profile;
        public string DisplayId => profile != null ? profile.id : "monster";

        public string LastHeardSource { get; private set; } = "none";
        public string LastHeardBand { get; private set; } = "quiet";
        public float LastHeardDistance { get; private set; }
        public float LastHeardAttenuation { get; private set; }
        public string LastChaseSource => lastChaseSource;
        public float LastLostSeconds { get; private set; }
        public string LastUnstickRescued => lastUnstickRescued;

        public int ChaseCount => chaseCount;
        public int AttackCount => attackCount;
        public int StunCount => stunCount;
        public int UnstickCount => unstickCount;
        public int CartHitCount => cartHitCount;

        private int chaseCount;
        private int attackCount;
        private int stunCount;
        private int unstickCount;
        private int cartHitCount;

        protected virtual string MonsterId => "holloweye";

        protected virtual void Awake()
        {
            controller = GetComponent<CharacterController>();
            navigator = new MonsterNavigator(transform, controller);
            LoadProfileIfMissing();
            senses = new MonsterSenses(this, profile, controller);
        }

        public override void OnStartServer()
        {
            base.OnStartServer();

            spawnPosition = transform.position;
            lastKnownTarget = spawnPosition;
            lastSightAt = Time.time;
            lastNoiseAt = Time.time;
            director = MonsterDirector.Instance;

            anchors.Clear();
            foreach (PatrolAnchor anchor in FindObjectsByType<PatrolAnchor>(FindObjectsSortMode.None))
            {
                if (anchor != null)
                {
                    anchors.Add(anchor);
                }
            }

            EnterState(MonsterState.Spawn);
            NextPatrolPoint();
            Debug.Log($"[S4B] S4B_MON_SPAWN monster={DisplayId} netId={netId} at=({transform.position.x:F1},{transform.position.z:F1}) anchors={anchors.Count}");
        }

        public override void OnStopServer()
        {
            if (director != null)
            {
                director.ReleaseChase(this);
            }

            base.OnStopServer();
        }

        private void Update()
        {
            if (!isServer || !NetworkServer.active)
            {
                return;
            }

            ServerTick(Time.deltaTime);
        }

        private void ServerTick(float deltaTime)
        {
            if (deltaTime <= 0f)
            {
                return;
            }

            float now = Time.time;

            if (knockbackVelocity.sqrMagnitude > 0.01f)
            {
                navigator.MoveDirection(knockbackVelocity.normalized, knockbackVelocity.magnitude);
                knockbackVelocity -= knockbackVelocity.normalized * 8f * deltaTime;
                return;
            }

            UpdateCartKnockback(deltaTime);

            PlayerVitals seen = senses.FindVisiblePlayer();
            if (seen != null)
            {
                lastSightAt = now;
                targetVitals = seen;
                targetNetId = seen.netId;
                lastKnownTarget = seen.transform.position;
            }

            AudibleNoise heard = senses.HearNoise();
            if (heard.HasSource)
            {
                LastHeardSource = heard.Source;
                LastHeardBand = NoiseBus.ZoneForLevel(NoiseBus.Level);
                LastHeardDistance = heard.Distance;
                LastHeardAttenuation = heard.Attenuation;
            }

            UpdateFlashlight(deltaTime, now);

            if (HasCustomTick)
            {
                CustomTick(deltaTime, now, seen, heard);
                return;
            }

            switch (State)
            {
                case MonsterState.Spawn:
                    navigator.Stop();
                    if (now - stateEnteredAt >= 0.5f)
                    {
                        EnterState(MonsterState.Patrol);
                    }

                    break;

                case MonsterState.Patrol:
                    TickPatrol(seen, heard, now);
                    break;

                case MonsterState.Investigate:
                    TickInvestigate(seen, heard, now);
                    break;

                case MonsterState.Chase:
                    TickChase(seen, heard, now);
                    break;

                case MonsterState.Attack:
                    TickAttack(seen, now);
                    break;

                case MonsterState.Search:
                    TickSearch(seen, heard, now);
                    break;

                case MonsterState.Stunned:
                    navigator.Stop();
                    if (now >= stunUntil)
                    {
                        EnterState(seen != null ? MonsterState.Chase : MonsterState.Search);
                    }

                    break;
            }
        }

        /// <summary>
        /// 子类专用 tick：接管后基类不再跑巡逻/追击状态机。
        /// 仍然保留感知刷新、手电与推车击退——导购可以被车撞，尖啸罐不该被感知带跑。
        /// </summary>
        protected virtual void CustomTick(float deltaTime, float now, PlayerVitals seen, AudibleNoise heard)
        {
        }

        private void TickPatrol(PlayerVitals seen, AudibleNoise heard, float now)
        {
            if (seen != null)
            {
                EnterChase(seen, $"sight:{seen.netId}", seen.transform.position);
                return;
            }

            if (heard.HasSource)
            {
                if (NoiseBus.Level >= NoiseBus.HuntLevel)
                {
                    EnterChase(null, $"noise:{heard.Source}", heard.Position, heard);
                }
                else
                {
                    EnterInvestigate(heard);
                }

                return;
            }

            MoveTo(patrolTarget, profile != null ? profile.patrolSpeed : 0.9f);

            if (PlanarDistance(transform.position, patrolTarget) <= (profile != null ? profile.arriveDistance : 0.6f))
            {
                forcedTarget = false;
                NextPatrolPoint();
            }
        }

        private void TickInvestigate(PlayerVitals seen, AudibleNoise heard, float now)
        {
            if (seen != null)
            {
                EnterChase(seen, $"sight:{seen.netId}", seen.transform.position);
                return;
            }

            if (heard.HasSource)
            {
                lastKnownTarget = heard.Position;

                if (NoiseBus.Level >= NoiseBus.HuntLevel)
                {
                    EnterChase(null, $"noise:{heard.Source}", heard.Position, heard);
                    return;
                }
            }

            MoveTo(lastKnownTarget, profile != null ? profile.investigateSpeed : 1.0f);
            if (PlanarDistance(transform.position, lastKnownTarget) <= (profile != null ? profile.arriveDistance : 0.6f))
            {
                EnterState(MonsterState.Search);
            }
        }

        private void TickChase(PlayerVitals seen, AudibleNoise heard, float now)
        {
            float loseSeconds = profile != null ? profile.loseTargetSeconds : 4f;

            if (seen != null)
            {
                targetVitals = seen;
                targetNetId = seen.netId;
                lastKnownTarget = seen.transform.position;
            }

            if (targetVitals != null && !targetVitals.IsDowned)
            {
                float sinceSight = now - lastSightAt;
                if (sinceSight > loseSeconds)
                {
                    EnterSearch(sinceSight);
                    return;
                }

                float distance = PlanarDistance(transform.position, targetVitals.transform.position);
                float attackRange = profile != null ? profile.attackRange : 1.8f;
                if (distance <= attackRange)
                {
                    EnterAttack();
                    return;
                }

                MoveTo(lastKnownTarget, profile != null ? profile.chaseSpeed : 3.2f);
                return;
            }

            // 噪音驱动的追猎：目标不是玩家，必须持续有本地可听记忆，否则 4s 后转搜索。
            targetVitals = null;
            targetNetId = 0;

            if (heard.HasSource)
            {
                lastKnownTarget = heard.Position;
                lastNoiseAt = now;
            }

            float sinceNoise = now - lastNoiseAt;
            if (sinceNoise > loseSeconds)
            {
                EnterSearch(sinceNoise);
                return;
            }

            if (PlanarDistance(transform.position, lastKnownTarget) <= (profile != null ? profile.arriveDistance : 0.6f))
            {
                EnterState(MonsterState.Search);
                return;
            }

            MoveTo(lastKnownTarget, profile != null ? profile.chaseSpeed : 3.2f);
        }

        private void TickAttack(PlayerVitals seen, float now)
        {
            navigator.Stop();

            if (!attackLanded && now >= attackHitAt)
            {
                attackLanded = true;

                if (targetVitals != null && !targetVitals.IsDowned)
                {
                    float distance = PlanarDistance(transform.position, targetVitals.transform.position);
                    if (distance <= (profile != null ? profile.attackRange : 1.8f) + 0.6f)
                    {
                        attackCount++;
                        targetVitals.ServerApplyDamage(profile != null ? profile.attackDamage : 50, DisplayId);
                    }
                }
            }

            if (now >= attackEndsAt)
            {
                EnterState(seen != null ? MonsterState.Chase : MonsterState.Search);
            }
        }

        private void TickSearch(PlayerVitals seen, AudibleNoise heard, float now)
        {
            if (seen != null)
            {
                EnterChase(seen, $"sight:{seen.netId}", seen.transform.position);
                return;
            }

            if (heard.HasSource)
            {
                if (NoiseBus.Level >= NoiseBus.HuntLevel)
                {
                    EnterChase(null, $"noise:{heard.Source}", heard.Position, heard);
                    return;
                }

                EnterInvestigate(heard);
                return;
            }

            MoveTo(lastKnownTarget, profile != null ? profile.investigateSpeed : 1.0f);
            bool arrived = PlanarDistance(transform.position, lastKnownTarget) <= (profile != null ? profile.arriveDistance : 0.6f);
            float searchSeconds = profile != null ? profile.searchSeconds : 3f;

            if (now - stateEnteredAt >= searchSeconds && (arrived || PlanarDistance(transform.position, lastKnownTarget) <= 1.2f))
            {
                NextPatrolPoint();
                EnterState(MonsterState.Patrol);
            }
        }

        protected void MoveTo(Vector3 target, float speed)
        {
            navigator.MoveTowards(target, speed);
            CheckStuck();
        }

        private void CheckStuck()
        {
            // 指令要求移动，但实际位移几乎为零：累计到 2s 就回退锚点，绝不追求「一直顶着墙」。
            if (navigator.LastPlanarDelta < 0.005f)
            {
                stalledSeconds += Time.deltaTime;

                if (stalledSeconds >= 2f)
                {
                    ServerUnstick();
                }
            }
            else
            {
                stalledSeconds = 0f;
            }
        }

        protected void EnterInvestigate(AudibleNoise heard)
        {
            lastKnownTarget = heard.Position;
            lastNoiseAt = Time.time;

            Debug.Log($"[S4B] GATE_MON_HEARD monster={DisplayId} source={heard.Source} " +
                      $"at=({heard.Position.x:F1},{heard.Position.z:F1}) band={NoiseBus.ZoneForLevel(NoiseBus.Level)} " +
                      $"level={NoiseBus.Level:F1} dist={heard.Distance:F1} attenuation={heard.Attenuation:F2}");

            EnterState(MonsterState.Investigate);
        }

        protected void EnterChase(PlayerVitals target, string source, Vector3 at, AudibleNoise heard = default)
        {
            if (!heard.HasSource)
            {
                heard = default;
            }

            if (State == MonsterState.Chase)
            {
                lastKnownTarget = at;
                return;
            }

            if (director == null)
            {
                director = MonsterDirector.Instance;
            }

            if (director != null && !director.TryEnterChase(this))
            {
                lastKnownTarget = at;
                EnterState(MonsterState.Investigate);
                return;
            }

            if (target == null)
            {
                targetVitals = null;
                targetNetId = 0;
                lastNoiseAt = Time.time;
            }
            else
            {
                // 导购这类「不靠视野找目标」的子类需要这里补写，否则 TickChase/命中判定拿不到目标。
                targetVitals = target;
                targetNetId = target.netId;
            }

            lastChaseSource = source;
            chaseCount++;

            Debug.Log($"[S4B] GATE_MON_CHASE monster={DisplayId} state=Chase source={source} " +
                      $"at=({at.x:F1},{at.z:F1}) band={NoiseBus.ZoneForLevel(NoiseBus.Level)} " +
                      $"level={NoiseBus.Level:F1} dist={PlanarDistance(transform.position, at):F1}");

            if (string.IsNullOrEmpty(source) || string.Equals(source, "none", System.StringComparison.Ordinal))
            {
                Debug.LogError($"[S4B] S4B_A9_FAIL monster={DisplayId} reason=chase_without_source at=({at.x:F1},{at.z:F1})");
            }

            EnterState(MonsterState.Chase);
        }

        private void EnterAttack()
        {
            float windup = profile != null ? profile.attackWindup : 0.8f;
            float cooldown = profile != null ? profile.attackCooldown : 2.5f;
            attackHitAt = Time.time + windup;
            attackEndsAt = Time.time + windup + cooldown;
            attackLanded = false;
            EnterState(MonsterState.Attack);
        }

        private void EnterSearch(float lostSeconds)
        {
            LastLostSeconds = lostSeconds;
            Debug.Log($"[S4B] GATE_MON_LOST monster={DisplayId} seconds={lostSeconds:F1} searched=true");
            EnterState(MonsterState.Search);
        }

        protected void EnterState(MonsterState next)
        {
            if (State == MonsterState.Chase && next != MonsterState.Chase && director != null)
            {
                director.ReleaseChase(this);
            }

            stateByte = (byte)next;
            stateEnteredAt = Time.time;
            stalledSeconds = 0f;

            if (next != MonsterState.Attack)
            {
                targetNetId = targetVitals != null ? targetVitals.netId : 0;
            }
        }

        private void UpdateFlashlight(float deltaTime, float now)
        {
            if (State == MonsterState.Stunned || profile == null || profile.flashlightStunSeconds <= 0f)
            {
                flashlightExposure = 0f;
                return;
            }

            if (senses.IsBeamLit())
            {
                flashlightExposure += deltaTime;

                if (flashlightExposure >= profile.flashlightStunSeconds)
                {
                    flashlightExposure = 0f;
                    ServerStun(profile.stunSeconds, "flashlight");
                }
            }
            else
            {
                flashlightExposure = 0f;
            }
        }

        private void UpdateCartKnockback(float deltaTime)
        {
            CartController cart = CartController.Instance;
            if (cart == null)
            {
                hasLastCartPosition = false;
                return;
            }

            Vector3 cartPosition = cart.transform.position;

            if (!hasLastCartPosition)
            {
                lastCartPosition = cartPosition;
                hasLastCartPosition = true;
                return;
            }

            Vector3 velocity = (cartPosition - lastCartPosition) / deltaTime;
            velocity.y = 0f;
            lastCartPosition = cartPosition;

            float speed = velocity.magnitude;
            if (speed < 2f || Time.time - lastCartHitAt < 0.8f)
            {
                return;
            }

            Vector3 away = transform.position - cartPosition;
            away.y = 0f;
            if (away.sqrMagnitude > 3.24f || away.sqrMagnitude < 0.0001f)
            {
                return;
            }

            lastCartHitAt = Time.time;
            knockbackVelocity = away.normalized * 5f;
            stalledSeconds = 0f;
            cartHitCount++;

            Debug.Log($"[S4B] GATE_MON_CART_HIT monster={DisplayId} cartSpeed={speed:F1} push=5.0 dist={away.magnitude:F1}");
        }

        /// <summary>服务端：手电/技能触发的僵硬。任何来源进 Stunned 都必须走这里，并打统一日志。</summary>
        [Server]
        public void ServerStun(float seconds, string source)
        {
            if (seconds <= 0f)
            {
                return;
            }

            stunCount++;
            stunUntil = Time.time + seconds;
            knockbackVelocity = Vector3.zero;
            EnterState(MonsterState.Stunned);

            Debug.Log($"[S4B] GATE_MON_STUN monster={DisplayId} seconds={seconds:F1} source={source}");
        }

        /// <summary>
        /// 服务端：被点名听到一声巨响（尖啸罐的尖叫）。只进 Investigate，绝不直接 Chase——
        /// 「最近的那只去看一眼」是 4B-3 的明确口径，全图追猎会毁掉 Chase 预算。
        /// </summary>
        [Server]
        public void ServerAlert(Vector3 position, string source)
        {
            lastKnownTarget = position;
            lastNoiseAt = Time.time;
            LastHeardSource = source;
            LastHeardBand = NoiseBus.ZoneForLevel(NoiseBus.Level);
            LastHeardDistance = PlanarDistance(transform.position, position);
            LastHeardAttenuation = 0f;

            Debug.Log($"[S4B] GATE_MON_HEARD monster={DisplayId} source={source} " +
                      $"at=({position.x:F1},{position.z:F1}) band={LastHeardBand} " +
                      $"level={NoiseBus.Level:F1} dist={LastHeardDistance:F1} attenuation=forced");

            EnterState(MonsterState.Investigate);
        }

        /// <summary>连续 2s 卡住：回退最近巡逻锚点。两次都失败才算 fail（A9 不接受 fail）。</summary>
        [Server]
        public void ServerUnstick()
        {
            float duration = stalledSeconds;
            string where = $"({transform.position.x:F1},{transform.position.z:F1})";
            PatrolAnchor nearest = FindNearestAnchor(transform.position);
            bool rescued = false;

            if (nearest != null)
            {
                navigator.Warp(nearest.transform.position + Vector3.up * 0.1f);
                lastKnownTarget = nearest.transform.position;
                patrolTarget = lastKnownTarget;
                rescued = true;
            }

            unstickCount++;
            lastUnstickRescued = rescued ? "self" : "fail";
            stalledSeconds = 0f;

            Debug.Log($"[S4B] GATE_MON_UNSTICK monster={DisplayId} duration={duration:F1} where={where} rescued={lastUnstickRescued}");

            if (!rescued)
            {
                Debug.LogError($"[S4B] S4B_MON_UNSTICK_FAIL monster={DisplayId} where={where}");
            }
        }

        private PatrolAnchor FindNearestAnchor(Vector3 position)
        {
            PatrolAnchor best = null;
            float bestDistance = float.MaxValue;

            foreach (PatrolAnchor anchor in anchors)
            {
                if (anchor == null)
                {
                    continue;
                }

                float distance = PlanarDistance(position, anchor.transform.position);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = anchor;
                }
            }

            return best;
        }

        private void NextPatrolPoint()
        {
            if (forcedTarget)
            {
                return;
            }

            PatrolAnchor anchor = anchors.Count > 0
                ? anchors[Random.Range(0, anchors.Count)]
                : null;

            if (anchor != null)
            {
                Vector2 offset = Random.insideUnitCircle * Mathf.Min(1.5f, anchor.radius);
                patrolTarget = anchor.transform.position + new Vector3(offset.x, 0f, offset.y);
            }
            else
            {
                patrolTarget = spawnPosition + new Vector3(Random.Range(-3f, 3f), 0f, Random.Range(-3f, 3f));
            }
        }

        protected static float PlanarDistance(Vector3 a, Vector3 b)
        {
            return new Vector2(a.x - b.x, a.z - b.z).magnitude;
        }

        private void LoadProfileIfMissing()
        {
            if (profile != null)
            {
                return;
            }

            profile = Resources.Load<MonsterProfile>($"Monsters/{MonsterId}");

            if (profile == null)
            {
                Debug.LogError($"[S4B] S4B_MON_PROFILE_MISSING monster={MonsterId}");
            }
        }

        private void OnStateSynced(byte oldValue, byte newValue)
        {
            // 视觉侧自己轮询 State，钩子只用于日志（不在这里做任何服务端逻辑）。
            if (NetworkServer.active)
            {
                return;
            }

            Debug.Log($"[S4B] S4B_MON_STATE monster={DisplayId} state={(MonsterState)newValue}");
        }

        // ==================== 测试钩子（仅 -s4b* 进程调用） ====================

        [Server]
        public void ServerTestWarp(Vector3 position)
        {
            navigator.Warp(position);
            lastKnownTarget = position;
            patrolTarget = position;
            stalledSeconds = 0f;
        }

        [Server]
        public void ServerTestForcePatrolTarget(Vector3 position)
        {
            forcedTarget = true;
            patrolTarget = position;
            lastKnownTarget = position;
            stalledSeconds = 0f;
            EnterState(MonsterState.Patrol);
        }

        [Server]
        public void ServerTestClearForceTarget()
        {
            forcedTarget = false;
            NextPatrolPoint();
        }
    }
}
