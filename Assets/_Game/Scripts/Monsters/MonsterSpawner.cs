using System.Collections;
using System.Collections.Generic;
using Mirror;
using ScavengeNightmare.Save;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ScavengeNightmare.Monsters
{
    /// <summary>
    /// 服务端怪物生成器。挂在 NetworkManager 上（普通 MonoBehaviour，NM 没有 NetworkIdentity）。
    /// S4B-2 只生成空眼；按 LegContentTable 的每关梯度在 S4B-5 接入。
    /// 安全期 L1=120s：玩家落地后先有一段安静时间，避免「一睁眼就被追」。
    /// </summary>
    public sealed class MonsterSpawner : MonoBehaviour
    {
        [SerializeField] private GameObject hollowEyePrefab;
        [SerializeField] private GameObject plasticGreeterPrefab;
        [SerializeField] private GameObject screamCanPrefab;

        [Tooltip("生成安全期（秒）。L1=120 / L2=90 / L3=60，S4B-5 按关读取")]
        [SerializeField] private float safetySeconds = 120f;

        private readonly List<MonsterBrain> spawned = new();

        public int SpawnCount
        {
            get
            {
                spawned.RemoveAll(b => b == null);
                return spawned.Count;
            }
        }

        private void Start()
        {
            StartCoroutine(BootAfterServerReady());
        }

        private IEnumerator BootAfterServerReady()
        {
            float waited = 0f;
            while (!NetworkServer.active && waited < 15f)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }

            if (!NetworkServer.active)
            {
                yield break;
            }

            if (SceneManager.GetActiveScene().name == "Safehouse")
            {
                Debug.Log("[S4B] S4B_MON_SPAWN_SKIP reason=safehouse");
                yield break;
            }

            // 4A 基线进程不生成怪；两条 4B-2 用例自己精确控制生成位置。
            if (S4ACli.Is4ARun || S4ACli.Any("-s4bnoise", "-s4bmon", "-s4bgreeter", "-s4bscream"))
            {
                Debug.Log("[S4B] S4B_MON_SPAWN_SKIP reason=test_controlled");
                yield break;
            }

            if (safetySeconds > 0f)
            {
                yield return new WaitForSeconds(safetySeconds);
            }

            if (SpawnCount == 0)
            {
                ServerSpawnAtAnchor(0);
            }
        }

        [Server]
        public MonsterBrain ServerSpawnAtAnchor(int index)
        {
            MonsterSpawnAnchor[] anchors = FindObjectsByType<MonsterSpawnAnchor>(FindObjectsSortMode.None);
            if (anchors.Length == 0)
            {
                Debug.LogError("[S4B] S4B_MON_SPAWN_FAIL reason=no_anchor");
                return null;
            }

            MonsterSpawnAnchor anchor = anchors[Mathf.Clamp(index, 0, anchors.Length - 1)];
            return ServerSpawnHollowEye(anchor.transform.position + Vector3.up * 0.05f);
        }

        [Server]
        public MonsterBrain ServerSpawnHollowEye(Vector3 feetPosition)
        {
            return ServerSpawnPrefab(hollowEyePrefab, "Monster_HollowEye", "holloweye", feetPosition);
        }

        [Server]
        public MonsterBrain ServerSpawnPlasticGreeter(Vector3 feetPosition)
        {
            return ServerSpawnPrefab(plasticGreeterPrefab, "Monster_PlasticGreeter", "plastic_greeter", feetPosition);
        }

        [Server]
        public MonsterBrain ServerSpawnScreamCan(Vector3 feetPosition)
        {
            return ServerSpawnPrefab(screamCanPrefab, "Monster_ScreamCan", "scream_can", feetPosition);
        }

        private MonsterBrain ServerSpawnPrefab(GameObject prefab, string instanceName, string kind, Vector3 feetPosition)
        {
            if (prefab == null)
            {
                Debug.LogError($"[S4B] S4B_MON_SPAWN_FAIL reason=missing_prefab kind={kind}");
                return null;
            }

            Vector3 position = new(feetPosition.x, Mathf.Max(0.05f, feetPosition.y), feetPosition.z);
            GameObject instance = Instantiate(
                prefab,
                position,
                Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
            instance.name = instanceName;

            NetworkServer.Spawn(instance);
            MonsterBrain brain = instance.GetComponent<MonsterBrain>();

            if (brain != null)
            {
                spawned.Add(brain);
            }

            Debug.Log($"[S4B] S4B_MON_SPAWN monster={kind} at=({position.x:F1},{position.z:F1}) total={SpawnCount}");
            return brain;
        }

        [Server]
        public void ServerDespawnAll()
        {
            spawned.RemoveAll(b => b == null);

            foreach (MonsterBrain brain in spawned)
            {
                if (brain != null)
                {
                    NetworkServer.Destroy(brain.gameObject);
                }
            }

            spawned.Clear();
            Debug.Log("[S4B] S4B_MON_DESPAWN_ALL");
        }
    }
}
