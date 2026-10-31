using System.Collections;
using System.Collections.Generic;
using Mirror;
using ScavengeNightmare.Economy;
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
            if (S4ACli.Is4ARun || S4ACli.Any("-s4bnoise", "-s4bmon", "-s4bgreeter", "-s4bscream",
                    "-s4bcurse", "-s4blegcurse", "-s4bleg", "-s4bperf", "-s4bbelt"))
            {
                Debug.Log("[S4B] S4B_MON_SPAWN_SKIP reason=test_controlled");
                yield break;
            }

            int leg = CurrentLeg();
            float safety = LegContentTable.Instance.SafetyFor(leg);

            if (safety > 0f)
            {
                yield return new WaitForSeconds(Mathf.Max(60f, safety));
            }

            if (SpawnCount == 0)
            {
                ServerSpawnForLeg(CurrentLeg());
            }
        }

        private static int CurrentLeg()
        {
            return TeamAccount.Instance != null ? TeamAccount.Instance.LegIndex : 1;
        }

        /// <summary>
        /// S4B-5 三关梯度：L1 空眼×1 / L2 空眼×2+导购×1 / L3 空眼×2+导购×1+尖啸罐×1。
        /// 每次先清旧怪再生成，换关不会累积（R3 的「第二关有两批怪」就是这么来的）。
        /// </summary>
        [Server]
        public int ServerSpawnForLeg(int leg)
        {
            ServerDespawnAll();

            int eyes = leg >= 2 ? 2 : 1;
            int greeters = leg >= 2 ? 1 : 0;
            int screams = leg >= 3 ? 1 : 0;

            MonsterSpawnAnchor[] anchors = FindObjectsByType<MonsterSpawnAnchor>(FindObjectsSortMode.None);
            if (anchors.Length == 0)
            {
                Debug.LogError("[S4B] S4B_MON_SPAWN_FAIL reason=no_anchor");
                return 0;
            }

            int cursor = 0;

            for (int i = 0; i < eyes; i++)
            {
                ServerSpawnHollowEye(AnchorFeet(anchors, cursor++));
            }

            for (int i = 0; i < greeters; i++)
            {
                ServerSpawnPlasticGreeter(AnchorFeet(anchors, cursor++));
            }

            for (int i = 0; i < screams; i++)
            {
                ServerSpawnScreamCan(AnchorFeet(anchors, cursor++));
            }

            Debug.Log($"[S4B] S4B_MON_LEG leg={leg} spawned={SpawnCount} holloweye={eyes} " +
                      $"greeter={greeters} scream={screams} safety={LegContentTable.Instance.SafetyFor(leg):F0}");
            return SpawnCount;
        }

        private static Vector3 AnchorFeet(MonsterSpawnAnchor[] anchors, int index)
        {
            MonsterSpawnAnchor anchor = anchors[index % anchors.Length];
            return anchor.transform.position + Vector3.up * 0.05f;
        }

        /// <summary>换关重入：由 <see cref="S4ABootstrap"/> 在货生成之后调用，顺序见 R3。</summary>
        [Server]
        public void RespawnForNewLeg()
        {
            if (!NetworkServer.active)
            {
                return;
            }

            int leg = CurrentLeg();
            int count = ServerSpawnForLeg(leg);
            Debug.Log($"[S4B] S4B_MON_RESPAWN leg={leg} count={count}");
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
