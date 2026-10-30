using Mirror;
using ScavengeNightmare.Save;
using ScavengeNightmare.Gameplay;
using UnityEngine;

namespace ScavengeNightmare.Economy
{
    /// <summary>
    /// 4A 启动器：服务端起来后运行时 Spawn 一个 GameRoot，并 DontDestroyOnLoad。
    /// 不能把账本放在场景对象上 —— Mirror 切场景会 unspawn 场景对象。
    ///
    /// Story 2 起启动顺序固定为：
    /// Instantiate → P0SaveService.Initialize/LoadOrCreate → NetworkServer.Spawn(TeamAccount)。
    /// 存档必须在 Spawn 之前加载完，否则账本的 SyncVar 会先带着 0 广播出去。
    /// </summary>
    public sealed class S4ABootstrap : MonoBehaviour
    {
        [SerializeField] private GameObject gameRootPrefab;
        [SerializeField] private GameObject cartPrefab;

        private void Start()
        {
            // verify / corrupt 是纯离线文件操作，不需要（也不该）起一个 Host：
            // 起来就会占住存档锁，反而让校验进程读不到真实文件。
            if (S4ACli.Any("-s4asave-verify", "-s4acorrupt"))
            {
                Debug.Log("[S4A] S4A_BOOT_SKIP reason=offline_save_mode");
                return;
            }

            StartCoroutine(BootWhenServerReady());
        }

        private System.Collections.IEnumerator BootWhenServerReady()
        {
            // 纯客户端进程不该干等 20 秒：它永远不会变成 server，白等会拖慢双人冒烟。
            if (!NetworkServer.active && NetworkClient.active)
            {
                Debug.Log("[S4A] S4A_BOOT_OK spawned=0 reason=client_only");
                yield break;
            }

            float waited = 0f;
            while (!NetworkServer.active && waited < 20f)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }

            if (!NetworkServer.active)
            {
                Debug.LogError("[S4A] S4A_BOOT_FAIL reason=server_never_active");
                yield break;
            }

            if (TeamAccount.Instance != null)
            {
                // 换关回到本场景时 GameRoot 还在（DontDestroyOnLoad），不能再建第二个账本，
                // 但**必须**把本场景的物件与推车补上——早先这里直接 yield break，
                // 结果是「第二次进关没有车、没有货」，一切与推车相关的验收全部静默失效。
                Debug.Log("[S4A] S4A_BOOT_OK spawned=0 reason=already_exists");
                EnsureSceneObjects();
                yield break;
            }

            if (gameRootPrefab == null)
            {
                Debug.LogError("[S4A] S4A_BOOT_FAIL reason=missing_game_root_prefab");
                yield break;
            }

            GameObject go = Instantiate(gameRootPrefab);
            go.name = "S4AGameRoot";
            DontDestroyOnLoad(go);

            P0SaveService save = go.GetComponent<P0SaveService>();
            if (save == null)
            {
                // 缺存档服务还继续 Spawn，会得到一份「看起来能用、其实写不进盘」的账本——宁可当场炸。
                Debug.LogError("[S4A] S4A_BOOT_FAIL reason=missing_save_service");
                yield break;
            }

            save.Initialize(S4ACli.ResolveSaveDir(), true);
            save.LoadOrCreate();

            if (save.State == SaveState.Locked)
            {
                // 双开时仍把 GameRoot 挂上（HUD 要显示 LOCKED、入账要被明确拒绝），
                // 但绝不允许写盘：TryCommit 会以 reason=locked 拒绝。
                Debug.LogError("[S4A] S4A_BOOT_LOCKED note=wallet_in_use_readonly_session");
            }

            NetworkServer.Spawn(go);

            Debug.Log($"[S4A] S4A_BOOT_OK spawned=1 save_state={save.State} dir={save.SaveDir}");

            yield return new WaitForSeconds(0.2f);
            SpawnCart();
        }

        /// <summary>
        /// 换关后的重入入口：补推车、（服务端）补物件。
        /// 由 <see cref="LevelRouteService"/> 在场景切换完成后调用，幂等。
        /// 为什么不能只靠 Start()：NetworkManager 是 DontDestroyOnLoad 的，
        /// 它身上的组件 Start() 一生只跑一次，换关根本不会再触发。
        /// </summary>
        [Server]
        public void EnsureSceneObjects()
        {
            SpawnCart();

            PropSpawner spawner = GetComponent<PropSpawner>();
            if (spawner != null)
            {
                spawner.RespawnForNewScene();
            }
        }

        /// <summary>
        /// 推车必须由服务端运行时 Spawn：场景里摆的 prefab 实例拿不到 sceneId（D6.5 已确立）。
        /// 场景里只摆 CartSpawnAnchor，位置与朝向从锚点读。
        /// </summary>
        private void SpawnCart()
        {
            if (cartPrefab == null)
            {
                Debug.LogWarning("[S4A] S4A_CART_SPAWN_SKIP reason=no_cart_prefab");
                return;
            }

            if (ScavengeNightmare.Cart.CartController.Instance != null)
            {
                Debug.Log("[S4A] S4A_CART_SPAWN_SKIP reason=already_exists");
                return;
            }

            ScavengeNightmare.Cart.CartSpawnAnchor anchor =
                UnityEngine.Object.FindObjectsByType<ScavengeNightmare.Cart.CartSpawnAnchor>(FindObjectsSortMode.None).Length > 0
                    ? UnityEngine.Object.FindObjectsByType<ScavengeNightmare.Cart.CartSpawnAnchor>(FindObjectsSortMode.None)[0]
                    : null;

            Vector3 pos = anchor != null ? anchor.transform.position : new Vector3(0f, 0.3f, 17f);
            float yaw = anchor != null ? anchor.yawDegrees : 0f;

            GameObject cart = Instantiate(cartPrefab, pos, Quaternion.Euler(0f, yaw, 0f));
            cart.name = "Cart";
            NetworkServer.Spawn(cart);

            Debug.Log($"[S4A] S4A_CART_SPAWN at=({pos.x:F1},{pos.z:F1}) yaw={yaw}");
        }
    }
}
