using Mirror;
using UnityEngine;

namespace ScavengeNightmare.Economy
{
    /// <summary>
    /// 4A 启动器：服务端起来后运行时 Spawn 一个 GameRoot，并 DontDestroyOnLoad。
    /// 不能把账本放在场景对象上 —— Mirror 切场景会 unspawn 场景对象。
    /// </summary>
    public sealed class S4ABootstrap : MonoBehaviour
    {
        [SerializeField] private GameObject gameRootPrefab;

        private void Start()
        {
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
                Debug.Log("[S4A] S4A_BOOT_OK spawned=0 reason=already_exists");
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
            NetworkServer.Spawn(go);

            Debug.Log("[S4A] S4A_BOOT_OK spawned=1");
        }
    }
}
