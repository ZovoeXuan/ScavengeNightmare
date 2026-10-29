using UnityEngine;

namespace ScavengeNightmare.Cart
{
    /// <summary>
    /// 推车生成锚点（同 PropSpawnAnchor 模式）：场景里只摆锚点，
    /// 推车本体必须由服务端运行时 Spawn —— 场景里摆的 prefab 实例拿不到 sceneId（D6.5 已确立）。
    /// </summary>
    public sealed class CartSpawnAnchor : MonoBehaviour
    {
        [Tooltip("生成时的朝向（度）。默认朝向场景 +Z")]
        public float yawDegrees = 0f;

        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.35f, 0.75f, 0.95f, 0.9f);
            Gizmos.DrawWireCube(transform.position + Vector3.up * 0.3f, new Vector3(1.3f, 0.6f, 0.75f));
        }
    }
}
