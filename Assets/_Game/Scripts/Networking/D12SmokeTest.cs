using System.Collections;
using Mirror;
using ScavengeNightmare.Gameplay;
using UnityEngine;

namespace ScavengeNightmare.Networking
{
    /// <summary>
    /// D12 进图冒烟：验证「可进图拾荒、动线合理、窄门与楼梯可用」这三条。
    /// 只跑房主单机即可——它验的是场景内容与系统接线，不是联机同步。
    ///
    /// 用法：Supermarket 包加 -d12smoke
    /// </summary>
    public sealed class D12SmokeTest : MonoBehaviour
    {
        private IEnumerator Start()
        {
            bool enabled = false;
            foreach (string a in System.Environment.GetCommandLineArgs())
            {
                if (string.Equals(a, "-d12smoke", System.StringComparison.OrdinalIgnoreCase))
                {
                    enabled = true;
                }
            }

            if (!enabled)
            {
                yield break;
            }

            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 60;
            // 中性身份：这里验的是地图，不是角色差异（D8 的老规矩）
            Traits.TraitSettings.ForceNoTraits = true;

            NetworkManager.singleton.StartHost();

            float wait = 0f;
            while (NetworkClient.localPlayer == null && wait < 15f)
            {
                wait += Time.unscaledDeltaTime;
                yield return null;
            }

            NetworkIdentity player = NetworkClient.localPlayer;
            if (player == null)
            {
                Debug.LogError("[D12] D12_SMOKE_FAIL reason=no_local_player");
                yield break;
            }

            PlayerMotor motor = player.GetComponent<PlayerMotor>();
            GrabController grab = player.GetComponent<GrabController>();

            Debug.Log("[D12] D12_SMOKE_START map=Supermarket");

            // ---- 1. 物件是否按锚点刷出来 ----
            float propWait = 0f;
            while (propWait < 12f)
            {
                propWait += Time.unscaledDeltaTime;
                if (Object.FindObjectsByType<GrabbableProp>(FindObjectsSortMode.None).Length > 0) break;
                yield return null;
            }

            GrabbableProp[] props = Object.FindObjectsByType<GrabbableProp>(FindObjectsSortMode.None);
            int anchors = Object.FindObjectsByType<PropSpawnAnchor>(FindObjectsSortMode.None).Length;
            bool propsOk = props.Length >= anchors && anchors > 0;
            Debug.Log($"[D12] D12_SMOKE_PROPS spawned={props.Length} anchors={anchors} ok={propsOk}");

            // ---- 2. 能抓到东西（可进图拾荒）----
            bool grabOk = false;
            if (props.Length > 0 && grab != null)
            {
                GrabbableProp target = NearestProp(props, motor.transform.position);
                grab.SetInputEnabled(false);

                // 把玩家挪到物件旁并面向它（客户端权威，本地改即可）
                Vector3 tp = target.transform.position;
                motor.transform.position = new Vector3(tp.x, tp.y + 1.0f, tp.z - 1.3f);
                motor.transform.LookAt(new Vector3(tp.x, tp.y + 1.0f, tp.z));
                yield return new WaitForSeconds(0.3f);

                grab.TryGrab(target);
                yield return new WaitForSeconds(0.5f);
                grabOk = grab.IsHolding;
                Debug.Log($"[D12] D12_SMOKE_GRAB target={target.name} holding={grabOk} mass={target.Mass:F0}kg");
                grab.Release(false);
            }

            // ---- 3. 楼梯能上（夹层 +3m）----
            float before = motor.transform.position.y;
            Vector3 stairBottom = new Vector3(16.5f, 1.05f, -6.4f);
            motor.transform.position = stairBottom;
            motor.transform.LookAt(new Vector3(16.5f, 3f, -12f));

            // 用自动化前进走上去：步高 0.25 < stepOffset 0.4，应能直接走
            motor.ApplyAutomationMove(new Vector2(0f, 1f), false);
            yield return new WaitForSeconds(3.5f);
            motor.ClearAutomationMove();

            float climbed = motor.transform.position.y - before;
            bool stairsOk = climbed >= 1.5f;
            Debug.Log($"[D12] D12_SMOKE_STAIRS from={before:F2} to={motor.transform.position.y:F2} climbed={climbed:F2}m ok={stairsOk}");

            bool pass = propsOk && grabOk && stairsOk;
            Debug.Log($"[D12] D12_SMOKE_{(pass ? "OK" : "FAIL")} propsOk={propsOk} grabOk={grabOk} stairsOk={stairsOk}");

            yield return new WaitForSeconds(0.5f);
            Application.Quit(0);
        }

        private static GrabbableProp NearestProp(GrabbableProp[] props, Vector3 from)
        {
            GrabbableProp best = props[0];
            float bestD = Vector3.Distance(best.transform.position, from);
            foreach (GrabbableProp p in props)
            {
                float d = Vector3.Distance(p.transform.position, from);
                if (d < bestD)
                {
                    bestD = d;
                    best = p;
                }
            }

            return best;
        }
    }
}
