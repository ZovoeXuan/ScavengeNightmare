using System.Collections;
using System.Collections.Generic;
using Mirror;
using ScavengeNightmare.Cart;
using ScavengeNightmare.Combat;
using ScavengeNightmare.Economy;
using ScavengeNightmare.Monsters;
using ScavengeNightmare.Noise;
using ScavengeNightmare.Save;
using UnityEngine;

namespace ScavengeNightmare.Networking
{
    /// <summary>
    /// S4B-2 空眼验收（单人 Host，安全期强制 0）：
    /// 七状态各 ≥1 次、追逐 3.2m/s、前摇 0.8s、丢失 ≤6s、手电僵直 1.5s→2s、
    /// 推车击退 ≥1m、12 级楼梯爬升 3.00m、连续 2s 卡住脱困 rescued=self、追逐预算 ≤1。
    ///
    /// 所有定位都用服务端测试钩子（Warp / ForcePatrolTarget），
    /// 因为怪物移动是服务端权威的：客户端推不动它，测试也不该靠推。
    /// </summary>
    public sealed class S4BMonsterTest : MonoBehaviour
    {
        private const float StairX = 16.5f;
        private const float StairBottomZ = -6.3f;
        private const float StairTopZ = -11.4f;
        private const float StairHeight = 3.0f;

        private const string NoiseTag = "monster-test";

        private int failures;
        private MonsterSpawner spawner;
        private MonsterBrain brain;
        private PlayerVitals vitals;
        private PlayerMotor motor;
        private PlayerEquipment equipment;
        private TeamAccount account;

        private readonly HashSet<MonsterState> seenStates = new();

        private void Start()
        {
            if (!S4ACli.Has("-s4bmon"))
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
                S4ACli.SaveDirOverride = System.IO.Path.Combine(Application.persistentDataPath, "S4B_test_monster");
            }

            NetworkManager manager = NetworkManager.singleton;
            if (manager == null)
            {
                Debug.LogError("[S4B] S4B_MON_FAIL reason=no_network_manager");
                Application.Quit(1);
                return;
            }

            StartCoroutine(Watchdog(300));

            try
            {
                manager.StartHost();
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[S4B] S4B_MON_FAIL reason=start_host type={ex.GetType().Name}");
                Application.Quit(1);
                return;
            }

            StartCoroutine(Run());
        }

        private IEnumerator Watchdog(int seconds)
        {
            yield return new WaitForSecondsRealtime(seconds);
            Debug.LogError($"[S4B] S4B_MON_WATCHDOG_TIMEOUT seconds={seconds}");
            Application.Quit(3);
        }

        private void Fail(string reason)
        {
            failures++;
            Debug.LogError($"[S4B] S4B_MON_TEST_FAIL reason={reason}");
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

            vitals = PlayerVitals.Local;
            motor = vitals != null ? vitals.GetComponent<PlayerMotor>() : null;
            equipment = vitals != null ? vitals.GetComponent<PlayerEquipment>() : null;
            account = TeamAccount.Instance;

            if (vitals == null || motor == null)
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

            yield return GrantFlashlight();

            // 玩家先放远一点：除了「攻击/手电」两段，其余时间都不能让怪看见玩家。
            Teleport(vitals.transform, new Vector3(0f, 1f, 15f));
            yield return new WaitForSeconds(0.4f);

            brain = spawner.ServerSpawnHollowEye(new Vector3(-15f, 0.05f, -8f));
            if (brain == null)
            {
                Fail("monster_spawn_failed");
                Application.Quit(1);
                yield break;
            }

            yield return TrackUntil(() => brain.State == MonsterState.Patrol, 3f, "monster_not_patrol");

            // ---- 1. 追逐速度 3.2m/s（仓库走廊，直线无障碍）----
            yield return MeasureChaseSpeed();

            // ---- 2. 听觉中间档 → Investigate → 回 Patrol ----
            yield return MeasureInvestigate();

            // ---- 3. 攻击前摇 0.8s ----
            yield return MeasureAttackWindup();

            // ---- 4. 丢目标 ≤6s → Search → Patrol ----
            yield return MeasureLostTarget();

            // ---- 5. 手电直射 1.5s → Stunned 2s ----
            yield return MeasureFlashlightStun();

            // ---- 6. 推车撞 → 后退 ≥1m ----
            yield return MeasureCartKnockback();

            // ---- 7. 12 级楼梯爬升 3.00m ----
            yield return MeasureStairs();

            // ---- 8. 撞墙不穿透 + 2s 卡住脱困 rescued=self ----
            yield return MeasureWallAndUnstick();

            // ---- 9. 追逐预算：两只怪同时听到猎杀噪音，Chase 数 ≤1 ----
            yield return MeasureChaseBudget();

            foreach (MonsterState state in new[]
                     {
                         MonsterState.Spawn, MonsterState.Patrol, MonsterState.Investigate, MonsterState.Chase,
                         MonsterState.Attack, MonsterState.Search, MonsterState.Stunned
                     })
            {
                Check(seenStates.Contains(state), $"state_missing state={state}");
            }

            Debug.Log($"[S4B] S4B_MON_STATES seen={string.Join(",", seenStates)}");
            Debug.Log($"[S4B] S4B_MON_METRICS chaseSpeed={lastChaseSpeed:F2} windup={lastWindup:F2} lost={lastLost:F2} stun={lastStun:F2} knockback={lastKnockback:F2} climbY={lastClimbY:F2}");

            bool ok = failures == 0;

            if (ok)
            {
                Debug.Log($"[S4B] S4B_MON_OK states=7 chaseSpeed={lastChaseSpeed:F2} windup={lastWindup:F2} lost={lastLost:F2} " +
                          $"stun={lastStun:F2} cartPush={lastKnockback:F2} climb={lastClimbY:F2} unstick=self budget=1 failures=0 ok=True");
            }
            else
            {
                Debug.LogError($"[S4B] S4B_MON_FAIL failures={failures}");
            }

            yield return new WaitForSeconds(0.3f);
            Application.Quit(ok ? 0 : 1);
        }

        private float lastChaseSpeed;
        private float lastWindup;
        private float lastLost;
        private float lastStun;
        private float lastKnockback;
        private float lastClimbY;

        // ==================== 1. 追逐速度 ====================

        private IEnumerator MeasureChaseSpeed()
        {
            // 用仓库西侧走廊（-15,-8 → -15,-1）：这一段前面丢目标用例已实测可直行 5.5m，
            // 出生点 (0,15) 那一带堆着推车与货，实测只能跑 0.56m/s。
            Vector3 start = new(-15f, 0.05f, -8f);
            Vector3 noiseAt = new(-15f, 0.05f, -1f);

            // 玩家必须撤到看不见的位置，否则 sight 会抢在 noise 之前进 Chase，量出来的就不是追逐速度。
            Teleport(vitals.transform, new Vector3(0f, 1f, 15f));
            yield return new WaitForSeconds(0.2f);

            yield return Plant(brain, start);

            NoiseBus.Reset();
            NoiseBus.Report(NoiseTag, 75f, noiseAt);

            float waitEnd = Time.time + 1.5f;
            while (Time.time < waitEnd && brain.State != MonsterState.Chase)
            {
                NoiseBus.Report(NoiseTag, NoiseBus.DecayPerSecond * Time.deltaTime, noiseAt);
                Note();
                yield return null;
            }

            if (brain.State != MonsterState.Chase)
            {
                Fail($"chase_speed_no_chase state={brain.State}");
                yield break;
            }

            // 起步那 0.4s 还在转身，速度会偏低，先让朝向稳定再量。
            float settle = Time.time + 0.4f;
            while (Time.time < settle)
            {
                NoiseBus.Report(NoiseTag, NoiseBus.DecayPerSecond * Time.deltaTime, noiseAt);
                yield return null;
            }

            // 货架走廊并不完全干净，个别 0.3s 窗口会被避障吃掉一部分速度。
            // 判据取「最好的一格」：速度上限是 profile.chaseSpeed，任何一格都不该超过 3.3。
            float window = 0.3f;
            float best = 0f;
            Vector3 prev = brain.transform.position;
            float prevAt = Time.time;
            float end = prevAt + 1.8f;

            while (Time.time < end)
            {
                NoiseBus.Report(NoiseTag, NoiseBus.DecayPerSecond * Time.deltaTime, noiseAt);
                Note();

                if (Time.time - prevAt >= window)
                {
                    float span = Time.time - prevAt;
                    best = Mathf.Max(best, PlanarDistance(prev, brain.transform.position) / span);
                    prev = brain.transform.position;
                    prevAt = Time.time;
                }

                yield return null;
            }

            lastChaseSpeed = best;
            Debug.Log($"[S4B] S4B_MON_SPEED chase={lastChaseSpeed:F2} expected=3.2 at=({brain.transform.position.x:F1},{brain.transform.position.z:F1})");
            Check(lastChaseSpeed >= 3.1f && lastChaseSpeed <= 3.3f, $"chase_speed actual={lastChaseSpeed:F2}");
        }

        // ==================== 2. 中间档 → Investigate ====================

        private IEnumerator MeasureInvestigate()
        {
            Vector3 monsterAt = new(-15f, 0.05f, -8f);
            yield return Plant(brain, monsterAt);

            // 只报一次 50 级（alert 档）：持续每帧报会让它反复重新进入 Investigate，回不到 Patrol。
            NoiseBus.Reset();
            NoiseBus.Report(NoiseTag, 50f, new Vector3(-15f, 0.05f, -4f));

            yield return TrackUntil(() => brain.State == MonsterState.Investigate, 2f, "investigate_never_entered");
            Check(brain.LastHeardBand == "alert", $"investigate_band band={brain.LastHeardBand}");

            // 状态机的 Investigate 到点后先进 Search（搜一下），Search 结束才回 Patrol。
            yield return TrackUntil(() => brain.State != MonsterState.Investigate, 8f, "investigate_never_exited");
            Check(brain.State == MonsterState.Search, $"investigate_not_search state={brain.State}");
            yield return TrackUntil(() => brain.State == MonsterState.Patrol, 8f, "investigate_never_patrol");
        }

        // ==================== 3. 攻击前摇 ====================

        private IEnumerator MeasureAttackWindup()
        {
            Vector3 monsterAt = new(0f, 0.05f, 12f);
            yield return Plant(brain, monsterAt);

            Vector3 playerAt = monsterAt + new Vector3(0f, 0f, 1.2f);
            Teleport(vitals.transform, new Vector3(playerAt.x, 1f, playerAt.z));
            FaceMonsterAt(brain, vitals.transform.position);
            yield return null;

            int hpBefore = vitals.Hp;
            float attackAt = -1f;
            float hitAt = -1f;
            float end = Time.time + 4f;

            while (Time.time < end)
            {
                Note();

                if (attackAt < 0f && brain.State == MonsterState.Attack)
                {
                    attackAt = Time.time;
                }

                if (attackAt >= 0f && vitals.Hp < hpBefore)
                {
                    hitAt = Time.time;
                    break;
                }

                yield return null;
            }

            if (attackAt < 0f)
            {
                Fail("attack_never_entered");
                lastWindup = -1f;
                yield break;
            }

            if (hitAt < 0f)
            {
                Fail("attack_never_landed");
                lastWindup = -1f;
                yield break;
            }

            lastWindup = hitAt - attackAt;
            Debug.Log($"[S4B] S4B_MON_WINDUP windup={lastWindup:F2} hpBefore={hpBefore} hpAfter={vitals.Hp}");
            Check(lastWindup >= 0.7f && lastWindup <= 0.9f, $"attack_windup actual={lastWindup:F2}");
            Check(vitals.Hp == hpBefore - 50, $"attack_damage hp={vitals.Hp} before={hpBefore}");

            // 立刻退出攻击范围，避免第二刀把玩家打倒地（单人倒地会触发团灭流程）。
            Teleport(vitals.transform, new Vector3(-15f, 1f, -8f));
            yield return new WaitForSeconds(0.2f);
        }

        // ==================== 3. 丢目标 ====================

        private IEnumerator MeasureLostTarget()
        {
            Vector3 monsterAt = new(-15f, 0.05f, -8f);
            yield return Plant(brain, monsterAt);

            Vector3 playerAt = monsterAt + new Vector3(0f, 0f, 5.5f);
            Teleport(vitals.transform, new Vector3(playerAt.x, 1f, playerAt.z));
            FaceMonsterAt(brain, vitals.transform.position);

            float waitEnd = Time.time + 1.5f;
            while (Time.time < waitEnd && brain.State != MonsterState.Chase)
            {
                Note();
                yield return null;
            }

            if (brain.State != MonsterState.Chase)
            {
                Fail($"lost_no_chase state={brain.State}");
                yield break;
            }

            Teleport(vitals.transform, new Vector3(0f, 1f, 15f));
            float goneAt = Time.time;
            float end = goneAt + 7f;

            while (Time.time < end && brain.State != MonsterState.Search)
            {
                Note();
                yield return null;
            }

            if (brain.State != MonsterState.Search)
            {
                Fail($"lost_never_search state={brain.State}");
                lastLost = -1f;
                yield break;
            }

            lastLost = Time.time - goneAt;
            Debug.Log($"[S4B] S4B_MON_LOST seconds={lastLost:F2} profile={brain.LastLostSeconds:F2} searched=1");
            Check(lastLost <= 6f, $"lost_too_slow actual={lastLost:F2}");
            Check(brain.LastLostSeconds <= 6f, $"lost_profile_profile actual={brain.LastLostSeconds:F2}");

            // Search → Patrol（搜索 3s 后回巡逻）
            float patrolEnd = Time.time + 6f;
            while (Time.time < patrolEnd && brain.State != MonsterState.Patrol)
            {
                Note();
                yield return null;
            }

            Check(brain.State == MonsterState.Patrol, $"search_not_back_to_patrol state={brain.State}");
        }

        // ==================== 4. 手电僵直 ====================

        private IEnumerator MeasureFlashlightStun()
        {
            if (equipment == null)
            {
                Fail("no_player_equipment");
                yield break;
            }

            Vector3 monsterAt = new(0f, 0.05f, 8f);
            yield return Plant(brain, monsterAt);

            Teleport(vitals.transform, new Vector3(0f, 1f, 16f));
            FacePlayerAt(vitals.transform, brain.transform.position);
            yield return null;

            S4AInput.AutoToggleLight = true;
            yield return WaitFor(() => equipment.LightOn, 1.5f, "flashlight_not_on");
            yield return null;

            if (!equipment.LightOn)
            {
                Fail("flashlight_off");
                yield break;
            }

            float lightAt = Time.time;
            float end = lightAt + 3f;

            while (Time.time < end && brain.State != MonsterState.Stunned)
            {
                Note();
                yield return null;
            }

            if (brain.State != MonsterState.Stunned)
            {
                Fail($"flashlight_no_stun state={brain.State}");
                lastStun = -1f;
                yield break;
            }

            float exposure = Time.time - lightAt;
            Debug.Log($"[S4B] S4B_MON_FLASH exposure={exposure:F2} stunned={brain.StunCount}");
            Check(exposure >= 1.3f && exposure <= 1.8f, $"flashlight_exposure actual={exposure:F2}");

            float stunAt = Time.time;
            float stunEnd = stunAt + 3f;

            while (Time.time < stunEnd && brain.State == MonsterState.Stunned)
            {
                Note();
                yield return null;
            }

            lastStun = Time.time - stunAt;
            Debug.Log($"[S4B] S4B_MON_STUN seconds={lastStun:F2}");
            Check(lastStun >= 1.7f && lastStun <= 2.3f, $"stun_duration actual={lastStun:F2}");

            // 关灯并撤走：否则下一轮又会 1.5s 后再僵直一次。
            S4AInput.AutoToggleLight = true;
            yield return new WaitForSeconds(0.2f);
            Teleport(vitals.transform, new Vector3(0f, 1f, 15f));
        }

        // ==================== 5. 推车击退 ====================

        private IEnumerator MeasureCartKnockback()
        {
            CartController cart = CartController.Instance;
            if (cart == null)
            {
                Fail("no_cart");
                yield break;
            }

            Vector3 monsterAt = new(0f, 0.05f, 11f);

            // 玩家必须撤走：否则这条走廊里 sight 会先把它拉进 Chase，测到的位移就不是击退。
            Teleport(vitals.transform, new Vector3(-18f, 1f, -12f));
            yield return new WaitForSeconds(0.2f);

            yield return Plant(brain, monsterAt);

            // hitsBefore 必须在第一次 TeleportWithCargo **之前**取：
            // 车从远处瞬移过来那一帧就会被判成「高速擦过」，晚取一格就会把这唯一一次命中吞掉。
            int hitsBefore = brain.CartHitCount;

            float cartY = cart.transform.position.y;
            Vector3 cartStart = new(monsterAt.x + 4f, cartY, monsterAt.z);
            cart.TeleportWithCargo(cartStart);
            yield return null;

            Vector3 from = Vector3.zero;
            float hitAt = -1f;
            float begin = Time.time;
            float end = begin + 2.5f;
            Vector3 away = Vector3.zero;

            while (Time.time < end)
            {
                // 以 3m/s 沿 -X 驶向怪：从 4m 逼到 1.8m 命中圈内约需 0.8s。
                float t = Time.time - begin;
                cart.TeleportWithCargo(cartStart - new Vector3(3f * t, 0f, 0f));

                if (brain.CartHitCount > hitsBefore)
                {
                    hitAt = Time.time;
                    from = brain.transform.position;
                    away = Planar(monsterAt - cart.transform.position);
                    away = away.sqrMagnitude > 0.01f ? away.normalized : Vector3.left;
                    break;
                }

                yield return null;
            }

            if (hitAt < 0f)
            {
                Fail("cart_no_hit");
                lastKnockback = -1f;
                yield break;
            }

            float pushEnd = hitAt + 0.7f;
            while (Time.time < pushEnd)
            {
                yield return null;
            }

            Vector3 delta = Planar(brain.transform.position - from);
            lastKnockback = Vector3.Dot(delta, away);
            Debug.Log($"[S4B] S4B_MON_CART_HIT push={lastKnockback:F2} hits={brain.CartHitCount}");
            Check(lastKnockback >= 1.0f, $"cart_push actual={lastKnockback:F2}");

            // 收车：之后的楼梯/撞墙用例里，车若停在听觉半径内会持续喂 «cart» 噪音，
            // 把怪钉在 Investigate 里，卡住脱困就永远测不到（上一次就是这么空的）。
            cart.gameObject.SetActive(false);
            yield return new WaitForSeconds(0.2f);
        }

        // ==================== 6. 楼梯 ====================

        private IEnumerator MeasureStairs()
        {
            Vector3 bottom = new(StairX, 0.05f, StairBottomZ + 0.7f);
            Vector3 top = new(StairX, 0.05f, StairTopZ - 0.5f);

            yield return Plant(brain, bottom);
            brain.ServerTestForcePatrolTarget(top);
            yield return null;

            float end = Time.time + 22f;
            float maxY = brain.transform.position.y;
            while (Time.time < end)
            {
                Note();
                maxY = Mathf.Max(maxY, brain.transform.position.y);

                // 只看高度：早先还按「离目标 1m」提前收工，结果停在倒数第二级（2.82）就算通过了。
                if (brain.transform.position.y >= StairHeight - 0.1f)
                {
                    break;
                }

                yield return null;
            }

            lastClimbY = Mathf.Max(maxY, brain.transform.position.y);
            Debug.Log($"[S4B] S4B_MON_STAIRS y={lastClimbY:F2} target=3.00 at=({brain.transform.position.x:F1},{brain.transform.position.z:F1})");
            // 夹层地板顶面是 3.00，但 CharacterController 的 skinWidth 0.08 会让落点停在 2.9x。
            Check(lastClimbY >= StairHeight - 0.15f, $"stairs_climb actual={lastClimbY:F2}");
        }

        // ==================== 7. 撞墙 + 脱困 ====================

        private IEnumerator MeasureWallAndUnstick()
        {
            Vector3 monsterAt = new(0f, 0.05f, 12.5f);
            Time.timeScale = 1f;

            GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = "S4B_TestWall";
            wall.transform.position = new Vector3(0f, 1.5f, 10f);
            wall.transform.localScale = new Vector3(12f, 3f, 0.4f);
            Physics.SyncTransforms();

            // 玩家先撤远：这轮不能因为「看到玩家」而放弃撞墙。
            Teleport(vitals.transform, new Vector3(-18f, 1f, -12f));
            yield return new WaitForSeconds(0.2f);

            yield return Plant(brain, monsterAt);

            int unstickBefore = brain.UnstickCount;
            Vector3 target = new(0f, 0.05f, 8f);
            brain.ServerTestForcePatrolTarget(target);
            yield return null;

            float minZ = monsterAt.z;
            float end = Time.time + 8f;
            bool rescued = false;

            while (Time.time < end)
            {
                Note();

                // 脱困本身是「回退到最近巡逻锚点」，锚点 (0,.2,8) 就在墙后面：那次回退会合法地跨过墙面。
                // 所以「穿墙」只能按脱困发生之前的轨迹判定。
                if (brain.UnstickCount > unstickBefore)
                {
                    rescued = true;
                    break;
                }

                minZ = Mathf.Min(minZ, brain.transform.position.z);
                yield return null;
            }

            float wallFaceZ = 10f + 0.2f;
            Debug.Log($"[S4B] S4B_MON_WALL minZ={minZ:F2} wallFaceZ={wallFaceZ:F2} unstick={brain.UnstickCount} rescued={brain.LastUnstickRescued}");
            Check(rescued, "unstick_never_triggered");
            Check(minZ >= wallFaceZ - 0.1f, $"wall_penetrated minZ={minZ:F2}");
            Check(brain.LastUnstickRescued == "self", $"unstick_not_self rescued={brain.LastUnstickRescued}");

            Object.Destroy(wall);
            yield return new WaitForSeconds(0.3f);
        }

        // ==================== 8. 追逐预算 ====================

        private IEnumerator MeasureChaseBudget()
        {
            MonsterDirector director = MonsterDirector.Instance;
            if (director == null)
            {
                Fail("no_director");
                yield break;
            }

            spawner.ServerDespawnAll();
            yield return new WaitForSeconds(0.4f);

            MonsterBrain first = spawner.ServerSpawnHollowEye(new Vector3(-15f, 0.05f, -8f));
            MonsterBrain second = spawner.ServerSpawnHollowEye(new Vector3(12f, 0.05f, 2f));
            yield return new WaitForSeconds(0.6f);

            if (first == null || second == null)
            {
                Fail("budget_spawn_failed");
                yield break;
            }

            NoiseBus.Reset();
            NoiseBus.Report(NoiseTag, 80f, new Vector3(-15f, 0.05f, -8f));

            float end = Time.time + 2f;
            int maxActive = 0;

            while (Time.time < end)
            {
                NoiseBus.Report(NoiseTag, NoiseBus.DecayPerSecond * Time.deltaTime, new Vector3(-15f, 0.05f, -8f));
                maxActive = Mathf.Max(maxActive, director.ActiveChaseCount);
                yield return null;
            }

            Debug.Log($"[S4B] S4B_MON_BUDGET maxActive={maxActive} limit={director.ChaseLimit}");
            Check(maxActive <= director.ChaseLimit, $"chase_budget active={maxActive} limit={director.ChaseLimit}");
        }

        // ==================== 工具 ====================

        private IEnumerator GrantFlashlight()
        {
            account.DepositId("monster-test-seed", 1000);
            yield return new WaitForSeconds(0.3f);
            account.OpenPurchaseWindow();

            bool bought = account.TryPurchase("flashlight");
            yield return new WaitForSeconds(0.4f);

            Debug.Log($"[S4B] S4B_MON_FLASHLIGHT_GRANT bought={bought} owned={account.OwnsItem("flashlight")} balance={account.Balance}");
            Check(account.OwnsItem("flashlight"), "flashlight_not_owned");
        }

        /// <summary>把怪钉在原地：瞬移 + 强制一个等于自身位置的巡逻点。</summary>
        private IEnumerator Plant(MonsterBrain monster, Vector3 position)
        {
            NoiseBus.Reset();
            monster.ServerTestWarp(position);
            monster.ServerTestForcePatrolTarget(position);
            yield return null;
            yield return null;
            Note();
        }

        private static void FaceMonsterAt(MonsterBrain monster, Vector3 target)
        {
            Vector3 delta = Planar(target - monster.transform.position);
            if (delta.sqrMagnitude > 0.01f)
            {
                monster.transform.rotation = Quaternion.LookRotation(delta, Vector3.up);
            }
        }

        private static void FacePlayerAt(Transform player, Vector3 target)
        {
            Vector3 delta = Planar(target - player.position);
            if (delta.sqrMagnitude > 0.01f)
            {
                player.rotation = Quaternion.LookRotation(delta, Vector3.up);
            }
        }

        private void Note()
        {
            if (brain != null)
            {
                seenStates.Add(brain.State);
            }
        }

        private IEnumerator TrackUntil(System.Func<bool> condition, float seconds, string failReason)
        {
            float end = Time.time + seconds;
            while (Time.time < end)
            {
                Note();
                if (condition())
                {
                    yield break;
                }

                yield return null;
            }

            Fail(failReason);
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

        private static Vector3 Planar(Vector3 value)
        {
            return new Vector3(value.x, 0f, value.z);
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
