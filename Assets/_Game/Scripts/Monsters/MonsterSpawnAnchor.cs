using UnityEngine;

namespace ScavengeNightmare.Monsters
{
    /// <summary>
    /// 怪物生成锚点（S4B-2 摆 6 个）。场景里只摆锚点，真正的怪物由 MonsterSpawner 运行时 Spawn。
    /// </summary>
    public sealed class MonsterSpawnAnchor : MonoBehaviour
    {
        [Tooltip("对应 MonsterProfile.id；S4B-2 只有 holloweye")]
        public string profileId = "holloweye";

        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.85f, 0.15f, 0.2f, 0.9f);
            Gizmos.DrawWireSphere(transform.position, 0.4f);
        }
    }
}
