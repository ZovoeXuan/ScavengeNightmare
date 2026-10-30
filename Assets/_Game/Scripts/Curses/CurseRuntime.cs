using System.Collections;
using System.Collections.Generic;
using Mirror;
using ScavengeNightmare.Cart;
using ScavengeNightmare.Combat;
using ScavengeNightmare.Gameplay;
using ScavengeNightmare.Noise;
using ScavengeNightmare.Save;
using UnityEngine;

namespace ScavengeNightmare.Curses
{
    /// <summary>
    /// 诅咒运行时（服务端权威）。挂在 NetworkManager 上（普通 MonoBehaviour，
    /// NM 没有 NetworkIdentity，NetworkBehaviour 收不到 OnStartServer——与 MonsterSpawner 同构）。
    ///
    /// 三件事只有它能做：
    /// 1. 判「这件诅咒归谁」（持有可能 + 随车）；
    /// 2. 判「同一玩家身上哪一件生效、其余休眠」；
    /// 3. 驱动笑面偶的发笑计时与噪音上报。
    ///
    /// 归属口径（P0）：
    ///   手持 > 随车。
    ///   「随车」成立的依据在 §4.4 笑面偶的解法原文：「换手 / 放下 / **丢车外**」——
    ///   既然「丢车外」是解法，诅咒货放在车上就仍然算在你头上。
    ///   P0 没有工具带槽（BeltSlot 属 S4B-5），「手持 + 随车」是同一玩家同时招惹
    ///   两件诅咒的唯一真实路径；等工具带做完，这里只是多一条归属来源，规则不用改。
    /// </summary>
    public sealed class CurseRuntime : MonoBehaviour
    {
        private sealed class LaughState
        {
            public bool armed;
            public float wait;
            public float windup;
            public float interval;
            public int count;
        }

        public static CurseRuntime Instance { get; private set; }

        [Header("诅咒预制体")]
        [SerializeField] private GameObject dollPrefab;
        [SerializeField] private GameObject tabletPrefab;

        [Header("归属")]
        [Tooltip("车上的诅咒货归因给离它最近的玩家（米）")]
        [SerializeField] private float cartCarryRadius = 2.5f;

        [Header("扫描")]
        [Tooltip("场上诅咒物的扫描间隔（秒）。物件极少，不用每帧 Find")]
        [SerializeField] private float refreshInterval = 0.2f;

        private readonly List<CursedItem> items = new();
        private readonly Dictionary<uint, List<CursedItem>> carried = new();
        private readonly Dictionary<CursedItem, LaughState> laughs = new();
        private float refreshIn;
        private int laughTotal;

        /// <summary>场上全部诅咒物（按 netId 升序，顺序稳定）。</summary>
        public IReadOnlyList<CursedItem> Items => items;

        /// <summary>本次进程内发笑总次数（服务端）。</summary>
        public int LaughTotal => laughTotal;

        private void Awake()
        {
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void Start()
        {
            StartCoroutine(BootAfterServerReady());
        }

        /// <summary>
        /// 正式游玩才自动生成；**任何自动化进程一律不自动生成**。
        /// 理由：诅咒会发笑（噪音）、会占位、会改结算价值，
        /// 让它在 4A 的 13 条或 4B 的其他用例里「顺带出现」，等于给每条用例都加了一个
        /// 说不清来源的干扰项。测试要诅咒就自己显式生成（S4BCurseTest 就是这么做的）。
        /// </summary>
        private IEnumerator BootAfterServerReady()
        {
            float waited = 0f;
            while (!NetworkServer.active && waited < 15f)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }

            if (!NetworkServer.active)
            {
                yield break;
            }

            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "Safehouse")
            {
                Debug.Log("[S4B] S4B_CURSE_SPAWN_SKIP reason=safehouse");
                yield break;
            }

            if (S4ACli.IsTestRun)
            {
                Debug.Log("[S4B] S4B_CURSE_SPAWN_SKIP reason=test_controlled");
                yield break;
            }

            ServerSpawnForLeg(CurrentLeg());
        }

        private void Update()
        {
            if (!NetworkServer.active)
            {
                return;
            }

            refreshIn -= Time.deltaTime;
            if (refreshIn <= 0f)
            {
                refreshIn = refreshInterval;
                RefreshItems();
            }

            AssignCarriers();
            TickLaughs(Time.deltaTime);
        }

        // ---------------- 生成 ----------------

        /// <summary>
        /// 按关卡生成诅咒：L1 0 件 / L2 1 件 / L3 2 件（§4.4 与 §2.1「0/1/2 诅咒」同一口径）。
        /// 每次都是「先清旧、再生成」，换关不会累积。
        /// </summary>
        [Server]
        public int ServerSpawnForLeg(int leg)
        {
            int want = leg <= 1 ? 0 : (leg >= 3 ? 2 : 1);

            ServerDespawnAll();

            int spawned = 0;

            for (int i = 0; i < want; i++)
            {
                CurseSpawnAnchor anchor = AnchorAt(i);
                CurseKind kind = anchor != null ? anchor.Kind : (i == 0 ? CurseKind.Doll : CurseKind.Tablet);
                Vector3 at = anchor != null
                    ? anchor.transform.position + Vector3.up * 0.05f
                    : new Vector3(i == 0 ? 4f : -4f, 0.35f, 6f);

                if (ServerSpawnCurse(kind, at) != null)
                {
                    spawned++;
                }
            }

            Debug.Log($"[S4B] GATE_CURSE_SPAWN leg={leg} want={want} count={spawned} anchors={CountAnchors()}");
            return spawned;
        }

        [Server]
        public CursedItem ServerSpawnCurse(CurseKind kind, Vector3 position)
        {
            GameObject prefab = kind == CurseKind.Tablet ? tabletPrefab : dollPrefab;

            if (prefab == null)
            {
                Debug.LogError($"[S4B] S4B_CURSE_SPAWN_FAIL reason=missing_prefab kind={kind}");
                return null;
            }

            GameObject instance = Instantiate(prefab, position, Quaternion.identity);
            instance.name = kind == CurseKind.Tablet ? "Curse_Tablet" : "Curse_Doll";
            NetworkServer.Spawn(instance);

            CursedItem item = instance.GetComponent<CursedItem>();
            if (item == null)
            {
                Debug.LogError($"[S4B] S4B_CURSE_SPAWN_FAIL reason=no_cursed_item kind={kind}");
                NetworkServer.Destroy(instance);
                return null;
            }

            RefreshItems();
            Debug.Log($"[S4B] S4B_CURSE_SPAWN kind={kind} at=({position.x:F1},{position.z:F1}) total={items.Count}");
            return item;
        }

        [Server]
        public void ServerDespawnAll()
        {
            RefreshItems();

            foreach (CursedItem item in items)
            {
                if (item != null)
                {
                    NetworkServer.Destroy(item.gameObject);
                }
            }

            items.Clear();
            carried.Clear();
            laughs.Clear();
            Debug.Log("[S4B] S4B_CURSE_DESPAWN_ALL");
        }

        /// <summary>换关重入（与 MonsterSpawner.RespawnForNewScene 同构）。S4ABootstrap 会调。</summary>
        public void RespawnForNewLeg()
        {
            if (!NetworkServer.active)
            {
                return;
            }

            ServerDespawnAll();
            ServerSpawnForLeg(CurrentLeg());
        }

        // ---------------- 裁决 ----------------

        /// <summary>这件诅咒归谁；0 = 无主（躺在地上，不生效）。</summary>
        public uint CarrierOf(CursedItem item)
        {
            if (item == null)
            {
                return 0;
            }

            GrabbableProp prop = item.Prop;

            if (prop == null)
            {
                return 0;
            }

            if (prop.IsHeld && prop.Holder != null)
            {
                return prop.Holder.netId;
            }

            CartController cart = CartController.Instance;
            if (cart != null && cart.Cargo != null && OnCart(cart.Cargo, prop))
            {
                return NearestPlayer(prop.transform.position, cartCarryRadius);
            }

            return 0;
        }

        /// <summary>不用 LINQ 的 Contains：IReadOnlyList 上没有，为它引 System.Linq 不值当。</summary>
        private static bool OnCart(CartCargo cargo, GrabbableProp prop)
        {
            foreach (GrabbableProp item in cargo.Items)
            {
                if (item == prop)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>某玩家当前身上背着的诅咒（按入手顺序，第 0 个生效，其余休眠）。</summary>
        public IReadOnlyList<CursedItem> CarriedBy(uint netId)
        {
            return carried.TryGetValue(netId, out List<CursedItem> list) ? list : System.Array.Empty<CursedItem>();
        }

        // ---------------- 内部 ----------------

        private void RefreshItems()
        {
            items.Clear();

            foreach (CursedItem item in FindObjectsByType<CursedItem>(FindObjectsSortMode.None))
            {
                if (item != null)
                {
                    items.Add(item);
                }
            }

            // netId 升序：FindObjectsByType 的返回顺序是未定义的，
            // 而「谁是先拿的那件」决定谁休眠——顺序不稳，断言就会时红时绿。
            items.Sort((a, b) => a.netId.CompareTo(b.netId));
        }

        private void AssignCarriers()
        {
            // 1) 先算这一帧每件诅咒的归属
            Dictionary<CursedItem, uint> now = new();
            foreach (CursedItem item in items)
            {
                if (item == null)
                {
                    continue;
                }

                uint carrier = CarrierOf(item);
                if (carrier != 0)
                {
                    now[item] = carrier;
                }
            }

            // 2) 从各玩家的携带清单里摘掉已经不在他身上的
            foreach (KeyValuePair<uint, List<CursedItem>> kv in carried)
            {
                kv.Value.RemoveAll(it => it == null || !now.TryGetValue(it, out uint owner) || owner != kv.Key);
            }

            // 3) 追加新入手的（items 已按 netId 升序，同帧多件也有确定顺序）
            foreach (CursedItem item in items)
            {
                if (item == null || !now.TryGetValue(item, out uint owner))
                {
                    item.ServerSetHolder(0);
                    item.ServerSetDormant(false);
                    continue;
                }

                if (!carried.TryGetValue(owner, out List<CursedItem> list))
                {
                    list = new List<CursedItem>();
                    carried[owner] = list;
                }

                if (!list.Contains(item))
                {
                    list.Add(item);
                    Debug.Log($"[S4B] GATE_CURSE_ACQUIRE id={item.CurseId} holder={owner} slot={list.Count - 1}");
                }

                item.ServerSetHolder(owner);
            }

            // 4) 每人最多 1 种生效，其余休眠
            foreach (KeyValuePair<uint, List<CursedItem>> kv in carried)
            {
                for (int i = 0; i < kv.Value.Count; i++)
                {
                    CursedItem item = kv.Value[i];
                    if (item == null)
                    {
                        continue;
                    }

                    bool dormant = i > 0;

                    if (item.IsDormant != dormant)
                    {
                        item.ServerSetDormant(dormant);
                        Debug.Log($"[S4B] GATE_CURSE_DORMANT id={item.CurseId} holder={kv.Key} dormant={(dormant ? 1 : 0)}");
                    }
                }
            }
        }

        private void TickLaughs(float deltaTime)
        {
            foreach (CursedItem item in items)
            {
                if (item == null || item.Kind != CurseKind.Doll)
                {
                    continue;
                }

                if (!laughs.TryGetValue(item, out LaughState state))
                {
                    state = new LaughState();
                    laughs[item] = state;
                }

                // 休眠 / 无主 / 不在手上：计时归零，下次入手重新抽间隔。
                // 「放下就能打断发笑」是 §4.4 解法「换手 / 放下」的落点。
                if (item.IsDormant || item.HolderNetId == 0)
                {
                    state.armed = false;
                    state.wait = 0f;
                    state.windup = 0f;
                    item.ServerSetLaughCountdown(0f);
                    continue;
                }

                if (!state.armed)
                {
                    state.armed = true;
                    state.interval = Random.Range(item.LaughIntervalMin, item.LaughIntervalMax);
                    // 前兆算在间隔之内：拿到手到第一次笑出来的时间也落在 12–20s，
                    // 否则「间隔」会变成 interval + 1.5 而越过 20s 上限。
                    state.wait = Mathf.Max(0f, state.interval - item.LaughWindup);
                    state.windup = 0f;
                    state.count = 0;
                    item.ServerSetLaughCount(0);

                    // 抽到的间隔按原文落日志：测试除了掐墙钟，还要能按这条精确核对 12–20s。
                    // 只掐墙钟的话，帧边界上的 ±1 帧误差会让判据变成「差不多」。
                    Debug.Log($"[S4B] GATE_CURSE_ARM id={item.CurseId} holder={item.HolderNetId} interval={state.interval:F2}");
                }

                if (state.windup > 0f)
                {
                    state.windup -= deltaTime;
                    item.ServerSetLaughCountdown(0f);

                    if (state.windup <= 0f)
                    {
                        state.windup = 0f;
                        DoLaugh(item, state);
                    }

                    continue;
                }

                state.wait -= deltaTime;
                item.ServerSetLaughCountdown(Mathf.Max(0f, state.wait));

                if (state.wait <= 0f)
                {
                    state.windup = item.LaughWindup;
                    item.RpcLaughWindup();
                    Debug.Log($"[S4B] GATE_CURSE_WINDUP id={item.CurseId} holder={item.HolderNetId} windup={item.LaughWindup:F2}");
                }
            }
        }

        private void DoLaugh(CursedItem item, LaughState state)
        {
            Vector3 at = item.transform.position;

            state.count++;
            item.ServerSetLaughCount(state.count);
            laughTotal++;
            item.RpcLaugh();

            // 可归因是硬要求（A9）：必须带 source 与位置进 NoiseBus，
            // 严禁只往全局等级上加一个数——那样谁都说不出噪音是谁弄出来的。
            NoiseBus.Report(item.CurseId, item.LaughNoise, at);

            Debug.Log($"[S4B] GATE_CURSE_LAUGH id={item.CurseId} holder={item.HolderNetId} " +
                      $"count={state.count} interval={state.interval:F2} at=({at.x:F1},{at.z:F1})");

            float interval = Random.Range(item.LaughIntervalMin, item.LaughIntervalMax);
            state.interval = interval;
            // 把「触发那一帧多走掉的」补回下一段：定时器只能在帧边界上触发，
            // 不补的话每段都比抽到的值多出最多 1 帧，量久了整体偏慢。
            state.wait = Mathf.Max(0f, interval - item.LaughWindup) + Mathf.Min(0f, state.wait);
        }

        private uint NearestPlayer(Vector3 position, float radius)
        {
            PlayerVitals best = null;
            float bestDistance = radius;

            foreach (PlayerVitals vitals in PlayerVitals.All)
            {
                if (vitals == null)
                {
                    continue;
                }

                float distance = Vector3.Distance(vitals.transform.position, position);
                if (distance <= bestDistance)
                {
                    bestDistance = distance;
                    best = vitals;
                }
            }

            return best != null ? best.netId : 0;
        }

        private static CurseSpawnAnchor AnchorAt(int index)
        {
            CurseSpawnAnchor[] anchors = FindObjectsByType<CurseSpawnAnchor>(FindObjectsSortMode.None);
            if (anchors.Length == 0)
            {
                return null;
            }

            // 锚点按名字排序：同样是为了让「第几关出哪一件」可复现。
            System.Array.Sort(anchors, (a, b) => string.CompareOrdinal(a.name, b.name));
            return anchors[Mathf.Clamp(index, 0, anchors.Length - 1)];
        }

        private static int CountAnchors()
        {
            return FindObjectsByType<CurseSpawnAnchor>(FindObjectsSortMode.None).Length;
        }

        private static int CurrentLeg()
        {
            ScavengeNightmare.Economy.TeamAccount account = ScavengeNightmare.Economy.TeamAccount.Instance;
            return account != null ? Mathf.Max(1, account.LegIndex) : 1;
        }
    }
}
