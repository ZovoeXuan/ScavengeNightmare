using Mirror;
using ScavengeNightmare.Combat;
using ScavengeNightmare.Economy;
using UnityEngine;

namespace ScavengeNightmare.Networking
{
    /// <summary>
    /// 手电筒 V2 升级版：
    /// 1) 修复 Update() 在 EnsureSpot() 之前 return 导致远端队友的手电筒在本地永远不发光 (NET-01)；
    /// 2) 远端玩家的手电筒根据同步的 ViewPitchDeg 实时俯仰旋转，四人联机可见真实光柱朝向；
    /// 3) 服务端 CmdToggleLight 校验手电筒所有权与倒地状态，并提供 ServerForceLightOff()。
    /// </summary>
    public sealed class PlayerEquipment : NetworkBehaviour
    {
        [SyncVar] private bool lightOn;

        private Light spot;
        private PlayerMotor motor;
        private PlayerVitals vitals;
        private bool lastHadFlashlight;

        public bool LightOn => lightOn;

        public bool SpotActive => spot != null && spot.enabled && spot.gameObject.activeInHierarchy;

        private void Awake()
        {
            motor = GetComponent<PlayerMotor>();
            vitals = GetComponent<PlayerVitals>();
        }

        private void Update()
        {
            // 修复 NET-01：无论是本地玩家还是远端队友，都必须实例化并同步聚光灯组件
            EnsureSpot();

            if (!isLocalPlayer)
            {
                return;
            }

            SyncFlashlightOwnership();

            if (!EquippedEffects.HasFlashlight || (vitals != null && vitals.IsDowned))
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
            if (on && (!EquippedEffects.HasFlashlight || (vitals != null && vitals.IsDowned)))
            {
                lightOn = false;
                return;
            }

            lightOn = on;
            Debug.Log($"[S4A] S4A_FLASHLIGHT on={on} netId={netId}");
        }

        [Server]
        public void ServerForceLightOff()
        {
            if (lightOn)
            {
                lightOn = false;
                Debug.Log($"[S4A] S4A_FLASHLIGHT on=False netId={netId} reason=downed");
            }
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

            if (motor != null)
            {
                spot.transform.localRotation = Quaternion.Euler(motor.ViewPitchDeg, 0f, 0f);
            }

            float range = Mathf.Max(8f, EquippedEffects.FlashlightRange);
            if (!Mathf.Approximately(spot.range, range))
            {
                spot.range = range;
            }

            bool isDown = vitals != null && vitals.IsDowned;
            bool want = lightOn && EquippedEffects.HasFlashlight && !isDown;
            if (spot.enabled != want)
            {
                spot.enabled = want;
            }
        }
    }
}
