using ScavengeNightmare.Economy;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace ScavengeNightmare.Combat
{
    /// <summary>
    /// 4B 生存输入：R = 倒地自救，H = 对队友用急救包。
    /// 与 S4AInput 同构：自动化置 Auto* 标志，正式游玩读 Input System。
    /// 绝不读 UnityEngine.Input —— 工程已切 Input System，旧 Input 每帧抛异常。
    /// </summary>
    public static class S4BInput
    {
        /// <summary>自动化：置 true 后由 PlayerVitals 消费并复位。</summary>
        public static bool AutoSelfInject;

        /// <summary>自动化：置 true 后由 PlayerVitals 消费并复位。</summary>
        public static bool AutoMedkit;

        public static bool SelfInjectPressed => AutoSelfInject || ManualSelfInject;

        public static bool MedkitPressed => AutoMedkit || ManualMedkit;

        private static bool ManualSelfInject
        {
            get
            {
                if (S4AInput.SuppressManual)
                {
                    return false;
                }

#if ENABLE_INPUT_SYSTEM
                Keyboard keyboard = Keyboard.current;
                return keyboard != null && keyboard.rKey.wasPressedThisFrame;
#else
                return Input.GetKeyDown(KeyCode.R);
#endif
            }
        }

        private static bool ManualMedkit
        {
            get
            {
                if (S4AInput.SuppressManual)
                {
                    return false;
                }

#if ENABLE_INPUT_SYSTEM
                Keyboard keyboard = Keyboard.current;
                return keyboard != null && keyboard.hKey.wasPressedThisFrame;
#else
                return Input.GetKeyDown(KeyCode.H);
#endif
            }
        }
    }
}
