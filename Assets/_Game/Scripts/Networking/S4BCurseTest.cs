using System.Collections;
using System.Collections.Generic;
using Mirror;
using ScavengeNightmare.Cart;
using ScavengeNightmare.Combat;
using ScavengeNightmare.Curses;
using ScavengeNightmare.Economy;
using ScavengeNightmare.Gameplay;
using ScavengeNightmare.Noise;
using ScavengeNightmare.Save;
using UnityEngine;

namespace ScavengeNightmare.Networking
{
    /// <summary>
    /// S4B-4 两件诅咒验收（单人 Host）：
    ///   1. 笑面偶：持有者每 12–20s 发笑一次，噪音 +8 且可归因（NOISE_ADD source=curse_doll）；
    ///      未封印 60% → 510。
    ///   2. 无字牌位：放下必须长按 1.5s；不足 1.5s 松手 → GATE_CURSE_HOLD_REJECT，
    ///      够了 → GATE_CURSE_HOLD_OK；未封印 60% → 780。
    ///   3. 同一玩家同时招惹两件（手持 + 随车）→ 只有 1 件生效，第 2 件 dormant=1，HUD 明示。
    ///   4. -s4bleg：L1/L2/L3 生成 0/1/2 件。
    ///
    /// 判据来源：需要断言「日志里真的出现了某一行」的，一律从 Application.logMessageReceived
    /// 抓原文 + 时间戳，不去读被测对象自己暴露的计数器——读自己写的字段等于把判据建在被
    /// 测物上，那是自证，不是证据。
    /// </summary>
    public sealed class S4BCurseTest : MonoBehaviour
    {
        /// <summary>
        /// 帧量化容差（60fps 下 3 帧）。不是放宽 12–20s 的设计判据：
        /// 区间本身另外按日志原文（GATE_CURSE_ARM / GATE_CURSE_LAUGH 里的 interval=）
        /// 精确断言一次。这里只吸收「定时器在帧边界上触发」带来的 ±1 帧误差。
        /// </summary>
        private const float FrameTolerance = 0.05f;

        private struct LogLine
        {
            public string Text;
            public float Time;
        }

        private readonly List<LogLine> lines = new();

        private int failures;
        private bool runLeg;

        private CurseRuntime runtime;
        private CurseHud hud;
        private PlayerVitals vitals;
        private PlayerMotor motor;
        private GrabController grab;

        private CursedItem doll;
        private CursedItem tablet;

        private float interval1;
        private float interval2;
        private float rejectActual = -1f;
        private float okActual = -1f;
        private int dollValue = -1;
        private int tabletValue = -1;
        private int dormantSeen = -1;
        private int carriedSeen = -1;
        private string hudText = string.Empty;

        private void Start()
        {
            bool curse = S4ACli.Has("-s4bcurse");
            runLeg = S4ACli.Has("-s4bleg");

            if (!curse && !runLeg)
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
                S4ACli.SaveDirOverride = System.IO.Path.Combine(Application.persistentDataPath, "S4B_test_curse");
            }

            Application.logMessageReceived += OnLog;

            NetworkManager manager = NetworkManager.singleton;
            if (manager == null)
            {
                Debug.LogError("[S4B] S4B_CURSE_FAIL reason=no_network_manager");
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
                Debug.LogError($"[S4B] S4B_CURSE_FAIL reason=start_host type={ex.GetType().Name}");
                Application.Quit(1);
                return;
            }

            StartCoroutine(Run());
        }

        private void OnDestroy()
        {
            Application.logMessageReceived -= OnLog;
            CurseInput.AutoPutDown = false;
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
            Debug.LogError($"[S4B] S4B_CURSE_WATCHDOG_TIMEOUT seconds={seconds}");
            Application.Quit(3);
        }

        private void Fail(string reason)
        {
            failures++;
            Debug.LogError($"[S4B] S4B_CURSE_TEST_FAIL reason={reason}");
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

            vitals = PlayerVitals.Local;
            motor = vitals != null ? vitals.GetComponent<PlayerMotor>() : null;
            grab = vitals != null ? vitals.GetComponent<GrabController>() : null;
            hud = vitals != null ? vitals.GetComponent<CurseHud>() : null;
            runtime = FindFirst<CurseRuntime>();

            if (vitals == null || motor == null || grab == null)
            {
                Fail("missing_player_components");
                Application.Quit(1);
                yield break;
            }

            if (hud == null)
            {
                Fail("no_curse_hud");
                Application.Quit(1);
                yield break;
            }

            if (runtime == null)
            {
                Fail("no_curse_runtime");
                Application.Quit(1);
                yield break;
            }

            // 与 4A/4B 既有用例一致：关掉本地鼠标读入，否则「没按住左键」会把货自动扔出去。
            grab.SetInputEnabled(false);

            Teleport(vitals.transform, new Vector3(0f, 1f, 18f));
            Face(vitals.transform, new Vector3(0f, 0f, -1f));
            NoiseBus.Reset();
            yield return new WaitForSeconds(0.4f);

            if (runLeg)
            {
                yield return RunLegGradient();
                yield return Finish("S4B_CURSE_LEG_OK", "S4B_CURSE_LEG_FAIL", "legGradient=1");
                yield break;
            }

            yield return RunDoll();
            yield return RunTablet();
            yield return RunDualCarry();

            yield return Finish("S4B_CURSE_OK", "S4B_CURSE_FAIL",
                $"laugh1={interval1:F2} laugh2={interval2:F2} dollValue={dollValue} " +
                $"tabletValue={tabletValue} reject={rejectActual:F2} ok={okActual:F2} " +
                $"carried={carriedSeen} dormant={dormantSeen}");
        }

        // ---------------- 1. 笑面偶 ----------------

        private IEnumerator RunDoll()
        {
            ClearLogs();

            doll = runtime.ServerSpawnCurse(CurseKind.Doll, HoldSpot());
            if (doll == null)
            {
                Fail("doll_spawn_failed");
                Application.Quit(1);
                yield break;
            }

            PlaceNearPlayer(doll);
            yield return null;

            if (!grab.ServerForceGrab(doll.Prop))
            {
                Fail("doll_force_grab_failed");
                Application.Quit(1);
                yield break;
            }

            yield return new WaitForSeconds(0.4f);

            dollValue = doll.Prop.CurrentValue;
            Debug.Log($"[S4B] S4B_CURSE_VALUE id={doll.CurseId} listed={(doll.Prop.Profile != null ? doll.Prop.Profile.value : 0)} current={dollValue}");
            Check(dollValue == 510, $"doll_value actual={dollValue} want=510");

            // HUD 常驻「未封印 60%」：拿在手上就要看得见，不是结算才给。
            string shown = hud.DisplayedText;
            Check(shown.Contains("未封印 60%"), $"hud_missing_unsealed_rate text={shown}");
            Check(hud.ActiveCurseId == CursedItem.DollId, $"hud_active_curse actual={hud.ActiveCurseId}");

            // 掐表用「日志原文的时间戳」，不用协程里读到的 Time.time：
            // yield return null 的协程是在 Update 之后才恢复的，从那里掐表会晚一帧，
            // 20s 的窗口里这一帧看不出来，但 12s 的下边界会因此被量短。
            yield return WaitFor(() => doll.LaughCountdown > 0.05f, 3f, "doll_laugh_timer_never_armed");
            yield return WaitFor(() => doll.LaughCount >= 1, 26f, "doll_first_laugh_never");
            yield return WaitFor(() => doll.LaughCount >= 2, 26f, "doll_second_laugh_never");

            float armAt = TimeOfNth("GATE_CURSE_ARM id=curse_doll", 0);
            float laugh1At = TimeOfNth("GATE_CURSE_LAUGH id=curse_doll", 0);
            float laugh2At = TimeOfNth("GATE_CURSE_LAUGH id=curse_doll", 1);

            interval1 = laugh1At - armAt;
            interval2 = laugh2At - laugh1At;

            Check(armAt > 0f && laugh1At > 0f && laugh2At > 0f,
                $"laugh_timestamps_missing arm={armAt:F2} l1={laugh1At:F2} l2={laugh2At:F2}");

            Debug.Log($"[S4B] S4B_CURSE_LAUGH_METRICS first={interval1:F2} second={interval2:F2} count={doll.LaughCount}");

            Check(interval1 >= 12f - FrameTolerance && interval1 <= 20f + FrameTolerance,
                $"laugh_interval_1 actual={interval1:F2}");
            Check(interval2 >= 12f - FrameTolerance && interval2 <= 20f + FrameTolerance,
                $"laugh_interval_2 actual={interval2:F2}");

            // 区间本身再按日志原文精确断言一次（容差只出现在墙钟那一侧）。
            IReadOnlyList<float> drawn = DrawnIntervalsFromLog();
            Check(drawn.Count >= 2, $"laugh_interval_log_lines actual={drawn.Count}");
            foreach (float value in drawn)
            {
                Check(value >= 12f && value <= 20f, $"laugh_interval_drawn actual={value:F2}");
            }

            // 可归因（A9）：噪音必须带着 source=curse_doll 进总线，不能只加全局等级。
            Check(FindLine("NOISE_ADD source=curse_doll") >= 0, "noise_not_attributed_to_curse_doll");
            Check(FindLine("GATE_CURSE_LAUGH id=curse_doll") >= 0, "no_curse_laugh_gate_line");
            Check(FindLine("GATE_CURSE_WINDUP id=curse_doll") >= 0, "no_curse_windup_gate_line");

            // 普通货的松手即脱手路径不受影响（requiredReleaseHold 恒 0）。
            grab.Release(false);
            yield return new WaitForSeconds(0.4f);
            Check(!grab.IsHolding, "doll_still_held_after_release");
        }

        // ---------------- 2. 无字牌位 ----------------

        private IEnumerator RunTablet()
        {
            ClearLogs();

            tablet = runtime.ServerSpawnCurse(CurseKind.Tablet, HoldSpot());
            if (tablet == null)
            {
                Fail("tablet_spawn_failed");
                Application.Quit(1);
                yield break;
            }

            PlaceNearPlayer(tablet);
            yield return null;

            if (!grab.ServerForceGrab(tablet.Prop))
            {
                Fail("tablet_force_grab_failed");
                Application.Quit(1);
                yield break;
            }

            yield return new WaitForSeconds(0.4f);

            tabletValue = tablet.Prop.CurrentValue;
            Debug.Log($"[S4B] S4B_CURSE_VALUE id={tablet.CurseId} listed={(tablet.Prop.Profile != null ? tablet.Prop.Profile.value : 0)} current={tabletValue}");
            Check(tabletValue == 780, $"tablet_value actual={tabletValue} want=780");
            Check(Mathf.Abs(tablet.RequiredReleaseHold - 1.5f) < 0.001f,
                $"tablet_required_hold actual={tablet.RequiredReleaseHold:F2} want=1.5");

            // --- 2a. 不足 1.5s 松手 → 拒绝，货还在手上 ---
            ClearLogs();
            CurseInput.AutoPutDown = true;
            yield return new WaitForSeconds(0.8f);
            CurseInput.AutoPutDown = false;
            yield return new WaitForSeconds(0.4f);

            Check(grab.IsHolding, "tablet_dropped_on_short_hold");

            int rejectIndex = FindLine("GATE_CURSE_HOLD_REJECT need=1.5");
            Check(rejectIndex >= 0, "no_curse_hold_reject_line");
            rejectActual = ParseFloat(FindText(rejectIndex), "actual=");
            Check(rejectActual > 0.4f && rejectActual < 1.5f, $"reject_actual_out_of_window actual={rejectActual:F2}");

            // --- 2b. 按满 1.5s → 放下成功 ---
            ClearLogs();
            CurseInput.AutoPutDown = true;
            yield return WaitFor(() => !grab.IsHolding, 4f, "tablet_never_released_on_long_hold");
            CurseInput.AutoPutDown = false;

            // 出手速度必须在脱手那一帧量：晚了 0.3s 就是自由落体（9.8*0.3≈2.9m/s），
            // 会把「轻轻放下」误判成「扔出去」。同时只看水平分量，彻底避开重力。
            float releaseSpeed = 0f;
            if (tablet.Prop != null && tablet.Prop.Body != null)
            {
                Vector3 v = tablet.Prop.Body.linearVelocity;
                releaseSpeed = new Vector2(v.x, v.z).magnitude;
            }

            float throwSpeed = tablet.Prop != null && tablet.Prop.Profile != null
                ? tablet.Prop.Profile.ThrowSpeed
                : 0f;

            Debug.Log($"[S4B] S4B_CURSE_RELEASE speed={releaseSpeed:F2} throwSpeed={throwSpeed:F2}");

            yield return new WaitForSeconds(0.3f);

            Check(!grab.IsHolding, "tablet_still_held_after_long_hold");

            int okIndex = FindLine("GATE_CURSE_HOLD_OK need=1.5");
            Check(okIndex >= 0, "no_curse_hold_ok_line");
            okActual = ParseFloat(FindText(okIndex), "actual=");
            Check(okActual >= 1.5f, $"hold_ok_actual_too_short actual={okActual:F2}");

            // 放下不是扔：出手速度必须远低于「扔」会给出的速度。
            Check(releaseSpeed < throwSpeed * 0.7f,
                $"tablet_was_thrown speed={releaseSpeed:F2} throwSpeed={throwSpeed:F2}");
        }

        // ---------------- 3. 同一玩家两件：1 生效 1 休眠 ----------------

        private IEnumerator RunDualCarry()
        {
            CartController cart = CartController.Instance;
            if (cart == null)
            {
                Fail("no_cart_for_dual_carry");
                Application.Quit(1);
                yield break;
            }

            // 真实装车路径：抓起牌位 → 站到车旁（1.2m < interactReach 1.8m）→ E 键装车。
            // 不用「直接塞进 CartCargo」这种绕过闸门的写法：那等于测了个空壳。
            Vector3 cartPos = cart.transform.position;
            Teleport(vitals.transform, new Vector3(cartPos.x + 1.2f, 1f, cartPos.z));
            Face(vitals.transform, cartPos - vitals.transform.position);
            yield return new WaitForSeconds(0.3f);

            PlaceNearPlayer(tablet);
            yield return null;

            if (!grab.ServerForceGrab(tablet.Prop))
            {
                Fail("tablet_regrab_failed");
                Application.Quit(1);
                yield break;
            }

            yield return new WaitForSeconds(0.3f);

            S4AInput.AutoInteract = true;
            yield return WaitFor(() => IsOnCart(cart, tablet.Prop), 5f, "tablet_not_loaded_on_cart");
            S4AInput.AutoInteract = false;
            yield return new WaitForSeconds(0.3f);

            Check(IsOnCart(cart, tablet.Prop), "tablet_not_on_cart");

            // 手上再拿笑面偶 → 同一玩家同时招惹两件。
            PlaceNearPlayer(doll);
            yield return null;

            if (!grab.ServerForceGrab(doll.Prop))
            {
                Fail("doll_regrab_failed");
                Application.Quit(1);
                yield break;
            }

            yield return new WaitForSeconds(0.8f);

            IReadOnlyList<CursedItem> carried = runtime.CarriedBy(vitals.netId);
            carriedSeen = carried.Count;

            int dormant = 0;
            string dormantId = "none";
            string activeId = "none";

            foreach (CursedItem item in carried)
            {
                if (item == null)
                {
                    continue;
                }

                if (item.IsDormant)
                {
                    dormant++;
                    dormantId = item.CurseId;
                }
                else
                {
                    activeId = item.CurseId;
                }
            }

            dormantSeen = dormant;
            hudText = hud.DisplayedText;

            Debug.Log($"[S4B] S4B_CURSE_DUAL carried={carriedSeen} active={activeId} dormant={dormantId} hud={hudText.Replace('\n', '|')}");

            Check(carriedSeen == 2, $"dual_carry_count actual={carriedSeen} want=2");
            Check(dormant == 1, $"dual_dormant_count actual={dormant} want=1");
            Check(activeId != "none" && activeId != dormantId, $"dual_active_and_dormant_same id={activeId}");

            // 休眠不是隐藏：玩家必须看得见。
            Check(hud.HasDormant, "hud_missing_dormant_flag");
            Check(hudText.Contains("休眠"), $"hud_missing_dormant_word text={hudText}");
            Check(hudText.Contains("未封印 60%"), $"hud_missing_unsealed_rate_dual text={hudText}");

            // 休眠的那一件不许再产生噪音/发笑。
            int laughsBefore = runtime.LaughTotal;
            yield return new WaitForSeconds(2f);
            Check(runtime.LaughTotal == laughsBefore, "dormant_curse_still_produced_laugh");
        }

        // ---------------- -s4bleg：L1/L2/L3 生成 0/1/2 件 ----------------

        private IEnumerator RunLegGradient()
        {
            for (int leg = 1; leg <= 3; leg++)
            {
                int want = leg <= 1 ? 0 : (leg >= 3 ? 2 : 1);

                runtime.ServerDespawnAll();
                yield return new WaitForSeconds(0.35f);

                int spawned = runtime.ServerSpawnForLeg(leg);
                yield return new WaitForSeconds(0.45f);

                int actual = runtime.Items.Count;

                Debug.Log($"[S4B] GATE_CURSE_LEG leg={leg} want={want} spawned={spawned} count={actual}");
                Check(spawned == want, $"curse_leg_spawned leg={leg} want={want} actual={spawned}");
                Check(actual == want, $"curse_leg_count leg={leg} want={want} actual={actual}");
            }

            runtime.ServerDespawnAll();
            yield return new WaitForSeconds(0.2f);
        }

        // ==================== 收尾 ====================

        private IEnumerator Finish(string okMarker, string failMarker, string metrics)
        {
            bool ok = failures == 0;

            if (ok)
            {
                Debug.Log($"[S4B] {okMarker} {metrics} ok=True");
            }
            else
            {
                Debug.LogError($"[S4B] {failMarker} {metrics} failures={failures}");
            }

            yield return new WaitForSeconds(0.3f);
            Application.Quit(ok ? 0 : 1);
        }

        // ==================== 日志判据 ====================

        private void ClearLogs()
        {
            lines.Clear();
        }

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

        private string FindText(int index)
        {
            return index >= 0 && index < lines.Count ? lines[index].Text : string.Empty;
        }

        /// <summary>第 n 次（从 0 起）出现该片段的日志时间戳；找不到返回 -1。</summary>
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

        /// <summary>从 GATE_CURSE_ARM / GATE_CURSE_LAUGH 原文里读出本次抽到的间隔（精确判据，无容差）。</summary>
        private IReadOnlyList<float> DrawnIntervalsFromLog()
        {
            List<float> result = new();

            foreach (LogLine line in lines)
            {
                if (line.Text == null)
                {
                    continue;
                }

                bool arm = line.Text.Contains("GATE_CURSE_ARM id=curse_doll");
                bool laugh = line.Text.Contains("GATE_CURSE_LAUGH id=curse_doll");

                if (!arm && !laugh)
                {
                    continue;
                }

                float value = ParseFloat(line.Text, "interval=");
                if (value > 0f)
                {
                    result.Add(value);
                }
            }

            return result;
        }

        private static float ParseFloat(string text, string key)
        {
            if (string.IsNullOrEmpty(text))
            {
                return -1f;
            }

            int at = text.IndexOf(key, System.StringComparison.Ordinal);
            if (at < 0)
            {
                return -1f;
            }

            int start = at + key.Length;
            int end = start;

            while (end < text.Length && (char.IsDigit(text[end]) || text[end] == '.' || text[end] == '-'))
            {
                end++;
            }

            return float.TryParse(
                text.Substring(start, end - start),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out float value)
                ? value
                : -1f;
        }

        // ==================== 工具 ====================

        private static bool IsOnCart(CartController cart, GrabbableProp prop)
        {
            if (cart == null || cart.Cargo == null || prop == null)
            {
                return false;
            }

            foreach (GrabbableProp item in cart.Cargo.Items)
            {
                if (item == prop)
                {
                    return true;
                }
            }

            return false;
        }

        private Vector3 HoldSpot()
        {
            return vitals.transform.position + vitals.transform.forward * 0.8f + Vector3.up * 1.2f;
        }

        /// <summary>
        /// 把货挪到玩家面前。测试是「瞬移抓货」，不挪的话第一帧 ServerHoldStep
        /// 会因为 delta 远超 breakDistance 直接脱手（S4A_HOLD_RELEASE）。
        /// </summary>
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
            body.position = HoldSpot();
            body.rotation = Quaternion.identity;
            body.WakeUp();
            Physics.SyncTransforms();
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
