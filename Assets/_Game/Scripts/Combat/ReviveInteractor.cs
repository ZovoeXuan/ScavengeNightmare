using Mirror;
using ScavengeNightmare.Networking;
using UnityEngine;

namespace ScavengeNightmare.Combat
{
    /// <summary>
    /// 队友救援 V2 升级版：
    /// 使用 ServerUpdateReviveProgress(netId, progress) 避免多名队友靠近时互相覆盖清空救援进度条。
    /// </summary>
    public sealed class ReviveInteractor : NetworkBehaviour
    {
        private const float RangeMeters = 2.5f;
        private const float ReviveSeconds = 4f;

        private PlayerVitals self;
        private PlayerInteract interact;
        private PlayerVitals target;
        private float progress;

        private void Awake()
        {
            self = GetComponent<PlayerVitals>();
            interact = GetComponent<PlayerInteract>();
        }

        private void Update()
        {
            if (!isServer)
            {
                return;
            }

            if (self == null || self.IsDowned || interact == null)
            {
                ResetTarget("no_reviver");
                return;
            }

            if (!interact.InteractHeldServer)
            {
                ResetTarget(progress > 0 ? "interrupted" : "no_input");
                return;
            }

            if (target != null && !target.IsDowned)
            {
                ResetTarget("already_up");
                return;
            }

            PlayerVitals next = FindNearestDowned();
            if (next == null)
            {
                ResetTarget(progress > 0 ? "no_target" : "idle");
                return;
            }

            if (next != target)
            {
                if (target != null && progress > 0f)
                {
                    target.ServerUpdateReviveProgress(netId, 0f);
                    Debug.Log($"[S4B] GATE_REVIVE_REJECT target={target.netId} reason=target_changed");
                }

                target = next;
                progress = 0f;
                Debug.Log($"[S4B] GATE_REVIVE_START reviver={netId} target={target.netId} range={RangeMeters:F1}");
            }

            progress += Time.deltaTime;
            target.ServerUpdateReviveProgress(netId, progress / ReviveSeconds);

            if (progress >= ReviveSeconds)
            {
                bool ok = target.ServerRevive(false);
                Debug.Log($"[S4B] GATE_REVIVE_RESULT reviver={netId} target={target.netId} ok={ok}");
                ResetTarget("done");
            }
        }

        private PlayerVitals FindNearestDowned()
        {
            PlayerVitals best = null;
            float bestDistance = RangeMeters;

            foreach (PlayerVitals candidate in PlayerVitals.All)
            {
                if (candidate == null || candidate == self || !candidate.IsDowned)
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

            return best;
        }

        private void ResetTarget(string reason)
        {
            if (target != null)
            {
                target.ServerUpdateReviveProgress(netId, 0f);

                if (progress > 0f && reason == "interrupted")
                {
                    Debug.Log($"[S4B] GATE_REVIVE_REJECT target={target.netId} reason=interrupted elapsed={progress:F1}");
                }
            }

            target = null;
            progress = 0f;
        }
    }
}
