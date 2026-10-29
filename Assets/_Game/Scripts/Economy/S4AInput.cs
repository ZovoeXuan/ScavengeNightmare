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
    }
}
