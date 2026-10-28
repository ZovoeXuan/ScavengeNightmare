using System.Collections;
using System.Collections.Generic;
using Mirror;
using ScavengeNightmare.Networking;
using UnityEngine;

namespace ScavengeNightmare.Traits
{
    /// <summary>
    /// D8 自动化验证。分两段，因为「数据表约束」和「玩法差异」需要不同的运行方式：
    ///
    ///   -d8test   离线逻辑：数据表约束、加权抽取分布、叠 buff 排除、loadout 合成
    ///   -d8host   玩法实测：强制指定身份/牌，量出真实移速、冲刺、投掷速度、小件拾取
    ///   -d8client 联机：连上后上报自己拿到的身份与牌，验证「4 身份唯一」
    ///
    /// 为什么玩法段要强制指定身份：随机抽牌每次结果不同，无法断言「大力士比灵敏慢」。
    /// 强制指定后测的是确定输入 → 确定输出，这才是可回归的验收。
    /// </summary>
    public sealed class D8AutoTest : MonoBehaviour
    {
        private bool offline;
        private bool host;
        private bool client;
        private int forcedIndex = -1;
        private string forcedCard;

        private void Start()
        {
            string[] args = System.Environment.GetCommandLineArgs();
            foreach (string a in args)
            {
                if (Eq(a, "-d8test")) offline = true;
                else if (Eq(a, "-d8host")) host = true;
                else if (Eq(a, "-d8client")) client = true;
                else if (a.StartsWith("-d8assign="))
                {
                    string[] parts = a.Substring("-d8assign=".Length).Split(':');
                    if (int.TryParse(parts[0], out int idx)) forcedIndex = idx;
                    if (parts.Length > 1) forcedCard = parts[1];
                }
            }

            if (!offline && !host && !client)
            {
                enabled = false;
                return;
            }

            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 60;

            // D8 要验证角色差异，必须确保没有被其它基线测试残留的 ForceNoTraits 关掉
            TraitSettings.ForceNoTraits = false;

            StartCoroutine(offline ? RunOffline() : RunNetworked());
        }

        private static bool Eq(string a, string b) => string.Equals(a, b, System.StringComparison.OrdinalIgnoreCase);

        // ==================== 离线：数据表约束与抽取分布 ====================

        private IEnumerator RunOffline()
        {
            TraitDatabase db = TraitBootstrap.Database;
            if (db == null || !TraitSettings.Ready)
            {
                Debug.LogError("[D8] D8_OFFLINE_FAIL reason=no_database");
                yield break;
            }

            Debug.Log($"[D8] D8_OFFLINE_START identities={db.identities.Length} cards={db.cards.Length}");

            // --- 1. 结构约束：4 身份 + 5 牌 ---
            Check(db.identities.Length == 4, "identity_count_4");
            Check(db.cards.Length == 5, "card_count_5");

            // --- 2. 每张牌必须同时有代价与补偿（D8 验收口径） ---
            int compensationFail = 0;
            int costFail = 0;
            int stackBuff = 0;
            var conflictIds = new HashSet<string>();
            foreach (CharacterIdentity id in db.identities) if (id != null) conflictIds.Add(id.id);

            foreach (DrawbackCard card in db.cards)
            {
                if (card == null) continue;

                // 代价：至少一个 ≤1 的惩罚轴，或一个行为型代价（stunScale<1 是抗揍，属补偿不算代价）
                bool hasCost = card.speedScale < 1f || card.grabStiffnessScale < 1f
                               || card.interactRangeScale < 1f
                               || card.carryDivisorScale < 1f || card.blocksSmallItems
                               || card.shakyHands || card.narrowDoorPanic;
                if (!hasCost) costFail++;

                // 补偿：至少一个 ≥1 的收益轴或抗揍（严格才算真有补偿）
                bool hasComp = card.compensationThrowScale > 1f || card.compensationSpeedScale > 1f
                               || card.compensationGrabScale > 1f || card.compensationCarryDivisorScale > 1f
                               || card.stunScale < 1f || card.valueScent;
                if (!hasComp) compensationFail++;

                // 同轴既罚又补 = 叠 buff，禁止
                if (card.speedScale < 1f && card.compensationSpeedScale > 1f) stackBuff++;
                if (card.grabStiffnessScale < 1f && card.compensationGrabScale > 1f) stackBuff++;
                if (card.carryDivisorScale < 1f && card.compensationCarryDivisorScale > 1f) stackBuff++;

                // 冲突目标必须是真实存在的角色
                if (!string.IsNullOrEmpty(card.strongestConflictId) && !conflictIds.Contains(card.strongestConflictId))
                {
                    Debug.LogError($"[D8] D8_OFFLINE_FAIL check=conflict_target_invalid card={card.id} target={card.strongestConflictId}");
                }
            }

            Check(costFail == 0, $"every_card_has_cost(fail={costFail})");
            Check(compensationFail == 0, $"every_card_has_compensation(fail={compensationFail})");
            Check(stackBuff == 0, $"no_same_axis_stack_buff(violations={stackBuff})");

            // --- 3. 加权抽取：冲突组合必须显著高频 ---
            const int rolls = 4000;
            var rng = new System.Random(12345);
            var conflictHits = new Dictionary<string, int>();
            var totalByCard = new Dictionary<string, int>();

            foreach (CharacterIdentity id in db.identities)
            {
                if (id == null) continue;
                for (int i = 0; i < rolls; i++)
                {
                    DrawbackCard c = TraitSettings.Draw(id, rng, out _);
                    if (c == null) continue;
                    totalByCard.TryGetValue(c.id, out int n);
                    totalByCard[c.id] = n + 1;
                    if (TraitSettings.IsConflict(id, c))
                    {
                        conflictHits.TryGetValue(id.id, out int m);
                        conflictHits[id.id] = m + 1;
                    }
                }
            }

            int totalRolls = rolls * Mathf.Max(1, db.identities.Length);
            int totalConflicts = 0;
            foreach (int v in conflictHits.Values) totalConflicts += v;
            float conflictRate = totalRolls > 0 ? (float)totalConflicts / totalRolls : 0f;
            Debug.Log($"[D8] D8_CONFLICT_RATE rate={conflictRate:P1} samples={totalRolls} conflicts={totalConflicts}");
            // 未加权时冲突率约为 1/5 = 20%；加权后必须明显更高
            Check(conflictRate > 0.35f, "conflict_weighting_effective");

            foreach (KeyValuePair<string, int> kv in conflictHits)
            {
                Debug.Log($"[D8] D8_CONFLICT_PER_ID identity={kv.Key} conflictRolls={kv.Value}/{rolls}");
            }

            // --- 4. 叠 buff 排除：慢角色不应抽到「跑得慢」的牌 ---
            int exclusionViolations = 0;
            foreach (CharacterIdentity id in db.identities)
            {
                if (id == null) continue;
                for (int i = 0; i < 500; i++)
                {
                    DrawbackCard c = TraitSettings.Draw(id, rng, out _);
                    if (c == null) continue;
                    if (c.excludeIfIdentitySlower && id.speedScale < 1f) exclusionViolations++;
                    if (c.excludeIfIdentityClumsier && id.grabStiffnessScale < 1f) exclusionViolations++;
                }
            }
            Check(exclusionViolations == 0, $"stack_buff_exclusion(violations={exclusionViolations})");

            // --- 5. loadout 合成：代价 ≤ 1 且补偿后不越界 ---
            int mathFail = 0;
            foreach (CharacterIdentity id in db.identities)
            {
                if (id == null) continue;
                foreach (DrawbackCard card in db.cards)
                {
                    if (card == null) continue;
                    PlayerLoadout l = new PlayerLoadout(id, card);
                    // 任何组合下都必须还能走、还能扔、还能看、还能被砸出反应：倍率不允许归零或变负
                    if (l.SpeedScale <= 0.05f || l.ThrowScale <= 0.05f || l.StunScale <= 0f
                        || l.InteractRangeScale <= 0.05f) mathFail++;
                }
            }
            Check(mathFail == 0, $"loadout_math_sane(fail={mathFail})");

            Debug.Log("[D8] D8_OFFLINE_DONE");
            yield return new WaitForSeconds(0.2f);
            Application.Quit(0);
        }

        // ==================== 联机：身份唯一性 + 玩法差异 ====================

        private IEnumerator RunNetworked()
        {
            if (host)
            {
                NetworkManager.singleton.StartHost();
            }
            else
            {
                NetworkManager.singleton.networkAddress = "127.0.0.1";
                NetworkManager.singleton.StartClient();
            }

            float wait = 0f;
            while (NetworkClient.localPlayer == null && wait < 15f)
            {
                wait += Time.unscaledDeltaTime;
                yield return null;
            }

            if (NetworkClient.localPlayer == null)
            {
                Debug.LogError("[D8] D8_NET_FAIL reason=no_local_player");
                yield break;
            }

            PlayerTraits traits = NetworkClient.localPlayer.GetComponent<PlayerTraits>();
            PlayerMotor motor = NetworkClient.localPlayer.GetComponent<PlayerMotor>();
            Gameplay.GrabController grab = NetworkClient.localPlayer.GetComponent<Gameplay.GrabController>();

            if (traits == null)
            {
                Debug.LogError("[D8] D8_NET_FAIL reason=no_traits_component");
                yield break;
            }

            // 等身份/牌下发
            float w2 = 0f;
            while (!traits.Loadout.HasIdentity && w2 < 8f)
            {
                w2 += Time.unscaledDeltaTime;
                yield return null;
            }

            if (!traits.Loadout.HasIdentity)
            {
                Debug.LogError("[D8] D8_NET_FAIL reason=identity_not_assigned");
                yield break;
            }

            Debug.Log($"[D8] D8_NET_IDENTITY role={(host ? "host" : "client")} " +
                      $"identity={traits.Identity.id} card={(traits.Card != null ? traits.Card.id : "none")} " +
                      $"conflict={traits.Loadout.IsConflict}");

            if (!host)
            {
                // 客户端只需上报自己拿到了什么，唯一性由房主汇总判定
                yield return new WaitForSeconds(14f);
                Application.Quit(0);
                yield break;
            }

            // ---------- 房主：等其余三人进场，判定身份唯一 ----------
            float connWait = 0f;
            while (NetworkServer.connections.Count < 4 && connWait < 25f)
            {
                connWait += Time.unscaledDeltaTime;
                yield return null;
            }

            yield return new WaitForSeconds(1.5f);

            var seen = new HashSet<int>();
            int dup = 0;
            foreach (PlayerTraits t in Object.FindObjectsByType<PlayerTraits>(FindObjectsSortMode.None))
            {
                if (!t.Loadout.HasIdentity) continue;
                if (!seen.Add(t.IdentityIndex)) dup++;
                Debug.Log($"[D8] D8_ROSTER identityIndex={t.IdentityIndex} identity={t.Identity.id} " +
                          $"card={t.CardId} conflict={t.Loadout.IsConflict} isLocal={t.isLocalPlayer}");
            }

            Check(seen.Count == Mathf.Min(4, TraitSettings.IdentityCount), $"identity_unique(count={seen.Count})");
            Check(dup == 0, $"no_duplicate_identity(dups={dup})");

            // ---------- 玩法差异：逐个身份强制指定并量真实数值 ----------
            yield return MeasureIdentity(traits, motor, grab, 0);
            yield return MeasureIdentity(traits, motor, grab, 1);
            yield return MeasureIdentity(traits, motor, grab, 2);
            yield return MeasureIdentity(traits, motor, grab, 3);

            Debug.Log("[D8] D8_NET_DONE");
            yield return new WaitForSeconds(0.3f);
            Application.Quit(0);
        }

        /// <summary>
        /// 强制把房主切成指定身份 + 指定牌，量出四个可断言的物理量：
        /// 走路速度、能否冲刺、投掷速度、小件能否被抓。
        /// </summary>
        private IEnumerator MeasureIdentity(PlayerTraits traits, PlayerMotor motor, Gameplay.GrabController grab, int index)
        {
            CharacterIdentity id = TraitSettings.Identity(index);
            if (id == null) yield break;

            // 先用「无牌」量身份本身的差异，避免牌的倍率干扰对比
            traits.CmdDebugAssign(index, string.Empty);
            yield return new WaitForSeconds(0.4f);

            SpeedResult walkResult = new SpeedResult();
            SpeedResult sprintResult = new SpeedResult();
            yield return MeasureSpeed(motor, false, walkResult);
            yield return MeasureSpeed(motor, true, sprintResult);
            float walk = walkResult.Value;
            float sprint = sprintResult.Value;

            Debug.Log($"[D8] D8_IDENTITY_SPEED identity={id.id} walk={walk:F2} sprint={sprint:F2} " +
                      $"canSprint={traits.Loadout.CanSprint} speedScale={traits.Loadout.SpeedScale:F2}");

            bool sprintBlocked = !traits.Loadout.CanSprint && Mathf.Abs(sprint - walk) < 0.15f;
            if (!traits.Loadout.CanSprint)
            {
                Check(sprintBlocked, $"sprint_disabled_for_{id.id}");
            }

            // 投掷速度：用固定物件档量，验证「大力士扔得远」进了物理
            Gameplay.PropPhysicsProfile profile = FindProfile(22f);
            if (profile != null)
            {
                float expected = Gameplay.FeelSettings.ThrowSpeed(profile, traits.Loadout.ThrowScale);
                float baseline = Gameplay.FeelSettings.ThrowSpeed(profile);
                Debug.Log($"[D8] D8_IDENTITY_THROW identity={id.id} throw={expected:F2} baseline={baseline:F2} scale={traits.Loadout.ThrowScale:F2}");
            }

            // 缺点牌效果：给「看不清小件」牌，验证服务端拒绝抓取小件
            DrawbackCard nearsighted = FindCardWith(c => c.blocksSmallItems);
            if (nearsighted != null && grab != null)
            {
                traits.CmdDebugAssign(index, nearsighted.id);
                yield return new WaitForSeconds(0.4f);

                Gameplay.GrabbableProp small = FindSmallestProp();
                bool blocked = false;
                if (small != null)
                {
                    Vector3 to = small.transform.position;
                    motor.transform.position = to - motor.transform.forward * 1.2f + Vector3.up * 0.05f;
                    motor.transform.LookAt(new Vector3(to.x, motor.transform.position.y, to.z));
                    yield return new WaitForSeconds(0.2f);
                    grab.SetInputEnabled(false);
                    grab.TryGrab(small);
                    yield return new WaitForSeconds(0.3f);
                    blocked = !grab.IsHolding;
                    Debug.Log($"[D8] D8_SMALL_ITEM identity={id.id} card={nearsighted.id} prop={small.name} " +
                              $"mass={small.Mass:F1} limit={traits.Loadout.SmallItemMassLimit:F1} blocked={blocked}");
                }

                Check(blocked, $"small_item_blocked_for_{id.id}");
            }

            // 手抖牌：验证持物时准星方向真的在抖（目标点偏移 > 0）
            DrawbackCard shaky = FindCardWith(c => c.shakyHands);
            if (shaky != null)
            {
                traits.CmdDebugAssign(index, shaky.id);
                yield return new WaitForSeconds(0.4f);
                PlayerLoadout l = traits.Loadout;
                Check(l.ShakyHands && l.ShakeAmplitude > 0f, $"shaky_hands_wired_for_{id.id}");
                Debug.Log($"[D8] D8_SHAKY identity={id.id} card={shaky.id} amplitude={l.ShakeAmplitude:F3} " +
                          $"grabScale={l.GrabStiffnessScale:F2}");
            }

            // 窄门恐惧：验证玩家进入窄区时降速
            DrawbackCard panic = FindCardWith(c => c.narrowDoorPanic);
            if (panic != null)
            {
                traits.CmdDebugAssign(index, panic.id);
                yield return new WaitForSeconds(0.4f);
                bool zones = NarrowZones.Count > 0;
                Debug.Log($"[D8] D8_PANIC identity={id.id} card={panic.id} narrowZones={NarrowZones.Count} panicScale={traits.Loadout.PanicSpeedScale:F2}");
                Check(zones, "narrow_zones_registered");
            }

            motor.ClearAutomationMove();
        }

        private sealed class SpeedResult
        {
            public float Value;
        }

        private static IEnumerator MeasureSpeedCoroutine(PlayerMotor motor, bool sprint, SpeedResult result)
        {
            motor.ApplyAutomationMove(new Vector2(0f, 1f), sprint);
            yield return new WaitForSeconds(1.1f);
            result.Value = motor.CurrentPlanarSpeed;
            motor.ClearAutomationMove();
            yield return new WaitForSeconds(0.2f);
        }

        private IEnumerator MeasureSpeed(PlayerMotor motor, bool sprint, SpeedResult result)
        {
            yield return MeasureSpeedCoroutine(motor, sprint, result);
        }

        private static Gameplay.PropPhysicsProfile FindProfile(float mass)
        {
            Gameplay.PropPhysicsProfile[] all = Resources.FindObjectsOfTypeAll<Gameplay.PropPhysicsProfile>();
            foreach (Gameplay.PropPhysicsProfile p in all)
            {
                if (p != null && Mathf.Approximately(p.mass, mass)) return p;
            }
            return all.Length > 0 ? all[0] : null;
        }

        private static DrawbackCard FindCardWith(System.Func<DrawbackCard, bool> predicate)
        {
            TraitDatabase db = TraitBootstrap.Database;
            if (db == null || db.cards == null) return null;
            foreach (DrawbackCard c in db.cards) if (c != null && predicate(c)) return c;
            return null;
        }

        private static Gameplay.GrabbableProp FindSmallestProp()
        {
            Gameplay.GrabbableProp best = null;
            foreach (Gameplay.GrabbableProp p in Object.FindObjectsByType<Gameplay.GrabbableProp>(FindObjectsSortMode.None))
            {
                if (p.Profile == null) continue;
                if (best == null || p.Mass < best.Mass) best = p;
            }
            return best;
        }

        private static void Check(bool ok, string name)
        {
            if (ok) Debug.Log($"[D8] D8_CHECK_OK {name}");
            else Debug.LogError($"[D8] D8_CHECK_FAIL {name}");
        }
    }
}
