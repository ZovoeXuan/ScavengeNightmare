using Mirror;
using ScavengeNightmare.Economy;
using UnityEngine;

namespace ScavengeNightmare.Networking
{
    /// <summary>
    /// 玩家的「交互意图」通道 V2 升级版：
    /// 修复消费 S4AInput.PendingBuyIndex 与 S4AInput.UndoPressed 时未复位 AutoBuyIndex / AutoUndo 导致每帧重复发送 Command 的 Bug (NET-07)。
    /// </summary>
    public sealed class PlayerInteract : NetworkBehaviour
    {
        [SyncVar] private bool interactHeld;
        [SyncVar] private bool grabHeld;

        public bool InteractHeldServer => interactHeld;
        public bool GrabHeldServer => grabHeld;

        private bool lastSent;
        private bool lastGrabSent;

        private void Update()
        {
            if (!isLocalPlayer)
            {
                return;
            }

            if (QuotaTerminal.Instance == null
                && ScavengeNightmare.Cart.CartController.Instance == null
                && ShopTerminal.Instance == null
                && LevelPortal.Instance == null)
            {
                return;
            }

            if (ShopTerminal.Instance != null)
            {
                int buy = S4AInput.PendingBuyIndex;
                if (buy > 0)
                {
                    S4AInput.AutoBuyIndex = 0;
                    CmdShopBuy(buy);
                }

                if (S4AInput.UndoPressed)
                {
                    S4AInput.AutoUndo = false;
                    CmdShopUndo();
                }
            }

            bool held = S4AInput.InteractHeld;
            if (held != lastSent)
            {
                lastSent = held;
                CmdSetInteract(held);
            }

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
