using ScavengeNightmare.Economy;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace ScavengeNightmare.Curses
{
    /// <summary>
    /// 诅咒输入：Q = 长按「放下」（无字牌位专用，1.5s）。
    ///
    /// 与 S4AInput / S4BInput 同构：自动化置 Auto*，正式游玩读 Input System。
    /// 绝不读 UnityEngine.Input —— 工程已切 Input System，旧 Input 每帧抛异常，
    /// 而且是那种「日志报错但看起来还在跑」的假通过来源。
    ///
    /// 为什么单开一个文件而不是塞进 S4BInput：4B 的纪律是既有脚本只增不改，
    /// 需要新行为一律加新文件（技术计划 §3.3 结尾）。
    /// </summary>
    public static class CurseInput
    {
        /// <summary>自动化：置 true 表示「正按着放下键」。</summary>
        public static bool AutoPutDown;

        public static bool PutDownHeld => AutoPutDown || ManualPutDown;

        private static bool ManualPutDown
        {
            get
            {
                if (S4AInput.SuppressManual)
                {
                    return false;
                }

#if ENABLE_INPUT_SYSTEM
                Keyboard keyboard = Keyboard.current;
                return keyboard != null && keyboard.qKey.isPressed;
#else
                return Input.GetKey(KeyCode.Q);
#endif
            }
        }
    }
}
