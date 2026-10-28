using UnityEngine;

namespace ScavengeNightmare.Gameplay
{
    /// <summary>
    /// 怪物巡逻锚点（D12 只摆位，D14 实现状态机时读取）。
    /// 现在挂组件而不是纯空物体，是为了让「这张图有没有配够巡逻点」能被自动化校验，
    /// 而不是靠数物体个数。
    /// </summary>
    public sealed class PatrolAnchor : MonoBehaviour
    {
        [Tooltip("该点的巡逻半径（米），D14 巡逻状态在半径内选随机目标")]
        public float radius = 4f;

        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.4f, 0.7f, 0.35f, 0.8f);
            Gizmos.DrawWireSphere(transform.position, 0.35f);
            Gizmos.color = new Color(0.4f, 0.7f, 0.35f, 0.25f);
            Gizmos.DrawWireSphere(transform.position, radius);
        }
    }
}
