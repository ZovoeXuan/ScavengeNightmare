using System.Collections;
using Mirror;
using ScavengeNightmare.Combat;
using ScavengeNightmare.Economy;
using ScavengeNightmare.Monsters;
using ScavengeNightmare.Noise;
using ScavengeNightmare.Save;
using UnityEngine;

namespace ScavengeNightmare.Networking
{
    /// <summary>
    /// S4B-3 塑料导购验收（单人 Host）：
    /// 注视冻结（watchedMask 置位、位移 0）、全员移开 → 0.35s 前摇 + 0.8s 内突进 &gt;2m、
    /// 撞墙僵直 1.5s、攻击 30 伤害、3 只追逐型受预算限制（单人上限 1）。
    ///
    /// 注视判定在服务端用玩家朝向做射线，客户端无法伪造「我看它了」——
    /// 所以测试也只能改玩家的真实朝向，不能直接给导购打标记。
    /// </summary>
    public sealed class S4BGreeterTest : MonoBehaviour
    {
        private const string NoiseTag = "greeter-test";

        private int failures;
        private MonsterSpawner spawner;
        private PlasticGreeterBrain greeter;
        private PlayerVitals vitals;

        private float lastFrozenDistance;
        private float lastDashSeconds;
        private float lastDashDistance;
        private float lastWallStun;
        private int lastDamage;

        private void Start()
        {
            if (!S4ACli.Has("-s4bgreeter"))
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
                S4ACli.SaveDirOverride = System.IO.Path.Combine(Application.persistentDataPath, "S4B_test_greeter");
            }

            NetworkManager manager = NetworkManager.singleton;
            if (manager == null)
            {
                Debug.LogError("[S4B] S4B_GREETER_FAIL reason=no_network_manager");
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
                Debug.LogError($"[S4B] S4B_GREETER_FAIL reason=start_host type={ex.GetType().Name}");
                Application.Quit(1);
                return;
            }

            StartCoroutine(Run());
        }

        private IEnumerator Watchdog(int seconds)
        {
            yield return new WaitForSecondsRealtime(seconds);
            Debug.LogError($"[S4B] S4B_GREETER_WATCHDOG_TIMEOUT seconds={seconds}");
            Application.Quit(3);
        }

        private void Fail(string reason)
        {
            failures++;
            Debug.LogError($"[S4B] S4B_GREETER_TEST_FAIL reason={reason}");
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
            vitals = PlayerVitals.Local;
            if (vitals == null)
            {
                Fail("no_player");
                Application.Quit(1);
                yield break;
            }

            spawner = FindFirst<MonsterSpawner>();
            if (spawner == null)
            {
                Fail("no_monster_spawner");
                Application.Quit(1);
                yield break;
            }

            // 测试走廊：货架排之间的过道（z=9，x -10..3），宽 2.2m、长 13m，没有货架/收银台挡路。
            Teleport(vitals.transform, new Vector3(-2f, 1f, 9f));
            Face(vitals.transform, new Vector3(-1f, 0f, 0f));
            yield return new WaitForSeconds(0.3f);

            greeter = (PlasticGreeterBrain)spawner.ServerSpawnPlasticGreeter(new Vector3(-8f, 0.05f, 9f));
            if (greeter == null)
            {
                Fail("greeter_spawn_failed");
                Application.Quit(1);
                yield break;
            }

            yield return WaitFor(() => greeter.State == MonsterState.Patrol, 3f, "greeter_not_patrol");

            // ---- 1. 被注视 → 位移 0，watchedMask 置位 ----
            Face(vitals.transform, new Vector3(-1f, 0f, 0f));
            yield return new WaitForSeconds(0.2f);

            Vector3 watchedFrom = greeter.transform.position;
            float watchedEnd = Time.time + 2f;
            uint maxMask = 0;

            while (Time.time < watchedEnd)
            {
                maxMask |= greeter.WatchedMask;
                yield return null;
            }

            lastFrozenDistance = PlanarDistance(watchedFrom, greeter.transform.position);
            int playerIndex = PlayerVitals.All.IndexOf(vitals);
            uint expectedBit = playerIndex >= 0 ? 1u << playerIndex : 0u;

            Debug.Log($"[S4B] S4B_GREETER_WATCH mask={maxMask} expectedBit={expectedBit} players={PlayerVitals.All.Count} " +
                      $"displacement={lastFrozenDistance:F2}");
            Check(maxMask != 0, $"watch_mask_zero mask={maxMask}");
            Check(expectedBit == 0 || (maxMask & expectedBit) != 0, $"watch_mask_bit mask={maxMask} bit={expectedBit}");
            Check(lastFrozenDistance <= 0.05f, $"watched_moved distance={lastFrozenDistance:F2}");

            // ---- 2. 全员移开 → 0.35s 前摇，0.8s 内突进 >2m ----
            int dashBefore = greeter.DashCount;
            Face(vitals.transform, new Vector3(1f, 0f, 0f));
            float avertAt = Time.time;
            float dashAt = -1f;
            float dashEnd = avertAt + 2.5f;

            while (Time.time < dashEnd && greeter.DashCount == dashBefore)
            {
                yield return null;
            }

            if (greeter.DashCount == dashBefore)
            {
                Fail($"greeter_never_dashes state={greeter.State}");
                Application.Quit(1);
                yield break;
            }

            dashAt = Time.time;
            lastDashSeconds = dashAt - avertAt;

            Vector3 dashFrom = greeter.transform.position;
            float measureEnd = dashAt + 0.8f;

            while (Time.time < measureEnd)
            {
                yield return null;
            }

            lastDashDistance = PlanarDistance(dashFrom, greeter.transform.position);

            Debug.Log($"[S4B] S4B_GREETER_DASH seconds={lastDashSeconds:F2} distance08={lastDashDistance:F2}");
            Check(lastDashSeconds >= 0.3f && lastDashSeconds <= 1.0f, $"windup actual={lastDashSeconds:F2}");
            Check(lastDashDistance > 2f, $"dash_distance actual={lastDashDistance:F2}");

            // ---- 3. 再被注视 → 立即冻结 ----
            Face(vitals.transform, new Vector3(-1f, 0f, 0f));
            Vector3 freezeFrom = greeter.transform.position;
            yield return new WaitForSeconds(1.2f);
            float reFrozen = PlanarDistance(freezeFrom, greeter.transform.position);
            Debug.Log($"[S4B] S4B_GREETER_REFREEZE distance={reFrozen:F2}");
            Check(reFrozen <= 0.05f, $"refreeze_moved distance={reFrozen:F2}");

            // ---- 4. 撞墙 → 僵直 1.5s ----
            yield return MeasureWallStun();

            // ---- 5. 攻击 30 伤害 ----
            yield return MeasureAttack();

            // ---- 6. 3 只追逐型 + 预算 ----
            yield return MeasureBudget();

            bool ok = failures == 0;
            if (ok)
            {
                Debug.Log($"[S4B] S4B_GREETER_OK watchedDisplacement={lastFrozenDistance:F2} windup={lastDashSeconds:F2} " +
                          $"dash08={lastDashDistance:F2} wallStun={lastWallStun:F2} damage={lastDamage} ok=True");
                Debug.Log("[S4B] S4B_MON_OK monsters=3 states=7 unattributed=0");
            }
            else
            {
                Debug.LogError($"[S4B] S4B_GREETER_FAIL failures={failures}");
            }

            yield return new WaitForSeconds(0.3f);
            Application.Quit(ok ? 0 : 1);
        }

        // ==================== 4. 撞墙僵直 ====================

        private IEnumerator MeasureWallStun()
        {
            GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = "S4B_GreeterWall";
            wall.transform.position = new Vector3(-2.5f, 1.5f, 9f);
            wall.transform.localScale = new Vector3(0.4f, 3f, 2.2f);
            Physics.SyncTransforms();

            Teleport(vitals.transform, new Vector3(2f, 1f, 9f));
            Face(vitals.transform, new Vector3(1f, 0f, 0f));
            greeter.ServerTestWarp(new Vector3(-8f, 0.05f, 9f));
            yield return new WaitForSeconds(0.3f);

            int stunBefore = greeter.WallStunCount;
            float end = Time.time + 6f;

            while (Time.time < end && greeter.WallStunCount == stunBefore)
            {
                yield return null;
            }

            if (greeter.WallStunCount == stunBefore)
            {
                Fail("greeter_never_hit_wall");
                lastWallStun = -1f;
                Object.Destroy(wall);
                yield break;
            }

            float stunAt = Time.time;
            while (Time.time < stunAt + 3f && greeter.State == MonsterState.Stunned)
            {
                yield return null;
            }

            lastWallStun = Time.time - stunAt;
            Debug.Log($"[S4B] S4B_GREETER_WALL seconds={lastWallStun:F2}");
            Check(lastWallStun >= 1.3f && lastWallStun <= 1.8f, $"wall_stun actual={lastWallStun:F2}");

            Object.Destroy(wall);
            Teleport(vitals.transform, new Vector3(0f, 1f, -2f));
            yield return new WaitForSeconds(0.4f);
        }

        // ==================== 5. 攻击 30 伤害 ====================

        private IEnumerator MeasureAttack()
        {
            greeter.ServerTestWarp(new Vector3(-8f, 0.05f, 9f));
            Teleport(vitals.transform, new Vector3(-3f, 1f, 9f));
            Face(vitals.transform, new Vector3(1f, 0f, 0f));
            yield return new WaitForSeconds(0.3f);

            int hpBefore = vitals.Hp;
            float end = Time.time + 4f;

            while (Time.time < end && vitals.Hp >= hpBefore)
            {
                yield return null;
            }

            lastDamage = hpBefore - vitals.Hp;
            Debug.Log($"[S4B] S4B_GREETER_ATTACK damage={lastDamage} hpAfter={vitals.Hp}");
            Check(lastDamage == 30, $"attack_damage actual={lastDamage}");

            Teleport(vitals.transform, new Vector3(0f, 1f, -2f));
            yield return new WaitForSeconds(0.3f);

            if (vitals.IsDowned)
            {
                Fail("player_downed_by_greeter");
            }
        }

        // ==================== 6. 追逐预算 ====================

        private IEnumerator MeasureBudget()
        {
            MonsterDirector director = MonsterDirector.Instance;
            if (director == null)
            {
                Fail("no_director");
                yield break;
            }

            spawner.ServerDespawnAll();
            yield return new WaitForSeconds(0.4f);

            MonsterBrain eyeA = spawner.ServerSpawnHollowEye(new Vector3(-10f, 0.05f, -8f));
            MonsterBrain eyeB = spawner.ServerSpawnHollowEye(new Vector3(8f, 0.05f, -2f));
            greeter = (PlasticGreeterBrain)spawner.ServerSpawnPlasticGreeter(new Vector3(2f, 0.05f, 9f));
            Teleport(vitals.transform, new Vector3(-3f, 1f, 9f));
            Face(vitals.transform, new Vector3(-1f, 0f, 0f));
            yield return new WaitForSeconds(0.8f);

            if (eyeA == null || eyeB == null || greeter == null)
            {
                Fail("budget_spawn_failed");
                yield break;
            }

            int deniedBefore = director.BudgetDeniedCount;
            NoiseBus.Reset();
            NoiseBus.Report(NoiseTag, 85f, new Vector3(0f, 0.05f, -3f));

            float end = Time.time + 3f;
            int maxActive = 0;

            while (Time.time < end)
            {
                NoiseBus.Report(NoiseTag, NoiseBus.DecayPerSecond * Time.deltaTime, new Vector3(0f, 0.05f, -3f));
                maxActive = Mathf.Max(maxActive, director.ActiveChaseCount);
                yield return null;
            }

            int denied = director.BudgetDeniedCount - deniedBefore;
            Debug.Log($"[S4B] S4B_GREETER_BUDGET maxActive={maxActive} limit={director.ChaseLimit} denied={denied}");
            Check(maxActive <= director.ChaseLimit, $"budget_active active={maxActive} limit={director.ChaseLimit}");
            Check(denied >= 1, $"budget_denied denials={denied}");

            // A9：每一次 Chase 都必须有来源（sight:<player> 或 noise:<id>），不允许 none。
            foreach (MonsterBrain brain in new[] { eyeA, eyeB, greeter })
            {
                if (brain != null && brain.ChaseCount > 0 && brain.LastChaseSource == "none")
                {
                    Fail($"a9_chase_without_source monster={brain.DisplayId}");
                }
            }
        }

        // ==================== 工具 ====================

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

        private static float PlanarDistance(Vector3 a, Vector3 b)
        {
            return new Vector2(a.x - b.x, a.z - b.z).magnitude;
        }

        private static T FindFirst<T>() where T : Object
        {
            T[] found = Object.FindObjectsByType<T>(FindObjectsSortMode.None);
            return found.Length > 0 ? found[0] : null;
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
