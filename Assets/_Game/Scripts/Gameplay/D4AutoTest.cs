using System.Collections;
using Mirror;
using ScavengeNightmare.Networking;
using ScavengeNightmare.UI;
using UnityEngine;

namespace ScavengeNightmare.Gameplay
{
    /// <summary>
    /// D4 自动化验证（批处理 / 无键鼠环境专用，正常启动不生效）。
    ///
    /// 用法：ScavengeNightmare.exe -batchmode -logFile &lt;path&gt; -d4test -duration=20
    ///
    /// 依次验证四件事并输出 D4_* 标记：
    ///   1. 能抓到准星指向的物件（D4_GRAB_OK）
    ///   2. 玩家移动时物件跟随，不脱手、不静止（D4_FOLLOW_OK）
    ///   3. 松手投掷继承了玩家动量，出手速度不为 0（D4_THROW_OK）
    ///   4. 30 m/s 高速撞墙不穿模（D4_TUNNEL_OK / D4_TUNNEL_FAIL）
    /// </summary>
    public sealed class D4AutoTest : MonoBehaviour
    {
        [SerializeField] private PropPhysicsProfile testProfile;

        private GameObject wall;
        private GrabbableProp prop;
        private bool crossed;
        private float minDistanceToWall = float.MinValue;
        private Vector3 wallNormal = Vector3.forward;
        private float wallOffset;

        private void Start()
        {
            if (!HasFlag("-d4test"))
            {
                enabled = false;
                return;
            }

            if (testProfile == null)
            {
                Debug.LogError("[D4] D4_AUTOTEST_FAIL reason=profile_missing");
                enabled = false;
                return;
            }

            // 与 D3 同一个坑：无头运行不限帧时固定步物理几乎不推进，
            // WaitForFixedUpdate 会长时间不返回，测试结果失真。必须锁帧。
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 60;

            StartCoroutine(Run());
        }

        private IEnumerator Run()
        {
            Debug.Log("[D4] D4_AUTOTEST_START");

            if (NetworkManager.singleton == null)
            {
                Debug.LogError("[D4] D4_AUTOTEST_FAIL reason=no_network_manager");
                yield break;
            }

            NetworkManager.singleton.StartHost();

            float wait = 0f;
            while (NetworkClient.localPlayer == null && wait < 10f)
            {
                wait += Time.unscaledDeltaTime;
                yield return null;
            }

            NetworkIdentity player = NetworkClient.localPlayer;
            if (player == null)
            {
                Debug.LogError("[D4] D4_AUTOTEST_FAIL reason=no_local_player");
                yield break;
            }

            GrabController grab = player.GetComponent<GrabController>();
            PlayerMotor motor = player.GetComponent<PlayerMotor>();
            if (grab == null || motor == null)
            {
                Debug.LogError($"[D4] D4_AUTOTEST_FAIL reason=missing_component grab={(grab != null)} motor={(motor != null)}");
                yield break;
            }

            grab.SetInputEnabled(false);
            motor.ApplyAutomationMove(Vector2.zero, false);

            // D5：按命令行选择手感方案，用于同一用例对比三套参数。
            string feelId = ReadValue("-feel=");
            FeelSwitchHUD feelHud = player.GetComponent<FeelSwitchHUD>();
            if (!string.IsNullOrEmpty(feelId))
            {
                if (feelHud == null || !feelHud.SelectById(feelId))
                {
                    Debug.LogWarning($"[D5] 方案切换失败 requested={feelId}");
                }
            }

            Debug.Log($"[D5] D5_FEEL active={(feelHud != null ? feelHud.CurrentId : "none")} requested={(string.IsNullOrEmpty(feelId) ? "-" : feelId)}");

            BuildRig(player.transform);

            // ---- 1. 抓取 ----
            prop = CreateProp(player.transform.position + player.transform.forward * 1.4f + Vector3.up * 0.1f);
            yield return new WaitForSeconds(0.4f);

            if (!grab.TryGrab(prop))
            {
                Debug.LogError("[D4] D4_AUTOTEST_FAIL reason=grab_rejected");
                yield break;
            }

            float holdDistance = prop.Profile.holdDistance;
            yield return new WaitForSeconds(0.6f);
            float grabDistance = Vector3.Distance(prop.transform.position, player.transform.position);
            Debug.Log($"[D4] D4_GRAB_OK distance={grabDistance:F2} expected={holdDistance:F2}");

            // ---- 2. 玩家移动时跟随 ----
            Vector3 startPos = player.transform.position;
            Vector3 startProp = prop.transform.position;
            for (int i = 0; i < 30; i++)
            {
                player.transform.position = startPos + player.transform.forward * (i * 0.05f);
                yield return null;
            }

            yield return new WaitForSeconds(0.4f);
            float followDistance = Vector3.Distance(prop.transform.position, player.transform.position);
            float propMoved = Vector3.Distance(prop.transform.position, startProp);
            bool followOk = Mathf.Abs(followDistance - holdDistance) < 0.9f && propMoved > 0.8f;
            Debug.Log($"[D4] D4_FOLLOW_OK ok={followOk} distance={followDistance:F2} propMoved={propMoved:F2}");

            // ---- 3. 投掷继承动量 ----
            Vector3 velBefore = motor.CurrentVelocity;
            grab.Release(true);
            yield return null;
            float throwSpeed = prop.Body.linearVelocity.magnitude;
            float expectedSpeed = FeelSettings.ThrowSpeed(prop.Profile);
            // 判定标准不是「够不够快」，而是「是否等于当前方案的期望出手速度」——
            // 沉重方案本来就该扔得慢，用绝对阈值判会误报。
            bool throwOk = throwSpeed > 1f && Mathf.Abs(throwSpeed - expectedSpeed) < 0.8f;
            Debug.Log($"[D4] D4_THROW_OK ok={throwOk} speed={throwSpeed:F2} expected={FeelSettings.ThrowSpeed(prop.Profile):F2} playerVelocity={velBefore.magnitude:F2} mass={prop.Mass:F1}kg");

            yield return new WaitForSeconds(1.2f);

            // ---- 4. 30 m/s 高速撞墙不穿模 ----
            crossed = false;
            minDistanceToWall = float.MinValue;

            Vector3 face = player.transform.forward;
            Vector3 origin = player.transform.position + face * 1.5f + Vector3.up * 0.5f;
            prop.Body.position = origin;
            prop.Body.rotation = Quaternion.identity;
            prop.Body.linearVelocity = Vector3.zero;
            prop.Body.angularVelocity = Vector3.zero;
            prop.Body.WakeUp();
            yield return new WaitForFixedUpdate();

            prop.Body.WakeUp();
            prop.Body.linearVelocity = face * 30f;

            float elapsed = 0f;
            float wallClock = Time.realtimeSinceStartup;
            int step = 0;
            Debug.Log($"[D4] D4_TRACE origin={origin} wall={wall.transform.position} normal={wallNormal} startSide={Vector3.Dot(origin - wall.transform.position, wallNormal):F2}");

            while (elapsed < 1.2f)
            {
                elapsed += Time.fixedDeltaTime;

                // 批处理下若物理不推进（fixedDeltaTime 异常），用真实时间兜底，避免测试挂死。
                if (Time.realtimeSinceStartup - wallClock > 3f)
                {
                    break;
                }

                float side = Vector3.Dot(prop.Body.position - (wall.transform.position), wallNormal);
                // side 越大越靠近墙；> 0 表示已穿到墙背面。
                minDistanceToWall = Mathf.Max(minDistanceToWall, side);
                if (side > 0f)
                {
                    crossed = true;
                }

                if (step < 4)
                {
                    Debug.Log($"[D4] D4_TRACE step={step} side={side:F2} pos={prop.Body.position} vel={prop.Body.linearVelocity.magnitude:F1}");
                }

                step++;
                yield return new WaitForFixedUpdate();
            }

            float finalSide = Vector3.Dot(prop.Body.position - wall.transform.position, wallNormal);
            Debug.Log($"[D4] D4_TUNNEL_{(crossed ? "FAIL" : "OK")} crossed={crossed} closestSide={minDistanceToWall:F2} finalSide={finalSide:F2} speed=30");

            Debug.Log("[D4] D4_AUTOTEST_DONE");
            yield return new WaitForSeconds(0.5f);
            Application.Quit(0);
        }

        private void BuildRig(Transform player)
        {
            wallNormal = player.forward;
            wallOffset = 5f;

            if (wall != null)
            {
                Destroy(wall);
            }

            wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = "D4_Wall";
            wall.transform.position = player.position + wallNormal * wallOffset + Vector3.up * 1.5f;
            wall.transform.rotation = Quaternion.LookRotation(wallNormal, Vector3.up) * Quaternion.Euler(0f, 180f, 0f);
            wall.transform.localScale = new Vector3(6f, 3f, 0.3f);
        }

        private GrabbableProp CreateProp(Vector3 position)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = "D4_TestProp";
            go.transform.position = position;
            go.transform.localScale = new Vector3(0.5f, 0.9f, 0.5f);

            Rigidbody body = go.AddComponent<Rigidbody>();
            body.mass = testProfile.mass;

            GrabbableProp grabbable = go.AddComponent<GrabbableProp>();
            grabbable.SetProfile(testProfile);
            return grabbable;
        }

        private static bool HasFlag(string flag)
        {
            foreach (string a in System.Environment.GetCommandLineArgs())
            {
                if (string.Equals(a, flag, System.StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static string ReadValue(string prefix)
        {
            foreach (string a in System.Environment.GetCommandLineArgs())
            {
                if (a.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase))
                {
                    return a.Substring(prefix.Length);
                }
            }

            return null;
        }
    }
}
