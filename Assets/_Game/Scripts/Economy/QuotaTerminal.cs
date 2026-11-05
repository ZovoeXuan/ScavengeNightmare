using System.Collections.Generic;
using Mirror;
using ScavengeNightmare.Cart;
using ScavengeNightmare.Gameplay;
using ScavengeNightmare.Networking;
using ScavengeNightmare.Save;
using UnityEngine;

namespace ScavengeNightmare.Economy
{
    /// <summary>
    /// 公秤提交终端 V2 升级版：
    /// 1) 修复玩家走出公秤区域或倒地时未归零 hold[motor] 导致下次进区瞬间提交的 Bug (ECO-03)；
    /// 2) 修复 SubmitCart 在校验存档可写状态前就调用 cart.UnloadAll() 导致写盘失败时车货脱离的 Bug。
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
                if (motor == null)
                {
                    continue;
                }

                if (motor.VitalsDowned || !zone.bounds.Contains(motor.transform.position))
                {
                    hold[motor] = 0f;
                    continue;
                }

                GrabController grab = motor.GetComponent<GrabController>();
                bool holding = grab != null && grab.IsHolding;

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
            P0SaveService save = P0SaveService.Instance;
            if (save != null && (!save.Loaded || save.State == SaveState.Locked))
            {
                Debug.LogError($"[S4A] S4A_SAVE_REJECT cargoId=cart value=0 reason=save_unavailable state={save.State}");
                return;
            }

            List<GrabbableProp> items = cart.UnloadAll();
            int sum = account.DepositAll(items);
            Debug.Log($"[S4A] S4A_CART_SUBMIT_DONE requested={items.Count} accepted_sum={sum} balance={account.Balance}");
        }

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
