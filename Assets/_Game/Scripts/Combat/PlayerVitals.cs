using System.Collections.Generic;
using Mirror;
using ScavengeNightmare.Economy;
using ScavengeNightmare.Gameplay;
using ScavengeNightmare.Networking;
using UnityEngine;

namespace ScavengeNightmare.Combat
{
    /// <summary>
    /// 4B 玩家生存 V2 升级版：
    /// 1) EnterDowned 改为调用 grab.ServerForceRelease()，修复手持无字牌位倒地时无法脱手的 Bug；
    /// 2) 倒地时自动关闭手电筒（防止倒地玩家继续照射致盲空眼）；
    /// 3) 新增 activeReviverNetId 锁，修复双人同时救援或路过松开 E 键清空他人救援进度条的竞态 Bug；
    /// 4) 补充 SubsystemRegistration 静态列表清理。
    /// </summary>
    public sealed class PlayerVitals : NetworkBehaviour
    {
        public const int MaxHp = 100;
        public const int DownedHp = 1;
        public const int ReviveHp = 50;
        public const float SelfReviveDelaySeconds = 8f;
        public const float ReviveInvulnSeconds = 2f;
        public const float DownedSpeed = 0.8f;

        public static readonly List<PlayerVitals> All = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            All.Clear();
        }

        public static PlayerVitals Local
        {
            get
            {
                NetworkIdentity local = NetworkClient.localPlayer;
                return local != null ? local.GetComponent<PlayerVitals>() : null;
            }
        }

        [SyncVar] private int hp = MaxHp;
        [SyncVar(hook = nameof(OnDownedChanged))] private bool downed;
        [SyncVar] private float reviveProgress;
        [SyncVar] private int freeInjectors = 1;

        private float downedAtServer;
        private float downedAtLocal;
        private float invulnUntilServer;
        private uint activeReviverNetId;
        private PlayerMotor motor;
        private GrabController grab;
        private PlayerEquipment equipment;

        public int Hp => hp;
        public bool IsDowned => downed;
        public float ReviveProgress => reviveProgress;
        public int FreeInjectors => freeInjectors;
        public float DownedSeconds => downed ? Mathf.Max(0f, Time.time - downedAtLocal) : 0f;

        public bool SelfReviveReady
        {
            get
            {
                if (!downed || DownedSeconds < SelfReviveDelaySeconds)
                {
                    return false;
                }

                TeamAccount account = TeamAccount.Instance;
                int bought = account != null ? account.SelfInjectorCharges : 0;
                return freeInjectors > 0 || bought > 0;
            }
        }

        public bool HasSelfReviveAvailable
        {
            get
            {
                TeamAccount account = TeamAccount.Instance;
                int bought = account != null ? account.SelfInjectorCharges : 0;
                return freeInjectors > 0 || bought > 0;
            }
        }

        private void Awake()
        {
            motor = GetComponent<PlayerMotor>();
            grab = GetComponent<GrabController>();
            equipment = GetComponent<PlayerEquipment>();
        }

        public override void OnStartServer()
        {
            base.OnStartServer();

            hp = MaxHp;
            downed = false;
            reviveProgress = 0f;
            freeInjectors = 1;
            downedAtServer = 0f;
            invulnUntilServer = 0f;
            activeReviverNetId = 0;
            Register();
        }

        public override void OnStartClient()
        {
            base.OnStartClient();

            downedAtLocal = Time.time;
            Register();
            ApplyDownedVisual(downed);

            if (downed)
            {
                Debug.Log($"[S4B] S4B_VITALS_CLIENT_DOWNED netId={netId}");
            }
        }

        private void OnDestroy()
        {
            All.Remove(this);
        }

        private void Register()
        {
            if (!All.Contains(this))
            {
                All.Add(this);
            }
        }

        private void Update()
        {
            if (!isLocalPlayer)
            {
                return;
            }

            if (S4BInput.SelfInjectPressed)
            {
                if (S4BInput.AutoSelfInject)
                {
                    S4BInput.AutoSelfInject = false;
                }

                CmdUseInjector();
            }

            if (S4BInput.MedkitPressed)
            {
                if (S4BInput.AutoMedkit)
                {
                    S4BInput.AutoMedkit = false;
                }

                CmdUseMedkit();
            }
        }

        [Command]
        private void CmdUseInjector()
        {
            ServerRevive(true);
        }

        [Command]
        private void CmdUseMedkit()
        {
            ServerUseMedkit();
        }

        [Server]
        public void ServerApplyDamage(int amount, string source)
        {
            if (amount <= 0)
            {
                return;
            }

            if (downed)
            {
                Debug.Log($"[S4B] GATE_DAMAGE_REJECT target={netId} amount={amount} reason=already_downed source={source}");
                return;
            }

            if (Time.time < invulnUntilServer)
            {
                Debug.Log($"[S4B] GATE_DAMAGE_REJECT target={netId} amount={amount} reason=invulnerable source={source}");
                return;
            }

            int before = hp;
            int rawAfter = before - amount;

            if (before >= 15 && rawAfter < 1)
            {
                rawAfter = 1;
            }

            Debug.Log($"[S4B] GATE_DAMAGE target={netId} amount={amount} hpBefore={before} hpAfter={rawAfter} source={source}");

            if (rawAfter <= 0)
            {
                hp = DownedHp;
                EnterDowned(source);
                return;
            }

            hp = rawAfter;
        }

        [Server]
        private void EnterDowned(string source)
        {
            downed = true;
            downedAtServer = Time.time;
            downedAtLocal = Time.time;
            reviveProgress = 0f;
            activeReviverNetId = 0;

            if (grab != null)
            {
                grab.SetInputEnabled(false);
                if (grab.IsHolding)
                {
                    grab.ServerForceRelease();
                }
            }

            if (equipment != null)
            {
                equipment.ServerForceLightOff();
            }

            BeltSlot belt = GetComponent<BeltSlot>();
            if (belt != null)
            {
                belt.ServerDropOnDowned();
            }

            ApplyDownedVisual(true);
            Debug.Log($"[S4B] GATE_DOWN target={netId} hp={DownedHp} source={source}");
        }

        [Server]
        public bool ServerRevive(bool bySelf)
        {
            if (!downed)
            {
                return false;
            }

            float elapsed = Time.time - downedAtServer;

            if (bySelf)
            {
                if (elapsed < SelfReviveDelaySeconds)
                {
                    Debug.Log($"[S4B] GATE_REVIVE_REJECT target={netId} mode=self reason=not_ready remaining={(SelfReviveDelaySeconds - elapsed):F1}");
                    return false;
                }

                TeamAccount account = TeamAccount.Instance;
                if (freeInjectors > 0)
                {
                    freeInjectors--;
                }
                else if (account != null && account.TryUseSelfInjector())
                {
                }
                else
                {
                    Debug.Log($"[S4B] GATE_REVIVE_REJECT target={netId} mode=self reason=no_injector");
                    return false;
                }
            }

            downed = false;
            hp = ReviveHp;
            reviveProgress = 0f;
            activeReviverNetId = 0;
            invulnUntilServer = Time.time + ReviveInvulnSeconds;
            ApplyDownedVisual(false);

            Debug.Log($"[S4B] GATE_REVIVE mode={(bySelf ? "self" : "teammate")} target={netId} reviver={(bySelf ? netId : 0)}");
            return true;
        }

        [Server]
        public void ServerSetReviveProgress(float value)
        {
            reviveProgress = downed ? Mathf.Clamp01(value) : 0f;
        }

        /// <summary>V2 带救援者身份锁的进度更新：防止其他路过玩家松开 E 键把正在读条的队友进度清零。</summary>
        [Server]
        public void ServerUpdateReviveProgress(uint reviverNetId, float value)
        {
            if (!downed)
            {
                reviveProgress = 0f;
                activeReviverNetId = 0;
                return;
            }

            if (value <= 0f)
            {
                if (activeReviverNetId == 0 || activeReviverNetId == reviverNetId)
                {
                    reviveProgress = 0f;
                    activeReviverNetId = 0;
                }

                return;
            }

            if (activeReviverNetId == 0 || activeReviverNetId == reviverNetId || value >= reviveProgress)
            {
                activeReviverNetId = reviverNetId;
                reviveProgress = Mathf.Clamp01(value);
            }
        }

        [Server]
        public bool ServerHeal(int amount, string source)
        {
            if (amount <= 0)
            {
                return false;
            }

            if (downed)
            {
                Debug.Log($"[S4B] GATE_HEAL_REJECT target={netId} reason=downed source={source}");
                return false;
            }

            int before = hp;
            hp = Mathf.Min(MaxHp, hp + amount);
            Debug.Log($"[S4B] GATE_HEAL target={netId} amount={amount} hpBefore={before} hpAfter={hp} source={source}");
            return true;
        }

        [Server]
        private void ServerUseMedkit()
        {
            TeamAccount account = TeamAccount.Instance;
            if (account == null || account.MedkitCharges <= 0)
            {
                Debug.Log($"[S4B] GATE_HEAL_REJECT target={netId} reason=no_charge mode=medkit");
                return;
            }

            PlayerVitals best = null;
            float bestDistance = 2.5f;
            bool selfInjured = hp < MaxHp;

            foreach (PlayerVitals candidate in All)
            {
                if (candidate == null || candidate == this || candidate.downed)
                {
                    continue;
                }

                if (candidate.hp >= MaxHp)
                {
                    continue;
                }

                float distance = Vector3.Distance(transform.position, candidate.transform.position);
                if (distance <= bestDistance)
                {
                    bestDistance = distance;
                    best = candidate;
                }
            }

            if (best == null)
            {
                Debug.Log($"[S4B] GATE_HEAL_REJECT target={netId} reason={(selfInjured ? "self_target" : "no_target")}");
                return;
            }

            if (!account.TryUseMedkit())
            {
                Debug.Log($"[S4B] GATE_HEAL_REJECT target={netId} reason=no_charge mode=medkit");
                return;
            }

            best.ServerHeal(60, "medkit");
            Debug.Log($"[S4B] GATE_HEAL mode=medkit target={best.netId} charges={account.MedkitCharges}");
        }

        /// <summary>测试钩子：强制设置免费自救针数量</summary>
        [Server]
        public void ServerSetFreeInjectors(int value)
        {
            freeInjectors = Mathf.Max(0, value);
        }

        [Server]
        public void ServerResetForNewRun()
        {
            hp = MaxHp;
            downed = false;
            reviveProgress = 0f;
            activeReviverNetId = 0;
            freeInjectors = 1;
            downedAtServer = 0f;
            invulnUntilServer = 0f;
            ApplyDownedVisual(false);
        }

        private void OnDownedChanged(bool oldVal, bool newVal)
        {
            if (newVal)
            {
                downedAtLocal = Time.time;
            }

            ApplyDownedVisual(newVal);
        }

        private void ApplyDownedVisual(bool isDown)
        {
            if (motor != null)
            {
                motor.SetVitalsDowned(isDown);
                motor.SetVitalsSpeedCap(isDown ? DownedSpeed : -1f);
            }

            if (grab != null && isLocalPlayer)
            {
                grab.SetInputEnabled(!isDown);
            }
        }
    }
}
