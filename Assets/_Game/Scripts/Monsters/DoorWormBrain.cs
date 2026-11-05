using Mirror;
using ScavengeNightmare.Combat;
using ScavengeNightmare.Gameplay;
using ScavengeNightmare.Noise;
using UnityEngine;

namespace ScavengeNightmare.Monsters
{
    /// <summary>
    /// V2 新增怪物：窄门兽 (door_worm, T1)。
    /// 设计规格来自 Docs/design/Monster_Roster_v1.md：
    /// - 行为：盘踞在 1.2m 窄门洞或走廊咽喉处，膨胀身体堵塞推车与重物通行，玩家强行贴靠会受到挤压减速与擦伤；
    /// - 克制：用手电筒直射 1.2s 或用推车冲撞，窄门兽会畏光收缩至 35% 体积并退让 8 秒，让满载推车顺利通过！
    /// </summary>
    public sealed class DoorWormBrain : MonsterBrain
    {
        [Header("窄门兽 (DoorWorm) 专属参数")]
        [SerializeField] private float blockRadius = 1.35f;
        [SerializeField] private float shrinkDuration = 8f;
        [SerializeField] private int contactDamage = 12;
        [SerializeField] private float contactCooldown = 2.0f;

        [SyncVar] private bool isShrunk;

        private float shrunkUntil;
        private float nextContactDamageAt;
        private Vector3 homeDoorPosition;
        private Vector3 originalScale = Vector3.one;

        public bool IsShrunk => isShrunk;

        protected override string MonsterId => "door_worm";
        protected override bool HasCustomTick => true;

        protected override void Awake()
        {
            base.Awake();
            originalScale = transform.localScale;
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            NarrowDoorWatcher nearestDoor = FindNearestDoorway();
            homeDoorPosition = nearestDoor != null ? nearestDoor.transform.position : transform.position;
        }

        private void LateUpdate()
        {
            Vector3 targetScale = isShrunk
                ? new Vector3(originalScale.x * 0.4f, originalScale.y * 0.35f, originalScale.z * 0.4f)
                : originalScale;
            transform.localScale = Vector3.Lerp(transform.localScale, targetScale, Time.deltaTime * 8f);
        }

        protected override void CustomTick(float deltaTime, float now, PlayerVitals seen, AudibleNoise heard)
        {
            if (State == MonsterState.Stunned)
            {
                if (!isShrunk)
                {
                    isShrunk = true;
                    shrunkUntil = now + shrinkDuration;
                    Debug.Log($"[S4B] GATE_DOORWORM_SHRINK monster={DisplayId} seconds={shrinkDuration:F1}");
                }

                navigator.Stop();
                if (now >= stunUntil)
                {
                    EnterState(MonsterState.Patrol);
                }
                return;
            }

            if (isShrunk && now >= shrunkUntil)
            {
                isShrunk = false;
                Debug.Log($"[S4B] GATE_DOORWORM_EXPAND monster={DisplayId}");
            }

            if (PlanarDistance(transform.position, homeDoorPosition) > 0.6f)
            {
                MoveTo(homeDoorPosition, profile != null ? profile.patrolSpeed : 0.6f);
                return;
            }

            navigator.Stop();

            if (isShrunk || now < nextContactDamageAt)
            {
                return;
            }

            foreach (PlayerVitals player in PlayerVitals.All)
            {
                if (player == null || player.IsDowned)
                {
                    continue;
                }

                if (PlanarDistance(transform.position, player.transform.position) <= blockRadius)
                {
                    nextContactDamageAt = now + contactCooldown;
                    player.ServerApplyDamage(contactDamage, DisplayId);
                    NoiseBus.Report("door_worm_squeeze", 8f, transform.position);
                    break;
                }
            }
        }

        private NarrowDoorWatcher FindNearestDoorway()
        {
            NarrowDoorWatcher best = null;
            float bestDist = float.MaxValue;

            foreach (NarrowDoorWatcher door in FindObjectsByType<NarrowDoorWatcher>(FindObjectsSortMode.None))
            {
                if (door == null)
                {
                    continue;
                }

                float d = PlanarDistance(transform.position, door.transform.position);
                if (d < bestDist)
                {
                    bestDist = d;
                    best = door;
                }
            }

            return best;
        }
    }
}
