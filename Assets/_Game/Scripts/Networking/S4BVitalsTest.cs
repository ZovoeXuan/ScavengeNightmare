using System.Collections;
using Mirror;
using ScavengeNightmare.Combat;
using ScavengeNightmare.Economy;
using ScavengeNightmare.Gameplay;
using ScavengeNightmare.Save;
using UnityEngine;

namespace ScavengeNightmare.Networking
{
    /// <summary>
    /// S4B-1 生存链验收：伤害 → 单次保护 → 倒地 → 掉手持物 → 8s 自救。
    /// 单人 Host 进程，不依赖第二台机器。
    /// </summary>
    public sealed class S4BVitalsTest : MonoBehaviour
    {
        private int failures;

        private void Start()
        {
            if (!S4ACli.Has("-s4bvitals"))
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
                S4ACli.SaveDirOverride = System.IO.Path.Combine(Application.persistentDataPath, "S4B_test_vitals");
            }

            NetworkManager manager = NetworkManager.singleton;
            if (manager == null)
            {
                Debug.LogError("[S4B] S4B_VITALS_FAIL reason=no_network_manager");
                Application.Quit(1);
                return;
            }

            StartCoroutine(Watchdog(120));

            try
            {
                manager.StartHost();
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[S4B] S4B_VITALS_FAIL reason=start_host type={ex.GetType().Name}");
                Application.Quit(1);
                return;
            }

            StartCoroutine(Run());
        }

        private IEnumerator Watchdog(int seconds)
        {
            yield return new WaitForSecondsRealtime(seconds);
            Debug.LogError($"[S4B] S4B_VITALS_WATCHDOG_TIMEOUT seconds={seconds}");
            Application.Quit(3);
        }

        private void Fail(string reason)
        {
            failures++;
            Debug.LogError($"[S4B] S4B_VITALS_TEST_FAIL reason={reason}");
        }

        private IEnumerator Run()
        {
            yield return WaitFor(() => NetworkClient.localPlayer != null, 25f, "no_local_player");
            yield return WaitFor(() => TeamAccount.Instance != null, 25f, "no_team_account");
            yield return WaitFor(() => FindObjectsByType<GrabbableProp>(FindObjectsSortMode.None).Length > 0, 8f, "no_props");

            NetworkIdentity local = NetworkClient.localPlayer;
            PlayerVitals vitals = local.GetComponent<PlayerVitals>();
            PlayerMotor motor = local.GetComponent<PlayerMotor>();
            GrabController grab = local.GetComponent<GrabController>();

            if (vitals == null || motor == null || grab == null)
            {
                Fail("missing_components");
                Application.Quit(1);
                yield break;
            }

            GrabbableProp prop = FindObjectsByType<GrabbableProp>(FindObjectsSortMode.None)[0];
            bool grabbed = grab.ServerForceGrab(prop);
            Debug.Log($"[S4B] S4B_VITALS_GRAB prop={prop.Profile?.id} grabbed={grabbed}");
            yield return new WaitForSeconds(0.3f);

            vitals.ServerApplyDamage(50, "test");
            yield return new WaitForSeconds(0.3f);
            Check(vitals.Hp == 50, $"hp_after_50 expected=50 actual={vitals.Hp}");

            vitals.ServerApplyDamage(60, "test");
            yield return new WaitForSeconds(0.3f);
            Check(vitals.Hp == 1 && !vitals.IsDowned, $"protection hp={vitals.Hp} downed={vitals.IsDowned}");

            vitals.ServerApplyDamage(1, "test");
            yield return new WaitForSeconds(0.4f);
            Check(vitals.IsDowned, "not_downed_after_lethal");
            Check(vitals.Hp == PlayerVitals.DownedHp, $"downed_hp expected={PlayerVitals.DownedHp} actual={vitals.Hp}");
            Check(motor.VitalsDowned, "motor_not_downed");
            Check(!grab.IsHolding, "held_item_not_dropped");

            Debug.Log($"[S4B] S4B_VITALS_DOWNED hp={vitals.Hp} downed={vitals.IsDowned} held={grab.IsHolding}");

            yield return new WaitForSeconds(PlayerVitals.SelfReviveDelaySeconds + 0.6f);

            S4BInput.AutoSelfInject = true;
            yield return new WaitForSeconds(1.0f);

            Check(!vitals.IsDowned, "not_revived");
            Check(vitals.Hp == PlayerVitals.ReviveHp, $"revive_hp expected={PlayerVitals.ReviveHp} actual={vitals.Hp}");
            Check(vitals.FreeInjectors == 0, $"free_injector expected=0 actual={vitals.FreeInjectors}");

            // ---- 已购自救针：商店买 1 支，用掉它，而不是继续走免费基线 ----
            TeamAccount account = TeamAccount.Instance;
            account.DepositId("vitals-shop-seed", 300);
            yield return new WaitForSeconds(0.3f);

            bool bought = account.TryPurchase("self_injector");
            Debug.Log($"[S4B] S4B_VITALS_BUY item=self_injector ok={bought} charges={account.SelfInjectorCharges} balance={account.Balance}");
            Check(bought && account.SelfInjectorCharges == 1, "self_injector_purchase");

            // 起身后有 2s 无敌，必须等过去再打，否则会命中 GATE_DAMAGE_REJECT reason=invulnerable。
            yield return new WaitForSeconds(2.2f);

            vitals.ServerSetFreeInjectors(0);
            vitals.ServerApplyDamage(50, "test");
            yield return new WaitForSeconds(0.3f);
            vitals.ServerApplyDamage(1, "test");
            yield return new WaitForSeconds(0.4f);
            Check(vitals.IsDowned, "not_downed_second");

            yield return new WaitForSeconds(PlayerVitals.SelfReviveDelaySeconds + 0.6f);
            S4BInput.AutoSelfInject = true;
            yield return new WaitForSeconds(1.0f);

            Check(!vitals.IsDowned, "not_revived_with_bought");
            Check(account.SelfInjectorCharges == 0, $"bought_injector_not_consumed charges={account.SelfInjectorCharges}");

            bool ok = failures == 0;

            if (ok)
            {
                Debug.Log($"[S4B] S4B_VITALS_OK hp={vitals.Hp} downed=1 revived=1 free={vitals.FreeInjectors} failures=0 ok=True");
            }
            else
            {
                Debug.LogError($"[S4B] S4B_VITALS_FAIL failures={failures}");
            }

            yield return new WaitForSeconds(0.3f);
            Application.Quit(ok ? 0 : 1);
        }

        private void Check(bool condition, string reason)
        {
            if (!condition)
            {
                Fail(reason);
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
