using UnityEngine;

namespace ScavengeNightmare.Curses
{
    /// <summary>
    /// 诅咒生成锚点（S4B-4）。场景里摆位，<see cref="CurseRuntime"/> 按关取前 N 个。
    ///
    /// 为什么不复用 PropSpawnAnchor：PropSpawner 会把所有 PropSpawnAnchor 无条件生成出来，
    /// 诅咒却是「L1 0 件 / L2 1 件 / L3 2 件」。走那条路的话第 1 关也会掉两件诅咒，
    /// 而且 4A 的 16 件货口径会被多出来的两件污染。
    /// </summary>
    public sealed class CurseSpawnAnchor : MonoBehaviour
    {
        [SerializeField] private CurseKind curse = CurseKind.Doll;

        public CurseKind Kind => curse;

        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.62f, 0.25f, 0.72f, 0.9f);
            Gizmos.DrawWireSphere(transform.position + Vector3.up * 0.15f, 0.25f);
        }
    }
}
