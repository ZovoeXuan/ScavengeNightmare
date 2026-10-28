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

            Debug.Log($"[D65] D65_SPAWNED count={count}");
        }
    }
}
