using UnityEngine;

namespace ScavengeNightmare.Economy
{
    /// <summary>
    /// 场景入口锚点。玩家跨关重建后放到这里。
    ///
    /// 为什么不用 <c>NetworkManager.startPositions</c>：那是场景里的 Transform 列表，
    /// Mirror 在 <c>OnServerAddPlayer</c> 里读它；而换关时玩家重建的时机早于场景对象可用，
    /// 实测会拿到空表 → 玩家生成在原点掉出地图。用一个自带组件当锚点，
    /// 读取时按 <c>activeScene</c> 过滤，时序上稳得多。
    /// </summary>
    public sealed class SceneEntryAnchor : MonoBehaviour
    {
        [Tooltip("出生朝向（度）。0 = 面向 +Z")]
        public float yawDegrees;

        public Vector3 SpawnPosition => transform.position;

        public Quaternion SpawnRotation => Quaternion.Euler(0f, yawDegrees, 0f);

        /// <summary>取当前激活场景里的第一个锚点。Safehouse 与夜路各有一个，不会互相串。</summary>
        public static SceneEntryAnchor ResolveActive()
        {
            SceneEntryAnchor[] anchors = FindObjectsByType<SceneEntryAnchor>(FindObjectsSortMode.None);
            string active = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;

            foreach (SceneEntryAnchor anchor in anchors)
            {
                if (anchor != null && anchor.gameObject.scene.name == active)
                {
                    return anchor;
                }
            }

            return anchors.Length > 0 ? anchors[0] : null;
        }
    }
}
