using System.Collections.Generic;
using ScavengeNightmare.Combat;
using UnityEngine;

namespace ScavengeNightmare.Monsters
{
    /// <summary>
    /// 追逐预算（P0 §2.2「同屏最多 2 只」）。服务端唯一裁决方：
    /// 单人/双人 limit=1，3–4 人 limit=2；超出的怪物留在 Investigate 并打 GATE_MON_BUDGET。
    /// 挂在 NetworkManager 上（DDOL），换关不丢。
    /// </summary>
    public sealed class MonsterDirector : MonoBehaviour
    {
        public static MonsterDirector Instance { get; private set; }

        private readonly HashSet<MonsterBrain> chasers = new();

        private void Awake()
        {
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        public int ActiveChaseCount => chasers.Count;

        public int ChaseLimit => PlayerVitals.All.Count <= 2 ? 1 : 2;

        public bool TryEnterChase(MonsterBrain brain)
        {
            if (brain == null)
            {
                return false;
            }

            if (chasers.Contains(brain))
            {
                return true;
            }

            if (chasers.Count >= ChaseLimit)
            {
                Debug.Log($"[S4B] GATE_MON_BUDGET denied={brain.netId} active={chasers.Count} limit={ChaseLimit}");
                return false;
            }

            chasers.Add(brain);
            return true;
        }

        public void ReleaseChase(MonsterBrain brain)
        {
            if (brain != null)
            {
                chasers.Remove(brain);
            }
        }
    }
}
