using Mirror;
using UnityEngine;

namespace ScavengeNightmare.Traits
{
    /// <summary>
    /// 窄区注册表：D6 的门洞（GapZone）把自己登记在这里，D8 的「窄门恐惧」靠它判定玩家是否在窄区内。
    ///
    /// 为什么不用 Trigger：玩家的碰撞体是 CharacterController，Trigger 回调需要额外配置且
    /// 在服务端权威模型下不可靠。按 AABB 距离判定既能在服务端算，也便于自动化验证。
    /// </summary>
    public static class NarrowZones
    {
        private static readonly System.Collections.Generic.List<NarrowZone> zones = new();

        public struct NarrowZone
        {
            public Transform Transform;
            public Vector3 HalfExtent;
            public float Margin;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnLoad()
        {
            zones.Clear();
        }

        public static void Register(Transform t, Vector3 halfExtent, float margin = 0.8f)
        {
            if (t == null)
            {
                return;
            }

            zones.Add(new NarrowZone { Transform = t, HalfExtent = halfExtent, Margin = margin });
        }

        public static void Unregister(Transform t)
        {
            for (int i = zones.Count - 1; i >= 0; i--)
            {
                if (zones[i].Transform == t || zones[i].Transform == null)
                {
                    zones.RemoveAt(i);
                }
            }
        }

        public static int Count => zones.Count;

        /// <summary>玩家是否处在任一窄区内（含 margin 余量）。</summary>
        public static bool InsideAny(Vector3 position)
        {
            for (int i = zones.Count - 1; i >= 0; i--)
            {
                NarrowZone z = zones[i];
                if (z.Transform == null)
                {
                    zones.RemoveAt(i);
                    continue;
                }

                // 转到区域本地空间做 AABB 判定，支持任意朝向的门洞
                Vector3 local = z.Transform.InverseTransformPoint(position);
                Vector3 half = z.HalfExtent + Vector3.one * z.Margin;
                if (Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.y) <= half.y && Mathf.Abs(local.z) <= half.z)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
