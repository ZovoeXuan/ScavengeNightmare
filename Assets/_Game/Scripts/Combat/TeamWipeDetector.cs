using Mirror;
using ScavengeNightmare.Cart;
using ScavengeNightmare.Economy;
using ScavengeNightmare.Gameplay;
using ScavengeNightmare.Noise;
using UnityEngine;

namespace ScavengeNightmare.Combat
{
    /// <summary>
    /// 团灭判定 V2 升级版：
    /// 1) 锁定倒地宽限期 latchedGraceSeconds，避免倒地期间针剂状态变动导致宽限期从 10s 骤降到 3s 瞬间误判团灭；
    /// 2) 团灭重置时同步清空 NoiseBus 与安全校验 NetworkManager.singleton。
    /// </summary>
    public sealed class TeamWipeDetector : MonoBehaviour
    {
        [SerializeField] private float noInjectorGraceSeconds = 3f;
        [SerializeField] private float injectorGraceSeconds = 10f;

        private float allDownedSince = -1f;
        private float latchedGraceSeconds = 3f;
        private float nextScan;

        private void Update()
        {
            if (!NetworkServer.active)
            {
                return;
            }

            if (Time.unscaledTime < nextScan)
            {
                return;
            }

            nextScan = Time.unscaledTime + 0.5f;

            PlayerVitals.All.RemoveAll(v => v == null);

            int total = 0;
            int downed = 0;
            bool canSelfRevive = false;

            foreach (PlayerVitals vitals in PlayerVitals.All)
            {
                total++;
                if (!vitals.IsDowned)
                {
                    continue;
                }

                downed++;
                if (vitals.HasSelfReviveAvailable)
                {
                    canSelfRevive = true;
                }
            }

            if (total == 0 || downed < total)
            {
                allDownedSince = -1f;
                latchedGraceSeconds = noInjectorGraceSeconds;
                return;
            }

            float currentGrace = canSelfRevive ? injectorGraceSeconds : noInjectorGraceSeconds;

            if (allDownedSince < 0f)
            {
                allDownedSince = Time.unscaledTime;
                latchedGraceSeconds = currentGrace;
                Debug.Log($"[S4B] GATE_WIPE_WAIT total={total} downed={downed} injector={canSelfRevive} grace={latchedGraceSeconds}");
                return;
            }

            latchedGraceSeconds = Mathf.Max(latchedGraceSeconds, currentGrace);
            if (Time.unscaledTime - allDownedSince < latchedGraceSeconds)
            {
                return;
            }

            Wipe(total);
        }

        private void Wipe(int playerCount)
        {
            TeamAccount account = TeamAccount.Instance;
            if (account == null)
            {
                Debug.LogError("[S4B] GATE_WIPE_FAIL reason=no_account");
                allDownedSince = -1f;
                return;
            }

            int legBefore = account.LegIndex;
            long balanceBefore = account.Balance;

            if (!account.ServerEndRun("wipe"))
            {
                Debug.LogError($"[S4B] GATE_WIPE_FAIL reason=save_failed leg={legBefore} balance={balanceBefore}");
                allDownedSince = -1f;
                return;
            }

            int destroyed = 0;
            foreach (GrabbableProp prop in FindObjectsByType<GrabbableProp>(FindObjectsSortMode.None))
            {
                if (prop == null)
                {
                    continue;
                }

                NetworkServer.Destroy(prop.gameObject);
                destroyed++;
            }

            CartController cart = CartController.Instance;
            if (cart != null)
            {
                NetworkServer.Destroy(cart.gameObject);
                destroyed++;
            }

            foreach (PlayerVitals vitals in PlayerVitals.All)
            {
                if (vitals != null)
                {
                    vitals.ServerResetForNewRun();
                }
            }

            NoiseBus.Reset();

            Debug.Log($"[S4B] GATE_WIPE legBefore={legBefore} legAfter={account.LegIndex} balanceBefore={balanceBefore} balanceAfter={account.Balance} destroyedProps={destroyed}");

            allDownedSince = -1f;
            latchedGraceSeconds = noInjectorGraceSeconds;

            string safehouse = LevelRouteService.Instance != null
                ? LevelRouteService.Instance.SafehouseSceneName
                : "Safehouse";

            if (NetworkManager.singleton != null)
            {
                NetworkManager.singleton.ServerChangeScene(safehouse);
            }
        }
    }
}
