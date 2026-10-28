using Mirror;
using UnityEngine;

namespace ScavengeNightmare.Traits
{
    /// <summary>
    /// 玩家身份与缺点牌（D8）：服务端权威分配，SyncVar 下发，所有端自行合成同一份 <see cref="PlayerLoadout"/>。
    ///
    /// 为什么身份必须由服务端分配而不是客户端自选：D8 要求「开局 4 身份唯一」。
    /// 客户端自选会重复，服务端按进场顺序发牌才能保证唯一，且客户端拿不到分配权就没法作弊。
    ///
    /// 抽牌用服务端统一 RNG，因此四端看到的组合完全一致（SyncVar 只传结果，不传随机过程）。
    /// </summary>
    public sealed class PlayerTraits : NetworkBehaviour
    {
        [Header("数据（留空则从 TraitBootstrap 取全局库）")]
        [SerializeField] private TraitDatabase database;

        [SyncVar(hook = nameof(OnIdentityChanged))] private int identityIndex = -1;
        [SyncVar(hook = nameof(OnCardChanged))] private string cardId = string.Empty;

        [Tooltip("灰盒：把体型缩放与颜色应用到 Mesh 子物体，无美术时靠这个区分四人")]
        [SerializeField] private Renderer[] bodyRenderers;

        [SerializeField] private Transform bodyRoot;

        private PlayerLoadout loadout;
        private bool loadoutBuilt;

        /// <summary>当前生效的身份 × 缺点牌合成结果。未分配时为全 1 的空配置。</summary>
        public PlayerLoadout Loadout
        {
            get
            {
                if (!loadoutBuilt)
                {
                    RebuildLoadout();
                }

                return loadout;
            }
        }

        public CharacterIdentity Identity => Loadout.Identity;
        public DrawbackCard Card => Loadout.Card;
        public int IdentityIndex => identityIndex;
        public string CardId => cardId;

        /// <summary>身份变更事件：HUD、体型、自动化验证都订阅这个，不轮询 SyncVar。</summary>
        public event System.Action<PlayerLoadout> LoadoutChanged;

        private void Awake()
        {
            if (database == null)
            {
                database = TraitBootstrap.Database;
            }

            if (database != null)
            {
                TraitSettings.Use(database);
            }
        }

        public override void OnStartServer()
        {
            AssignIdentityAndCard();
        }

        private void AssignIdentityAndCard()
        {
            // D4/D6 自动化基线用 -notraits 跑：保持中性身份，所有倍率为 1，
            // 避免随机大力士/手抖把投掷速度、硬直时长带偏（D8 上线时实测复现过）。
            if (TraitSettings.NoTraitsRequested)
            {
                Debug.Log("[D8] D8_TRAITS_DISABLED reason=-notraits");
                return;
            }

            if (!TraitSettings.Ready)
            {
                Debug.LogWarning("[D8] 未配置 TraitDatabase，身份与缺点牌均留空（倍率全为 1）");
                return;
            }

            identityIndex = TraitAllocator.Take(TraitSettings.IdentityCount);
            CharacterIdentity identity = TraitSettings.Identity(identityIndex);

            // 服务端统一 RNG：seed 非 0 时可复现，自动化验证靠这个
            System.Random rng = database.seed != 0
                ? new System.Random(unchecked(database.seed * 31 + identityIndex * 7))
                : new System.Random();

            DrawbackCard card = TraitSettings.Draw(identity, rng, out _);
            cardId = card != null ? card.id : string.Empty;

            Debug.Log($"[D8] D8_ASSIGN identity={(identity != null ? identity.id : "none")} " +
                      $"card={(card != null ? card.id : "none")} conflict={TraitSettings.IsConflict(identity, card)}");
        }

        private void OnIdentityChanged(int _, int __) => RebuildLoadout();

        private void OnCardChanged(string _, string __) => RebuildLoadout();

        private void RebuildLoadout()
        {
            CharacterIdentity identity = TraitSettings.Identity(identityIndex);
            DrawbackCard card = TraitSettings.Card(cardId);
            loadout = new PlayerLoadout(identity, card);
            loadoutBuilt = true;

            ApplyBody();
            LoadoutChanged?.Invoke(loadout);
        }

        /// <summary>
        /// 灰盒外观：体型缩放 + 身份配色。D8 无美术，四人在同一画面里必须一眼能分辨，
        /// 否则「冲突组合」在实测时无法被观察到。D16 换正式角色模型后这段由美术资产接管。
        /// </summary>
        private void ApplyBody()
        {
            if (bodyRoot == null)
            {
                bodyRoot = transform.Find("Mesh");
            }

            if (bodyRoot != null)
            {
                bodyRoot.localScale = loadout.BodyScale;
            }

            if (bodyRenderers == null || bodyRenderers.Length == 0)
            {
                bodyRenderers = GetComponentsInChildren<Renderer>(true);
            }

            if (!loadout.HasIdentity)
            {
                return;
            }

            foreach (Renderer r in bodyRenderers)
            {
                if (r == null)
                {
                    continue;
                }

                MaterialPropertyBlock block = new MaterialPropertyBlock();
                r.GetPropertyBlock(block);
                block.SetColor("_BaseColor", loadout.BodyColor);
                block.SetColor("_Color", loadout.BodyColor);
                r.SetPropertyBlock(block);
            }
        }

        public override void OnStopServer()
        {
            TraitAllocator.Release(identityIndex);
            identityIndex = -1;
        }

        /// <summary>自动化验证入口：强制指定身份与牌，绕开随机。</summary>
        [Command(requiresAuthority = false)]
        public void CmdDebugAssign(int index, string id)
        {
            if (!isServer || !TraitSettings.Ready)
            {
                return;
            }

            TraitAllocator.Release(identityIndex);
            identityIndex = Mathf.Clamp(index, 0, TraitSettings.IdentityCount - 1);
            TraitAllocator.Reserve(identityIndex);
            cardId = id ?? string.Empty;
            Debug.Log($"[D8] D8_DEBUG_ASSIGN identity={identityIndex} card={cardId}");
        }
    }

    /// <summary>
    /// 身份分配器：保证「开局 4 身份唯一」。
    /// 静态表由服务端维护；客户端不读它（客户端只认 SyncVar），因此不需要跨端同步。
    /// </summary>
    public static class TraitAllocator
    {
        private static readonly System.Collections.Generic.HashSet<int> taken = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnLoad() => taken.Clear();

        public static void Reset() => taken.Clear();

        public static int Take(int count)
        {
            if (count <= 0)
            {
                return 0;
            }

            // 优先取最小编号空位，第 5 人及以后循环复用（D8 上限 4 人，这里只是兜底）
            for (int i = 0; i < count; i++)
            {
                if (!taken.Contains(i))
                {
                    taken.Add(i);
                    return i;
                }
            }

            return 0;
        }

        public static void Reserve(int index)
        {
            if (index >= 0)
            {
                taken.Add(index);
            }
        }

        public static void Release(int index)
        {
            if (index >= 0)
            {
                taken.Remove(index);
            }
        }

        public static int TakenCount => taken.Count;
    }
}
