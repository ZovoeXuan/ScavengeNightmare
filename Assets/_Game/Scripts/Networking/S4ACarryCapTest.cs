using System.Collections;
using Mirror;
using ScavengeNightmare.Economy;
using ScavengeNightmare.Gameplay;
using ScavengeNightmare.Save;
using UnityEngine;

namespace ScavengeNightmare.Networking
{
    /// <summary>
    /// Story 4 · 商品效果接线验收（负重上限 + 握力训练 + 手电筒）。
    ///
    /// 测法是**同一件 42kg 货、同一个场景、只换「有没有买握力训练」**，
    /// 用前后速度比证明效果真的接上了——只断言「买了之后能跑」证明不了任何事。
    /// 速度取稳态平均值（丢掉起步 1 秒），单帧采样会被加速度段污染。
    /// </summary>
    public sealed class S4ACarryCapTest : MonoBehaviour
    {
        private const float HeavyMass = 42f;
        private const float SpeedCapMetersPerSecond = 3.135f;

        private int failures;
        private PlayerMotor motor;
        private GrabController grab;
        private TeamAccount account;
        private PlayerEquipment equipment;

        private void Start()
        {
            if (!S4ACli.Has("-s4acarrycap"))
            {
                this.enabled = false;
                return;
            }

            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 60;
            ScavengeNightmare.Traits.TraitSettings.ForceNoTraits = true;
            ScavengeNightmare.Economy.S4AInput.SuppressManual = true;
            Debug.Log("[S4A] S4A_NEUTRAL_OK traits=none feel=light");

            if (string.IsNullOrEmpty(S4ACli.Value("-savedir=")))
            {
                S4ACli.SaveDirOverride = System.IO.Path.Combine(Application.persistentDataPath, "S4A_test_carrycap");
            }

            NetworkManager manager = NetworkManager.singleton;
            if (manager == null)
            {
                Fail("no_network_manager");
                Application.Quit(1);
                return;
            }

            StartCoroutine(Watchdog(180));

            try
            {
                manager.StartHost();
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[S4A] S4A_CARRY_CAP_FAIL reason=start_host type={ex.GetType().Name}");
                Application.Quit(1);
                return;
            }

            StartCoroutine(Run());
        }

        private void Fail(string reason)
        {
            failures++;
            Debug.LogError($"[S4A] S4A_CARRY_CAP_TEST_FAIL reason={reason}");
        }

        private IEnumerator Watchdog(int seconds)
        {
            yield return new WaitForSecondsRealtime(seconds);
            Debug.LogError($"[S4A] S4A_CARRY_CAP_WATCHDOG_TIMEOUT seconds={seconds}");
            Application.Quit(3);
        }

        private IEnumerator Run()
        {
            yield return WaitFor(() => NetworkClient.localPlayer != null, 20f, "no_local_player");
            yield return WaitFor(() => TeamAccount.Instance != null, 20f, "no_team_account");
            yield return WaitFor(() => FindObjectsByType<GrabbableProp>(FindObjectsSortMode.None).Length > 0, 20f, "no_props");

            motor = NetworkClient.localPlayer.GetComponent<PlayerMotor>();
            grab = NetworkClient.localPlayer.GetComponent<GrabController>();
            equipment = NetworkClient.localPlayer.GetComponent<PlayerEquipment>();
            account = TeamAccount.Instance;

            if (equipment == null)
            {
                Fail("no_player_equipment_component");
            }

            // 起步资金：两件商品 900 + 350 = 1250，给 2000 留余量
            account.DepositId("carrycap-seed", 2000);
            yield return new WaitForSeconds(0.3f);

            GrabbableProp heavy = null;
            GrabbableProp light = null;

            foreach (GrabbableProp prop in FindObjectsByType<GrabbableProp>(FindObjectsSortMode.None))
            {
                if (prop == null)
                {
                    continue;
                }

                if (heavy == null && Mathf.Abs(prop.Mass - HeavyMass) <= 6f)
                {
                    heavy = prop;
                }
                else if (light == null && prop.Mass >= 9f && prop.Mass <= 12f)
                {
                    // 轻货用来测「握力训练」的加成：它不会撞上负重硬上限，
                    // 用 42kg 测加成会被上限一起钳住，比值恒等于 1，测不出东西。
                    light = prop;
                }
            }

            if (heavy == null || light == null)
            {
                Fail($"missing_props heavy={(heavy != null)} light={(light != null)}");
                Application.Quit(1);
                yield break;
            }

            // 空旷地带跑直线：z=-9 → -12 是后墙前的空场，不会被货架挡住
            Teleport(motor.transform, new Vector3(0f, 1.05f, -9f));
            motor.transform.LookAt(new Vector3(0f, 1.05f, -13f));
            yield return new WaitForSeconds(0.4f);

            grab.SetInputEnabled(false);

            float baseHeavy = 0f;
            yield return MeasureWith(heavy, value => baseHeavy = value);

            float baseLight = 0f;
            yield return MeasureWith(light, value => baseLight = value);

            Debug.Log($"[S4A] S4A_CARRY_CAP baseline heavy={heavy.Mass:F1}@{baseHeavy:F3} light={light.Mass:F1}@{baseLight:F3} cap={SpeedCapMetersPerSecond:F3}");

            // 42kg 必须先被上限钳住：没买训练就已经超了，这正是上限存在的理由
            if (baseHeavy > SpeedCapMetersPerSecond + 0.02f)
            {
                Fail($"baseline_over_cap speed={baseHeavy:F3} cap={SpeedCapMetersPerSecond:F3}");
            }

            if (baseHeavy <= 0.05f || baseLight <= 0.05f)
            {
                Fail($"baseline_speed_zero heavy={baseHeavy:F3} light={baseLight:F3}");
            }

            // ---- 买握力训练（走真实七步购买链路）----
            bool boughtTraining = account.TryPurchase("grip_training");
            yield return new WaitForSeconds(0.4f);
            Debug.Log($"[S4A] S4A_CARRY_CAP_BUY item=grip_training ok={boughtTraining} balance={account.Balance}");

            if (!boughtTraining)
            {
                Fail("buy_grip_training_failed");
                Application.Quit(1);
                yield break;
            }

            float boostLight = 0f;
            yield return MeasureWith(light, value => boostLight = value);

            float boostHeavy = 0f;
            yield return MeasureWith(heavy, value => boostHeavy = value);

            float lightRatio = baseLight > 0.001f ? boostLight / baseLight : 0f;
            float heavyRatio = baseHeavy > 0.001f ? boostHeavy / baseHeavy : 0f;

            Debug.Log($"[S4A] S4A_CARRY_CAP boosted light={boostLight:F3} lightRatio={lightRatio:F3} heavy={boostHeavy:F3} heavyRatio={heavyRatio:F3} cap={SpeedCapMetersPerSecond:F3}");

            // 断言放在 42kg 上：握力训练抬的是「负重硬上限」，所以重货那一档才是它的正脸。
            // 轻货（10kg）本来减速就很小，倍率只影响 1~2%，在那里卡 15% 是数学上不可能的。
            if (heavyRatio < 1.12f || heavyRatio > 1.18f)
            {
                Fail($"carry_boost_out_of_range ratio={heavyRatio:F3} expect=[1.12,1.18]");
            }

            if (lightRatio < 0.99f)
            {
                Fail($"light_load_regressed ratio={lightRatio:F3}");
            }

            float expectedCap = SpeedCapMetersPerSecond * 1.15f;
            if (boostHeavy > expectedCap + 0.05f)
            {
                Fail($"boosted_over_raised_cap speed={boostHeavy:F3} cap={expectedCap:F3}");
            }

            yield return new WaitForSeconds(0.2f);

            // ---- 手电筒：买 → F 开 → 灯真的亮 → F 关 ----
            bool boughtLamp = account.TryPurchase("flashlight");
            yield return new WaitForSeconds(0.3f);

            if (!boughtLamp)
            {
                Fail("buy_flashlight_failed");
            }
            else
            {
                S4AInput.AutoToggleLight = true;
                yield return new WaitForSeconds(0.5f);

                bool on = equipment != null && equipment.LightOn && equipment.SpotActive;
                Debug.Log($"[S4A] S4A_CARRY_CAP_LIGHT step=on lightOn={(equipment != null && equipment.LightOn)} spot={equipment != null && equipment.SpotActive}");

                if (!on)
                {
                    Fail("flashlight_not_on");
                }

                S4AInput.AutoToggleLight = true;
                yield return new WaitForSeconds(0.5f);

                bool off = equipment != null && !equipment.LightOn;
                Debug.Log($"[S4A] S4A_CARRY_CAP_LIGHT step=off lightOn={(equipment != null && equipment.LightOn)}");

                if (!off)
                {
                    Fail("flashlight_not_off");
                }
            }

            bool clean = failures == 0;
            Debug.Log($"[S4A] S4A_CARRY_CAP_OK base={baseLight:F3} boosted={boostLight:F3} lightRatio={lightRatio:F3} heavyBase={baseHeavy:F3} heavyBoost={boostHeavy:F3} cap={SpeedCapMetersPerSecond:F3} flashlight={(boughtLamp ? 1 : 0)} ok={clean}");

            if (!clean)
            {
                Debug.LogError($"[S4A] S4A_CARRY_CAP_FAIL failures={failures}");
            }

            yield return new WaitForSeconds(0.3f);
            Application.Quit(clean ? 0 : 1);
        }

        /// <summary>同一件货、同一条直线、同一个起点：抓 → 测稳态速度 → 放下。</summary>
        private IEnumerator MeasureWith(GrabbableProp prop, System.Action<float> report)
        {
            Teleport(motor.transform, new Vector3(0f, 1.05f, -9f));
            motor.transform.LookAt(new Vector3(0f, 1.05f, -13f));
            yield return new WaitForSeconds(0.3f);

            yield return GrabAndHold(prop);

            if (!grab.IsHolding)
            {
                Fail($"grab_failed id={(prop.Profile != null ? prop.Profile.id : "?")}");
                report(0f);
                yield break;
            }

            float speed = 0f;
            yield return MeasureSpeed(value => speed = value);

            grab.Release(false);
            yield return new WaitForSeconds(0.35f);
            report(speed);
        }

        /// <summary>把重货摆到身前并抓住。抓取后必须等它稳住再测速，否则测的是加速度段。</summary>
        private IEnumerator GrabAndHold(GrabbableProp prop)
        {
            float holdDist = FeelSettings.HoldDistance(prop.Profile.holdDistance);
            Vector3 holdPos = motor.transform.position + motor.transform.forward * holdDist + new Vector3(0f, 0.45f, 0f);
            Vector3 comOffset = prop.Body != null ? prop.Body.worldCenterOfMass - prop.transform.position : Vector3.zero;
            Teleport(prop.transform, holdPos - comOffset);

            if (prop.Body != null)
            {
                prop.Body.velocity = Vector3.zero;
                prop.Body.angularVelocity = Vector3.zero;
            }

            Physics.SyncTransforms();

            grab.TryGrab(prop);
            yield return null;
            yield return WaitFor(() => grab.IsHolding, 3f, "grab_timeout");
            yield return new WaitForSeconds(0.5f);
        }

        /// <summary>稳态速度：丢掉起步 1 秒，再取 1.2 秒平均。</summary>
        private IEnumerator MeasureSpeed(System.Action<float> report)
        {
            motor.ApplyAutomationMove(new Vector2(0f, 1f), true);
            yield return new WaitForSeconds(1f);

            float sum = 0f;
            int samples = 0;
            float t = 0f;

            while (t < 1.2f)
            {
                t += Time.unscaledDeltaTime;
                sum += motor.CurrentPlanarSpeed;
                samples++;
                yield return null;
            }

            motor.ClearAutomationMove();
            yield return new WaitForSeconds(0.2f);
            report(samples > 0 ? sum / samples : 0f);
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
