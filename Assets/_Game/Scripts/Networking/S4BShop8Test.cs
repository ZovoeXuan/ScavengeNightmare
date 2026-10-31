using System.Collections;
using Mirror;
using ScavengeNightmare.Economy;
using ScavengeNightmare.Gameplay;
using ScavengeNightmare.Save;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ScavengeNightmare.Networking
{
    /// <summary>
    /// S4C-1 商店 8/8 验收（单人 Host）。
    ///
    /// 判据不是「买了 8 件」，而是**每件都能读出效果**：
    /// 4 件 4A 商品看 <see cref="EquippedEffects"/> 的倍率，2 件消耗品看库存，
    /// 工具带看腰带槽 1→2，体能训练看冲刺容量 5.0→7.5。
    /// 只看余额变少 = 假购买（P0 §2.5）。
    ///
    /// 商店只在安全屋开张（S4C-3 / T12），所以本用例必须先换关再买。
    /// </summary>
    public sealed class S4BShop8Test : MonoBehaviour
    {
        private const int SeedBalance = 5000;
        private const int TotalPrice = 400 + 500 + 350 + 900 + 120 + 200 + 700 + 1200;

        private int failures;

        private TeamAccount account;
        private PlayerMotor motor;
        private ShopTerminal terminal;
        private BeltSlot belt;
        private PlayerStamina stamina;

        private void Start()
        {
            if (!S4ACli.Has("-s4bshop8"))
            {
                this.enabled = false;
                return;
            }

            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 60;
            ScavengeNightmare.Traits.TraitSettings.ForceNoTraits = true;
            S4AInput.SuppressManual = true;
            Debug.Log("[S4A] S4A_NEUTRAL_OK traits=none feel=" + (S4ACli.Value("-feel=") ?? "default").ToLowerInvariant());

            if (string.IsNullOrEmpty(S4ACli.Value("-savedir=")))
            {
                S4ACli.SaveDirOverride = System.IO.Path.Combine(Application.persistentDataPath, "S4C_test_shop8");
            }

            NetworkManager manager = NetworkManager.singleton;
            if (manager == null)
            {
                Debug.LogError("[S4C] S4C_SHOP8_FAIL reason=no_network_manager");
                Application.Quit(1);
                return;
            }

            StartCoroutine(Watchdog(240));

            try
            {
                manager.StartHost();
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[S4C] S4C_SHOP8_FAIL reason=start_host type={ex.GetType().Name}");
                Application.Quit(1);
                return;
            }

            StartCoroutine(Run());
        }

        private IEnumerator Watchdog(int seconds)
        {
            yield return new WaitForSecondsRealtime(seconds);
            Debug.LogError($"[S4C] S4C_SHOP8_WATCHDOG_TIMEOUT seconds={seconds}");
            Application.Quit(3);
        }

        private void Fail(string reason)
        {
            failures++;
            Debug.LogError($"[S4C] S4C_SHOP8_TEST_FAIL reason={reason}");
        }

        private void Check(bool condition, string reason)
        {
            if (!condition)
            {
                Fail(reason);
            }
        }

        private IEnumerator Run()
        {
            yield return WaitFor(() => NetworkClient.localPlayer != null, 25f, "no_local_player");
            yield return WaitFor(() => TeamAccount.Instance != null, 25f, "no_team_account");
            yield return WaitFor(() => ShopCatalog.Instance != null && ShopCatalog.Instance.Count >= 8, 25f, "catalog_not_8");

            account = TeamAccount.Instance;

            NetworkManager manager = NetworkManager.singleton;
            Debug.Log("[S4C] S4C_SHOP8_SCENE from=Night1 to=Safehouse");
            manager.ServerChangeScene("Safehouse");
            yield return WaitFor(() => SceneManager.GetActiveScene().name == "Safehouse", 25f, "safehouse_not_loaded");
            yield return WaitFor(() => NetworkClient.localPlayer != null, 25f, "no_local_player_after_safehouse");
            yield return WaitFor(() => ShopTerminal.Instance != null, 25f, "no_shop_terminal");
            yield return WaitFor(() => account.PurchaseWindowOpen, 25f, "purchase_window_closed");

            motor = NetworkClient.localPlayer != null ? NetworkClient.localPlayer.GetComponent<PlayerMotor>() : null;
            terminal = ShopTerminal.Instance;
            belt = motor != null ? motor.GetComponent<BeltSlot>() : null;
            stamina = motor != null ? motor.GetComponent<PlayerStamina>() : null;

            if (motor == null || belt == null || stamina == null)
            {
                Fail($"missing_player_parts motor={motor != null} belt={belt != null} stamina={stamina != null}");
                Application.Quit(1);
                yield break;
            }

            int slotsBefore = belt.EffectiveMaxSlots;
            float capacityBefore = stamina.CapacitySeconds;
            Debug.Log($"[S4C] GATE_BELT_SLOTS before={slotsBefore} after=? owned=0");
            Debug.Log($"[S4C] GATE_STAMINA_CAP before={capacityBefore:F1} after=? trained=0");

            Teleport(motor.transform, terminal.transform.position + new Vector3(0f, 1.05f, 1.6f));
            yield return new WaitForSeconds(0.4f);
            Check(terminal.LocalPlayerInside(), "player_not_in_zone");

            account.DepositId("shop8-test-seed", SeedBalance);
            yield return new WaitForSeconds(0.3f);

            long start = account.Balance;
            Debug.Log($"[S4C] S4C_SHOP8_START balance={start} owned={account.OwnedMask} visit={account.VisitPurchases}");
            Check(start == SeedBalance, $"seed_balance expected={SeedBalance} actual={start}");

            for (int hotkey = 1; hotkey <= 8; hotkey++)
            {
                yield return PressBuy(hotkey);
            }

            yield return new WaitForSeconds(0.5f);

            long expected = start - TotalPrice;
            Check(account.Balance == expected, $"balance expected={expected} actual={account.Balance}");

            Check(string.IsNullOrEmpty(account.LastPurchaseId) == false, "no_purchase_recorded");
            Check(account.VisitPurchases == 8, $"visit_purchases expected=8 actual={account.VisitPurchases}");

            // ---- 每件都读出效果 ----
            Check(account.OwnsItem("grip_gloves"), "not_owned_grip_gloves");
            Check(account.OwnsItem("packing_tape"), "not_owned_packing_tape");
            Check(account.OwnsItem("flashlight"), "not_owned_flashlight");
            Check(account.OwnsItem("grip_training"), "not_owned_grip_training");
            Check(account.OwnsItem("tool_belt"), "not_owned_tool_belt");
            Check(account.OwnsItem("stamina_training"), "not_owned_stamina_training");

            Check(EquippedEffects.GrabBreakScale > 1.4f, $"grab_break_scale {EquippedEffects.GrabBreakScale:F2}");
            Check(EquippedEffects.CartSlipScale > 1.9f, $"cart_slip_scale {EquippedEffects.CartSlipScale:F2}");
            Check(EquippedEffects.HasFlashlight, "flashlight_effect_missing");
            Check(EquippedEffects.FlashlightRange >= 17.9f, $"flashlight_range {EquippedEffects.FlashlightRange:F1}");
            Check(EquippedEffects.CarryBoost > 1.1f, $"carry_boost {EquippedEffects.CarryBoost:F2}");

            Check(account.SelfInjectorCharges >= 1, $"self_injector_charges {account.SelfInjectorCharges}");
            Check(account.MedkitCharges >= 1, $"medkit_charges {account.MedkitCharges}");

            int slotsAfter = belt.EffectiveMaxSlots;
            Check(slotsBefore == 1, $"base_slots expected=1 actual={slotsBefore}");
            Check(slotsAfter == 2, $"trained_slots expected=2 actual={slotsAfter}");
            Debug.Log($"[S4C] GATE_BELT_SLOTS before={slotsBefore} after={slotsAfter} owned=1");

            float capacityAfter = stamina.CapacitySeconds;
            Check(Mathf.Abs(capacityBefore - PlayerStamina.BaseCapacitySeconds) < 0.01f,
                $"base_capacity expected={PlayerStamina.BaseCapacitySeconds:F1} actual={capacityBefore:F1}");
            Check(Mathf.Abs(capacityAfter - PlayerStamina.TrainedCapacitySeconds) < 0.01f,
                $"trained_capacity expected={PlayerStamina.TrainedCapacitySeconds:F1} actual={capacityAfter:F1}");
            Debug.Log($"[S4C] GATE_STAMINA_CAP before={capacityBefore:F1} after={capacityAfter:F1} trained=1");

            Debug.Log($"[S4C] S4C_SHOP8_EFFECT grabBreak={EquippedEffects.GrabBreakScale:F2} " +
                      $"cartSlip={EquippedEffects.CartSlipScale:F2} flashlight={EquippedEffects.FlashlightRange:F1} " +
                      $"carryBoost={EquippedEffects.CarryBoost:F2} injector={account.SelfInjectorCharges} " +
                      $"medkit={account.MedkitCharges} slots={slotsAfter} capacity={capacityAfter:F1}");

            // 余额恒等式：购买走负向流水，balance == SumFromTxns() 不能被购物打破。
            P0SaveService save = P0SaveService.Instance;
            long sum = save != null && save.Data != null ? save.Data.SumFromTxns() : -1;
            bool identity = save != null && save.Data != null && save.Data.balance == sum;
            Check(identity, $"balance_identity balance={(save != null && save.Data != null ? save.Data.balance : -1)} sum={sum}");

            bool ok = failures == 0;
            string metrics = $"items=8 durable=6 balanceDelta={start - account.Balance} slots={slotsAfter} " +
                             $"capacity={capacityAfter:F1}";

            if (ok)
            {
                Debug.Log($"[S4B] S4B_SHOP8_OK {metrics} ok=True");
            }
            else
            {
                Debug.LogError($"[S4B] S4B_SHOP8_FAIL {metrics} failures={failures}");
            }

            yield return new WaitForSeconds(0.3f);
            Application.Quit(ok ? 0 : 1);
        }

        private IEnumerator PressBuy(int hotkey)
        {
            S4AInput.AutoBuyIndex = hotkey;
            yield return null;
            S4AInput.AutoBuyIndex = 0;
            yield return new WaitForSeconds(0.4f);
        }

        private static void Teleport(Transform target, Vector3 position)
        {
            NetworkTransformReliable nt = target.GetComponent<NetworkTransformReliable>();
            bool wasEnabled = nt != null && nt.enabled;

            if (wasEnabled)
            {
                nt.enabled = false;
            }

            target.position = position;

            if (wasEnabled)
            {
                nt.enabled = true;
            }
        }

        private IEnumerator WaitFor(System.Func<bool> condition, float seconds, string failReason)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < end)
            {
                if (condition())
                {
                    yield break;
                }

                yield return null;
            }

            Fail(failReason);
        }
    }
}
