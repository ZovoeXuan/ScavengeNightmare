using System.Collections.Generic;
using Mirror;
using ScavengeNightmare.Economy;
using ScavengeNightmare.Gameplay;
using ScavengeNightmare.Networking;
using UnityEngine;

namespace ScavengeNightmare.Combat
{
    /// <summary>
    /// 4B 玩家生存：HP / 倒地 / 自救 / 救援 / 团灭输入。
    ///
    /// 权威边界：HP、倒地、无敌、救援进度全部服务端权威。
    /// 客户端只做两件事：读本地 R/H 键发 Command、按 SyncVar 显示遮罩与提示。
    ///
    /// 单次伤害保护（P0 §2.3）：HP ≥15 时任何一次伤害后最低保留 1 HP。
    /// 这保证「不会被一击秒」，也让「两次倒地」在 HP 模型下变成三次攻击——
    /// 这是 4B 首版口径，真人测试后再调 MonsterProfile 的伤害值，不在这里写死。
    /// </summary>
    public sealed class PlayerVitals : NetworkBehaviour
    {
        public const int MaxHp = 100;
        public const int DownedHp = 1;
        public const int ReviveHp = 50;
        public const float SelfReviveDelaySeconds = 8f;
        public const float ReviveInvulnSeconds = 2f;
        public const float DownedSpeed = 0.8f;

        /// <summary>场上所有玩家（服务端与客户端各维护一份，UI 与团灭判定共用）。</summary>
        public static readonly List<PlayerVitals> All = new();

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
        private PlayerMotor motor;
        private GrabController grab;

        public int Hp => hp;
        public bool IsDowned => downed;
        public float ReviveProgress => reviveProgress;

        /// <summary>本关免费自救针剩余（基础 1 支，不可被商品替代）。</summary>
        public int FreeInjectors => freeInjectors;

        /// <summary>倒地已过多少秒（客户端用于自救倒计时）。</summary>
        public float DownedSeconds => downed ? Mathf.Max(0f, Time.time - downedAtLocal) : 0f;

        /// <summary>客户端提示用：自救窗口是否已到、且还有针可用。</summary>
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

        /// <summary>服务端团灭判定用：是否还有自救手段。</summary>
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

        /// <summary>
        /// 服务端：受到伤害。怪物、尖啸罐、测试都走这里。
        /// </summary>
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

            // 单次伤害保护：从 ≥15 被打到 0 以下时只保留 1 HP。
            // 从 <15 开始则允许倒地，这是「已经重伤」的惩罚，不是无预警秒杀。
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

            if (grab != null)
            {
                grab.SetInputEnabled(false);
                if (grab.IsHolding)
                {
                    grab.Release(false);
                }
            }

            // 4B-5：腰带上的小件同样必须掉。不做这一步会出现「人倒了，腰上的东西还挂着」，
            // 队友把你救起来之后等于白捡一次负重豁免。
            BeltSlot belt = GetComponent<BeltSlot>();
            if (belt != null)
            {
                belt.ServerDropOnDowned();
            }

            ApplyDownedVisual(true);
            Debug.Log($"[S4B] GATE_DOWN target={netId} hp={DownedHp} source={source}");
        }

        /// <summary>
        /// 服务端：队友救援或自救。返回是否真的起身。
        /// bySelf=true 时强制检查 8s 窗口与针数量；bySelf=false 由 ReviveInteractor 先长按 4s。
        /// </summary>
        [Server]
        public bool ServerRevive(bool bySelf)
        {
            if (!downed)
            {
                return false;
            }

            float elapsed = Time.time - downedAtServer;
            bool usedFree = false;

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
                    usedFree = true;
                }
                else if (account != null && account.TryUseSelfInjector())
                {
                    usedFree = false;
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
            invulnUntilServer = Time.time + ReviveInvulnSeconds;
            ApplyDownedVisual(false);

            Debug.Log($"[S4B] GATE_REVIVE mode={(bySelf ? "self" : "teammate")} target={netId} reviver={(bySelf ? netId : 0)} seconds={elapsed:F1} hpAfter={ReviveHp} free={usedFree}");
            return true;
        }

        /// <summary>服务端：救援进度 0–1，供 HUD 显示。</summary>
        [Server]
        public void ServerSetReviveProgress(float value)
        {
            reviveProgress = downed ? Mathf.Clamp01(value) : 0f;
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
                Debug.Log($"[S4B] GATE_HEAL_REJECT target={netId} reason={(selfInjured ? "self_target" : "no_target")} mode=medkit");
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

        /// <summary>服务端：团灭重开时重置本关生存状态（余额不重置）。</summary>
        [Server]
        public void ServerResetForNewRun()
        {
            hp = MaxHp;
            downed = false;
            reviveProgress = 0f;
            freeInjectors = 1;
            downedAtServer = 0f;
            invulnUntilServer = 0f;
            ApplyDownedVisual(false);
        }

        /// <summary>测试钩子：强制设置免费自救针数量。</summary>
        [Server]
        public void ServerSetFreeInjectors(int value)
        {
            freeInjectors = Mathf.Max(0, value);
        }

        private void OnDownedChanged(bool oldValue, bool newValue)
        {
            if (newValue)
            {
                downedAtLocal = Time.time;
            }

            ApplyDownedVisual(newValue);

            if (newValue && isLocalPlayer && grab != null && grab.IsHolding)
            {
                grab.Release(false);
            }
        }

        private void ApplyDownedVisual(bool value)
        {
            if (motor != null)
            {
                motor.SetVitalsDowned(value);
                motor.SetVitalsSpeedCap(value ? DownedSpeed : -1f);
            }

            if (grab != null)
            {
                grab.SetInputEnabled(!value);
            }
        }
    }
}
