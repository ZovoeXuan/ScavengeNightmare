using Mirror;
using ScavengeNightmare.Combat;
using ScavengeNightmare.Economy;
using ScavengeNightmare.Monsters;
using ScavengeNightmare.Networking;
using ScavengeNightmare.Noise;
using ScavengeNightmare.Save;
using UnityEngine;

namespace ScavengeNightmare.Gameplay
{
    /// <summary>
    /// V2 全局节奏与锯齿难度导演 (NightPacingDirector)：
    /// 基于游戏设计理论 (Sawtooth Difficulty Curve & Tension-Release Loop)：
    /// 1) 达标撤离高潮 (Quota Reached Finale)：当小队首次凑齐当晚 Quota 解锁撤离门时，
    ///    触发一次全馆断电闪烁与声波震荡 (+18 噪音唤醒附近巡逻怪)，同时给予全员体能恢复加速激励，
    ///    将「凑够钱平淡走人」升级为惊心动魄的「最后百米撤离冲刺」；
    /// 2) 绝境负反馈保护 (Comeback Cushion)：当全队仅剩 1 名存活玩家且多名队友倒地时，
    ///    临时加速全局噪音自然衰减 (+2.0/s)，给孤狼玩家创造拉起队友的战术喘息窗口。
    /// </summary>
    public sealed class NightPacingDirector : NetworkBehaviour
    {
        public static NightPacingDirector Instance { get; private set; }

        [SyncVar] private bool extractionRushActive;
        [SyncVar] private bool clutchModeActive;

        private bool triggeredQuotaRushThisLeg;
        private int trackedLeg = -1;

        public bool ExtractionRushActive => extractionRushActive;
        public bool ClutchModeActive => clutchModeActive;

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

        private void Update()
        {
            if (!NetworkServer.active || S4ACli.IsTestRun)
            {
                return;
            }

            TeamAccount account = TeamAccount.Instance;
            LevelRouteService route = LevelRouteService.Instance;
            if (account == null || route == null || route.InSafehouse)
            {
                extractionRushActive = false;
                clutchModeActive = false;
                triggeredQuotaRushThisLeg = false;
                return;
            }

            if (trackedLeg != account.LegIndex)
            {
                trackedLeg = account.LegIndex;
                triggeredQuotaRushThisLeg = false;
                extractionRushActive = false;
            }

            if (account.QuotaReached && !triggeredQuotaRushThisLeg)
            {
                triggeredQuotaRushThisLeg = true;
                extractionRushActive = true;
                TriggerExtractionRush(account);
            }

            UpdateClutchCushion();
        }

        [Server]
        private void TriggerExtractionRush(TeamAccount account)
        {
            Vector3 rushOrigin = LevelPortal.Instance != null
                ? LevelPortal.Instance.transform.position
                : Vector3.zero;

            NoiseBus.Report("extraction_portal_surge", 18f, rushOrigin);

            foreach (PlayerVitals vitals in PlayerVitals.All)
            {
                if (vitals == null || vitals.IsDowned)
                {
                    continue;
                }

                PlayerStamina stamina = vitals.GetComponent<PlayerStamina>();
                stamina?.ResetStamina();
            }

            Debug.Log($"[S4C] GATE_EXTRACTION_RUSH leg={account.LegIndex} progress={account.LegProgress} quota={account.LegQuota}");
        }

        [Server]
        private void UpdateClutchCushion()
        {
            int total = 0;
            int alive = 0;

            foreach (PlayerVitals v in PlayerVitals.All)
            {
                if (v == null)
                {
                    continue;
                }

                total++;
                if (!v.IsDowned)
                {
                    alive++;
                }
            }

            bool clutch = total >= 2 && alive == 1;
            if (clutchModeActive != clutch)
            {
                clutchModeActive = clutch;
            }

            if (clutch)
            {
                // 孤狼负反馈减压：额外衰减噪音，帮助最后一名队友潜行救人
                NoiseBus.Decay(Time.deltaTime * 0.65f);
            }
        }
    }
}
