using System.Collections;
using System.Collections.Generic;
using Mirror;
using ScavengeNightmare.Combat;
using ScavengeNightmare.Economy;
using ScavengeNightmare.Gameplay;
using ScavengeNightmare.Networking;
using ScavengeNightmare.Save;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ScavengeNightmare.Networking
{
    /// <summary>
    /// S4B-5 工具带 + 体能验收（单人 Host）：
    ///   1. 手持 42kg 雕像时，按 G 把 ≤7kg 小件挂到腰上；负重必须仍是 42+2=44kg；
    ///   2. 倒地时腰带上全部掉落（不留「倒地还挂着一件」的免费负重）；
    ///   3. 连续冲刺时长 ≥ 20s 的 35%（实现值 7.5s，37.5%）。
    ///
    /// 判据同样走「日志原文 + 实测时间」，不读被测对象自报的常量。
    /// </summary>
    public sealed class S4BBeltTest : MonoBehaviour
    {
        private int failures;
        private PlayerVitals vitals;
        private PlayerMotor motor;
        private GrabController grab;
        private BeltSlot belt;
        private PlayerStamina stamina;

        private float sprintSeconds = -1f;
        private float carryTotal = -1f;
        private float beltMassSeen = -1f;
        private int dropped = -1;

        private void Start()
        {
            if (!S4ACli.Has("-s4bbelt"))
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
                S4ACli.SaveDirOverride = System.IO.Path.Combine(Application.persistentDataPath, "S4B_test_belt");
            }

            NetworkManager manager = NetworkManager.singleton;
            if (manager == null)
            {
                Fail("no_network_manager");
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
                Debug.LogError($"[S4B] S4B_BELT_FAIL reason=start_host type={ex.GetType().Name}");
                Application.Quit(1);
                return;
            }

            StartCoroutine(Run());
        }

        private void OnDestroy()
        {
            S4BInput.AutoBelt = false;
        }

        private void Fail(string reason)
        {
            failures++;
            Debug.LogError($"[S4B] S4B_BELT_TEST_FAIL reason={reason}");
        }

        private void Check(bool condition, string reason)
        {
            if (!condition)
            {
                Fail(reason);
            }
        }

        private IEnumerator Watchdog(int seconds)
        {
            yield return new WaitForSecondsRealtime(seconds);
            Debug.LogError($"[S4B] S4B_BELT_WATCHDOG_TIMEOUT seconds={seconds}");
            Application.Quit(3);
        }

        private IEnumerator Run()
        {
            yield return WaitFor(() => NetworkClient.localPlayer != null, 25f, "no_local_player");

            vitals = PlayerVitals.Local;
            motor = vitals != null ? vitals.GetComponent<PlayerMotor>() : null;
            grab = vitals != null ? vitals.GetComponent<GrabController>() : null;
            belt = vitals != null ? vitals.GetComponent<BeltSlot>() : null;
            stamina = vitals != null ? vitals.GetComponent<PlayerStamina>() : null;

            if (vitals == null || motor == null || grab == null || belt == null || stamina == null)
            {
                Fail($"missing_components vitals={vitals != null} motor={motor != null} " +
                     $"grab={grab != null} belt={belt != null} stamina={stamina != null}");
                Application.Quit(1);
                yield break;
            }

            grab.SetInputEnabled(false);

            // S4C-1：工具带与体能训练已从「默认能力」改成商品（700 / 1200）。
            // 本用例要验的是 4B 的 37.5% 与第二槽，所以必须先真实购买，再看数值；
            // 购买前基线（1 槽 / 5.0s）由 -s4bshop8 与反向注入覆盖。
            TeamAccount account = TeamAccount.Instance;
            if (account == null)
            {
                Fail("no_team_account");
                Application.Quit(1);
                yield break;
            }

            account.DepositId("belt-test-seed", 2500);
            yield return new WaitForSeconds(0.3f);

            Debug.Log($"[S4C] GATE_BELT_SLOTS before={belt.EffectiveMaxSlots} after=? owned=0");
            Debug.Log($"[S4C] GATE_STAMINA_CAP before={stamina.CapacitySeconds:F1} after=? trained=0");

            account.TryPurchase("tool_belt");
            account.TryPurchase("stamina_training");
            yield return new WaitForSeconds(0.3f);

            Debug.Log($"[S4C] GATE_BELT_SLOTS before=1 after={belt.EffectiveMaxSlots} owned=1");
            Debug.Log($"[S4C] GATE_STAMINA_CAP before={PlayerStamina.BaseCapacitySeconds:F1} " +
                      $"after={stamina.CapacitySeconds:F1} trained=1");

            Check(belt.EffectiveMaxSlots == 2, $"tool_belt_not_effective slots={belt.EffectiveMaxSlots}");
            Check(Mathf.Abs(stamina.CapacitySeconds - PlayerStamina.TrainedCapacitySeconds) < 0.01f,
                $"stamina_training_not_effective capacity={stamina.CapacitySeconds:F1}");

            if (failures > 0)
            {
                Application.Quit(1);
                yield break;
            }

            Teleport(vitals.transform, new Vector3(0f, 1f, 18f));
            Face(vitals.transform, new Vector3(0f, 0f, -1f));
            yield return new WaitForSeconds(0.4f);

            yield return RunStamina();
            yield return RunBelt();
            yield return RunDownedDrop();

            bool ok = failures == 0;
            string metrics = $"sprint={sprintSeconds:F2} pct={(sprintSeconds / 20f * 100f):F1} " +
                             $"held=42.0 beltMass={beltMassSeen:F1} total={carryTotal:F1} dropped={dropped}";

            if (ok)
            {
                Debug.Log($"[S4B] S4B_BELT_OK {metrics} ok=True");
            }
            else
            {
                Debug.LogError($"[S4B] S4B_BELT_FAIL {metrics} failures={failures}");
            }

            yield return new WaitForSeconds(0.3f);
            Application.Quit(ok ? 0 : 1);
        }

        // ---------------- 体能 ----------------

        private IEnumerator RunStamina()
        {
            stamina.ResetStamina();
            motor.ApplyAutomationMove(Vector2.up, true);
            yield return new WaitForSeconds(0.2f);

            float startedAt = Time.realtimeSinceStartup;
            float limit = startedAt + 12f;

            while (motor.SprintInput && Time.realtimeSinceStartup < limit)
            {
                yield return null;
            }

            sprintSeconds = Time.realtimeSinceStartup - startedAt;
            motor.ClearAutomationMove();

            Debug.Log($"[S4B] S4B_STAMINA_METRICS continuous={sprintSeconds:F2} " +
                      $"pct={(sprintSeconds / 20f * 100f):F1} normalized={stamina.Normalized:F2}");

            // 目标：20s 一局里至少 35%（7.0s）。上限 9.0s 防止「体力闸门根本没接上」
            // 也能靠停跑判据混过去。
            Check(sprintSeconds >= 7.0f, $"stamina_too_short actual={sprintSeconds:F2} want>=7.0");
            Check(sprintSeconds <= 9.0f, $"stamina_too_long actual={sprintSeconds:F2} want<=9.0");
            Check(stamina.Normalized < 0.5f, $"stamina_not_consumed normalized={stamina.Normalized:F2}");

            Debug.Log($"[S4B] S4B_STAMINA_OK continuous={sprintSeconds:F2} pct={(sprintSeconds / 20f * 100f):F1}");

            // 给恢复留一点时间，避免紧接着的负重测试被「力竭」干扰。
            yield return new WaitForSeconds(1.2f);
        }

        // ---------------- 工具带 ----------------

        private IEnumerator RunBelt()
        {
            GrabbableProp statue = FindProp("statue");
            if (statue == null)
            {
                statue = SpawnProp("statue", vitals.transform.position + vitals.transform.forward * 1.4f);
            }

            if (statue == null)
            {
                Fail("statue_unavailable");
                Application.Quit(1);
                yield break;
            }

            MoveProp(statue, HoldSpot(1.2f));
            yield return new WaitForSeconds(0.3f);

            if (!grab.ServerForceGrab(statue))
            {
                Fail("statue_force_grab_failed");
                Application.Quit(1);
                yield break;
            }

            yield return new WaitForSeconds(0.5f);

            Check(grab.HeldMass >= 40f, $"held_mass actual={grab.HeldMass:F1} want>=40");

            GrabbableProp can = FindProp("can");
            if (can == null)
            {
                Fail("can_unavailable");
                Application.Quit(1);
                yield break;
            }

            // 放到脚前 1m：G 键走的是「最近合规小件」，离得太远会误抓别的货。
            MoveProp(can, vitals.transform.position + vitals.transform.forward * 1.0f + Vector3.up * 0.3f);
            yield return new WaitForSeconds(0.3f);

            // 真实按键路径：S4BInput 的 G → CmdBeltNearest → 服务端找最近小件。
            S4BInput.AutoBelt = true;
            yield return WaitFor(() => belt.SlotCount > 0, 2.5f, "belt_never_accepted");
            S4BInput.AutoBelt = false;
            yield return new WaitForSeconds(0.5f);

            Check(belt.SlotCount == 1, $"belt_slots actual={belt.SlotCount} want=1");
            Check(can.IsHeld, "belt_prop_not_marked_held");
            Check(!grab.IsHolding || grab.HeldProp != can, "belt_prop_endet_up_in_hand");
            Check(belt.BeltMass >= 1.5f && belt.BeltMass <= 2.5f,
                $"belt_mass actual={belt.BeltMass:F1} want~2");

            beltMassSeen = belt.BeltMass;
            carryTotal = motor.CarryMassKg;

            // 负重合计：42kg 手持 + 2kg 腰带 = 44kg。腰带不能把重量吞掉。
            Check(carryTotal >= grab.HeldMass + belt.BeltMass - 0.1f,
                $"carry_total actual={carryTotal:F1} want={grab.HeldMass + belt.BeltMass:F1}");
            Check(carryTotal >= 43f, $"carry_total_too_low actual={carryTotal:F1}");

            Debug.Log($"[S4B] S4B_BELT_METRICS held={grab.HeldMass:F1} beltMass={belt.BeltMass:F1} " +
                      $"total={carryTotal:F1} slots={belt.SlotCount}");
        }

        // ---------------- 倒地掉落 ----------------

        private IEnumerator RunDownedDrop()
        {
            // HP≥15 的单次伤害只保 1HP；要真倒地得先打到 1HP 再补一刀。
            vitals.ServerApplyDamage(200, "belt_test_first");
            yield return new WaitForSeconds(0.25f);
            Check(!vitals.IsDowned, "downed_on_first_hit_single_hit_protection_broken");

            vitals.ServerApplyDamage(10, "belt_test_second");
            yield return WaitFor(() => vitals.IsDowned, 2f, "never_downed");
            yield return new WaitForSeconds(0.4f);

            dropped = belt.SlotCount == 0 ? 1 : 0;
            Check(belt.SlotCount == 0, $"belt_still_holds actual={belt.SlotCount} after_down");
            Check(belt.BeltMass <= 0.001f, $"belt_mass_after_down actual={belt.BeltMass:F1}");
            Check(FindLine("S4B_BELT_DROP netId=") >= 0, "no_belt_drop_log");

            Debug.Log($"[S4B] S4B_BELT_DOWNED dropped={dropped} beltMass={belt.BeltMass:F1}");
        }

        // ---------------- 工具 ----------------

        private static readonly List<string> Captured = new();

        private static int FindLine(string fragment)
        {
            for (int i = Captured.Count - 1; i >= 0; i--)
            {
                if (Captured[i] != null && Captured[i].Contains(fragment))
                {
                    return i;
                }
            }

            return -1;
        }

        private void OnEnable()
        {
            Application.logMessageReceived += OnLog;
        }

        private void OnDisable()
        {
            Application.logMessageReceived -= OnLog;
        }

        private static void OnLog(string text, string stackTrace, LogType type)
        {
            Captured.Add(text);

            if (Captured.Count > 3000)
            {
                Captured.RemoveRange(0, 1500);
            }
        }

        private static GrabbableProp FindProp(string profileId)
        {
            foreach (GrabbableProp prop in FindObjectsByType<GrabbableProp>(FindObjectsSortMode.None))
            {
                if (prop != null && prop.Profile != null && prop.Profile.id == profileId && !prop.IsHeld)
                {
                    return prop;
                }
            }

            return null;
        }

        private static GrabbableProp SpawnProp(string profileId, Vector3 position)
        {
            NetworkManager nm = NetworkManager.singleton;
            if (nm == null || nm.spawnPrefabs == null)
            {
                return null;
            }

            foreach (GameObject prefab in nm.spawnPrefabs)
            {
                GrabbableProp prop = prefab != null ? prefab.GetComponent<GrabbableProp>() : null;
                if (prop == null || prop.Profile == null || prop.Profile.id != profileId || !NetworkServer.active)
                {
                    continue;
                }

                GameObject instance = Instantiate(prefab, position, Quaternion.identity);
                instance.name = $"{profileId}_belt_test";
                NetworkServer.Spawn(instance);
                return instance.GetComponent<GrabbableProp>();
            }

            return null;
        }

        private static void MoveProp(GrabbableProp prop, Vector3 position)
        {
            Rigidbody body = prop != null ? prop.Body : null;
            if (body == null)
            {
                return;
            }

            body.isKinematic = false;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.position = position;
            body.rotation = Quaternion.identity;
            body.WakeUp();
            Physics.SyncTransforms();
        }

        private Vector3 HoldSpot(float distance)
        {
            return vitals.transform.position + vitals.transform.forward * distance + Vector3.up * 1.2f;
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

        private static void Face(Transform target, Vector3 direction)
        {
            Vector3 flat = new(direction.x, 0f, direction.z);
            if (flat.sqrMagnitude > 0.001f)
            {
                target.rotation = Quaternion.LookRotation(flat.normalized, Vector3.up);
            }
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
    }
}
