using System.Collections;
using Mirror;
using ScavengeNightmare.Combat;
using ScavengeNightmare.Economy;
using ScavengeNightmare.Monsters;
using ScavengeNightmare.Noise;
using ScavengeNightmare.Save;
using ScavengeNightmare.UI;
using UnityEngine;

namespace ScavengeNightmare.Networking
{
    /// <summary>
    /// S4B-2 噪音消费验收（单人 Host）：
    ///   本地听觉半径 4/10/16m、距离衰减单调、全局 70 但本地无记忆不追猎（A9）、HUD 来源非空。
    ///
    /// 为什么要在「半径外不 Investigate」这条上死磕：噪音总线是全局的，
    /// 只要消费方漏掉一次距离衰减，就会变成「全图怪同时扑向一个声源」——
    /// 这是 4A 的 A9 在 4B 里最容易静默退化的地方。
    /// </summary>
    public sealed class S4BNoiseTest : MonoBehaviour
    {
        private const string EarlySource = "noise-test-early";
        private const string MidSource = "noise-test-mid";
        private const string HuntSource = "noise-test-hunt";

        private int failures;
        private MonsterSpawner spawner;
        private MonsterBrain brain;
        private PlayerVitals vitals;
        private NoiseHud hud;

        private void Start()
        {
            if (!S4ACli.Has("-s4bnoise"))
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
                S4ACli.SaveDirOverride = System.IO.Path.Combine(Application.persistentDataPath, "S4B_test_noise");
            }

            NetworkManager manager = NetworkManager.singleton;
            if (manager == null)
            {
                Debug.LogError("[S4B] S4B_NOISE_FAIL reason=no_network_manager");
                Application.Quit(1);
                return;
            }

            StartCoroutine(Watchdog(150));

            try
            {
                manager.StartHost();
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[S4B] S4B_NOISE_FAIL reason=start_host type={ex.GetType().Name}");
                Application.Quit(1);
                return;
            }

            StartCoroutine(Run());
        }

        private IEnumerator Watchdog(int seconds)
        {
            yield return new WaitForSecondsRealtime(seconds);
            Debug.LogError($"[S4B] S4B_NOISE_WATCHDOG_TIMEOUT seconds={seconds}");
            Application.Quit(3);
        }

        private void Fail(string reason)
        {
            failures++;
            Debug.LogError($"[S4B] S4B_NOISE_TEST_FAIL reason={reason}");
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
            yield return WaitFor(() => NoiseNetworkState.Instance != null, 25f, "no_noise_state");

            spawner = FindFirst<MonsterSpawner>();
            if (spawner == null)
            {
                Fail("no_monster_spawner");
                Application.Quit(1);
                yield break;
            }

            vitals = PlayerVitals.Local;
            if (vitals == null)
            {
                Fail("no_player_vitals");
                Application.Quit(1);
                yield break;
            }

            hud = vitals.GetComponent<NoiseHud>();

            // 玩家放到入口附近，离所有听觉测试点都 ≥20m：视线（12m）与追猎都不会混进来。
            Teleport(vitals.transform, new Vector3(0f, 1f, 15f));
            yield return new WaitForSeconds(0.4f);

            brain = spawner.ServerSpawnHollowEye(new Vector3(-15f, 0.05f, -8f));
            if (brain == null)
            {
                Fail("monster_spawn_failed");
                Application.Quit(1);
                yield break;
            }

            // Spawn → Patrol
            yield return WaitFor(() => brain.State == MonsterState.Patrol, 3f, "monster_not_patrol");

            // ---- 1. quiet 档：半径 4m ----
            yield return PlantMonster(brain, new Vector3(-15f, 0.05f, -8f));
            yield return ObserveNoResponse("quiet_outside", 20f, EarlySource, new Vector3(-9f, 0.05f, -8f), 1.2f, 6f);
            Check(brain.State == MonsterState.Patrol, $"quiet_outside_not_patrol state={brain.State}");

            yield return PlantMonster(brain, new Vector3(-15f, 0.05f, -8f));
            yield return ObserveInvestigate("quiet_inside", 20f, EarlySource, new Vector3(-13.5f, 0.05f, -8f), 1.6f);
            Check(brain.LastHeardBand == "quiet", $"quiet_band band={brain.LastHeardBand}");
            Check(Mathf.Abs(brain.LastHeardDistance - 1.5f) <= 0.6f, $"quiet_dist dist={brain.LastHeardDistance:F2}");
            Check(brain.LastHeardAttenuation >= 0.55f, $"quiet_atten atten={brain.LastHeardAttenuation:F2}");

            // ---- 2. alert 档：半径 10m ----
            yield return PlantMonster(brain, new Vector3(-15f, 0.05f, -8f));
            yield return ObserveNoResponse("alert_outside", 50f, MidSource, new Vector3(-2f, 0.05f, -8f), 1.2f, 13f);

            yield return PlantMonster(brain, new Vector3(-15f, 0.05f, -8f));
            yield return ObserveInvestigate("alert_inside", 50f, MidSource, new Vector3(-9f, 0.05f, -8f), 1.6f);
            Check(brain.LastHeardBand == "alert", $"alert_band band={brain.LastHeardBand}");
            Check(brain.LastHeardAttenuation >= 0.35f, $"alert_atten atten={brain.LastHeardAttenuation:F2}");

            // ---- 3. hunt 档：半径 16m ----
            // A9：全局 75（≥70）但声源在 20m 外，本地衰减 -0.25 → 听不到 → 不追猎。
            yield return PlantMonster(brain, new Vector3(-15f, 0.05f, -8f));
            int chaseBefore = brain.ChaseCount;
            yield return ObserveNoResponse("hunt_no_local_memory", 75f, HuntSource, new Vector3(5f, 0.05f, -8f), 1.6f, 20f);
            Check(brain.State == MonsterState.Patrol, $"hunt_no_local_state state={brain.State}");
            Check(brain.ChaseCount == chaseBefore, $"hunt_no_local_chase count={brain.ChaseCount} before={chaseBefore}");
            // 这里不能断言 LastHeardSource == "none"：上一段 alert 测试合法地留下了一条本地听觉记忆，
            // 断言它反倒会把「听得见」误判成失败。真正要验的是「没造成任何无来源的追猎」。
            Check(brain.LastChaseSource == "none", $"hunt_no_local_chase_source source={brain.LastChaseSource}");

            yield return PlantMonster(brain, new Vector3(-15f, 0.05f, -8f));
            yield return ObserveChase("hunt_inside", 75f, HuntSource, new Vector3(-7f, 0.05f, -8f), 2.0f, 8f);
            Check(brain.LastChaseSource != null && brain.LastChaseSource.StartsWith("noise:"), $"hunt_chase_source source={brain.LastChaseSource}");
            Check(brain.State == MonsterState.Chase, $"hunt_inside_not_chase state={brain.State}");

            // ---- 4. 衰减单调（直接问总线，避免状态机干扰）----
            NoiseBus.Reset();
            NoiseBus.Report("noise-test-mono", 60f, new Vector3(-15f, 0.05f, -8f));
            AudibleNoise near = NoiseBus.AudibleAt(new Vector3(-13f, 0.05f, -8f), 16f);
            AudibleNoise mid = NoiseBus.AudibleAt(new Vector3(-9f, 0.05f, -8f), 16f);
            AudibleNoise far = NoiseBus.AudibleAt(new Vector3(-5f, 0.05f, -8f), 16f);

            Check(near.HasSource && mid.HasSource && far.HasSource, "mono_missing_source");
            Check(near.Attenuation > mid.Attenuation && mid.Attenuation > far.Attenuation,
                $"mono_not_monotonic near={near.Attenuation:F2} mid={mid.Attenuation:F2} far={far.Attenuation:F2}");
            Debug.Log($"[S4B] S4B_NOISE_ATTEN near={near.Attenuation:F2} mid={mid.Attenuation:F2} far={far.Attenuation:F2}");

            // ---- 5. HUD：等级/来源/方向必须对玩家可见（A9 的可归因不是只进日志）----
            yield return PlantMonster(brain, new Vector3(-15f, 0.05f, -8f));
            Teleport(vitals.transform, new Vector3(0f, 1f, 10f));
            yield return new WaitForSeconds(0.4f);

            Vector3 hudNoiseAt = new(0f, 0.05f, 3f);
            float hudEnd = Time.time + 1.0f;

            while (Time.time < hudEnd)
            {
                if (Time.time < hudEnd - 0.8f)
                {
                    NoiseBus.Reset();
                    NoiseBus.Report("noise-test-hud", 60f, hudNoiseAt);
                }
                else
                {
                    NoiseBus.Report("noise-test-hud", NoiseBus.DecayPerSecond * Time.deltaTime, hudNoiseAt);
                }

                yield return null;
            }

            Check(hud != null, "no_hud");
            if (hud != null)
            {
                Debug.Log($"[S4B] S4B_NOISE_HUD source={hud.DisplayedSource} level={hud.DisplayedLevel:F0} dir={hud.DisplayedDirection}");
                Check(hud.DisplayedSource != "none", $"hud_source_empty source={hud.DisplayedSource}");
                Check(hud.DisplayedSource == "noise-test-hud", $"hud_source_mismatch source={hud.DisplayedSource}");
                Check(hud.DisplayedLevel > 0f, $"hud_level_zero level={hud.DisplayedLevel:F1}");
                Check(hud.DisplayedDirection != "-", $"hud_direction_missing dir={hud.DisplayedDirection}");
            }

            bool ok = failures == 0;

            if (ok)
            {
                Debug.Log($"[S4B] S4B_NOISE_OK quiet=1 alert=1 hunt=1 monotonic=1 hud=1 unattributed=0 failures=0 ok=True");
            }
            else
            {
                Debug.LogError($"[S4B] S4B_NOISE_FAIL failures={failures}");
            }

            yield return new WaitForSeconds(0.3f);
            Application.Quit(ok ? 0 : 1);
        }

        /// <summary>把怪钉在原地观察：先瞬移，再强制一个等于自身位置的巡逻点。</summary>
        private IEnumerator PlantMonster(MonsterBrain monster, Vector3 position)
        {
            NoiseBus.Reset();
            monster.ServerTestWarp(position);
            monster.ServerTestForcePatrolTarget(position);
            yield return null;
            yield return null;
        }

        /// <summary>持续供噪（每帧补回自然衰减，保证档位不漂移）。</summary>
        private IEnumerator ObserveNoResponse(string tag, float level, string source, Vector3 at, float seconds, float expectedDistance)
        {
            NoiseBus.Reset();
            NoiseBus.Report(source, level, at);

            float end = Time.time + seconds;
            while (Time.time < end)
            {
                NoiseBus.Report(source, NoiseBus.DecayPerSecond * Time.deltaTime, at);

                if (brain.State == MonsterState.Investigate || brain.State == MonsterState.Chase)
                {
                    Fail($"{tag}_reacted state={brain.State} dist={(brain.transform.position - at).magnitude:F2} expected>{expectedDistance:F0}");
                    yield break;
                }

                yield return null;
            }

            Debug.Log($"[S4B] S4B_NOISE_QUIET tag={tag} level={level:F0} state={brain.State}");
        }

        private IEnumerator ObserveInvestigate(string tag, float level, string source, Vector3 at, float seconds)
        {
            NoiseBus.Reset();
            NoiseBus.Report(source, level, at);

            float end = Time.time + seconds;
            bool observed = false;

            while (Time.time < end)
            {
                NoiseBus.Report(source, NoiseBus.DecayPerSecond * Time.deltaTime, at);

                if (brain.State == MonsterState.Investigate || brain.State == MonsterState.Chase)
                {
                    observed = true;
                    break;
                }

                yield return null;
            }

            Check(observed, $"{tag}_no_investigate state={brain.State}");
            if (observed)
            {
                Debug.Log($"[S4B] S4B_NOISE_HEARD tag={tag} level={level:F0} source={brain.LastHeardSource} band={brain.LastHeardBand} " +
                          $"dist={brain.LastHeardDistance:F2} attenuation={brain.LastHeardAttenuation:F2}");
            }
        }

        private IEnumerator ObserveChase(string tag, float level, string source, Vector3 at, float seconds, float expectedDistance)
        {
            NoiseBus.Reset();
            NoiseBus.Report(source, level, at);

            float end = Time.time + seconds;
            bool observed = false;

            while (Time.time < end)
            {
                NoiseBus.Report(source, NoiseBus.DecayPerSecond * Time.deltaTime, at);

                if (brain.State == MonsterState.Chase)
                {
                    observed = true;
                    break;
                }

                yield return null;
            }

            Check(observed, $"{tag}_no_chase state={brain.State}");
            if (observed)
            {
                Debug.Log($"[S4B] S4B_NOISE_CHASE tag={tag} source={brain.LastChaseSource} dist={expectedDistance:F0} state={brain.State}");
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
