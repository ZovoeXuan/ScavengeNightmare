using Mirror;
using ScavengeNightmare.Save;
using UnityEngine;

namespace ScavengeNightmare.Traits
{
    /// <summary>
    /// 玩家角色身份与缺点牌 V2 升级版：
    /// 1) 为 CmdDebugAssign 增加测试/编辑期/本地权限校验，修复无权限客户端越权篡改他人角色属性的漏洞 (NET-04)；
    /// 2) 新增 ServerCycleIdentity() 与 ServerRerollCard()，支持安全屋角色柜互动换人与洗牌。
    /// </summary>
    public sealed class PlayerTraits : NetworkBehaviour
    {
        [Header("数据 (留空则从 TraitBootstrap 取全局库)")]
        [SerializeField] private TraitDatabase database;

        [SyncVar(hook = nameof(OnIdentityChanged))] private int identityIndex = -1;
        [SyncVar(hook = nameof(OnCardChanged))] private string cardId = string.Empty;

        [SerializeField] private Renderer[] bodyRenderers;
        [SerializeField] private Transform bodyRoot;

        private PlayerLoadout loadout;
        private bool loadoutBuilt;

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
            if (TraitSettings.NoTraitsRequested)
            {
                Debug.Log("[D8] D8_TRAITS_DISABLED reason=-notraits");
                return;
            }

            if (!TraitSettings.Ready)
            {
                Debug.LogWarning("[D8] 未配置 TraitDatabase，身份与缺点牌均留空 (倍率全为 1) ");
                return;
            }

            identityIndex = TraitAllocator.Take(TraitSettings.IdentityCount);
            CharacterIdentity identity = TraitSettings.Identity(identityIndex);

            System.Random rng = database.seed != 0
                ? new System.Random(unchecked(database.seed * 31 + identityIndex * 7))
                : new System.Random();

            DrawbackCard card = TraitSettings.Draw(identity, rng, out _);
            cardId = card != null ? card.id : string.Empty;

            Debug.Log($"[D8] D8_ASSIGN identity={(identity != null ? identity.id : "none")} " +
                      $"card={(card != null ? card.id : "none")} conflict={TraitSettings.IsConflict(identity, card)}");
        }

        [Server]
        public void ServerCycleIdentity()
        {
            if (!TraitSettings.Ready || TraitSettings.IdentityCount <= 0)
            {
                return;
            }

            TraitAllocator.Release(identityIndex);
            identityIndex = (identityIndex + 1) % TraitSettings.IdentityCount;
            TraitAllocator.Reserve(identityIndex);
            RebuildLoadout();
        }

        [Server]
        public void ServerRerollCard()
        {
            if (!TraitSettings.Ready)
            {
                return;
            }

            CharacterIdentity identity = TraitSettings.Identity(identityIndex);
            DrawbackCard card = TraitSettings.Draw(identity, new System.Random(), out _);
            cardId = card != null ? card.id : string.Empty;
            RebuildLoadout();
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

        [Command(requiresAuthority = false)]
        public void CmdDebugAssign(int index, string id, NetworkConnectionToClient sender = null)
        {
            if (!isServer || !TraitSettings.Ready)
            {
                return;
            }

            // 修复 NET-04：非测试模式下仅允许修改自身角色，拒绝越权篡改其他玩家
            if (!S4ACli.IsTestRun && !Application.isEditor && sender != null && sender != connectionToClient)
            {
                Debug.LogWarning($"[D8] D8_DEBUG_ASSIGN_DENIED sender={sender.connectionId} target={netId}");
                return;
            }

            TraitAllocator.Release(identityIndex);
            identityIndex = Mathf.Clamp(index, 0, TraitSettings.IdentityCount - 1);
            TraitAllocator.Reserve(identityIndex);
            cardId = id ?? string.Empty;
            Debug.Log($"[D8] D8_DEBUG_ASSIGN identity={identityIndex} card={cardId}");
        }
    }

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
