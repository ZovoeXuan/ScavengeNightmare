using UnityEngine;

namespace ScavengeNightmare.Gameplay
{
    /// <summary>
    /// 道具生成锚点（D12）：在场景里摆一个点并指定物件档 id，PropSpawner 会自动匹配对应 prefab 生成。
    ///
    /// 为什么不用 PropSpawner 里那张硬编码坐标表：那张表是 Main 沙盒专用的，
    /// 每换一张图就要改一次组件配置，坐标表在 prefab/场景两端来回改容易漏。
    /// 锚点写在场景里，谁摆图谁负责，PropSpawner 只负责读。
    /// 这也是 D13「物件系统化」的前置：将来锚点可以直接挂物件类型与价值档。
    /// </summary>
    public sealed class PropSpawnAnchor : MonoBehaviour
    {
        [Tooltip("对应 PropPhysicsProfile.id，例如 can / box / barrel / statue / cabinet / vase")]
        public string profileId = "box";

        [Tooltip("生成后是否随机水平朝向，避免一排货全是同一个角度")]
        public bool randomYaw = true;

        [Tooltip("S4B-5 在哪些关出现：bit0=L1 / bit1=L2 / bit2=L3，默认 7（三关都出现）")]
        public int legsMask = 7;

        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.95f, 0.8f, 0.3f, 0.9f);
            Gizmos.DrawWireCube(transform.position + Vector3.up * 0.15f, new Vector3(0.4f, 0.3f, 0.4f));
        }
    }
}
