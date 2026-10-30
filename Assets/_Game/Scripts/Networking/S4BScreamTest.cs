using System.Collections;
using Mirror;
using ScavengeNightmare.Combat;
using ScavengeNightmare.Economy;
using ScavengeNightmare.Gameplay;
using ScavengeNightmare.Monsters;
using ScavengeNightmare.Noise;
using ScavengeNightmare.Save;
using UnityEngine;

namespace ScavengeNightmare.Networking
{
    /// <summary>
    /// S4B-3 尖啸罐验收（单人 Host）：
    /// 6m/噪音 40 → 1.5s 前摇 → 噪音 +45、12m 内眩晕 2s 且掉手持物；
    /// 前摇内投掷物命中 → 引爆（噪音只 +15、不眩晕不掉落）；
    /// 尖叫后只把最近空眼推进 Investigate（source=scream_can），远处空眼不动。
    /// </summary>
    public sealed class S4BScreamTest : MonoBehaviour
    {
        private const float CanZ = 10f;

        private int failures;
        private MonsterSpawner spawner;
        private ScreamCanBrain can;
        private PlayerVitals vitals;
        private PlayerMotor motor;
        private GrabController grab;
        private GrabbableProp held;

        private float lastWindup;
        private float lastStunRemaining;
        private int lastNoiseGain;
        private int lastPreemptGain;

        private void Start()
        {
            if (!S4ACli.Has("-s4bscream"))
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
                S4ACli.SaveDirOverride = System.IO.Path.Combine(Application.persistentDataPath, "S4B_test_scream");
            }

            NetworkManager manager = NetworkManager.singleton;
            if (manager == null)
            {
                Debug.LogError("[S4B] S4B_SCREAM_FAIL reason=no_network_manager");
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
                Debug.LogError($"[S4B] S4B_SCREAM_FAIL reason=start_host type={ex.GetType().Name}");
                Application.Quit(1);
                return;
            }

            StartCoroutine(Run());
        }

        private IEnumerator Watchdog(int seconds)
        {
            yield return new WaitForSecondsRealtime(seconds);
            Debug.LogError($"[S4B] S4B_SCREAM_WATCHDOG_TIMEOUT seconds={seconds}");
            Application.Quit(3);
        }

        private void Fail(string reason)
        {
            failures++;
            Debug.LogError($"[S4B] S4B_SCREAM_TEST_FAIL reason={reason}");
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

            motor = vitals.GetComponent<PlayerMotor>();
            grab = vitals.GetComponent<GrabController>();
            spawner = FindFirst<MonsterSpawner>();

            if (motor == null || grab == null || spawner == null)
            {
                Fail("missing_player_components");
                Application.Quit(1);
                yield break;
            }

            // 与 4A 自动化一致：关掉本地鼠标读入，否则「没按住左键」会在下一帧自动把货扔出去。
            grab.SetInputEnabled(false);

            // 先在 6m 外把东西抓稳，再走进触发半径：否则「前摇已开始才抓货」会把 1.5s 量短。
            Teleport(vitals.transform, new Vector3(0f, 1f, 22f));
            Face(vitals.transform, new Vector3(0f, 0f, -1f));
            NoiseBus.Reset();
            yield return new WaitForSeconds(0.3f);

            can = (ScreamCanBrain)spawner.ServerSpawnScreamCan(new Vector3(0f, 0.05f, CanZ));
            if (can == null)
            {
                Fail("can_spawn_failed");
                Application.Quit(1);
                yield break;
            }

            yield return WaitFor(() => can.State == MonsterState.Patrol, 3f, "can_not_patrol");

            // ---- 1. 尖叫：1.5s 前摇 → +45 噪音 + 12m 眩晕 2s + 掉手持物 ----
            yield return EnsureHolding();

            NoiseBus.Reset();
            int screamBefore = can.ScreamCount;
            bool holdBefore = grab.IsHolding;
            if (!holdBefore)
            {
                Fail("not_holding_before_scream");
            }

            TeleportAndCarry(new Vector3(0f, 1f, 15f));
            float windupStart = -1f;

            float windupEnd = Time.time + 4f;
            while (Time.time < windupEnd && can.ScreamCount == screamBefore)
            {
                if (windupStart < 0f && can.IsWindingUp)
                {
                    windupStart = Time.time;
                }

                yield return null;
            }

            if (can.ScreamCount == screamBefore || windupStart < 0f)
            {
                Fail($"scream_never_happened windup={(windupStart >= 0f)}");
                Application.Quit(1);
                yield break;
            }

            float screamAt = Time.time;
            lastWindup = screamAt - windupStart;
            lastNoiseGain = Mathf.RoundToInt(NoiseBus.Level);
            lastStunRemaining = motor.StunRemaining;
            bool dropped = !grab.IsHolding;

            Debug.Log($"[S4B] S4B_SCREAM_METRICS windup={lastWindup:F2} noise={lastNoiseGain} " +
                      $"stunRemaining={lastStunRemaining:F2} dropped={dropped}");
            Check(lastWindup >= 1.35f && lastWindup <= 1.7f, $"scream_windup actual={lastWindup:F2}");
            Check(lastNoiseGain >= 43, $"scream_noise actual={lastNoiseGain}");
            Check(lastStunRemaining >= 1.7f, $"scream_stun actual={lastStunRemaining:F2}");
            Check(dropped, "scream_did_not_drop");

            // ---- 2. 前摇内投掷物命中 → 引爆（+15、无眩晕无掉落）----
            // 站在原地等过冷却：冷却结束后罐子会立刻再次前摇。
            yield return new WaitForSeconds(2.2f);
            yield return EnsureHolding();
            int preemptBefore = can.PreemptCount;
            int screamBeforePreempt = can.ScreamCount;

            NoiseBus.Reset();
            yield return WaitFor(() => can.IsWindingUp, 8f, "preempt_windup_never_started");

            GrabbableProp thrown = FindFreeProp(held);
            if (thrown == null || thrown.Body == null)
            {
                Fail("no_prop_to_throw");
                yield break;
            }

            Vector3 canPos = can.transform.position;
            Rigidbody body = thrown.Body;
            body.isKinematic = false;
            body.position = canPos + new Vector3(1.2f, 0.5f, 0f);
            body.linearVelocity = new Vector3(-8f, 0f, 0f);
            body.angularVelocity = Vector3.zero;
            body.WakeUp();

            float preemptEnd = Time.time + 2f;
            while (Time.time < preemptEnd && can.PreemptCount == preemptBefore)
            {
                yield return null;
            }

            if (can.PreemptCount == preemptBefore)
            {
                Fail("preempt_never_triggered");
                Application.Quit(1);
                yield break;
            }

            lastPreemptGain = Mathf.RoundToInt(NoiseBus.Level);
            bool keptHolding = grab.IsHolding;
            bool stunnedByPreempt = motor.IsStunned;
            bool screamed = can.ScreamCount != screamBeforePreempt;

            Debug.Log($"[S4B] S4B_SCREAM_PREEMPT noise={lastPreemptGain} holding={keptHolding} " +
                      $"stunned={stunnedByPreempt} screamed={screamed}");
            Check(lastPreemptGain >= 14 && lastPreemptGain <= 18, $"preempt_noise actual={lastPreemptGain}");
            Check(keptHolding, $"preempt_dropped holding={keptHolding}");
            Check(!stunnedByPreempt, $"preempt_stunned stun={stunnedByPreempt}");
            Check(!screamed, $"preempt_still_screamed screams={can.ScreamCount}");

            // ---- 3. 尖叫后只把最近的空眼推进 Investigate ----
            yield return MeasureAlertScope();

            bool ok = failures == 0;
            if (ok)
            {
                Debug.Log($"[S4B] S4B_SCREAM_OK windup={lastWindup:F2} noise={lastNoiseGain} " +
                          $"preempt={lastPreemptGain} ok=True");
            }
            else
            {
                Debug.LogError($"[S4B] S4B_SCREAM_FAIL failures={failures}");
            }

            yield return new WaitForSeconds(0.3f);
            Application.Quit(ok ? 0 : 1);
        }

        private IEnumerator MeasureAlertScope()
        {
            NoiseBus.Reset();
            spawner.ServerDespawnAll();
            yield return new WaitForSeconds(0.4f);

            can = (ScreamCanBrain)spawner.ServerSpawnScreamCan(new Vector3(0f, 0.05f, CanZ));
            MonsterBrain near = spawner.ServerSpawnHollowEye(new Vector3(-5f, 0.05f, 8f));
            MonsterBrain far = spawner.ServerSpawnHollowEye(new Vector3(-15f, 0.05f, -8f));
            yield return new WaitForSeconds(0.8f);

            if (can == null || near == null || far == null)
            {
                Fail("alert_scope_spawn_failed");
                yield break;
            }

            // 墙挡在最近空眼与玩家之间，避免它靠视觉直接 Chase 抢走 Investigate。
            GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = "S4B_ScreamWall";
            wall.transform.position = new Vector3(0f, 1.5f, 12.5f);
            wall.transform.localScale = new Vector3(12f, 3f, 0.4f);
            Physics.SyncTransforms();

            Teleport(vitals.transform, new Vector3(0f, 1f, 15f));
            NoiseBus.Reset();

            int screamBefore = can.ScreamCount;
            yield return WaitFor(() => can.ScreamCount > screamBefore, 8f, "alert_scope_no_scream");

            yield return new WaitForSeconds(0.2f);

            bool nearInvestigate = near.State == MonsterState.Investigate && near.LastHeardSource == "scream_can";
            bool farNeutral = far.State != MonsterState.Chase && far.State != MonsterState.Investigate;

            Debug.Log($"[S4B] S4B_SCREAM_ALERT nearState={near.State} nearSource={near.LastHeardSource} " +
                      $"farState={far.State} level={NoiseBus.Level:F1}");
            Check(nearInvestigate, $"nearest_not_investigate state={near.State} source={near.LastHeardSource}");
            Check(farNeutral, $"far_monster_reacted state={far.State}");

            Object.Destroy(wall);
            Teleport(vitals.transform, new Vector3(0f, 1f, 22f));
            yield return new WaitForSeconds(0.3f);
        }

        // ==================== 工具 ====================

        private IEnumerator EnsureHolding()
        {
            if (grab.IsHolding)
            {
                yield return null;
                yield break;
            }

            GrabbableProp prop = held != null && !held.IsHeld ? held : FindFreeProp(null);
            if (prop == null)
            {
                Fail("no_prop_for_hold");
                yield break;
            }

            // 先把手里的货挪到玩家面前：测试是「瞬移抓货」，不挪的话第一帧
            // ServerHoldStep 会因为 delta 远超 breakDistance 直接脱手（S4A_HOLD_RELEASE）。
            PlacePropNearPlayer(prop);
            yield return null;

            bool ok = grab.ServerForceGrab(prop);
            if (!ok)
            {
                Fail($"force_grab_failed prop={(prop.Profile != null ? prop.Profile.id : "?")}");
                yield break;
            }

            held = prop;
            Debug.Log($"[S4B] S4B_SCREAM_HOLD prop={(prop.Profile != null ? prop.Profile.id : "?")} holding={grab.IsHolding}");
            yield return new WaitForSeconds(0.2f);
        }

        private void PlacePropNearPlayer(GrabbableProp prop)
        {
            Rigidbody body = prop.Body;
            body.isKinematic = false;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.position = vitals.transform.position + vitals.transform.forward * 0.8f + Vector3.up * 1.2f;
            body.WakeUp();
        }

        /// <summary>带着手里的货一起瞬移：只动玩家的话，第一帧持握点会跳 10m+ 直接脱手。</summary>
        private void TeleportAndCarry(Vector3 position)
        {
            Teleport(vitals.transform, position);

            if (held == null || !grab.IsHolding || held.Body == null)
            {
                return;
            }

            Rigidbody body = held.Body;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.position = position + vitals.transform.forward * 0.8f + Vector3.up * 1.2f;
            body.WakeUp();
        }

        private static void Face(Transform target, Vector3 direction)
        {
            Vector3 flat = new(direction.x, 0f, direction.z);
            if (flat.sqrMagnitude > 0.001f)
            {
                target.rotation = Quaternion.LookRotation(flat.normalized, Vector3.up);
            }
        }

        private GrabbableProp FindFreeProp(GrabbableProp exclude)
        {
            foreach (GrabbableProp prop in FindObjectsByType<GrabbableProp>(FindObjectsSortMode.None))
            {
                if (prop == null || prop == exclude || prop.IsHeld || prop.Body == null)
                {
                    continue;
                }

                return prop;
            }

            return null;
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
