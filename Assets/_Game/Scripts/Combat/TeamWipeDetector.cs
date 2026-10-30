using Mirror;
using ScavengeNightmare.Cart;
using ScavengeNightmare.Economy;
using ScavengeNightmare.Gameplay;
using UnityEngine;

namespace ScavengeNightmare.Combat
{
    /// <summary>
    /// 团灭判定（服务端）：全队倒地且无人能自救 → 宽限后结束本趟。
    ///
    /// 宽限分两档：
    /// - 还有人带着自救针：10s（8s 自救窗口 + 2s 余量），不能抢在玩家按键前判死。
    /// - 没人有针：3s（够最后一次队友救援，不给「无限等人」）。
    ///
    /// 团灭只结束本趟，不扣钱：TeamAccount.ServerEndRun 保留余额，重置 legIndex/progress，
    /// 未提交的货与推车销毁（Q3：团灭不折算、不补偿）。
    /// </summary>
    public sealed class TeamWipeDetector : MonoBehaviour
    {
        [SerializeField] private float noInjectorGraceSeconds = 3f;
        [SerializeField] private float injectorGraceSeconds = 10f;

        private float allDownedSince = -1f;
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

            int total = 0;
            int downed = 0;
            bool canSelfRevive = false;

            foreach (PlayerVitals vitals in PlayerVitals.All)
            {
                if (vitals == null)
                {
                    continue;
                }

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
                return;
            }

            if (allDownedSince < 0f)
            {
                allDownedSince = Time.unscaledTime;
                float grace = canSelfRevive ? injectorGraceSeconds : noInjectorGraceSeconds;
                Debug.Log($"[S4B] GATE_WIPE_WAIT total={total} downed={downed} injector={canSelfRevive} grace={grace:F1}");
                return;
            }

            float requiredGrace = canSelfRevive ? injectorGraceSeconds : noInjectorGraceSeconds;
            if (Time.unscaledTime - allDownedSince < requiredGrace)
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

            Debug.Log($"[S4B] GATE_WIPE legBefore={legBefore} legAfter={account.LegIndex} balanceBefore={balanceBefore} balanceAfter={account.Balance} cargoDestroyed={destroyed} players={playerCount}");

            allDownedSince = -1f;

            string safehouse = LevelRouteService.Instance != null
                ? LevelRouteService.Instance.SafehouseSceneName
                : "Safehouse";

            NetworkManager.singleton.ServerChangeScene(safehouse);
        }
    }
}
