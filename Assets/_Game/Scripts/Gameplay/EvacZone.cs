using Mirror;
using UnityEngine;

namespace ScavengeNightmare.Gameplay
{
    /// <summary>
    /// 撤离点（D12）：玩家进入即记录，作为 D13 撤离结算的挂点。
    /// D12 只要求「图里有撤离点且能走进去」，所以这里只做进入检测与日志，
    /// 不结算、不加 UI——结算属于 D13 的物件系统。
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public sealed class EvacZone : MonoBehaviour
    {
        private int inside;

        /// <summary>当前是否有玩家在撤离区内，D13 结算直接读这个。</summary>
        public bool HasPlayer => inside > 0;

        private void Awake()
        {
            BoxCollider box = GetComponent<BoxCollider>();
            box.isTrigger = true;
        }

        private void OnTriggerEnter(Collider other)
        {
            Networking.PlayerMotor motor = other.GetComponentInParent<Networking.PlayerMotor>();
            if (motor == null)
            {
                return;
            }

            inside++;
            Debug.Log($"[D12] D12_EVAC_ENTER player={other.name} inside={inside}");

            // D13：带货进入即结算。价值由 EvacSettlement 从物件档读取，
            // 这里不参与任何数值计算，只负责「把货交出去」这个动作。
            if (!NetworkServer.active)
            {
                return;
            }

            GrabController grab = motor.GetComponent<GrabController>();
            GrabbableProp carried = grab != null ? grab.HeldProp : null;

            if (carried != null && EvacSettlement.Instance != null)
            {
                EvacSettlement.Instance.Deliver(carried);
            }
        }

        private void OnTriggerExit(Collider other)
        {
            if (other.GetComponentInParent<Networking.PlayerMotor>() == null)
            {
                return;
            }

            inside = Mathf.Max(0, inside - 1);
            Debug.Log($"[D12] D12_EVAC_EXIT player={other.name} inside={inside}");
        }

        private void OnDrawGizmos()
        {
            BoxCollider box = GetComponent<BoxCollider>();
            if (box == null) return;
            Gizmos.color = new Color(0.9f, 0.35f, 0.2f, 0.5f);
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(box.center, box.size);
        }
    }
}
