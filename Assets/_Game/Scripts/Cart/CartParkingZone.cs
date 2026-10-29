using Mirror;
using UnityEngine;

namespace ScavengeNightmare.Cart
{
    /// <summary>
    /// 公秤前的停车位（1.8×2.6m）。车停进来才允许整车提交。
    ///
    /// 只改服务端状态：客户端上这个 Trigger 同样会触发事件，
    /// 但 SetParked 是 [Server]，客户端调用无效——不能让客户端自己决定「我停好了」。
    /// </summary>
    public sealed class CartParkingZone : MonoBehaviour
    {
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

        private static void Apply(Collider other, bool value)
        {
            if (!NetworkServer.active || other == null)
            {
                return;
            }

            CartController cart = other.GetComponent<CartController>();
            if (cart != null)
            {
                cart.SetParked(value);
            }
        }
    }
}
