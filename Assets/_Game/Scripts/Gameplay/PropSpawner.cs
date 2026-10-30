using System;
using System.Collections;
using Mirror;
using UnityEngine;

namespace ScavengeNightmare.Gameplay
{
    /// <summary>
    /// D6.5 物件生成器：由服务器在开局时从预制体实例化并 Spawn 所有可交互物件。
    ///
    /// 两个关键点：
    /// 1. 必须是普通 MonoBehaviour，不能是 NetworkBehaviour —— 它挂在 NetworkManager 上，
    ///    那个对象没有 NetworkIdentity，NetworkBehaviour 收不到 OnStartServer。
    /// 2. 不从场景里摆好的实例同步：Mirror 要求场景对象有 editor 期分配的 sceneId，
    ///    prefab 实例拿不到（实测 netId 恒为 0，客户端还会空引用）。运行时 Spawn 是确定性的，
    ///    也是 D13「物件系统化」的方向。
    /// </summary>
    public sealed class PropSpawner : MonoBehaviour
    {
        [Serializable]
        public struct Entry
        {
            public GameObject Prefab;
            public Vector3 Position;
            public Vector3 Euler;
        }

        [SerializeField] private Entry[] entries;

        private bool spawned;

        /// <summary>
        /// 换关重入：清掉「已生成」标记再跑一遍生成协程。
        /// 只增不改——原有 Start 路径一行不动，保证 Main / Supermarket 的既有验收不受影响。
        /// </summary>
        public void RespawnForNewScene()
        {
            if (!NetworkServer.active)
            {
                Debug.LogWarning("[D65] D65_RESPAWN_SKIP reason=server_inactive");
                return;
            }

            spawned = false;
            StartCoroutine(SpawnWhenServerReady());
        }

        private void Start()
        {
            StartCoroutine(SpawnWhenServerReady());
        }

        private IEnumerator SpawnWhenServerReady()
        {
            float waited = 0f;
            while (!NetworkServer.active && waited < 15f)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }

            if (!NetworkServer.active || spawned)
            {
                yield break;
            }

            spawned = true;
            yield return new WaitForSeconds(0.2f);

            // 场景里摆了 PropSpawnAnchor 就按锚点生成（D12 起的新图都走这条路）；
            // 没有锚点则回退到 entries 硬编码表，保证 Main 沙盒与全部既有用例不受影响。
            PropSpawnAnchor[] anchors = FindObjectsByType<PropSpawnAnchor>(FindObjectsSortMode.None);
            if (anchors.Length > 0)
            {
                int byAnchor = SpawnFromAnchors(anchors);
                Debug.Log($"[D65] D65_SPAWNED count={byAnchor} source=anchors");
                yield break;
            }

            int count = 0;
            foreach (Entry entry in entries)
            {
                if (entry.Prefab == null)
                {
                    continue;
                }

                GameObject instance = Instantiate(entry.Prefab, entry.Position, Quaternion.Euler(entry.Euler));
                instance.name = $"{entry.Prefab.name}_{count + 1}";
                NetworkServer.Spawn(instance);
                count++;
            }

            Debug.Log($"[D65] D65_SPAWNED count={count} source=entries");
        }

        /// <summary>
        /// 按锚点生成：锚点只写物件档 id，具体 prefab 从 NetworkManager.spawnPrefabs 里按档匹配。
        /// 匹配不上时跳过并告警——静默不生成会让「图里明明摆了 18 个锚点却只刷出 12 件」很难查。
        /// </summary>
        private static int SpawnFromAnchors(PropSpawnAnchor[] anchors)
        {
            NetworkManager nm = NetworkManager.singleton;
            if (nm == null || nm.spawnPrefabs == null || nm.spawnPrefabs.Count == 0)
            {
                Debug.LogError("[D12] D12_SPAWN_FAIL reason=no_spawn_prefabs");
                return 0;
            }

            int count = 0;
            int missing = 0;

            foreach (PropSpawnAnchor anchor in anchors)
            {
                if (anchor == null)
                {
                    continue;
                }

                GameObject prefab = FindPrefab(nm, anchor.profileId);
                if (prefab == null)
                {
                    missing++;
                    Debug.LogWarning($"[D12] D12_SPAWN_SKIP profile={anchor.profileId} 没有对应的 spawnPrefab");
                    continue;
                }

                // 必须限定 UnityEngine：本文件同时 using System，Random 会歧义
                Quaternion rot = anchor.randomYaw
                    ? Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f)
                    : anchor.transform.rotation;

                GameObject instance = Instantiate(prefab, anchor.transform.position, rot);
                instance.name = $"{anchor.profileId}_{count + 1}";
                NetworkServer.Spawn(instance);
                count++;
            }

            if (missing > 0)
            {
                Debug.LogError($"[D12] D12_SPAWN_INCOMPLETE missing={missing} spawned={count} anchors={anchors.Length}");
            }

            return count;
        }

        private static GameObject FindPrefab(NetworkManager nm, string profileId)
        {
            if (string.IsNullOrEmpty(profileId))
            {
                return null;
            }

            foreach (GameObject prefab in nm.spawnPrefabs)
            {
                if (prefab == null)
                {
                    continue;
                }

                GrabbableProp prop = prefab.GetComponent<GrabbableProp>();
                if (prop != null && prop.Profile != null && prop.Profile.id == profileId)
                {
                    return prefab;
                }
            }

            return null;
        }
    }
}
