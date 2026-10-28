using System.Collections;
using Mirror;
using ScavengeNightmare.Gameplay;
using UnityEngine;

namespace ScavengeNightmare.Networking
{
    /// <summary>
    /// D13 验收：撤离结算是否真的从物件档读表。
    ///
    /// 判定思路：挑四类各一件（价值互不相同），依次送进撤离区，
    /// 把结算总额与「从表里读到的原价之和」比对。
    /// 如果总额对得上且四件各不相同，说明结算走的是数据表而不是任何硬编码数字——
    /// 这也意味着改表里的 value，结算结果会跟着变（改表即生效）。
    ///
    /// 用法：Supermarket 包加 -d13accept
    /// </summary>
    public sealed class D13Acceptance : MonoBehaviour
    {
        private static readonly string[] PickIds = { "vase", "barrel", "statue", "can" };

        private IEnumerator Start()
        {
            bool run = false;
            foreach (string a in System.Environment.GetCommandLineArgs())
            {
                if (string.Equals(a, "-d13accept", System.StringComparison.OrdinalIgnoreCase))
                {
                    run = true;
                }
            }

            if (!run)
            {
                yield break;
            }

            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 60;
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
                Debug.LogError("[D13] D13_ACCEPT_FAIL reason=no_local_player");
                yield break;
            }

            PlayerMotor motor = player.GetComponent<PlayerMotor>();
            GrabController grab = player.GetComponent<GrabController>();
            grab.SetInputEnabled(false);

            float propWait = 0f;
            while (propWait < 12f)
            {
                propWait += Time.unscaledDeltaTime;
                if (Object.FindObjectsByType<GrabbableProp>(FindObjectsSortMode.None).Length > 0) break;
                yield return null;
            }

            EvacZone evac = Object.FindAnyObjectByType<EvacZone>();
            if (evac == null)
            {
                Debug.LogError("[D13] D13_ACCEPT_FAIL reason=no_evac_zone");
                yield break;
            }

            if (EvacSettlement.Instance != null)
            {
                EvacSettlement.Instance.ResetForTest();
            }

            Debug.Log("[D13] D13_ACCEPT_START items=" + string.Join(",", PickIds));

            int expected = 0;
            int delivered = 0;

            foreach (string id in PickIds)
            {
                GrabbableProp prop = FindById(id);
                if (prop == null)
                {
                    Debug.LogWarning($"[D13] D13_ACCEPT_SKIP id={id} 场景里没有该物件");
                    continue;
                }

                // 记下「从表里读到的原价」，作为稍后比对的期望值
                int tableValue = prop.CurrentValue;
                string type = prop.Profile != null ? prop.Profile.propType.ToString() : "?";

                // 走到物件旁抓起
                Vector3 tp = prop.transform.position;
                motor.transform.position = new Vector3(tp.x, tp.y + 1.0f, tp.z - 1.3f);
                motor.transform.LookAt(new Vector3(tp.x, tp.y + 1.0f, tp.z));
                yield return new WaitForSeconds(0.3f);

                if (!grab.TryGrab(prop) || !grab.IsHolding)
                {
                    Debug.LogWarning($"[D13] D13_ACCEPT_SKIP id={id} 抓取失败");
                    continue;
                }

                // 走到撤离区外侧，再走进去触发交付。
                // 关键：瞬移必须把手里的货一起带走——只挪玩家会让服务端
                // ServerHoldStep 看到 delta > breakDistance 立刻判脱手，
                // 结果就是「进了撤离区但手里是空的」，结算永远 0。
                Vector3 evacPos = evac.transform.position;
                motor.transform.position = new Vector3(evacPos.x, 1.05f, evacPos.z + 3.2f);
                motor.transform.LookAt(new Vector3(evacPos.x, 1.05f, evacPos.z));

                GrabbableProp carried = grab.HeldProp;
                if (carried != null && carried.Body != null)
                {
                    carried.Body.position = motor.transform.position
                                            + motor.transform.forward * 1.5f
                                            + Vector3.up * 0.5f;
                    carried.Body.linearVelocity = Vector3.zero;
                    carried.Body.angularVelocity = Vector3.zero;
                }

                yield return new WaitForSeconds(0.25f);

                motor.ApplyAutomationMove(new Vector2(0f, 1f), false);
                yield return new WaitForSeconds(2.0f);
                motor.ClearAutomationMove();
                yield return new WaitForSeconds(0.4f);

                expected += tableValue;
                delivered++;
                Debug.Log($"[D13] D13_ACCEPT_ITEM id={id} type={type} tableValue={tableValue} accExpected={expected}");
            }

            int total = EvacSettlement.Instance != null ? EvacSettlement.Instance.TotalValue : -1;
            int count = EvacSettlement.Instance != null ? EvacSettlement.Instance.DeliveredCount : -1;

            bool countsOk = count == delivered;
            bool valueOk = total == expected;
            bool pass = countsOk && valueOk && delivered == PickIds.Length;

            Debug.Log($"[D13] D13_ACCEPT_SUMMARY delivered={delivered}/{PickIds.Length} settleCount={count} expected={expected} total={total}");
            Debug.Log($"[D13] D13_ACCEPT_{(pass ? "OK" : "FAIL")} countsOk={countsOk}({count}=={delivered}) " +
                      $"valueOk={valueOk}({total}=={expected})");

            if (EvacSettlement.Instance != null)
            {
                EvacSettlement.Instance.LogSettlement();
            }

            yield return new WaitForSeconds(0.5f);
            Application.Quit(0);
        }

        private static GrabbableProp FindById(string id)
        {
            foreach (GrabbableProp p in Object.FindObjectsByType<GrabbableProp>(FindObjectsSortMode.None))
            {
                if (p.Profile != null && p.Profile.id == id)
                {
                    return p;
                }
            }

            return null;
        }
    }
}
