using System.Collections.Generic;
using Mirror;
using UnityEngine;

namespace ScavengeNightmare.Cart
{
    /// <summary>
    /// 公秤前的停车位（1.8×2.6m）。车停进来才允许整车提交。
    /// V2 修复：使用 GetComponentInParent 并维护活跃碰撞体集合，防止推车子碰撞体进出触发器导致误判离开停车位。
    /// </summary>
    public sealed class CartParkingZone : MonoBehaviour
    {
        private readonly HashSet<Collider> activeCartColliders = new();

        private void Awake()
        {
            Collider zone = GetComponent<Collider>();
            if (zone != null)
            {
                zone.isTrigger = true;
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            Apply(other, true);
        }

        private void OnTriggerExit(Collider other)
        {
            Apply(other, false);
        }

        private void Apply(Collider other, bool entered)
        {
            if (!NetworkServer.active || other == null)
            {
                return;
            }

            CartController cart = other.GetComponent<CartController>() ?? other.GetComponentInParent<CartController>();
            if (cart == null)
            {
                return;
            }

            activeCartColliders.RemoveWhere(c => c == null || !c.enabled || !c.gameObject.activeInHierarchy);

            if (entered)
            {
                activeCartColliders.Add(other);
                cart.SetParked(true);
            }
            else
            {
                activeCartColliders.Remove(other);
                if (activeCartColliders.Count == 0)
                {
                    cart.SetParked(false);
                }
            }
        }
    }
}
