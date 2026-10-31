using System.Collections;
using System.Collections.Generic;
using Mirror;
using ScavengeNightmare.Audio;
using ScavengeNightmare.Cart;
using ScavengeNightmare.Curses;
using ScavengeNightmare.Economy;
using ScavengeNightmare.Gameplay;
using ScavengeNightmare.Save;
using ScavengeNightmare.Traits;
using UnityEngine;

namespace ScavengeNightmare.Networking
{
    /// <summary>
    /// S4C-4 R6 四类占位音验收（单人 Host）：真实触发四条代码路径，而不是直接调 AudioBus。
    ///
    /// 四类 cue 的触发源：
    ///   1. heavy_drop：≥30kg 的重物落地（cabinet 35 / statue 42 / fridge 70 / safe 60 / washer 55）；
    ///   2. glass_break：易碎件（bottle / glass / vase / bulbbox）从 2.5m 落下砸碎；
    ///   3. curse_omen：笑面偶前兆（1.5s 后才笑，lead 必须对得上）；
    ///   4. cart_wheel：推车过 1.5m/s。
    ///
    /// 判据口径：可区分 = 日志里四类 cue 的 fingerprint（freq/dur/wave）去重后必须是 4 个。
    /// -s4caudioflat 把四类压成同一段波形，用例必须报 distinguishable=1 并 FAIL（C5 反向注入）。
    /// </summary>
    public sealed class S4CAudioTest : MonoBehaviour
    {
        private struct LogLine
        {
            public string Text;
            public float Time;
        }

        private const string OmenPrefix = "cue=curse_omen";
        private const string WheelPrefix = "cue=cart_wheel";
        private const string DropPrefix = "cue=heavy_drop";
        private const string GlassPrefix = "cue=glass_break";

        private readonly List<LogLine> lines = new();

        private int failures;
        private bool flatMode;

        private PlayerMotor motor;
        private GrabController grab;
        private CartController cart;

        private float heavyMass = -1f;
        private float glassSpeed = -1f;
        private float omenLead = -1f;
        private float wheelSpeed = -1f;
        private float omenToLaugh = -1f;

        private void Start()
        {
            bool run = S4ACli.Has("-s4baudio");
            flatMode = S4ACli.Has("-s4caudioflat");

            if (!run && !flatMode)
            {
                this.enabled = false;
                return;
            }

            // C5：四类 cue 只播同一段波形
            AudioBus.FlattenForTest = flatMode;

            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 60;
            TraitSettings.ForceNoTraits = true;
            S4AInput.SuppressManual = true;
            Debug.Log("[S4A] S4A_NEUTRAL_OK traits=none feel=" + (S4ACli.Value("-feel=") ?? "default").ToLowerInvariant());

            if (string.IsNullOrEmpty(S4ACli.Value("-savedir=")))
            {
                S4ACli.SaveDirOverride = System.IO.Path.Combine(Application.persistentDataPath, "S4C_test_audio");
            }

            Application.logMessageReceived += OnLog;

            NetworkManager manager = NetworkManager.singleton;
            if (manager == null)
            {
                Debug.LogError("[S4C] S4C_AUDIO_FAIL reason=no_network_manager");
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
                Debug.LogError($"[S4C] S4C_AUDIO_FAIL reason=start_host type={ex.GetType().Name}");
                Application.Quit(1);
                return;
            }

            StartCoroutine(Run());
        }

        private void OnDestroy()
        {
            Application.logMessageReceived -= OnLog;
            AudioBus.FlattenForTest = false;
            S4AInput.AutoGrab = false;
            S4AInput.AutoInteract = false;
        }

        private void OnLog(string text, string stackTrace, LogType type)
        {
            lines.Add(new LogLine { Text = text, Time = Time.time });

            if (lines.Count > 4000)
            {
                lines.RemoveRange(0, 2000);
            }
        }

        private IEnumerator Watchdog(int seconds)
        {
            yield return new WaitForSecondsRealtime(seconds);
            Debug.LogError($"[S4C] S4C_AUDIO_WATCHDOG_TIMEOUT seconds={seconds}");
            Application.Quit(3);
        }

        private void Fail(string reason)
        {
            failures++;
            Debug.LogError($"[S4C] S4C_AUDIO_TEST_FAIL reason={reason}");
        }

        private void Check(bool condition, string reason)
        {
            if (!condition)
            {
                Fail(reason);
            }
        }

        // ==================== 主流程 ====================

        private IEnumerator Run()
        {
            yield return WaitFor(() => NetworkClient.localPlayer != null, 25f, "no_local_player");

            motor = NetworkClient.localPlayer.GetComponent<PlayerMotor>();
            grab = NetworkClient.localPlayer.GetComponent<GrabController>();

            // 先等再取：CartController 由 S4ABootstrap 生成，早取到的会是 null 且永不刷新。
            yield return WaitFor(() => CartController.Instance != null, 20f, "no_cart_controller");
            cart = CartController.Instance;

            yield return WaitFor(() => FindObjectsByType<GrabbableProp>(FindObjectsSortMode.None).Length > 0, 20f, "no_props");

            if (failures > 0)
            {
                Debug.LogError($"[S4C] S4C_AUDIO_FAIL phase=boot failures={failures}");
                Application.Quit(1);
                yield break;
            }

            // 与既有用例一致：关掉本地鼠标读入，否则「没按住左键」会把货自动扔出去。
            grab.SetInputEnabled(false);

            Teleport(motor.transform, new Vector3(0f, 1f, 18f));
            motor.transform.LookAt(new Vector3(0f, 1f, 16f));
            yield return new WaitForSeconds(0.4f);

            yield return RunHeavyDrop();
            yield return RunGlassBreak();
            yield return RunCurseOmen();
            yield return RunCartWheel();

            yield return Finish();
        }

        // ---------------- 1. 重物落地 ----------------

        private IEnumerator RunHeavyDrop()
        {
            GrabbableProp heavy = FindHeaviestProp();
            if (heavy == null)
            {
                Fail("no_heavy_prop");
                yield break;
            }

            // 只看本步之后：开场景时货架上的重物自己落地也会打一条，不能拿它当本次证据。
            int mark = lines.Count;

            DropFrom(heavy, 2.5f);
            yield return WaitFor(() => FindLineFrom(mark, DropPrefix) >= 0, 8f, "heavy_drop_never_played");

            int heavyIndex = FindLineFrom(mark, DropPrefix);
            if (heavyIndex >= 0)
            {
                heavyMass = ParseFloat(FindText(heavyIndex), "mass=");
                Debug.Log($"[S4C] S4C_AUDIO_HEAVY id={(heavy.Profile != null ? heavy.Profile.id : "?")} mass={heavyMass:F1}");
                Check(heavyMass >= 30f, $"heavy_drop_mass_too_small actual={heavyMass:F1} want>=30");
            }

            // 反向：轻货落地不该触发重物音。用 1kg 罐头从同一高度落。
            // 先把重货停住挪远：它落地后会弹两下继续触发 heavy_drop，
            // 留在落点还会被轻货砸中，两条都会让这条负向判据假红。
            Park(heavy);
            yield return new WaitForSeconds(0.6f);

            // 不清日志：末尾的 cue 去重要用整段日志，这里只记「从这一行之后」的窗口。
            int negativeMark = lines.Count;

            GrabbableProp light = FindLightProp();
            if (light == null)
            {
                Fail("no_light_prop");
                yield break;
            }

            DropFrom(light, 2.5f);
            yield return new WaitForSeconds(1.5f);
            Check(FindLineFrom(negativeMark, DropPrefix) < 0, "light_prop_played_heavy_drop");
        }

        // ---------------- 2. 碎裂 ----------------

        private IEnumerator RunGlassBreak()
        {
            FragileProp fragile = FindFragileProp();
            if (fragile == null)
            {
                Fail("no_fragile_prop");
                yield break;
            }

            // 同样只认本步之后的线：开场景时易碎件掉地上先碎过一件。
            int mark = lines.Count;

            DropFrom(fragile.GetComponent<GrabbableProp>(), 2.5f);

            yield return WaitFor(() => FindLineFrom(mark, GlassPrefix) >= 0, 8f, "glass_break_never_played");

            int glassIndex = FindLineFrom(mark, GlassPrefix);
            if (glassIndex >= 0)
            {
                glassSpeed = ParseFloat(FindText(glassIndex), "speed=");
                Debug.Log($"[S4C] S4C_AUDIO_GLASS broken={(fragile.Broken ? 1 : 0)} speed={glassSpeed:F1}");
                Check(fragile.Broken, "fragile_prop_not_broken");
                Check(glassSpeed >= 1.5f, $"glass_break_speed_too_low actual={glassSpeed:F1}");
            }
        }

        // ---------------- 3. 诅咒前兆 ----------------

        private IEnumerator RunCurseOmen()
        {
            CurseRuntime runtime = FindFirst<CurseRuntime>();
            if (runtime == null)
            {
                Fail("no_curse_runtime");
                yield break;
            }

            Vector3 at = motor.transform.position + motor.transform.forward * 0.8f + Vector3.up * 1.2f;
            CursedItem doll = runtime.ServerSpawnCurse(CurseKind.Doll, at);
            if (doll == null)
            {
                Fail("doll_spawn_failed");
                yield break;
            }

            yield return null;
            PlaceNearPlayer(doll);

            if (!grab.ServerForceGrab(doll.Prop))
            {
                Fail("doll_force_grab_failed");
                yield break;
            }

            // 12–20s 的间隔里前兆占 1.5s，留足 26s 观察窗。
            yield return WaitFor(() => FindLine(OmenPrefix) >= 0, 26f, "curse_omen_never_played");

            if (FindLine(OmenPrefix) >= 0)
            {
                omenLead = ParseFloat(FindText(FindLine(OmenPrefix)), "lead=");
                Debug.Log($"[S4C] S4C_AUDIO_OMEN lead={omenLead:F2} windup={doll.LaughWindup:F2}");
                Check(Mathf.Abs(omenLead - 1.5f) <= 0.01f, $"curse_omen_lead actual={omenLead:F2} want=1.5");
            }

            // 前兆必须早于发笑：只播「笑」不播「前兆」的话，玩家没有反应时间。
            if (omenLead > 0f)
            {
                yield return WaitFor(() => FindLine("GATE_CURSE_LAUGH id=curse_doll") >= 0, 10f, "curse_laugh_never_after_omen");
                float omenAt = TimeOfNth(OmenPrefix, 0);
                float laughAt = TimeOfNth("GATE_CURSE_LAUGH id=curse_doll", 0);
                omenToLaugh = laughAt - omenAt;
                Check(omenToLaugh >= 1.4f && omenToLaugh <= 1.6f,
                    $"omen_lead_actual actual={omenToLaugh:F2} want=1.5");
            }

            grab.Release(false);
            yield return new WaitForSeconds(0.3f);
        }

        // ---------------- 4. 轮声 ----------------

        private IEnumerator RunCartWheel()
        {
            if (cart == null)
            {
                Fail("no_cart_for_wheel");
                yield break;
            }

            // 与 A4 同一条跑道：在店内地面推，测的是寻路不是轮速。
            SpawnTrack();
            yield return new WaitForSeconds(0.3f);

            cart.TeleportWithCargo(new Vector3(0f, 20.1f, 15f));
            yield return new WaitForSeconds(0.6f);

            Rigidbody rb = cart.GetComponent<Rigidbody>();
            if (rb == null)
            {
                Fail("cart_no_rigidbody");
                yield break;
            }

            Vector3 behind = rb.position + new Vector3(0f, 0f, 1.4f);
            behind.y = rb.position.y + 0.35f;
            Teleport(motor.transform, behind);
            motor.transform.LookAt(new Vector3(rb.position.x, behind.y, rb.position.z - 2f));
            yield return new WaitForSeconds(0.4f);

            S4AInput.AutoGrab = true;
            motor.ApplyAutomationMove(new Vector2(0f, 1f), false);

            yield return WaitFor(() => FindLine(WheelPrefix) >= 0, 15f, "cart_wheel_never_played");

            S4AInput.AutoGrab = false;
            motor.ClearAutomationMove();

            if (FindLine(WheelPrefix) >= 0)
            {
                wheelSpeed = ParseFloat(FindText(FindLine(WheelPrefix)), "speed=");
                float wheelPitch = ParseFloat(FindText(FindLine(WheelPrefix)), "pitch=");

                Debug.Log($"[S4C] S4C_AUDIO_WHEEL speed={wheelSpeed:F1} pitch={wheelPitch:F2}");
                Check(wheelSpeed >= 1.5f, $"cart_wheel_speed_too_low actual={wheelSpeed:F1} want>=1.5");
                Check(wheelPitch >= 0.8f && wheelPitch <= 1.2f,
                    $"cart_wheel_pitch_out_of_range actual={wheelPitch:F2} want=0.8..1.2");
            }

            yield return new WaitForSeconds(0.3f);
        }

        // ==================== 收尾 ====================

        private IEnumerator Finish()
        {
            // 判据只认日志原文的 fingerprint，不读 AudioBus 自己暴露的计数器。
            Dictionary<string, List<string>> byCue = new();
            HashSet<string> fingerprints = new();

            foreach (LogLine line in lines)
            {
                if (line.Text == null || !line.Text.Contains("S4C_AUDIO_PLAY"))
                {
                    continue;
                }

                string cue = Field(line.Text, "cue=");
                if (string.IsNullOrEmpty(cue))
                {
                    continue;
                }

                string fingerprint = Fingerprint(line.Text);
                if (string.IsNullOrEmpty(fingerprint))
                {
                    continue;
                }

                if (!byCue.TryGetValue(cue, out List<string> list))
                {
                    list = new List<string>();
                    byCue[cue] = list;
                }

                if (!list.Contains(fingerprint))
                {
                    list.Add(fingerprint);
                }

                fingerprints.Add(fingerprint);
            }

            string[] wanted = { "cart_wheel", "heavy_drop", "glass_break", "curse_omen" };
            int cues = 0;

            foreach (string cue in wanted)
            {
                if (byCue.ContainsKey(cue))
                {
                    cues++;
                }
                else
                {
                    Fail($"cue_missing cue={cue}");
                }
            }

            int distinguishable = fingerprints.Count;

            Debug.Log($"[S4C] S4C_AUDIO_METRICS cues={cues} distinguishable={distinguishable} " +
                      $"heavyMass={heavyMass:F1} glassSpeed={glassSpeed:F1} " +
                      $"omenLead={omenLead:F2} omenToLaugh={omenToLaugh:F2} wheelSpeed={wheelSpeed:F1} " +
                      $"flat={(flatMode ? 1 : 0)}");

            Check(cues == 4, $"cue_count actual={cues} want=4");
            Check(distinguishable == 4, $"not_distinguishable actual={distinguishable} want=4");

            bool ok = failures == 0;
            string metrics = $"cues={cues} distinguishable={distinguishable} failures={failures}";

            if (ok)
            {
                Debug.Log($"[S4C] S4C_AUDIO_OK {metrics} ok=True");
            }
            else
            {
                Debug.LogError($"[S4C] S4C_AUDIO_FAIL {metrics}");
            }

            yield return new WaitForSeconds(0.3f);
            Application.Quit(ok ? 0 : 1);
        }

        // ==================== 日志判据 ====================

        private int FindLine(string fragment)
        {
            for (int i = lines.Count - 1; i >= 0; i--)
            {
                if (lines[i].Text != null && lines[i].Text.Contains(fragment))
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>只从 mark 之后找：用于「这一段窗口里不许出现某条」的负向判据。</summary>
        private int FindLineFrom(int mark, string fragment)
        {
            for (int i = Mathf.Max(0, mark); i < lines.Count; i++)
            {
                if (lines[i].Text != null && lines[i].Text.Contains(fragment))
                {
                    return i;
                }
            }

            return -1;
        }

        private string FindText(int index)
        {
            return index >= 0 && index < lines.Count ? lines[index].Text : string.Empty;
        }

        private float TimeOfNth(string fragment, int nth)
        {
            int seen = 0;

            for (int i = 0; i < lines.Count; i++)
            {
                if (lines[i].Text == null || !lines[i].Text.Contains(fragment))
                {
                    continue;
                }

                if (seen == nth)
                {
                    return lines[i].Time;
                }

                seen++;
            }

            return -1f;
        }

        private static string Fingerprint(string text)
        {
            string freq = Field(text, "freq=");
            string dur = Field(text, "dur=");
            string wave = Field(text, "wave=");

            if (string.IsNullOrEmpty(freq) || string.IsNullOrEmpty(dur) || string.IsNullOrEmpty(wave))
            {
                return string.Empty;
            }

            return $"freq={freq} dur={dur} wave={wave}";
        }

        /// <summary>取 key=value 里的 value（到空格为止）。</summary>
        private static string Field(string text, string key)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            int at = text.IndexOf(key, System.StringComparison.Ordinal);
            if (at < 0)
            {
                return string.Empty;
            }

            int start = at + key.Length;
            int end = start;

            while (end < text.Length && !char.IsWhiteSpace(text[end]))
            {
                end++;
            }

            return text.Substring(start, end - start);
        }

        private static float ParseFloat(string text, string key)
        {
            return float.TryParse(
                Field(text, key),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out float value)
                ? value
                : -1f;
        }

        // ==================== 工具 ====================

        private static GrabbableProp FindHeaviestProp()
        {
            GrabbableProp best = null;

            foreach (GrabbableProp prop in FindObjectsByType<GrabbableProp>(FindObjectsSortMode.None))
            {
                if (prop == null || prop.Profile == null || prop.Profile.mass < 30f || prop.CurrentValue <= 0)
                {
                    continue;
                }

                // 稳定选择：质量最大者，质量相同按 id 字典序
                if (best == null
                    || prop.Profile.mass > best.Profile.mass
                    || (Mathf.Approximately(prop.Profile.mass, best.Profile.mass)
                        && string.CompareOrdinal(prop.Profile.id, best.Profile.id) < 0))
                {
                    best = prop;
                }
            }

            return best;
        }

        private static GrabbableProp FindLightProp()
        {
            foreach (GrabbableProp prop in FindObjectsByType<GrabbableProp>(FindObjectsSortMode.None))
            {
                if (prop != null && prop.Profile != null && prop.Profile.mass <= 2f && prop.CurrentValue > 0)
                {
                    return prop;
                }
            }

            return null;
        }

        private static FragileProp FindFragileProp()
        {
            foreach (FragileProp fragile in FindObjectsByType<FragileProp>(FindObjectsSortMode.None))
            {
                GrabbableProp prop = fragile != null ? fragile.GetComponent<GrabbableProp>() : null;

                if (prop == null || prop.Profile == null || !prop.Profile.isFragile)
                {
                    continue;
                }

                if (!fragile.Broken && prop.CurrentValue > 0)
                {
                    return fragile;
                }
            }

            return null;
        }

        /// <summary>从玩家前方 2.2m、离地 2.5m 自由落体：撞击速度约 7m/s，稳过全部易碎阈值。</summary>
        private void DropFrom(GrabbableProp prop, float height)
        {
            if (prop == null || prop.Body == null)
            {
                return;
            }

            Rigidbody body = prop.Body;
            Vector3 at = motor.transform.position + motor.transform.forward * 2.2f;
            float groundY = motor.transform.position.y - 1f;

            body.isKinematic = false;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.position = new Vector3(at.x, groundY + height, at.z);
            body.rotation = Quaternion.identity;
            body.WakeUp();
            Physics.SyncTransforms();
        }

        /// <summary>把用过的货停住并挪出场景中心，避免它继续弹跳干扰后续判据。</summary>
        private static void Park(GrabbableProp prop)
        {
            if (prop == null || prop.Body == null)
            {
                return;
            }

            Rigidbody body = prop.Body;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.isKinematic = true;
            body.position = new Vector3(60f, 5f, 60f);
            Physics.SyncTransforms();
        }

        private void PlaceNearPlayer(CursedItem item)
        {
            if (item == null || item.Prop == null || item.Prop.Body == null)
            {
                return;
            }

            Rigidbody body = item.Prop.Body;
            body.isKinematic = false;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.position = motor.transform.position + motor.transform.forward * 0.8f + Vector3.up * 1.2f;
            body.rotation = Quaternion.identity;
            body.WakeUp();
            Physics.SyncTransforms();
        }

        /// <summary>与 A4 同一条 20×60m 跑道，铺在地图上方 20m，隔离店内货架与门框。</summary>
        private static GameObject SpawnTrack()
        {
            GameObject track = GameObject.CreatePrimitive(PrimitiveType.Cube);
            track.name = "S4C_AudioTrack";
            track.transform.position = new Vector3(0f, 19.75f, 0f);
            track.transform.localScale = new Vector3(20f, 0.5f, 60f);
            return track;
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
