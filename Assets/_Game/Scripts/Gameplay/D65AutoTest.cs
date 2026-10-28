using System.Collections;
using Mirror;
using ScavengeNightmare.Networking;
using UnityEngine;

namespace ScavengeNightmare.Gameplay
{
    /// <summary>
    /// D6.5 双端验证（批处理专用）：
    ///   -d65host   起房主、抓起一个物件、带着走 2 秒、抛出，并每秒打印所有联网物件坐标
    ///   -d65client 连接房主，每秒打印本端看到的联网物件坐标
    /// 通过对比两端同一 netId 的坐标，判断物件是否真的同步。
    /// </summary>
    public sealed class D65AutoTest : MonoBehaviour
    {
        private bool host;
        private float deadline;
        private PlayerMotor motor;
        private GrabController grab;
        private GrabbableProp target;

        private void Start()
        {
            string[] args = System.Environment.GetCommandLineArgs();
            foreach (string a in args)
            {
                if (string.Equals(a, "-d65host", System.StringComparison.OrdinalIgnoreCase))
                {
                    host = true;
                }
            }

            bool client = false;
            foreach (string a in args)
            {
                if (string.Equals(a, "-d65client", System.StringComparison.OrdinalIgnoreCase))
                {
                    client = true;
                }
            }

            if (!host && !client)
            {
                enabled = false;
                return;
            }

            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 60;
            StartCoroutine(Run());
        }

        private IEnumerator Run()
        {
            Debug.Log($"[D65] D65_AUTOTEST_START role={(host ? "host" : "client")}");

            if (host)
            {
                NetworkManager.singleton.StartHost();
            }
            else
            {
                // 显式给地址：默认 localhost 在本机解析会抛异常，导致 connection 为空并每帧空引用
                NetworkManager.singleton.networkAddress = "127.0.0.1";
                NetworkManager.singleton.StartClient();
            }

            float wait = 0f;
            while (NetworkClient.localPlayer == null && wait < 12f)
            {
                wait += Time.unscaledDeltaTime;
                yield return null;
            }

            if (NetworkClient.localPlayer == null)
            {
                Debug.LogError("[D65] D65_AUTOTEST_FAIL reason=no_local_player");
                yield break;
            }

            if (host)
            {
                motor = NetworkClient.localPlayer.GetComponent<PlayerMotor>();
                grab = NetworkClient.localPlayer.GetComponent<GrabController>();
                grab.SetInputEnabled(false);

                // 等客户端进场再动手，否则对方只能看到最终位置，证明不了「过程中同步」
                float connWait = 0f;
                while (NetworkServer.connections.Count < 2 && connWait < 20f)
                {
                    connWait += Time.unscaledDeltaTime;
                    yield return null;
                }

                Debug.Log($"[D65] D65_CLIENT_PRESENT conns={NetworkServer.connections.Count} waited={connWait:F1}s");
                yield return new WaitForSeconds(0.5f);

                target = FindTarget();
                if (target == null)
                {
                    Debug.LogError("[D65] D65_AUTOTEST_FAIL reason=no_prop");
                    yield break;
                }

                // 先走近：TryGrab 不做距离校验，直接从 5m 外抓会在下一帧被 breakDistance 判脱手
                Transform pt = motor.transform;
                for (int i = 0; i < 80; i++)
                {
                    float dist = Vector3.Distance(pt.position, target.transform.position);
                    if (dist <= 1.9f)
                    {
                        break;
                    }

                    Vector3 dir = (target.transform.position - pt.position).normalized;
                    pt.position += dir * 0.08f;
                    yield return null;
                }

                yield return new WaitForSeconds(0.2f);
                bool grabbed = grab.TryGrab(target);
                Debug.Log($"[D65] D65_GRAB grabbed={grabbed} prop={target.name}");
                yield return new WaitForSeconds(0.5f);
                Debug.Log($"[D65] D65_HOLD_STABLE held={(grab.IsHolding)}");

                // 带着物件走 2 秒，制造明显的位移动画
                if (motor != null)
                {
                    motor.ApplyAutomationMove(new Vector2(0f, 1f), false);
                }

                yield return new WaitForSeconds(2f);

                if (motor != null)
                {
                    motor.ClearAutomationMove();
                }

                grab.Release(true);
                Debug.Log("[D65] D65_THROW released");
            }

            deadline = Time.realtimeSinceStartup + 5f;
            while (Time.realtimeSinceStartup < deadline)
            {
                LogProps();
                yield return new WaitForSeconds(0.5f);
            }

            Debug.Log("[D65] D65_AUTOTEST_DONE");
            yield return new WaitForSeconds(0.3f);
            Application.Quit(0);
        }

        private GrabbableProp FindTarget()
        {
            GrabbableProp[] props = Object.FindObjectsByType<GrabbableProp>(FindObjectsSortMode.None);
            GrabbableProp fallback = null;
            foreach (GrabbableProp prop in props)
            {
                if (prop.GetComponent<NetworkIdentity>() == null)
                {
                    continue;
                }

                if (fallback == null)
                {
                    fallback = prop;
                }

                if (prop.name.Contains("barrel"))
                {
                    return prop;
                }
            }

            return fallback;
        }

        private void LogProps()
        {
            GrabbableProp[] props = Object.FindObjectsByType<GrabbableProp>(FindObjectsSortMode.None);
            foreach (GrabbableProp prop in props)
            {
                NetworkIdentity id = prop.GetComponent<NetworkIdentity>();
                if (id == null)
                {
                    continue;
                }

                Vector3 p = prop.transform.position;
                string flag = target != null && prop == target ? "*" : string.Empty;
                long ms = System.DateTime.UtcNow.Ticks / System.TimeSpan.TicksPerMillisecond;
                Debug.Log($"[D65] D65_STATE t={ms} role={(host ? "host" : "client")} netId={id.netId} name={prop.name}{flag} pos={p.x:F2},{p.y:F2},{p.z:F2} kinematic={prop.Body.isKinematic}");
            }
        }
    }
}
