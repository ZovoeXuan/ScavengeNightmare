using Mirror;
using ScavengeNightmare.Economy;
using UnityEngine;

namespace ScavengeNightmare.Networking
{
    /// <summary>
    /// 玩家的「交互意图」通道：客户端读本地按键 → Command 上报服务端。
    ///
    /// 为什么必须有这一层：服务端不能自己读 S4AInput。服务器读的是它自己那台机器的键盘，
    /// 主机模式下房主按 E 能提交、看起来一切正常，但远端客户端永远按不动——这是最典型的
    /// 一类假联机（主机单机跑通 = 联机也跑通）。QA 的 4A-D1..D5 双人冒烟第一条就卡在这里。
    ///
    /// 惰性：只在场景里存在 QuotaTerminal 时才发 Command，Main / Supermarket 上零网络增量。
    /// </summary>
    public sealed class PlayerInteract : NetworkBehaviour
    {
        [SyncVar] private bool interactHeld;
        [SyncVar] private bool grabHeld;

        /// <summary>服务端可读的交互状态（E 键）。客户端本地读这个值没有意义，必须是服务端视角。</summary>
        public bool InteractHeldServer => interactHeld;

        /// <summary>服务端可读的抓握/推车状态（鼠标左键）。</summary>
        public bool GrabHeldServer => grabHeld;

        private bool lastSent;
        private bool lastGrabSent;

        private void Update()
        {
            if (!isLocalPlayer)
            {
                return;
            }

            // 惰性：场景里既没有公秤也没有推车也没有商店时完全不发消息，Main / Supermarket 零网络增量。
            if (QuotaTerminal.Instance == null
                && ScavengeNightmare.Cart.CartController.Instance == null
                && ShopTerminal.Instance == null)
            {
                return;
            }

            if (ShopTerminal.Instance != null)
            {
                int buy = S4AInput.PendingBuyIndex;
                if (buy > 0)
                {
                    CmdShopBuy(buy);
                }

                if (S4AInput.UndoPressed)
                {
                    CmdShopUndo();
                }
            }

            bool held = S4AInput.InteractHeld;
            if (held != lastSent)
            {
                lastSent = held;
                CmdSetInteract(held);
            }

            // 两条通道各自独立判断，不能用「E 键没变化就 return」提前退出——
            // 那样左键这条永远上报不上去（推车会一直判定成没人推）。
            bool grabbing = S4AInput.GrabHeld;
            if (grabbing != lastGrabSent)
            {
                lastGrabSent = grabbing;
                CmdSetGrab(grabbing);
            }
        }

        [Command]
        private void CmdSetInteract(bool value)
        {
            interactHeld = value;
        }

        [Command]
        private void CmdSetGrab(bool value)
        {
            grabHeld = value;
        }

        // 商店的 Command 必须挂在**玩家自己**身上，不能挂在场景里的 ShopTerminal 上：
        // 场景对象没有 owner，connectionToClient 恒为 null，服务端复核距离会拿到 -1，
        // 表现是「客户端以为在店里、服务端一律判不在店里」——实测就踩了这个坑。
        [Command]
        private void CmdShopBuy(int hotkey)
        {
            ShopTerminal terminal = ShopTerminal.Instance;
            if (terminal == null)
            {
                return;
            }

            terminal.ServerHandleBuy(transform, hotkey);
        }

        [Command]
        private void CmdShopUndo()
        {
            ShopTerminal terminal = ShopTerminal.Instance;
            if (terminal == null)
            {
                return;
            }

            terminal.ServerHandleUndo(transform);
        }
    }
}
