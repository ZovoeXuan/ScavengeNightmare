using System.Collections;
using Mirror;
using ScavengeNightmare.Economy;
using ScavengeNightmare.Gameplay;
using ScavengeNightmare.Monsters;
using ScavengeNightmare.Save;
using UnityEngine;

namespace ScavengeNightmare.Networking
{
    /// <summary>
    /// S4B-5 性能门：1 车 + 40 活跃物 + 4 怪（L3 满配）。
    ///
    /// 判据：平均 ≥60fps、客户端侧带宽 ≤600kbps。
    /// 不锁 60fps 上限再量——那会把「刚好 60」和「本来能跑 150」压成同一个数字，
    /// 一旦掉一帧就被判红，但玩家感知不到。这里放开上限量真实余量。
    /// </summary>
    public sealed class S4BPerfTest : MonoBehaviour
    {
        private const int TargetProps = 40;
        private const float MeasureSeconds = 10f;

        private int failures;

        private void Start()
        {
            if (!S4ACli.Has("-s4bperf"))
            {
                this.enabled = false;
                return;
            }

            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 120;
            ScavengeNightmare.Traits.TraitSettings.ForceNoTraits = true;
            S4AInput.SuppressManual = true;
            Debug.Log("[S4A] S4A_NEUTRAL_OK traits=none feel=" + (S4ACli.Value("-feel=") ?? "default").ToLowerInvariant());

            if (string.IsNullOrEmpty(S4ACli.Value("-savedir=")))
            {
                S4ACli.SaveDirOverride = System.IO.Path.Combine(Application.persistentDataPath, "S4B_test_perf");
            }

            NetworkManager manager = NetworkManager.singleton;
            if (manager == null)
            {
                Fail("no_network_manager");
                Application.Quit(1);
                return;
            }

            StartCoroutine(Watchdog(240));

            try
            {
                manager.StartHost();
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[S4B] S4B_PERF_FAIL reason=start_host type={ex.GetType().Name}");
                Application.Quit(1);
                return;
            }

            StartCoroutine(Run());
        }

        private void Fail(string reason)
        {
            failures++;
            Debug.LogError($"[S4B] S4B_PERF_TEST_FAIL reason={reason}");
        }

        private void Check(bool condition, string reason)
        {
            if (!condition)
            {
                Fail(reason);
            }
        }

        private IEnumerator Watchdog(int seconds)
        {
            yield return new WaitForSecondsRealtime(seconds);
            Debug.LogError($"[S4B] S4B_PERF_WATCHDOG_TIMEOUT seconds={seconds}");
            Application.Quit(3);
        }

        private IEnumerator Run()
        {
            yield return WaitFor(() => NetworkClient.localPlayer != null, 25f, "no_local_player");
            yield return WaitFor(() => ScavengeNightmare.Cart.CartController.Instance != null, 20f, "no_cart");
            yield return new WaitForSeconds(1.2f);

            int spawned = SpawnPropsUpTo(TargetProps);
            int activeProps = CountProps();
            Debug.Log($"[S4B] S4B_PERF_LOAD props={activeProps} extra={spawned}");

            if (activeProps < TargetProps)
            {
                Fail($"props_below_target actual={activeProps} want={TargetProps}");
            }

            MonsterSpawner spawner = FindFirst<MonsterSpawner>();
            int monsters = spawner != null ? spawner.ServerSpawnForLeg(3) : 0;

            if (monsters < 4)
            {
                Fail($"monsters_below_target actual={monsters} want=4");
            }

            // 让所有刚体真正进入模拟：刚 Spawn 的两三帧里还没有接触对，量出来是假的。
            yield return new WaitForSeconds(2.5f);

            int frames = 0;
            float total = 0f;
            float startedAt = Time.realtimeSinceStartup;

            while (Time.realtimeSinceStartup - startedAt < MeasureSeconds)
            {
                total += Time.unscaledDeltaTime;
                frames++;
                yield return null;
            }

            float fps = total > 0f ? frames / total : 0f;
            float kbps = SampleKbps();

            Debug.Log($"[S4B] S4B_PERF_METRICS fps={fps:F1} kbps={kbps:F1} frames={frames} seconds={total:F2} " +
                      $"props={activeProps} monsters={monsters}");

            Check(fps >= 60f, $"fps_below_gate actual={fps:F1} want>=60");
            Check(kbps <= 600f, $"bandwidth_above_gate actual={kbps:F1} want<=600");

            bool ok = failures == 0;

            if (ok)
            {
                Debug.Log($"[S4B] S4B_PERF_OK fps={fps:F1} kbps={kbps:F1} props={activeProps} monsters={monsters} ok=True");
            }
            else
            {
                Debug.LogError($"[S4B] S4B_PERF_FAIL fps={fps:F1} kbps={kbps:F1} failures={failures}");
            }

            yield return new WaitForSeconds(0.3f);
            Application.Quit(ok ? 0 : 1);
        }

        /// <summary>
        /// 把场上活跃物补到 <paramref name="target"/> 件。用 can（2kg）而不是雕像：
        /// 性能门量的是数量与网络开销，不是「最大单体物理压力」。
        /// </summary>
        private static int SpawnPropsUpTo(int target)
        {
            NetworkManager nm = NetworkManager.singleton;
            if (nm == null || nm.spawnPrefabs == null || !NetworkServer.active)
            {
                return 0;
            }

            GameObject prefab = FindPrefab(nm, "can");
            if (prefab == null)
            {
                Debug.LogError("[S4B] S4B_PERF_FAIL reason=no_can_prefab");
                return 0;
            }

            int spawned = 0;
            int current = CountProps();

            for (int i = current; i < target; i++)
            {
                int column = (i - current) % 8;
                int row = (i - current) / 8;
                Vector3 at = new(-12f + column * 3f, 0.4f, -6f + row * 3f);

                GameObject instance = Instantiate(prefab, at, Quaternion.identity);
                instance.name = $"perf_can_{i + 1}";
                NetworkServer.Spawn(instance);
                spawned++;
            }

            return spawned;
        }

        private static GameObject FindPrefab(NetworkManager nm, string profileId)
        {
            foreach (GameObject prefab in nm.spawnPrefabs)
            {
                GrabbableProp prop = prefab != null ? prefab.GetComponent<GrabbableProp>() : null;
                if (prop != null && prop.Profile != null && prop.Profile.id == profileId)
                {
                    return prefab;
                }
            }

            return null;
        }

        private static int CountProps()
        {
            int count = 0;
            foreach (GrabbableProp prop in FindObjectsByType<GrabbableProp>(FindObjectsSortMode.None))
            {
                if (prop != null)
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>服务端发出的平均最大发送速率（bytes/s）→ kbps。</summary>
        private static float SampleKbps()
        {
            kcp2k.KcpTransport kcp = NetworkManager.singleton != null
                ? NetworkManager.singleton.transport as kcp2k.KcpTransport
                : null;

            if (kcp == null)
            {
                return 0f;
            }

            return kcp.GetAverageMaxSendRate() * 8f / 1000f;
        }

        private IEnumerator WaitFor(System.Func<bool> condition, float seconds, string failReason)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < end)
            {
                if (condition())
                {
                    yield break;
                }

                yield return null;
            }

            Fail(failReason);
        }

        private static T FindFirst<T>() where T : Object
        {
            T[] found = Object.FindObjectsByType<T>(FindObjectsSortMode.None);
            return found.Length > 0 ? found[0] : null;
        }
    }
}
