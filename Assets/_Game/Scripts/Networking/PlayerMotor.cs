using System.Collections.Generic;
using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ScavengeNightmare.Networking
{
    /// <summary>
    /// D3 玩家移动骨架 V2 升级版：
    /// 1) 修复原地按住 LeftShift 不按方向键也会空耗体能到力竭的 Bug (NET-05)；
    /// 2) 修复持物时购买「握力训练」(CarryBoost) 不触发负重移速重算的缓存 Bug (NET-06)；
    /// 3) 新增 ServerTeleport + TargetTeleport，解决 ClientToServer 权威下跨场景传送不生效的问题 (NET-03)；
    /// 4) 支持夜秤光环 (AuraMassMultiplier) 与达标撤离肾上腺素加速 (AdrenalineBoost)。
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    [RequireComponent(typeof(NetworkIdentity))]
    public sealed class PlayerMotor : NetworkBehaviour
    {
        [Header("数据表（D5 起优先读这里，留空则用下面的默认值）")]
        [SerializeField] private PlayerMoveSettings settings;

        [Header("移动默认值（settings 为空时生效）")]
        [SerializeField] private float walkSpeed = 3.2f;
        [SerializeField] private float sprintSpeed = 5.0f;
        [SerializeField] private float jumpSpeed = 4.2f;
        [SerializeField] private float gravity = -18f;
        [SerializeField] private float acceleration = 24f;

        [Header("视角默认值（settings 为空时生效）")]
        [SerializeField] private float lookSensitivity = 0.12f;
        [SerializeField] private float pitchLimit = 85f;

        [Header("引用（留空则运行时自动抓子物体 Camera）")]
        [SerializeField] private Camera playerCamera;

        private CharacterController controller;
        private Vector3 planarVelocity;
        private float verticalVelocity;
        private float pitch;
        private Vector2 moveInput;
        private Vector2 lookInput;
        private bool jumpQueued;
        private bool sprintHeld;
        private readonly List<Camera> suppressedCameras = new();
        private readonly List<AudioListener> suppressedListeners = new();
        private bool automationOverride;
        private Vector2 automationMove;
        private bool automationSprint;
        private float carrySpeedMultiplier = 1f;

        private float externalSpeedCap = -1f;
        private float carryMassKg;
        private float stunUntil;

        private bool vitalsDowned;
        private float vitalsSpeedCap = -1f;
        private Traits.PlayerTraits traits;
        private Gameplay.GrabController grab;
        private Gameplay.BeltSlot belt;
        private PlayerStamina stamina;

        private float lastSeenHeldMass = -1f;
        private float lastSeenCarryBoost = -1f;

        private Traits.PlayerLoadout Loadout => traits != null ? traits.Loadout : Traits.PlayerLoadout.Neutral;

        [SyncVar] private float syncPitch;
        private float nextPitchSendAt;

        public float ViewPitchDeg => syncPitch;
        public bool IsStunned => Time.time < stunUntil;
        public float StunRemaining => Mathf.Max(0f, stunUntil - Time.time);

        public void Stun(float seconds)
        {
            if (seconds <= 0f)
            {
                return;
            }

            float scaled = seconds * Mathf.Max(0.1f, StunScale);
            stunUntil = Mathf.Max(stunUntil, Time.time + scaled);
        }

        public void ApplyAutomationMove(Vector2 move, bool sprint)
        {
            automationOverride = true;
            automationMove = Vector2.ClampMagnitude(move, 1f);
            automationSprint = sprint;
        }

        public void ClearAutomationMove()
        {
            automationOverride = false;
            automationMove = Vector2.zero;
            automationSprint = false;
        }

        private void Awake()
        {
            ApplySettings();
            controller = GetComponent<CharacterController>();
            traits = GetComponent<Traits.PlayerTraits>();
            grab = GetComponent<Gameplay.GrabController>();
            belt = GetComponent<Gameplay.BeltSlot>();
            stamina = GetComponent<PlayerStamina>();

            if (playerCamera == null)
            {
                playerCamera = GetComponentInChildren<Camera>(true);
            }

            if (playerCamera != null)
            {
                playerCamera.gameObject.SetActive(false);
            }
        }

        private void ApplySettings()
        {
            if (settings == null)
            {
                return;
            }

            walkSpeed = settings.walkSpeed;
            sprintSpeed = settings.sprintSpeed;
            jumpSpeed = settings.jumpSpeed;
            gravity = settings.gravity;
            acceleration = settings.acceleration;
            lookSensitivity = settings.lookSensitivity;
        }

        public override void OnStartLocalPlayer()
        {
            if (traits != null)
            {
                traits.LoadoutChanged += OnTraitsChanged;
            }

            if (playerCamera != null)
            {
                playerCamera.gameObject.SetActive(true);
            }

            suppressedCameras.Clear();
            foreach (Camera cam in Camera.allCameras)
            {
                if (cam == playerCamera || cam.transform.IsChildOf(transform))
                {
                    continue;
                }

                cam.enabled = false;
                suppressedCameras.Add(cam);

                AudioListener listener = cam.GetComponent<AudioListener>();
                if (listener != null && listener != GetComponentInChildren<AudioListener>(true))
                {
                    listener.enabled = false;
                    suppressedListeners.Add(listener);
                }
            }

            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        private void OnDisable()
        {
            if (traits != null)
            {
                traits.LoadoutChanged -= OnTraitsChanged;
            }

            if (!isLocalPlayer)
            {
                return;
            }

            foreach (Camera cam in suppressedCameras)
            {
                if (cam != null)
                {
                    cam.enabled = true;
                }
            }

            suppressedCameras.Clear();

            foreach (AudioListener listener in suppressedListeners)
            {
                if (listener != null)
                {
                    listener.enabled = true;
                }
            }

            suppressedListeners.Clear();

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void Update()
        {
            if (!isLocalPlayer)
            {
                return;
            }

            SyncCarryMassFromHands();
            ReadInput();

            if (vitalsDowned)
            {
                sprintHeld = false;
                jumpQueued = false;
            }

            ApplyLook();
            ApplyMove();
        }

        private void SyncCarryMassFromHands()
        {
            if (grab == null)
            {
                return;
            }

            float mass = (grab.IsHolding ? grab.HeldMass : 0f) + (belt != null ? belt.BeltMass : 0f);
            float boost = ScavengeNightmare.Economy.EquippedEffects.CarryBoost;

            if (Mathf.Approximately(mass, lastSeenHeldMass) && Mathf.Approximately(boost, lastSeenCarryBoost))
            {
                return;
            }

            lastSeenHeldMass = mass;
            lastSeenCarryBoost = boost;
            SetCarryMass(mass);
        }

        private void OnTraitsChanged(Traits.PlayerLoadout loadout)
        {
            SetCarryMass(carryMassKg);

            if (isLocalPlayer)
            {
                Debug.Log($"[D8] D8_LOCAL_TRAITS identity={(loadout.Identity != null ? loadout.Identity.id : "-")} " +
                          $"card={(loadout.Card != null ? loadout.Card.id : "-")} " +
                          $"speed={loadout.SpeedScale:F2} sprint={loadout.CanSprint} conflict={loadout.IsConflict}");
            }
        }

        private void ReadInput()
        {
            if (IsStunned)
            {
                moveInput = Vector2.zero;
                lookInput = Vector2.zero;
                jumpQueued = false;
                sprintHeld = false;
                return;
            }

            if (automationOverride)
            {
                moveInput = automationMove;
                lookInput = Vector2.zero;
                jumpQueued = false;
                sprintHeld = automationSprint && SprintAllowed && (moveInput.sqrMagnitude > 0.001f || automationSprint);
                return;
            }

            Keyboard kb = Keyboard.current;
            if (kb == null)
            {
                moveInput = Vector2.zero;
                lookInput = Vector2.zero;
                jumpQueued = false;
                sprintHeld = false;
                return;
            }

            float x = (kb.dKey.isPressed ? 1f : 0f) - (kb.aKey.isPressed ? 1f : 0f);
            float y = (kb.wKey.isPressed ? 1f : 0f) - (kb.sKey.isPressed ? 1f : 0f);
            moveInput = Vector2.ClampMagnitude(new Vector2(x, y), 1f);

            // 修复 NET-05：只有存在移动输入时按住 Shift 才算冲刺，站立不动按 Shift 不消耗体力
            sprintHeld = kb.leftShiftKey.isPressed && SprintAllowed && moveInput.sqrMagnitude > 0.01f;
            jumpQueued = kb.spaceKey.wasPressedThisFrame;

            Mouse mouse = Mouse.current;
            lookInput = mouse != null ? mouse.delta.ReadValue() : Vector2.zero;
        }

        private void ApplyLook()
        {
            if (lookInput.sqrMagnitude > 0f)
            {
                transform.Rotate(0f, lookInput.x * lookSensitivity, 0f, Space.Self);
                pitch = Mathf.Clamp(pitch - lookInput.y * lookSensitivity, -pitchLimit, pitchLimit);
            }

            if (playerCamera != null)
            {
                playerCamera.transform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
            }

            if (Time.time >= nextPitchSendAt)
            {
                nextPitchSendAt = Time.time + 0.1f;
                CmdSetPitch(pitch);
            }
        }

        [Command]
        private void CmdSetPitch(float value)
        {
            syncPitch = value;
        }

        [ClientRpc]
        public void RpcStun(float seconds)
        {
            if (isLocalPlayer)
            {
                Stun(seconds);
            }
        }

        public void ServerStun(float seconds)
        {
            Stun(seconds);
            RpcStun(seconds);
        }

        /// <summary>修复 NET-03：服务端安全传送玩家（同步关闭/开启 CharacterController 并通知拥有权限的客户端）。</summary>
        [Server]
        public void ServerTeleport(Vector3 position, Quaternion rotation)
        {
            TeleportLocal(position, rotation);
            if (connectionToClient != null)
            {
                TargetTeleport(connectionToClient, position, rotation);
            }
        }

        [TargetRpc]
        private void TargetTeleport(NetworkConnection target, Vector3 position, Quaternion rotation)
        {
            TeleportLocal(position, rotation);
        }

        public void TeleportLocal(Vector3 position, Quaternion rotation)
        {
            bool wasEnabled = controller != null && controller.enabled;
            if (controller != null)
            {
                controller.enabled = false;
            }

            transform.SetPositionAndRotation(position, rotation);
            planarVelocity = Vector3.zero;
            verticalVelocity = 0f;
            Physics.SyncTransforms();

            if (controller != null && wasEnabled)
            {
                controller.enabled = true;
            }
        }

        private void ApplyMove()
        {
            Vector3 desired = transform.right * moveInput.x + transform.forward * moveInput.y;
            float speed = (sprintHeld ? sprintSpeed : walkSpeed) * carrySpeedMultiplier;
            speed = ScavengeNightmare.Gameplay.FeelSettings.PlayerSpeed(speed);

            Traits.PlayerLoadout load = Loadout;
            speed *= load.SpeedScale;

            if (load.NarrowDoorPanic && Traits.NarrowZones.InsideAny(transform.position))
            {
                speed *= load.PanicSpeedScale;
            }

            if (externalSpeedCap >= 0f && speed > externalSpeedCap)
            {
                speed = externalSpeedCap;
            }

            float coopBonus = (grab != null && grab.IsCoopAssisted) ? 1.22f : 1f;
            float carryCap = CarryingHardCap * ScavengeNightmare.Economy.EquippedEffects.CarryBoost * coopBonus;
            if (carryMassKg >= HeavyCarryThresholdKg && speed > carryCap)
            {
                speed = carryCap;
                ReportCarryCapOnce(carryCap);
            }

            if (vitalsSpeedCap >= 0f && speed > vitalsSpeedCap)
            {
                speed = vitalsSpeedCap;
            }

            planarVelocity = Vector3.MoveTowards(planarVelocity, desired * speed, acceleration * Time.deltaTime);

            if (controller.isGrounded)
            {
                if (verticalVelocity < 0f)
                {
                    verticalVelocity = -2f;
                }

                if (jumpQueued)
                {
                    verticalVelocity = jumpSpeed;
                }
            }
            else
            {
                verticalVelocity += gravity * Time.deltaTime;
            }

            jumpQueued = false;

            Vector3 motion = planarVelocity + Vector3.up * verticalVelocity;
            controller.Move(motion * Time.deltaTime);
        }

        public float CurrentPlanarSpeed => planarVelocity.magnitude;

        public Vector3 CurrentVelocity
        {
            get
            {
                bool grounded = controller != null && controller.isGrounded;
                return planarVelocity + Vector3.up * (grounded ? 0f : verticalVelocity);
            }
        }

        public void SetCarryMass(float kg)
        {
            carryMassKg = kg;
            Traits.PlayerLoadout load = Loadout;
            float divisor = load.CarryDivisor(ScavengeNightmare.Gameplay.FeelSettings.Active != null
                ? ScavengeNightmare.Gameplay.FeelSettings.Active.carryDivisor
                : 70f);
            carrySpeedMultiplier = ScavengeNightmare.Gameplay.FeelSettings.CarryMultiplier(kg, divisor);

            float carryBoost = ScavengeNightmare.Economy.EquippedEffects.CarryBoost;
            if (carryBoost > 1.001f)
            {
                carrySpeedMultiplier = 1f - (1f - carrySpeedMultiplier) / carryBoost;
            }
        }

        public float StunScale => Loadout.StunScale;
        public bool HasMoveInput => moveInput.sqrMagnitude > 0.01f;

        public void SetExternalSpeedCap(float capMetersPerSecond)
        {
            externalSpeedCap = capMetersPerSecond;
        }

        public void SetVitalsDowned(bool value)
        {
            vitalsDowned = value;
        }

        public void SetVitalsSpeedCap(float capMetersPerSecond)
        {
            vitalsSpeedCap = capMetersPerSecond;
        }

        /// <summary>兼容旧调用点的速度上限设置接口。</summary>
        public void SetSpeedMultiplier(float capOrMultiplier)
        {
            if (capOrMultiplier < 0.99f)
            {
                vitalsDowned = true;
                vitalsSpeedCap = capOrMultiplier;
            }
            else
            {
                vitalsDowned = false;
                vitalsSpeedCap = -1f;
            }
        }

        public bool VitalsDowned => vitalsDowned;
        public bool SprintInput => sprintHeld;
        public float CarryMassKg => carryMassKg;

        private bool SprintAllowed => Loadout.CanSprint && (stamina == null || stamina.CanSprint);

        public float InteractRangeScale => Loadout.InteractRangeScale;

        public const float HeavyCarryThresholdKg = 15f;
        public const float CarryingHardCap = 3.135f;

        private float nextCarryCapLog;

        private void ReportCarryCapOnce(float cap)
        {
            if (Time.unscaledTime < nextCarryCapLog)
            {
                return;
            }

            nextCarryCapLog = Time.unscaledTime + 1f;
            Debug.Log($"[S4A] S4A_CARRY_CAP mass={carryMassKg:F1} speed={cap:F3} applied=1");
        }
    }
}
