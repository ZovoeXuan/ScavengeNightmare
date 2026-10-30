using System.Collections;
using Mirror;
using ScavengeNightmare.Combat;
using ScavengeNightmare.Economy;
using ScavengeNightmare.Save;
using UnityEngine;

namespace ScavengeNightmare.Networking
{
    /// <summary>
    /// S4B-1 队友救援验收（双进程）：
    ///   Host：-s4bdownhost
    ///   Client：-s4bdownclient -address=127.0.0.1
    /// Host 把 client 打倒地，站到 2.5m 内长按 E 4s，client 起身。
    /// 客户端只读 SyncVar，不参与救援计算。
    /// </summary>
    public sealed class S4BReviveTest : MonoBehaviour
    {
        private int failures;

        private void Start()
        {
            bool host = S4ACli.Has("-s4bdownhost");
            bool client = S4ACli.Has("-s4bdownclient");

            if (!host && !client)
            {
                this.enabled = false;
                return;
            }

            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 60;
            ScavengeNightmare.Traits.TraitSettings.ForceNoTraits = true;
            S4AInput.SuppressManual = true;

            if (string.IsNullOrEmpty(S4ACli.Value("-savedir=")))
            {
                S4ACli.SaveDirOverride = System.IO.Path.Combine(Application.persistentDataPath, host ? "S4B_test_down_host" : "S4B_test_down_client");
            }

            NetworkManager manager = NetworkManager.singleton;
            if (manager == null)
            {
                Debug.LogError("[S4B] S4B_DOWN_FAIL reason=no_network_manager");
                Application.Quit(1);
                return;
            }

            StartCoroutine(Watchdog(90));

            try
            {
                if (host)
                {
                    manager.StartHost();
                    StartCoroutine(RunHost());
                }
                else
                {
                    string address = S4ACli.Value("-address=");
                    manager.networkAddress = string.IsNullOrEmpty(address) ? "127.0.0.1" : address;
                    manager.StartClient();
                    StartCoroutine(RunClient());
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[S4B] S4B_DOWN_FAIL reason=start type={ex.GetType().Name}");
                Application.Quit(1);
            }
        }

        private IEnumerator Watchdog(int seconds)
        {
            yield return new WaitForSecondsRealtime(seconds);
            Debug.LogError($"[S4B] S4B_DOWN_WATCHDOG_TIMEOUT seconds={seconds}");
            Application.Quit(3);
        }

        private void Fail(string reason)
        {
            failures++;
            Debug.LogError($"[S4B] S4B_DOWN_TEST_FAIL reason={reason}");
        }

        private IEnumerator RunHost()
        {
            yield return WaitFor(() => NetworkClient.localPlayer != null, 25f, "no_local_player");
            yield return WaitFor(() => TeamAccount.Instance != null, 25f, "no_team_account");

            NetworkIdentity local = NetworkClient.localPlayer;
            PlayerMotor hostMotor = local.GetComponent<PlayerMotor>();

            float end = Time.realtimeSinceStartup + 25f;
            PlayerVitals clientVitals = null;

            while (Time.realtimeSinceStartup < end && clientVitals == null)
            {
                foreach (PlayerVitals vitals in PlayerVitals.All)
                {
                    if (vitals != null && !vitals.isLocalPlayer)
                    {
                        clientVitals = vitals;
                        break;
                    }
                }

                yield return null;
            }

            if (clientVitals == null)
            {
                Fail("no_client_vitals");
                Application.Quit(1);
                yield break;
            }

            Debug.Log($"[S4B] S4B_DOWN_HOST_READY clientNetId={clientVitals.netId}");

            // 第一次 200 伤害会被单次伤害保护钳到 1 HP，必须补一刀才会倒地。
            clientVitals.ServerApplyDamage(200, "revive_test");
            yield return new WaitForSeconds(0.3f);
            clientVitals.ServerApplyDamage(1, "revive_test");
            yield return new WaitForSeconds(0.4f);

            if (!clientVitals.IsDowned)
            {
                Fail("client_not_downed");
                Application.Quit(1);
                yield break;
            }

            Teleport(hostMotor.transform, clientVitals.transform.position + new Vector3(1.5f, 0f, 0f));
            yield return new WaitForSeconds(0.4f);

            S4AInput.AutoInteract = true;
            yield return new WaitForSeconds(5.0f);
            S4AInput.AutoInteract = false;

            bool revived = !clientVitals.IsDowned;
            Debug.Log($"[S4B] S4B_DOWN_HOST_RESULT revived={revived} hp={clientVitals.Hp}");

            if (!revived)
            {
                Fail("revive_timeout");
            }

            bool ok = failures == 0;

            if (ok)
            {
                Debug.Log($"[S4B] S4B_DOWN_OK revived={revived} failures=0 ok=True");
            }
            else
            {
                Debug.LogError($"[S4B] S4B_DOWN_FAIL failures={failures}");
            }

            yield return new WaitForSeconds(0.3f);
            Application.Quit(ok ? 0 : 1);
        }

        private IEnumerator RunClient()
        {
            yield return WaitFor(() => NetworkClient.localPlayer != null, 25f, "no_local_player");
            yield return WaitFor(() => PlayerVitals.Local != null, 5f, "no_local_vitals");

            PlayerVitals vitals = PlayerVitals.Local;
            Debug.Log($"[S4B] S4B_DOWN_CLIENT_READY netId={vitals.netId}");

            yield return WaitFor(() => vitals.IsDowned, 12f, "client_not_downed");
            Debug.Log($"[S4B] S4B_DOWN_CLIENT_DOWNED hp={vitals.Hp}");

            yield return WaitFor(() => !vitals.IsDowned, 12f, "client_not_revived");

            bool ok = failures == 0;

            if (ok)
            {
                Debug.Log($"[S4B] S4B_DOWN_CLIENT_OK hp={vitals.Hp} failures=0 ok=True");
            }
            else
            {
                Debug.LogError($"[S4B] S4B_DOWN_FAIL failures={failures}");
            }

            yield return new WaitForSeconds(0.3f);
            Application.Quit(ok ? 0 : 1);
        }

        private void Check(bool condition, string reason)
        {
            if (!condition)
            {
                Fail(reason);
            }
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

        private static void Teleport(Transform target, Vector3 position)
        {
            NetworkTransformReliable nt = target.GetComponent<NetworkTransformReliable>();
            bool wasEnabled = nt != null && nt.enabled;

            if (wasEnabled)
            {
                nt.enabled = false;
            }

            target.position = position;

            if (wasEnabled)
            {
                nt.enabled = true;
            }
        }
    }
}
