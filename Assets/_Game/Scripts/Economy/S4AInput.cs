using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace ScavengeNightmare.Economy
{
    /// <summary>
    /// 4A 交互输入抽象：正式交互优先用 Input System 的 E 键，自动化用 AutoInteract。
    /// 不能直接读 UnityEngine.Input —— 工程已切到 Input System，读旧 Input 会每帧抛异常，
    /// 而且是那种“日志报错但看起来还在跑”的假通过来源。
    /// </summary>
    public static class S4AInput
    {
        public static bool AutoInteract;

        /// <summary>自动化用的「按住左键」。推车与抓货共用这一个键。</summary>
        public static bool AutoGrab;

        public static bool ManualInteract
        {
            get
            {
#if ENABLE_INPUT_SYSTEM
                Keyboard keyboard = Keyboard.current;
                return keyboard != null && keyboard.eKey.isPressed;
#else
                return Input.GetKey(KeyCode.E);
#endif
            }
        }

        public static bool InteractHeld => AutoInteract || ManualInteract;

        /// <summary>
        /// 鼠标左键：抓货与推车共用的「用手」键。
        /// 推车之前是「靠近就自动推」，玩家没有参与感，也不符合抓握的肌肉记忆——
        /// 现在统一为：靠近是前提，按住左键才真的推。
        /// </summary>
        public static bool ManualGrab
        {
            get
            {
#if ENABLE_INPUT_SYSTEM
                UnityEngine.InputSystem.Mouse mouse = UnityEngine.InputSystem.Mouse.current;
                return mouse != null && mouse.leftButton.isPressed;
#else
                return Input.GetMouseButton(0);
#endif
            }
        }

        public static bool GrabHeld => AutoGrab || ManualGrab;

        /// <summary>自动化购买：1–4 表示按了对应数字键，0 表示没有。</summary>
        public static int AutoBuyIndex;

        /// <summary>自动化撤销（相当于按 U）。</summary>
        public static bool AutoUndo;

        public static int PendingBuyIndex => AutoBuyIndex > 0 ? AutoBuyIndex : ManualBuyIndex;

        public static bool UndoPressed => AutoUndo || ManualUndoPressed;

        private static int ManualBuyIndex
        {
            get
            {
#if ENABLE_INPUT_SYSTEM
                Keyboard keyboard = Keyboard.current;
                if (keyboard == null)
                {
                    return 0;
                }

                if (keyboard.digit1Key.wasPressedThisFrame)
                {
                    return 1;
                }

                if (keyboard.digit2Key.wasPressedThisFrame)
                {
                    return 2;
                }

                if (keyboard.digit3Key.wasPressedThisFrame)
                {
                    return 3;
                }

                if (keyboard.digit4Key.wasPressedThisFrame)
                {
                    return 4;
                }

                return 0;
#else
                if (Input.GetKeyDown(KeyCode.Alpha1))
                {
                    return 1;
                }

                if (Input.GetKeyDown(KeyCode.Alpha2))
                {
                    return 2;
                }

                if (Input.GetKeyDown(KeyCode.Alpha3))
                {
                    return 3;
                }

                if (Input.GetKeyDown(KeyCode.Alpha4))
                {
                    return 4;
                }

                return 0;
#endif
            }
        }

        private static bool ManualUndoPressed
        {
            get
            {
#if ENABLE_INPUT_SYSTEM
                Keyboard keyboard = Keyboard.current;
                return keyboard != null && keyboard.uKey.wasPressedThisFrame;
#else
                return Input.GetKeyDown(KeyCode.U);
#endif
            }
        }
    }
}
