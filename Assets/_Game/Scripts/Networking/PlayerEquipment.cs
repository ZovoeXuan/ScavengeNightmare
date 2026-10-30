using Mirror;
using ScavengeNightmare.Economy;
using UnityEngine;

namespace ScavengeNightmare.Networking
{
    /// <summary>
    /// 手电筒（4A 四件商品之一）。
    ///
    /// 开关走 Command + SyncVar，而不是「客户端自己切」：不权威的开关在四人局里会出现
    /// 「我看着开了、队友那边全黑」。灯光本体是本地生成的 SpotLight —— 它纯视觉、
    /// 不参与任何判定，所以不需要 SyncTransform。
    /// </summary>
    public sealed class PlayerEquipment : NetworkBehaviour
    {
        [SyncVar] private bool lightOn;

        private Light spot;
        private float nextCheck;
        private bool lastHadFlashlight;

        public bool LightOn => lightOn;

        /// <summary>给自动化读：本地是否真的有一盏亮着的灯。</summary>
        public bool SpotActive => spot != null && spot.enabled && spot.gameObject.activeInHierarchy;

        private void Update()
        {
            if (!isLocalPlayer)
            {
                return;
            }

            SyncFlashlightOwnership();
            EnsureSpot();

            if (!EquippedEffects.HasFlashlight)
            {
                return;
            }

            bool pressed = false;
#if ENABLE_INPUT_SYSTEM
            UnityEngine.InputSystem.Keyboard kb = UnityEngine.InputSystem.Keyboard.current;
            pressed = kb != null && kb.fKey.wasPressedThisFrame;
#else
            pressed = Input.GetKeyDown(KeyCode.F);
#endif

            if (S4AInput.AutoToggleLight)
            {
                pressed = true;
                S4AInput.AutoToggleLight = false;
            }

            if (pressed)
            {
                CmdToggleLight(!lightOn);
            }
        }

        /// <summary>
        /// 商品被撤销后灯必须灭。OwnedMask 是 SyncVar，所有权变化在客户端可见，
        /// 所以这一条不需要额外 RPC。
        /// </summary>
        private void SyncFlashlightOwnership()
        {
            bool has = EquippedEffects.HasFlashlight;

            if (has == lastHadFlashlight)
            {
                return;
            }

            lastHadFlashlight = has;

            if (!has && lightOn)
            {
                CmdToggleLight(false);
            }

            if (!has && spot != null)
            {
                spot.enabled = false;
            }
        }

        [Command]
        private void CmdToggleLight(bool on)
        {
            lightOn = on;
            Debug.Log($"[S4A] S4A_FLASHLIGHT on={on} netId={netId}");
        }

        private void EnsureSpot()
        {
            if (spot == null)
            {
                GameObject go = new("Flashlight");
                go.transform.SetParent(transform, false);
                go.transform.localPosition = new Vector3(0.2f, 1.4f, 0.25f);

                spot = go.AddComponent<Light>();
                spot.type = LightType.Spot;
                spot.spotAngle = 55f;
                spot.intensity = 6f;
                spot.range = Mathf.Max(8f, EquippedEffects.FlashlightRange);
                spot.shadows = LightShadows.None;
            }

            float range = Mathf.Max(8f, EquippedEffects.FlashlightRange);
            if (!Mathf.Approximately(spot.range, range))
            {
                spot.range = range;
            }

            bool want = lightOn && EquippedEffects.HasFlashlight;
            if (spot.enabled != want)
            {
                spot.enabled = want;
            }
        }
    }
}
