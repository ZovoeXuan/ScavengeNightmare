using System.Collections.Generic;
using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ScavengeNightmare.Networking
{
    /// <summary>
    /// D3 玩家移动骨架：新输入系统（WASD + 鼠标视角）+ CharacterController，
    /// 位置与朝向由 NetworkTransformReliable 以 ClientToServer 方向同步。
    /// 只做移动与视角，不含物理抓取（D4）、搬运摩擦（D6）与怪物（D14）。
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
        private float stunUntil;

        // 视角俯仰需要同步给服务端：服务端做抓取/投掷时要知道玩家瞄哪儿
        [SyncVar] private float syncPitch;
        private float nextPitchSendAt;

        /// <summary>同步到服务端的视角俯仰（度，0 = 水平，向下为正）。</summary>
        public float ViewPitchDeg => syncPitch;

        /// <summary>是否处于被砸硬直中。</summary>
        public bool IsStunned => Time.time < stunUntil;

        /// <summary>剩余硬直秒数（自动化验收用）。</summary>
        public float StunRemaining => Mathf.Max(0f, stunUntil - Time.time);

        /// <summary>
        /// D6 手滑砸脚：被重物命中时进入硬直。硬直期间忽略移动与视角输入，但保留重力与摩擦，
        /// 保证只是「顿一下」而不是失控飞天。
        /// </summary>
        public void Stun(float seconds)
        {
            if (seconds <= 0f)
            {
                return;
            }

            stunUntil = Mathf.Max(stunUntil, Time.time + seconds);
        }

        /// <summary>
        /// 自动化验证用输入注入（无键鼠的批处理环境下驱动移动），正式玩法不调用。
        /// </summary>
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
            if (playerCamera == null)
            {
                playerCamera = GetComponentInChildren<Camera>(true);
            }

            // 远程玩家永远不开相机；本地玩家在 OnStartLocalPlayer 里打开。
            if (playerCamera != null)
            {
                playerCamera.gameObject.SetActive(false);
            }
        }

        /// <summary>把数据表的数值覆盖到字段上，避免每帧分支判断。</summary>
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
            if (playerCamera != null)
            {
                playerCamera.gameObject.SetActive(true);
            }

            // 关掉场景里其它相机（观察机位），避免双画面。
            suppressedCameras.Clear();
            foreach (Camera cam in Camera.allCameras)
            {
                if (cam == playerCamera || cam.transform.IsChildOf(transform))
                {
                    continue;
                }

                cam.enabled = false;
                suppressedCameras.Add(cam);

                // 场景相机的 AudioListener 会让「只能有一个监听器」报警，一并静音。
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

            ReadInput();
            ApplyLook();
            ApplyMove();
        }

        private void ReadInput()
        {
            if (IsStunned)
            {
                // 被砸硬直：清空输入，但让重力与速度衰减照常工作。
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
                sprintHeld = automationSprint;
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

            sprintHeld = kb.leftShiftKey.isPressed;
            jumpQueued = kb.spaceKey.wasPressedThisFrame;

            Mouse mouse = Mouse.current;
            lookInput = mouse != null ? mouse.delta.ReadValue() : Vector2.zero;
        }

        private void ApplyLook()
        {
            if (lookInput.sqrMagnitude > 0f)
            {
                // 视角不缩放：鼠标 delta 已经是像素量。
                transform.Rotate(0f, lookInput.x * lookSensitivity, 0f, Space.Self);
                pitch = Mathf.Clamp(pitch - lookInput.y * lookSensitivity, -pitchLimit, pitchLimit);
            }

            if (playerCamera != null)
            {
                playerCamera.transform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
            }

            // 10Hz 上报俯仰，够服务端算准星方向，又不至于每帧刷网络
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

        /// <summary>服务端判定硬直后广播，真正让本地玩家顿住的是这条 Rpc。</summary>
        [ClientRpc]
        public void RpcStun(float seconds)
        {
            if (isLocalPlayer)
            {
                Stun(seconds);
            }
        }

        /// <summary>服务端调用：本端立刻硬直，并通知所属客户端。</summary>
        public void ServerStun(float seconds)
        {
            Stun(seconds);
            RpcStun(seconds);
        }

        private void ApplyMove()
        {
            Vector3 desired = transform.right * moveInput.x + transform.forward * moveInput.y;
            float speed = (sprintHeld ? sprintSpeed : walkSpeed) * carrySpeedMultiplier;
            speed = ScavengeNightmare.Gameplay.FeelSettings.PlayerSpeed(speed);
            planarVelocity = Vector3.MoveTowards(planarVelocity, desired * speed, acceleration * Time.deltaTime);

            if (controller.isGrounded)
            {
                // 贴地时保持一个小的向下速度，避免下坡时反复离地。
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

        /// <summary>供 HUD / 自动化验证读取的当前平面速度（m/s）。</summary>
        public float CurrentPlanarSpeed => planarVelocity.magnitude;

        /// <summary>
        /// 完整速度（含垂直分量），投掷时用于继承玩家动量。
        /// 贴地时不含那个 -2 的「压地速度」，否则每次平a投掷都会被额外往下压。
        /// </summary>
        public Vector3 CurrentVelocity
        {
            get
            {
                bool grounded = controller != null && controller.isGrounded;
                return planarVelocity + Vector3.up * (grounded ? 0f : verticalVelocity);
            }
        }

        /// <summary>负重减速：D1 冻结的「跑会被拖累」，系数由当前手感方案决定。</summary>
        public void SetCarryMass(float kg)
        {
            carrySpeedMultiplier = ScavengeNightmare.Gameplay.FeelSettings.CarryMultiplier(kg);
        }
    }
}
