using System.Collections.Generic;
using Mirror;
using ScavengeNightmare.Gameplay;
using ScavengeNightmare.Save;
using UnityEngine;

namespace ScavengeNightmare.Economy
{
    /// <summary>
    /// 团队账户 V2 升级版：
    /// 1) 修复重启游戏读档时未从 save.Data.purchases 恢复 ownedMask、selfInjectorCharges、medkitCharges 导致钱扣了装备全丢的严重 Bug (ECO-01)；
    /// 2) 修复 ServerEndRun 团灭时未清空 ownedMask 及存档中 Active 购买记录的 Bug (ECO-02)；
    /// 3) 修复 TryUndoPurchase 撤销一次后未回退 LastPurchaseId 导致无法连续撤销的 Bug (ECO-05)；
    /// 4) 提交手持诅咒物/重物时改用 ServerForceRelease() 确保安全销毁。
    /// </summary>
    public sealed class TeamAccount : NetworkBehaviour
    {
        public static TeamAccount Instance { get; private set; }

        public static bool Persisted
        {
            get
            {
                P0SaveService svc = P0SaveService.Instance;
                return svc != null && svc.Loaded && svc.State != SaveState.Locked && svc.State != SaveState.Failed;
            }
        }

        [SyncVar(hook = nameof(OnBalanceReplicated))] private long balance;

        private long observedBalance;

        public long ObservedBalance => observedBalance;

        public bool SkipNextBalanceHook;

        private void OnBalanceReplicated(long oldValue, long newValue)
        {
            if (SkipNextBalanceHook)
            {
                SkipNextBalanceHook = false;
                Debug.Log($"[S4A] S4A_WALLET_SYNC_BREAK injected=1 dropped={newValue} kept={observedBalance}");
                return;
            }

            observedBalance = newValue;
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            observedBalance = balance;
            DontDestroyOnLoad(gameObject);
        }

        [SyncVar] private int legProgress;
        [SyncVar] private int legQuota = 1800;
        [SyncVar] private int legIndex = 1;
        [SyncVar] private int runId;
        [SyncVar] private bool quotaReached;
        [SyncVar] private int ownedMask;
        [SyncVar] private int visitPurchases;
        [SyncVar] private int selfInjectorCharges;
        [SyncVar] private int medkitCharges;
        [SyncVar] private int continueReady;
        [SyncVar] private int continueTotal;

        private readonly Dictionary<string, int> visitConsumables = new();
        private readonly HashSet<string> committed = new();
        private bool warnedNoSave;

        public long Balance => balance;
        public int LegProgress => legProgress;
        public int LegQuota => legQuota;
        public int LegIndex => legIndex;
        public bool QuotaReached => quotaReached;
        public int OwnedMask => ownedMask;
        public int VisitPurchases => visitPurchases;
        public int SelfInjectorCharges => selfInjectorCharges;
        public int MedkitCharges => medkitCharges;
        public int ContinueReady => continueReady;
        public int ContinueTotal => continueTotal;

        public string LastPurchaseId { get; private set; } = string.Empty;

        public const int HardCapPerVisit = 8;
        public const float UndoWindowSeconds = 30f;

        public int AcceptedCount { get; private set; }
        public int UniqueCargoCount => committed.Count;
        public int CommittedTotal { get; private set; }
        public int DupBlocked { get; private set; }
        public string LastCommittedId { get; private set; } = string.Empty;
        public int LastCommittedValue { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogError($"[S4A] S4A_ACCOUNT_DUPLICATE existing_balance={Instance.balance} new_balance={balance}");
                return;
            }

            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        public override void OnStartServer()
        {
            base.OnStartServer();

            P0SaveService save = P0SaveService.Instance;
            bool restored = false;
            int persistedRunId = 0;

            if (save != null && save.Loaded)
            {
                TeamSaveData d = save.Data;
                persistedRunId = d.runId;

                balance = d.balance;
                legProgress = d.legProgress;
                legQuota = d.legQuota > 0 ? d.legQuota : 1800;
                legIndex = d.legIndex > 0 ? d.legIndex : 1;
                quotaReached = d.quotaReached;
                CommittedTotal = (int)d.sumCommitted;
                AcceptedCount = d.txnCount;
                RestorePurchasesFromSave(d);
                restored = true;

                runId = NewRunId();
            }

            if (runId == 0)
            {
                runId = NewRunId();
            }

            if (legIndex <= 0)
            {
                legIndex = 1;
            }

            if (legQuota <= 0)
            {
                legQuota = 1800;
            }

            if (restored)
            {
                Debug.Log($"[S4A] S4A_ACCOUNT_RESTORED balance={balance} legProgress={legProgress} legIndex={legIndex} quota={legQuota} txn={AcceptedCount} persistedRunId={persistedRunId} runId={runId} ownedMask={ownedMask}");
            }

            Debug.Log($"[S4A] S4A_ACCOUNT_READY runId={runId} legIndex={legIndex} quota={legQuota} balance={balance} persisted={Persisted}");
        }

        /// <summary>修复 ECO-01：读档时从 save.Data.purchases 重建 ownedMask 与消耗品层数。</summary>
        private void RestorePurchasesFromSave(TeamSaveData d)
        {
            ownedMask = 0;
            selfInjectorCharges = 0;
            medkitCharges = 0;
            LastPurchaseId = string.Empty;

            if (d == null || d.purchases == null)
            {
                return;
            }

            ShopCatalog catalog = ShopCatalog.Instance;
            foreach (PurchaseRecord record in d.purchases)
            {
                if (record == null || record.state != PurchaseRecord.Active || string.IsNullOrEmpty(record.itemId))
                {
                    continue;
                }

                LastPurchaseId = record.purchaseId;

                if (catalog == null)
                {
                    continue;
                }

                int idx = catalog.IndexOf(record.itemId);
                ShopItem item = idx >= 0 ? catalog.At(idx) : null;
                if (item == null)
                {
                    continue;
                }

                if (item.consumable)
                {
                    if (item.effect == ShopEffect.SelfInjector)
                    {
                        selfInjectorCharges++;
                    }
                    else if (item.effect == ShopEffect.Medkit)
                    {
                        medkitCharges++;
                    }
                }
                else if (idx >= 0 && idx < 31)
                {
                    ownedMask |= 1 << idx;
                }
            }
        }

        private static int NewRunId()
        {
            return new System.Random(System.Guid.NewGuid().GetHashCode()).Next(1, int.MaxValue);
        }

        public static string CargoId(GrabbableProp prop)
        {
            if (prop == null)
            {
                return string.Empty;
            }

            NetworkIdentity id = prop.GetComponent<NetworkIdentity>();
            uint netId = id != null ? id.netId : 0;
            int run = Instance != null ? Instance.runId : 0;
            int leg = Instance != null ? Instance.legIndex : 0;
            return $"{run}-{leg}-{netId}";
        }

        [Server]
        public bool Deposit(GrabbableProp prop)
        {
            if (prop == null)
            {
                return false;
            }

            return DepositId(CargoId(prop), prop.CurrentValue, prop);
        }

        [Server]
        public bool DepositId(string cargoId, int value, GrabbableProp prop = null)
        {
            long before = balance;

            if (string.IsNullOrEmpty(cargoId))
            {
                Debug.Log($"[S4A] S4A_DEPOSIT cargoId=- value=0 state=Rejected reason=empty balance={before} legProgress={legProgress} totalAfter={balance}");
                return false;
            }

            Debug.Log($"[S4A] S4A_DEPOSIT cargoId={cargoId} value={value} state=Pending reason=- balance={before} legProgress={legProgress} totalAfter={balance}");

            if (committed.Contains(cargoId))
            {
                DupBlocked++;
                Debug.Log($"[S4A] S4A_DEPOSIT_DUP cargoId={cargoId} balanceBefore={before} balanceAfter={balance}");
                Debug.Log($"[S4A] S4A_DEPOSIT cargoId={cargoId} value={value} state=Rejected reason=duplicate balance={balance} legProgress={legProgress} totalAfter={balance}");
                return false;
            }

            if (value <= 0)
            {
                Debug.Log($"[S4A] S4A_DEPOSIT_ZERO cargoId={cargoId} balance={balance} legProgress={legProgress}");
                Debug.Log($"[S4A] S4A_DEPOSIT cargoId={cargoId} value={value} state=Rejected reason=zero_value balance={balance} legProgress={legProgress} totalAfter={balance}");
                return false;
            }

            long newBalance = balance + value;
            int newProgress = legProgress + value;

            bool wasReached = quotaReached;
            bool newReached = quotaReached || newProgress >= legQuota;

            P0SaveService save = P0SaveService.Instance;
            if (save != null)
            {
                if (!save.Loaded)
                {
                    Debug.Log($"[S4A] S4A_DEPOSIT cargoId={cargoId} value={value} state=Rejected reason=save_unavailable save_state={save.State} balance={balance} legProgress={legProgress} totalAfter={balance}");
                    Debug.LogError($"[S4A] S4A_SAVE_REJECT cargoId={cargoId} value={value} reason=save_unavailable state={save.State}");
                    return false;
                }

                TeamSaveData next = save.Data.Clone();
                if (next == null)
                {
                    Debug.LogError($"[S4A] S4A_SAVE_REJECT cargoId={cargoId} value={value} reason=clone_failed");
                    return false;
                }

                next.runId = runId;
                next.legIndex = legIndex;
                next.legQuota = legQuota;
                next.balance = newBalance;
                next.legProgress = newProgress;
                next.quotaReached = newReached;

                SaveTxnRecord record = new()
                {
                    cargoId = cargoId,
                    value = value,
                    legIndex = legIndex,
                    balanceAfter = newBalance,
                    tsUnixMs = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                };

                if (!save.TryCommit(next, record, out string saveError))
                {
                    Debug.Log($"[S4A] S4A_DEPOSIT cargoId={cargoId} value={value} state=Rejected reason=save_failed detail={saveError} balance={balance} legProgress={legProgress} totalAfter={balance}");
                    Debug.LogError($"[S4A] S4A_SAVE_REJECT cargoId={cargoId} value={value} reason={saveError}");
                    return false;
                }
            }
            else
            {
                WarnSaveAbsentOnce();
            }

            committed.Add(cargoId);
            balance = newBalance;
            legProgress = newProgress;
            quotaReached = newReached;
            CommittedTotal += value;
            AcceptedCount++;
            LastCommittedId = cargoId;
            LastCommittedValue = value;

            Debug.Log($"[S4A] S4A_DEPOSIT cargoId={cargoId} value={value} state=Committed reason=- balance={balance} legProgress={legProgress} totalAfter={balance}");
            Debug.Log($"[S4A] GATE_QUOTA_GATE leg={legIndex} progress={legProgress} quota={legQuota} carry_over={Mathf.Max(0, legProgress - legQuota)} portal={(quotaReached ? "unlocked" : "locked")}");

            if (!wasReached && newReached)
            {
                PlaytestRecorder.Instance?.NoteLegEnd(legIndex, legProgress, legQuota);
            }

            if (prop != null)
            {
                GrabController holder = prop.Holder;
                if (holder != null)
                {
                    holder.ServerForceRelease();
                }

                NetworkServer.Destroy(prop.gameObject);
            }

            return true;
        }

        [Server]
        public int DepositAll(IReadOnlyList<GrabbableProp> props)
        {
            if (props == null)
            {
                return 0;
            }

            int accepted = 0;
            int sum = 0;

            foreach (GrabbableProp prop in props)
            {
                if (prop == null)
                {
                    continue;
                }

                int val = prop.CurrentValue;
                if (Deposit(prop))
                {
                    accepted++;
                    sum += val;
                }
            }

            Debug.Log($"[S4A] S4A_DEPOSIT_CART requested={props.Count} accepted={accepted} sum={sum} balance={balance}");
            return sum;
        }

        private void WarnSaveAbsentOnce()
        {
            if (warnedNoSave)
            {
                return;
            }

            warnedNoSave = true;
            Debug.LogWarning("[S4A] S4A_SAVE_ABSENT path=memory reason=no_save_service");
        }

        [Server]
        public void SetQuota(int quota)
        {
            P0SaveService save = P0SaveService.Instance;
            if (save != null)
            {
                if (!save.Loaded)
                {
                    Debug.LogError($"[S4A] S4A_SAVE_REJECT phase=set_quota quota={quota} reason=save_unavailable state={save.State}");
                    return;
                }

                TeamSaveData next = save.Data.Clone();
                if (next != null)
                {
                    next.legQuota = quota;
                    next.balance = balance;
                    next.legProgress = legProgress;
                    next.legIndex = legIndex;
                    next.quotaReached = quotaReached;
                    if (!save.TryCommitState(next, "set_quota", out string err))
                    {
                        Debug.LogError($"[S4A] S4A_SAVE_REJECT phase=set_quota quota={quota} reason={err}");
                        return;
                    }
                }
            }

            legQuota = quota;
        }

        [Server]
        public void NextLeg(int quota, string source)
        {
            int nextIndex = legIndex + 1;
            int nextQuota = quota;

            P0SaveService save = P0SaveService.Instance;
            if (save != null)
            {
                if (!save.Loaded)
                {
                    Debug.LogError($"[S4A] S4A_SAVE_REJECT phase=next_leg target={nextIndex} reason=save_unavailable state={save.State}");
                    return;
                }

                TeamSaveData next = save.Data.Clone();
                if (next != null)
                {
                    next.legIndex = nextIndex;
                    next.legProgress = 0;
                    next.legQuota = nextQuota;
                    next.quotaReached = false;
                    next.balance = balance;
                    if (!save.TryCommitState(next, "next_leg", out string err))
                    {
                        Debug.LogError($"[S4A] S4A_SAVE_REJECT phase=next_leg target={nextIndex} reason={err}");
                        return;
                    }
                }
            }

            legIndex = nextIndex;
            legProgress = 0;
            legQuota = nextQuota;
            quotaReached = false;
            visitPurchases = 0;
            visitConsumables.Clear();
            PurchaseWindowOpen = true;
            PurchaseWindowClosedAt = 0f;
            Debug.Log($"[S4A] GATE_LEVEL_ADVANCE n={legIndex} quota={legQuota} source={source} before={balance} after={balance}");
        }

        [Server]
        public bool ServerEndRun(string reason)
        {
            int legBefore = legIndex;
            long balanceBefore = balance;
            int quota = LevelRouteService.Instance != null ? LevelRouteService.Instance.QuotaFor(1) : 1800;
            int nextRunId = NewRunId();

            P0SaveService save = P0SaveService.Instance;
            if (save != null)
            {
                if (!save.Loaded)
                {
                    Debug.LogError($"[S4B] GATE_WIPE_FAIL reason=save_unavailable state={save.State} leg={legBefore}");
                    return false;
                }

                TeamSaveData next = save.Data.Clone();
                if (next == null)
                {
                    Debug.LogError($"[S4B] GATE_WIPE_FAIL reason=clone_failed leg={legBefore}");
                    return false;
                }

                next.runId = nextRunId;
                next.legIndex = 1;
                next.legProgress = 0;
                next.legQuota = quota;
                next.quotaReached = false;
                next.balance = balance;

                if (!save.TryCommitState(next, "wipe", out string error))
                {
                    Debug.LogError($"[S4B] GATE_WIPE_FAIL reason={error} leg={legBefore}");
                    return false;
                }
            }

            runId = nextRunId;
            legIndex = 1;
            legProgress = 0;
            legQuota = quota;
            quotaReached = false;
            visitPurchases = 0;
            visitConsumables.Clear();
            committed.Clear();
            selfInjectorCharges = 0;
            medkitCharges = 0;
            LastPurchaseId = string.Empty;

            Debug.Log($"[S4B] GATE_WIPE_STATE reason={reason} legBefore={legBefore} legAfter=1 balanceBefore={balanceBefore} balanceAfter={balance} runId={runId}");
            PlaytestRecorder.Instance?.NoteRunEnd(reason, legBefore, balanceBefore);
            return true;
        }

        [Server]
        public bool TryUseSelfInjector()
        {
            if (selfInjectorCharges <= 0)
            {
                return false;
            }

            selfInjectorCharges--;
            Debug.Log($"[S4B] GATE_INJECTOR_USE mode=bought remaining={selfInjectorCharges}");
            return true;
        }

        [Server]
        public bool TryUseMedkit()
        {
            if (medkitCharges <= 0)
            {
                return false;
            }

            medkitCharges--;
            Debug.Log($"[S4B] GATE_MEDKIT_USE remaining={medkitCharges}");
            return true;
        }

        [Server]
        public void ServerSetContinueProgress(int ready, int total)
        {
            if (continueReady == ready && continueTotal == total)
            {
                return;
            }

            continueReady = ready;
            continueTotal = total;
        }

        public bool PurchaseWindowOpen { get; private set; } = true;

        private float PurchaseWindowClosedAt { get; set; }

        public void OpenPurchaseWindow()
        {
            PurchaseWindowOpen = true;
            PurchaseWindowClosedAt = 0f;
        }

        [Server]
        public void ClosePurchaseWindow(string reason)
        {
            if (!PurchaseWindowOpen)
            {
                return;
            }

            PurchaseWindowOpen = false;
            PurchaseWindowClosedAt = Time.unscaledTime;
            Debug.Log($"[S4A] S4A_SHOP_WINDOW state=closed reason={reason} balance={balance}");
        }

        public bool Owns(int index)
        {
            if (index < 0 || index >= 31)
            {
                return false;
            }

            return (ownedMask & (1 << index)) != 0;
        }

        public bool OwnsItem(string itemId)
        {
            ShopCatalog catalog = ShopCatalog.Instance;
            return catalog != null && Owns(catalog.IndexOf(itemId));
        }

        [Server]
        public bool TryPurchase(string itemId)
        {
            ShopCatalog catalog = ShopCatalog.Instance;
            if (catalog == null)
            {
                Debug.LogError("[S4A] S4A_SHOP_FAIL reason=no_catalog");
                return false;
            }

            int index = catalog.IndexOf(itemId);
            ShopItem item = index >= 0 ? catalog.At(index) : null;

            if (item == null)
            {
                Debug.LogError($"[S4A] S4A_SHOP_FAIL reason=unknown_item itemId={itemId}");
                return false;
            }

            P0SaveService save = P0SaveService.Instance;

            if (save == null || !save.Loaded)
            {
                RejectBuy(itemId, "read_only");
                return false;
            }

            if (visitPurchases >= HardCapPerVisit)
            {
                RejectBuy(itemId, "hard_cap");
                return false;
            }

            int usedThisVisit = visitConsumables.TryGetValue(itemId, out int used) ? used : 0;

            if (item.consumable)
            {
                if (usedThisVisit >= Mathf.Max(1, item.maxOwned))
                {
                    RejectBuy(itemId, "already_owned");
                    return false;
                }
            }
            else if (Owns(index))
            {
                RejectBuy(itemId, "already_owned");
                return false;
            }

            if (balance < item.price)
            {
                RejectBuy(itemId, "insufficient");
                return false;
            }

            long newBalance = balance - item.price;
            string purchaseId = NextPurchaseId();

            TeamSaveData next = save.Data.Clone();
            if (next == null)
            {
                Debug.LogError($"[S4A] S4A_SHOP_FAIL reason=clone_failed itemId={itemId}");
                return false;
            }

            next.balance = newBalance;
            next.legIndex = legIndex;
            next.legQuota = legQuota;
            next.legProgress = legProgress;
            next.quotaReached = quotaReached;
            next.purchases ??= new List<PurchaseRecord>();
            next.purchases.Add(new PurchaseRecord
            {
                purchaseId = purchaseId,
                itemId = itemId,
                price = item.price,
                state = PurchaseRecord.Active,
                legIndex = legIndex,
                tsUnixMs = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            });

            SaveTxnRecord record = new()
            {
                cargoId = $"shop:{purchaseId}",
                value = -item.price,
                legIndex = legIndex,
                balanceAfter = newBalance,
                tsUnixMs = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            if (!save.TryCommit(next, record, out string saveError))
            {
                Debug.LogError($"[S4A] S4A_SHOP_FAIL reason={saveError} itemId={itemId}");
                return false;
            }

            if (item.consumable)
            {
                visitConsumables[itemId] = usedThisVisit + 1;

                if (item.effect == ShopEffect.SelfInjector)
                {
                    selfInjectorCharges++;
                }
                else if (item.effect == ShopEffect.Medkit)
                {
                    medkitCharges++;
                }
            }
            else
            {
                ownedMask |= 1 << index;
            }

            visitPurchases++;
            balance = newBalance;
            LastPurchaseId = purchaseId;

            Debug.Log($"[S4A] GATE_BUY itemId={itemId} price={item.price} balanceAfter={balance} purchaseId={purchaseId}");
            return true;
        }

        [Server]
        public bool TryUndoPurchase(string purchaseId)
        {
            P0SaveService save = P0SaveService.Instance;
            if (save == null || !save.Loaded)
            {
                Debug.Log($"[S4A] GATE_BUY_UNDO_REJECT purchaseId={purchaseId} state=unknown reason=window_closed balance={balance}");
                return false;
            }

            PurchaseRecord target = null;
            if (save.Data.purchases != null)
            {
                foreach (PurchaseRecord p in save.Data.purchases)
                {
                    if (p != null && string.Equals(p.purchaseId, purchaseId, System.StringComparison.Ordinal))
                    {
                        target = p;
                        break;
                    }
                }
            }

            if (target == null || target.state != PurchaseRecord.Active)
            {
                Debug.Log($"[S4A] GATE_BUY_UNDO_REJECT purchaseId={purchaseId} state={(target == null ? "unknown" : "revoked")} reason=already_revoked balance={balance}");
                return false;
            }

            if (!PurchaseWindowOpen)
            {
                Debug.Log($"[S4A] GATE_BUY_UNDO_REJECT purchaseId={purchaseId} state=active reason=window_closed balance={balance}");
                return false;
            }

            ShopCatalog catalog = ShopCatalog.Instance;
            ShopItem item = catalog != null ? catalog.Find(target.itemId) : null;
            bool consumable = item != null && item.consumable;

            if (consumable)
            {
                int used = visitConsumables.TryGetValue(target.itemId, out int count) ? count : 0;
                bool hasCharge = true;

                if (item.effect == ShopEffect.SelfInjector)
                {
                    hasCharge = selfInjectorCharges > 0;
                }
                else if (item.effect == ShopEffect.Medkit)
                {
                    hasCharge = medkitCharges > 0;
                }

                if (used <= 0 || !hasCharge)
                {
                    Debug.Log($"[S4A] GATE_BUY_UNDO_REJECT purchaseId={purchaseId} state=active reason=already_used balance={balance}");
                    return false;
                }
            }

            int refund = target.price;
            long newBalance = balance + refund;

            TeamSaveData next = save.Data.Clone();
            if (next == null)
            {
                Debug.LogError($"[S4A] S4A_SHOP_FAIL reason=clone_failed phase=undo purchaseId={purchaseId}");
                return false;
            }

            next.balance = newBalance;
            next.legIndex = legIndex;
            next.legQuota = legQuota;
            next.legProgress = legProgress;
            next.quotaReached = quotaReached;

            foreach (PurchaseRecord p in next.purchases)
            {
                if (p != null && string.Equals(p.purchaseId, purchaseId, System.StringComparison.Ordinal))
                {
                    p.state = PurchaseRecord.Revoked;
                    break;
                }
            }

            SaveTxnRecord record = new()
            {
                cargoId = $"shopundo:{purchaseId}",
                value = refund,
                legIndex = legIndex,
                balanceAfter = newBalance,
                tsUnixMs = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            if (!save.TryCommit(next, record, out string saveError))
            {
                Debug.LogError($"[S4A] S4A_SHOP_FAIL reason={saveError} phase=undo purchaseId={purchaseId}");
                return false;
            }

            if (consumable)
            {
                int usedNow = visitConsumables.TryGetValue(target.itemId, out int count) ? count : 1;
                visitConsumables[target.itemId] = Mathf.Max(0, usedNow - 1);

                if (item.effect == ShopEffect.SelfInjector)
                {
                    selfInjectorCharges = Mathf.Max(0, selfInjectorCharges - 1);
                }
                else if (item.effect == ShopEffect.Medkit)
                {
                    medkitCharges = Mathf.Max(0, medkitCharges - 1);
                }
            }
            else if (catalog != null)
            {
                int index = catalog.IndexOf(target.itemId);
                if (index >= 0)
                {
                    ownedMask &= ~(1 << index);
                }
            }

            balance = newBalance;

            Debug.Log($"[S4A] GATE_BUY_UNDO purchaseId={purchaseId} refund={refund} balanceAfter={balance}");
            return true;
        }

        private void RejectBuy(string itemId, string reason)
        {
            Debug.Log($"[S4A] GATE_BUY_REJECT itemId={itemId} reason={reason} balance={balance} visit={visitPurchases}");
        }

        private int purchaseSeq;

        private string NextPurchaseId()
        {
            purchaseSeq++;
            return $"{runId}-{legIndex}-{purchaseSeq}";
        }
    }
}
