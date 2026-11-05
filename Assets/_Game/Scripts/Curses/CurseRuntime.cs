using System.Collections;
using System.Collections.Generic;
using Mirror;
using ScavengeNightmare.Audio;
using ScavengeNightmare.Cart;
using ScavengeNightmare.Combat;
using ScavengeNightmare.Economy;
using ScavengeNightmare.Gameplay;
using ScavengeNightmare.Noise;
using ScavengeNightmare.Save;
using UnityEngine;

namespace ScavengeNightmare.Curses
{
    /// <summary>
    /// 诅咒运行时 V2 升级版：
    /// 1) 修复 ServerSpawnForLeg 硬编码 0/1/2 而不读 LegContentTable.Instance.CursesFor(leg) 的数据驱动违约 (CUR-02)；
    /// 2) 支持已封印诅咒物 (item.Sealed) 跳过发笑读秒。
    /// </summary>
    public sealed class CurseRuntime : MonoBehaviour
    {
        public const byte OwnerSourceNone = 0;
        public const byte OwnerSourceHand = 1;
        public const byte OwnerSourceCart = 2;

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

        [Header("扫描")]
        [SerializeField] private float refreshInterval = 0.2f;

        private readonly List<CursedItem> items = new();
        private readonly Dictionary<uint, List<CursedItem>> carried = new();
        private readonly Dictionary<CursedItem, LaughState> laughs = new();
        private readonly Dictionary<CursedItem, uint> owners = new();
        private readonly Dictionary<CursedItem, byte> sources = new();
        private float refreshIn;
        private int laughTotal;

        public IReadOnlyList<CursedItem> Items => items;
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

        [Server]
        public int ServerSpawnForLeg(int leg)
        {
            int want = LegContentTable.Instance != null
                ? LegContentTable.Instance.CursesFor(leg)
                : (leg <= 1 ? 0 : (leg >= 3 ? 2 : 1));

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
            owners.Clear();
            sources.Clear();
            Debug.Log("[S4B] S4B_CURSE_DESPAWN_ALL");
        }

        public void RespawnForNewLeg()
        {
            if (!NetworkServer.active)
            {
                return;
            }

            ServerDespawnAll();
            ServerSpawnForLeg(CurrentLeg());
        }

        public uint CarrierOf(CursedItem item, out byte source)
        {
            source = OwnerSourceNone;

            if (item == null || item.Sealed)
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
                source = OwnerSourceHand;
                return prop.Holder.netId;
            }

            CartController cart = CartController.Instance;
            if (cart != null && cart.Cargo != null && OnCart(cart.Cargo, prop) && item.CartOwnerNetId != 0)
            {
                source = OwnerSourceCart;
                return item.CartOwnerNetId;
            }

            return 0;
        }

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

        public IReadOnlyList<CursedItem> CarriedBy(uint netId)
        {
            return carried.TryGetValue(netId, out List<CursedItem> list) ? list : System.Array.Empty<CursedItem>();
        }

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

            items.Sort((a, b) => a.netId.CompareTo(b.netId));
        }

        private void AssignCarriers()
        {
            Dictionary<CursedItem, uint> now = new();
            foreach (CursedItem item in items)
            {
                if (item == null)
                {
                    continue;
                }

                uint carrier = CarrierOf(item, out byte source);

                owners.TryGetValue(item, out uint prevOwner);
                sources.TryGetValue(item, out byte prevSource);

                if (carrier != prevOwner || source != prevSource)
                {
                    Debug.Log($"[S4B] S4B_CURSE_OWNER prop={item.CurseId} owner={carrier} source={SourceName(source)}");
                    owners[item] = carrier;
                    sources[item] = source;
                }

                if (carrier != 0)
                {
                    now[item] = carrier;
                }
            }

            foreach (KeyValuePair<uint, List<CursedItem>> kv in carried)
            {
                kv.Value.RemoveAll(it => it == null || !now.TryGetValue(it, out uint owner) || owner != kv.Key);
            }

            foreach (CursedItem item in items)
            {
                if (item == null || !now.TryGetValue(item, out uint owner))
                {
                    if (item != null)
                    {
                        item.ServerSetHolder(0);
                        item.ServerSetDormant(item.Sealed);
                    }
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

            foreach (KeyValuePair<uint, List<CursedItem>> kv in carried)
            {
                bool ownerActive = OwnerActive(kv.Key);

                for (int i = 0; i < kv.Value.Count; i++)
                {
                    CursedItem item = kv.Value[i];
                    if (item == null)
                    {
                        continue;
                    }

                    bool dormant = item.Sealed || i > 0 || !ownerActive;

                    if (item.IsDormant != dormant)
                    {
                        item.ServerSetDormant(dormant);
                        Debug.Log($"[S4B] GATE_CURSE_DORMANT id={item.CurseId} holder={kv.Key} " +
                            $"dormant={(dormant ? 1 : 0)} reason={(ownerActive ? "second_curse" : "owner_inactive")}");
                    }
                }
            }
        }

        private static bool OwnerActive(uint netId)
        {
            if (netId == 0u || !NetworkServer.spawned.TryGetValue(netId, out NetworkIdentity identity) || identity == null)
            {
                return false;
            }

            PlayerVitals vitals = identity.GetComponent<PlayerVitals>();
            return vitals != null && !vitals.IsDowned;
        }

        private static string SourceName(byte source)
        {
            return source switch
            {
                OwnerSourceHand => "hand",
                OwnerSourceCart => "cart",
                _ => "none"
            };
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

                if (item.IsDormant || item.Sealed || item.HolderNetId == 0)
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
                    state.wait = Mathf.Max(0f, state.interval - item.LaughWindup);
                    state.windup = 0f;
                    state.count = 0;
                    item.ServerSetLaughCount(0);

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

                    AudioBus.Play(AudioCue.CurseOmen, item.transform.position, 1f, $"lead={item.LaughWindup:F1}");

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

            NoiseBus.Report(item.CurseId, item.LaughNoise, at);

            Debug.Log($"[S4B] GATE_CURSE_LAUGH id={item.CurseId} holder={item.HolderNetId} " +
                $"count={state.count} interval={state.interval:F2} at=({at.x:F1},{at.z:F1})");

            float interval = Random.Range(item.LaughIntervalMin, item.LaughIntervalMax);
            state.interval = interval;
            state.wait = Mathf.Max(0f, interval - item.LaughWindup) + Mathf.Min(0f, state.wait);
        }

        private static CurseSpawnAnchor AnchorAt(int index)
        {
            CurseSpawnAnchor[] anchors = FindObjectsByType<CurseSpawnAnchor>(FindObjectsSortMode.None);
            if (anchors.Length == 0)
            {
                return null;
            }

            System.Array.Sort(anchors, (a, b) => string.CompareOrdinal(a.name, b.name));
            return anchors[Mathf.Clamp(index, 0, anchors.Length - 1)];
        }

        private static int CountAnchors()
        {
            return FindObjectsByType<CurseSpawnAnchor>(FindObjectsSortMode.None).Length;
        }

        private static int CurrentLeg()
        {
            TeamAccount account = TeamAccount.Instance;
            return account != null ? Mathf.Max(1, account.LegIndex) : 1;
        }
    }
}
