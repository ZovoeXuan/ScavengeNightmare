using System.Collections.Generic;
using Mirror;
using ScavengeNightmare.Cart;
using ScavengeNightmare.Gameplay;
using ScavengeNightmare.Networking;
using UnityEngine;

namespace ScavengeNightmare.Economy
{
    /// <summary>
    /// 4A 公秤：玩家抱着货站在停车位里，长按 E 1.2s 提交手持货物。
    /// 服务端权威，客户端只发交互状态，不参与入账。
    ///
    /// 交互状态必须来自 PlayerInteract（客户端上报），不能读服务端本地输入。
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public sealed class QuotaTerminal : NetworkBehaviour
    {
        public static QuotaTerminal Instance { get; private set; }

        private const float HoldSeconds = 1.2f;
        private const float PlayerCacheSeconds = 0.5f;

        private Collider zone;
        private readonly Dictionary<PlayerMotor, float> hold = new();

        private PlayerMotor[] playerCache;
        private float nextPlayerRefresh = -1f;
        private bool warnedNoInteract;

        private void Awake()
        {
            Instance = this;
            zone = GetComponent<Collider>();
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void Update()
        {
            if (!isServer || zone == null)
            {
                return;
            }

            TeamAccount account = TeamAccount.Instance;
            if (account == null)
            {
                return;
            }

            foreach (PlayerMotor motor in Players())
            {
                if (motor == null || !zone.bounds.Contains(motor.transform.position))
                {
                    continue;
                }

                GrabController grab = motor.GetComponent<GrabController>();
                bool holding = grab != null && grab.IsHolding;

                // 整车提交（P0 无挑件）：手里空着、车停在停车位且有货，长按同一个 1.2s 即可提交。
                // 判据放在服务端，且必须同时满足「车在停车位」，避免玩家站在公秤上随便把远处的车交了。
                CartController cart = CartController.Instance;
                bool cartReady = !holding && cart != null && cart.HasCargo && cart.IsParked;

                if (!holding && !cartReady)
                {
                    hold[motor] = 0f;
                    continue;
                }

                PlayerInteract interact = motor.GetComponent<PlayerInteract>();
                if (interact == null)
                {
                    if (!warnedNoInteract)
                    {
                        warnedNoInteract = true;
                        Debug.LogError("[S4A] S4A_TERMINAL_FAIL reason=player_missing_player_interact");
                    }

                    hold[motor] = 0f;
                    continue;
                }

                if (!interact.InteractHeldServer)
                {
                    hold[motor] = 0f;
                    continue;
                }

                float t = hold.TryGetValue(motor, out float current) ? current : 0f;
                t += Time.unscaledDeltaTime;
                hold[motor] = t;

                if (t >= HoldSeconds)
                {
                    hold[motor] = 0f;

                    if (holding)
                    {
                        account.Deposit(grab.HeldProp);
                    }
                    else if (cart != null)
                    {
                        SubmitCart(cart, account);
                    }
                }
            }
        }

        private static void SubmitCart(CartController cart, TeamAccount account)
        {
            List<GrabbableProp> items = cart.UnloadAll();
            int sum = account.DepositAll(items);
            Debug.Log($"[S4A] S4A_CART_SUBMIT_DONE requested={items.Count} accepted_sum={sum} balance={account.Balance}");
        }

        /// <summary>
        /// 不用 OnTriggerEnter：玩家是 CharacterController，没有 Rigidbody，触发器事件不稳定
        /// （实测 inside 一直是空的）。改成按碰撞盒判断，结果可断言。
        /// 但 FindObjectsByType 每帧会分配整表，所以缓存 0.5s 并顺手清理已销毁的玩家。
        /// </summary>
        private PlayerMotor[] Players()
        {
            if (playerCache != null && Time.unscaledTime < nextPlayerRefresh)
            {
                return playerCache;
            }

            nextPlayerRefresh = Time.unscaledTime + PlayerCacheSeconds;
            playerCache = FindObjectsByType<PlayerMotor>(FindObjectsSortMode.None);

            if (hold.Count > 0)
            {
                List<PlayerMotor> dead = null;
                foreach (PlayerMotor key in hold.Keys)
                {
                    if (key == null)
                    {
                        (dead ??= new List<PlayerMotor>()).Add(key);
                    }
                }

                if (dead != null)
                {
                    foreach (PlayerMotor key in dead)
                    {
                        hold.Remove(key);
                    }
                }
            }

            return playerCache;
        }

        /// <summary>供 HUD 显示当前长按进度。</summary>
        public float HoldRatioFor(PlayerMotor motor)
        {
            if (motor == null || !hold.TryGetValue(motor, out float t))
            {
                return 0f;
            }

            return Mathf.Clamp01(t / HoldSeconds);
        }
    }
}
