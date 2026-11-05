using System.Collections.Generic;
using Mirror;
using ScavengeNightmare.Audio;
using ScavengeNightmare.Gameplay;
using ScavengeNightmare.Networking;
using ScavengeNightmare.Noise;
using UnityEngine;

namespace ScavengeNightmare.Curses
{
    /// <summary>
    /// V2 新增核心玩法：诅咒净化祭坛 (CursePurifyShrine)。
    /// 补全 P0/P1 设计文档中的「诅咒封印/净化」高风险博弈环：
    /// - 玩家手持未封印的诅咒物（笑面偶 60%=$510 / 无字牌位 60%=$780）走到祭坛 2.2m 内长按 E 键 2.0s；
    /// - 净化完成瞬间爆发 +35 噪音脉冲（可能引来空眼调查），但该诅咒物被永久封印 (ServerSealCurse)：
    ///   不仅彻底停止发笑、手抖和 1.5s 强行粘手，而且价值恢复至 100% 标价（$850 / $1,300）！
    /// </summary>
    public sealed class CursePurifyShrine : NetworkBehaviour
    {
        public static CursePurifyShrine Instance { get; private set; }

        [SerializeField] private float interactRadius = 2.2f;
        [SerializeField] private float purifyHoldSeconds = 2.0f;
        [SerializeField] private float purifyNoiseBurst = 35f;

        [SyncVar] private float activeProgress;
        [SyncVar] private int purifiedCount;

        private readonly Dictionary<PlayerMotor, float> holdTimers = new();

        public float ActiveProgress => activeProgress;
        public int PurifiedCount => purifiedCount;

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
            if (!NetworkServer.active)
            {
                return;
            }

            float maxRatio = 0f;

            foreach (PlayerMotor motor in FindObjectsByType<PlayerMotor>(FindObjectsSortMode.None))
            {
                if (motor == null || motor.VitalsDowned)
                {
                    continue;
                }

                float dist = Vector3.Distance(transform.position, motor.transform.position);
                GrabController grab = motor.GetComponent<GrabController>();
                CursedItem cursed = grab != null && grab.HeldProp != null
                    ? grab.HeldProp.GetComponent<CursedItem>()
                    : null;

                PlayerInteract interact = motor.GetComponent<PlayerInteract>();
                bool validHold = dist <= interactRadius
                                 && cursed != null
                                 && !cursed.Sealed
                                 && interact != null
                                 && interact.InteractHeldServer;

                if (!validHold)
                {
                    holdTimers[motor] = 0f;
                    continue;
                }

                float t = holdTimers.TryGetValue(motor, out float prev) ? prev : 0f;
                t += Time.deltaTime;
                holdTimers[motor] = t;

                float ratio = Mathf.Clamp01(t / Mathf.Max(0.1f, purifyHoldSeconds));
                if (ratio > maxRatio)
                {
                    maxRatio = ratio;
                }

                if (t >= purifyHoldSeconds)
                {
                    holdTimers[motor] = 0f;
                    CompletePurification(motor, cursed);
                    maxRatio = 0f;
                }
            }

            activeProgress = maxRatio;
        }

        [Server]
        private void CompletePurification(PlayerMotor player, CursedItem cursed)
        {
            if (cursed == null || cursed.Sealed)
            {
                return;
            }

            int beforeValue = cursed.CurrentValue;
            cursed.ServerSealCurse();
            int afterValue = cursed.CurrentValue;
            purifiedCount++;

            NoiseBus.Report("curse_purify_shrine", purifyNoiseBurst, transform.position);
            AudioBus.Play(AudioCue.CurseOmen, transform.position, 1.35f, "purified=1");

            Debug.Log($"[S4B] GATE_CURSE_PURIFIED id={cursed.CurseId} player={player.netId} " +
                      $"valueBefore={beforeValue} valueAfter={afterValue} noise={purifyNoiseBurst:F0}");
        }
    }
}
