using System.Collections;
using Mirror;
using ScavengeNightmare.Networking;
using UnityEngine;

namespace ScavengeNightmare.Gameplay
{
    /// <summary>
    /// D6 五条摩擦的自动化验收（批处理专用，正常启动不生效）。
    /// 用法：ScavengeNightmare.exe -batchmode -logFile &lt;path&gt; -d6test
    /// 每条用例都程序化搭一个独立小场景，避免依赖手工摆位，输出 D6_* 标记。
    /// </summary>
    public sealed class D6AutoTest : MonoBehaviour
    {
        [SerializeField] private PropPhysicsProfile barrelProfile;
        [SerializeField] private PropPhysicsProfile cabinetProfile;
        [SerializeField] private PropPhysicsProfile vaseProfile;

        private PlayerMotor motor;
        private GrabController grab;
        private readonly System.Collections.Generic.List<GameObject> spawned = new();

        private void Start()
        {
            if (!HasFlag("-d6test"))
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
            Debug.Log("[D6] D6_AUTOTEST_START");

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
                Debug.LogError("[D6] D6_AUTOTEST_FAIL reason=no_local_player");
                yield break;
            }

            motor = player.GetComponent<PlayerMotor>();
            grab = player.GetComponent<GrabController>();
            grab.SetInputEnabled(false);

            Transform playerT = player.transform;
            Vector3 basePos = playerT.position;
            Vector3 fwd = playerT.forward;

            yield return TestRolling(basePos, fwd);
            yield return TestImpactStun(playerT, fwd);
            yield return TestFragile(basePos, fwd);
            yield return TestDoorClamp(playerT, fwd);
            yield return TestNarrowDoor(playerT, fwd);

            Cleanup();
            Debug.Log("[D6] D6_AUTOTEST_DONE");
            yield return new WaitForSeconds(0.3f);
            Application.Quit(0);
        }

        // ---------- 1. 滚动体 ----------
        private IEnumerator TestRolling(Vector3 basePos, Vector3 fwd)
        {
            GrabbableProp barrel = MakeProp("D6_barrel", barrelProfile, PrimitiveType.Cylinder,
                new Vector3(0.5f, 0.9f, 0.5f), basePos + fwd * 2f + Vector3.up * 0.6f, rolling: true, fragile: false);

            barrel.Body.linearVelocity = fwd * 6f;
            barrel.Body.WakeUp();

            Vector3 start = barrel.transform.position;
            bool sawRolling = false;
            for (int i = 0; i < 90; i++)
            {
                RollingProp rp = barrel.GetComponent<RollingProp>();
                if (rp != null && rp.IsRolling)
                {
                    sawRolling = true;
                }

                yield return new WaitForFixedUpdate();
            }

            float distance = Vector3.Distance(
                new Vector3(barrel.transform.position.x, 0f, barrel.transform.position.z),
                new Vector3(start.x, 0f, start.z));

            Debug.Log($"[D6] D6_ROLLING_{(distance >= 3f && sawRolling ? "OK" : "FAIL")} distance={distance:F2} sawRolling={sawRolling}");
            DestroyTracked(barrel.gameObject);
        }

        // ---------- 2. 手滑砸脚 ----------
        private IEnumerator TestImpactStun(Transform player, Vector3 fwd)
        {
            GrabbableProp prop = MakeProp("D6_impact", barrelProfile, PrimitiveType.Cylinder,
                new Vector3(0.5f, 0.9f, 0.5f), player.position + fwd * 2.2f + Vector3.up * 1.1f, rolling: false, fragile: false);

            Vector3 toPlayer = (player.position + Vector3.up * 0.6f - prop.transform.position).normalized;
            prop.Body.linearVelocity = toPlayer * 10f;
            prop.Body.WakeUp();

            yield return new WaitForSeconds(0.9f);
            float remaining = motor.StunRemaining;
            Debug.Log($"[D6] D6_STUN_{(remaining > 0f ? "OK" : "FAIL")} remaining={remaining:F2}");
            DestroyTracked(prop.gameObject);
        }

        // ---------- 3. 易碎 ----------
        private IEnumerator TestFragile(Vector3 basePos, Vector3 fwd)
        {
            GrabbableProp vase = MakeProp("D6_vase", vaseProfile, PrimitiveType.Cylinder,
                new Vector3(0.3f, 0.3f, 0.3f), basePos + fwd * 4f + Vector3.up * 3f, rolling: false, fragile: true);

            yield return new WaitForSeconds(1.4f);

            FragileProp fragile = vase.GetComponent<FragileProp>();
            bool broken = fragile != null && fragile.Broken;
            int value = fragile != null ? fragile.CurrentValue : -1;
            Debug.Log($"[D6] D6_FRAGILE_{(broken && value == 0 ? "OK" : "FAIL")} broken={broken} value={value}");
            DestroyTracked(vase.gameObject);
        }

        // ---------- 4. 夹手 ----------
        private IEnumerator TestDoorClamp(Transform player, Vector3 fwd)
        {
            Vector3 center = player.position + fwd * 3f;
            DoorTrap door = BuildDoor(center, fwd);

            GrabbableProp prop = MakeProp("D6_clamp", cabinetProfile, PrimitiveType.Cube,
                new Vector3(1.6f, 1.2f, 0.5f), center + Vector3.up * 0.6f, rolling: false, fragile: false);

            grab.TryGrab(prop);
            door.Close();
            yield return new WaitForSeconds(1.2f);

            bool released = !grab.IsHolding;
            bool holderCleared = prop.Holder == null;
            Debug.Log($"[D6] D6_CLAMP_{(released && holderCleared ? "OK" : "FAIL")} released={released} holderCleared={holderCleared}");

            grab.Release(false);
            DestroyTracked(prop.gameObject);
            DestroyTracked(door.gameObject);
        }

        // ---------- 5. 窄门卡位 ----------
        private IEnumerator TestNarrowDoor(Transform player, Vector3 fwd)
        {
            Vector3 gap = player.position + fwd * 4f;
            BuildNarrowGap(gap, fwd, out GameObject gapRoot);

            GrabbableProp cabinet = MakeProp("D6_cabinet", cabinetProfile, PrimitiveType.Cube,
                new Vector3(1.6f, 1.2f, 0.5f), gap + Vector3.up * 0.6f, rolling: false, fragile: false);

            grab.TryGrab(cabinet);
            yield return new WaitForSeconds(0.2f);

            // 逐帧走过去而不是瞬移：瞬移会让「相机到物件」瞬间超过断裂距离，物件在判定前就脱手了
            Vector3 startPos = player.position;
            Vector3 targetPos = gap - fwd * 1.6f + Vector3.up * 0.02f;
            const int steps = 70;
            for (int i = 1; i <= steps; i++)
            {
                if (player == null || !grab.IsHolding)
                {
                    break;
                }

                player.position = Vector3.Lerp(startPos, targetPos, (float)i / steps);
                yield return null;
            }

            yield return new WaitForSeconds(0.9f);

            bool jammed = false;
            foreach (string line in FrictionEvents.Recent)
            {
                if (line.Contains("窄门卡位"))
                {
                    jammed = true;
                }
            }

            Debug.Log($"[D6] D6_NARROWDOOR_{(jammed ? "OK" : "FAIL")} jammed={jammed}");

            grab.Release(false);
            DestroyTracked(cabinet.gameObject);
            DestroyTracked(gapRoot);
        }

        // ---------- 工具 ----------
        private GrabbableProp MakeProp(string name, PropPhysicsProfile profile, PrimitiveType shape,
            Vector3 scale, Vector3 position, bool rolling, bool fragile)
        {
            GameObject go = GameObject.CreatePrimitive(shape);
            go.name = name;
            go.transform.position = position;
            go.transform.localScale = scale;
            spawned.Add(go);

            if (rolling)
            {
                CapsuleCollider capsule = go.GetComponent<CapsuleCollider>();
                if (capsule != null)
                {
                    Destroy(capsule);
                }

                MeshCollider mesh = go.AddComponent<MeshCollider>();
                mesh.sharedMesh = go.GetComponent<MeshFilter>().sharedMesh;
                mesh.convex = true;
            }

            Rigidbody body = go.AddComponent<Rigidbody>();
            body.mass = profile != null ? profile.mass : 5f;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            GrabbableProp prop = go.AddComponent<GrabbableProp>();
            prop.SetProfile(profile);

            if (rolling)
            {
                go.AddComponent<RollingProp>();
            }

            if (fragile)
            {
                go.AddComponent<FragileProp>();
            }

            return prop;
        }

        private DoorTrap BuildDoor(Vector3 center, Vector3 fwd)
        {
            GameObject root = new GameObject("D6_Door");
            root.transform.position = center;
            root.transform.rotation = Quaternion.LookRotation(fwd, Vector3.up);
            spawned.Add(root);

            GameObject pivot = new GameObject("LeafPivot");
            pivot.transform.SetParent(root.transform, false);
            pivot.transform.localPosition = Vector3.zero;

            GameObject leaf = GameObject.CreatePrimitive(PrimitiveType.Cube);
            leaf.name = "Leaf";
            leaf.transform.SetParent(pivot.transform, false);
            leaf.transform.localPosition = new Vector3(0f, 0.6f, 0f);
            leaf.transform.localScale = new Vector3(1.0f, 1.2f, 0.12f);

            DoorTrap trap = root.AddComponent<DoorTrap>();
            trap.Configure(pivot.transform, leaf.GetComponent<BoxCollider>());
            return trap;
        }

        private void BuildNarrowGap(Vector3 center, Vector3 fwd, out GameObject root)
        {
            root = new GameObject("D6_NarrowGap");
            root.transform.position = center;
            // 门洞平面必须垂直于玩家前向，否则玩家走过去是「横着穿」
            root.transform.rotation = Quaternion.LookRotation(fwd, Vector3.up);
            spawned.Add(root);

            MakeWall(root.transform, "L", new Vector3(-1.1f, 1.1f, 0f), new Vector3(1f, 2.2f, 0.3f));
            MakeWall(root.transform, "R", new Vector3(1.1f, 1.1f, 0f), new Vector3(1f, 2.2f, 0.3f));

            GameObject zone = new GameObject("GapZone");
            zone.transform.SetParent(root.transform, false);
            zone.transform.localPosition = new Vector3(0f, 1f, 0f);
            BoxCollider box = zone.AddComponent<BoxCollider>();
            box.size = new Vector3(1.2f, 2f, 0.4f);
            box.isTrigger = true;
            zone.AddComponent<NarrowDoorWatcher>();
        }

        private static void MakeWall(Transform parent, string name, Vector3 pos, Vector3 scale)
        {
            GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = name;
            wall.transform.SetParent(parent, false);
            wall.transform.localPosition = pos;
            wall.transform.localScale = scale;
        }

        private void DestroyTracked(GameObject go)
        {
            if (go != null)
            {
                spawned.Remove(go);
                Destroy(go);
            }
        }

        private void Cleanup()
        {
            foreach (GameObject go in spawned)
            {
                if (go != null)
                {
                    Destroy(go);
                }
            }

            spawned.Clear();
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
    }
}
