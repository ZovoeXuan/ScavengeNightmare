using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Mirror;
using ScavengeNightmare.Economy;
using ScavengeNightmare.Save;
using UnityEngine;

namespace ScavengeNightmare.Networking
{
    /// <summary>
    /// Story 2 自动化：存档的原子写、三级备份、损坏回退、写失败注入、双开锁、强杀恢复。
    ///
    /// 这里只测「账本 → 磁盘」这一段，不重复测抓货（Story 1 的 -s4adeposit 已覆盖）。
    /// 用固定金额直接走 DepositId，是为了让 sumCommitted 能被人手算核对；
    /// 金额随机的话，「balance == sumCommitted」这种断言很容易在巧合下自洽通过。
    /// </summary>
    public sealed class S4ASaveTest : MonoBehaviour
    {
        private enum Mode
        {
            None = 0,
            Save = 1,
            Slow = 2,
            Verify = 3,
            Corrupt = 4,
            CorruptAll = 5,
            FailIo = 6,
            Kill = 7,
            Double = 8
        }

        private const int Value1 = 500;
        private const int Value2 = 700;
        private const int Value3 = 900;

        private Mode mode;
        private int failures;
        private string dir;

        private static Mode Detect()
        {
            if (S4ACli.Has("-s4asave-verify"))
            {
                return Mode.Verify;
            }

            if (S4ACli.Has("-s4acorrupt-all") || string.Equals(S4ACli.Value("-s4acorrupt="), "all", StringComparison.OrdinalIgnoreCase))
            {
                return Mode.CorruptAll;
            }

            if (S4ACli.Has("-s4acorrupt"))
            {
                return Mode.Corrupt;
            }

            if (S4ACli.Has("-s4asave-failio"))
            {
                return Mode.FailIo;
            }

            if (S4ACli.Has("-s4asave-slow"))
            {
                return Mode.Slow;
            }

            if (S4ACli.Has("-s4asave-kill"))
            {
                return Mode.Kill;
            }

            if (S4ACli.Has("-s4asave-double"))
            {
                return Mode.Double;
            }

            if (S4ACli.Has("-s4asave"))
            {
                return Mode.Save;
            }

            return Mode.None;
        }

        private void Start()
        {
            mode = Detect();
            if (mode == Mode.None)
            {
                this.enabled = false;
                return;
            }

            dir = S4ACli.ResolveSaveDir();
            if (string.IsNullOrEmpty(dir))
            {
                dir = Path.Combine(Application.persistentDataPath, "S4A");
                S4ACli.SaveDirOverride = dir;
            }

            Debug.Log($"[S4A] S4A_SAVE_TEST_START mode={mode} dir={dir}");

            if (mode == Mode.Verify || mode == Mode.Corrupt || mode == Mode.CorruptAll)
            {
                RunOffline();
                return;
            }

            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 60;

            // 身份倍率会污染负重与交互距离，存档测试虽然不搬货，但保持和其他 4A 用例同一口径。
            ScavengeNightmare.Traits.TraitSettings.ForceNoTraits = true;
            ScavengeNightmare.Economy.S4AInput.SuppressManual = true;

            // 与其它 4A 用例同一口径：回归脚本第一行就查这句，缺了就整条判失败
            Debug.Log("[S4A] S4A_NEUTRAL_OK traits=none feel=" + (S4ACli.Value("-feel=") ?? "default").ToLowerInvariant());

            NetworkManager manager = NetworkManager.singleton;
            if (manager == null)
            {
                Fail("no_network_manager");
                Application.Quit(1);
                return;
            }

            // 端口必须能改：批测里多个用例串行跑，前一个进程刚退、端口还没放干净时，
            // StartHost 会抛 SocketException，而异常一抛 Start 就中断——协程根本没起来，
            // 进程会永久挂在那里。这个坑实测卡了整批测试 7 分钟。
            int port = S4ACli.IntValue("-port=", 0);
            if (port > 0)
            {
                kcp2k.KcpTransport kcp = manager.GetComponent<kcp2k.KcpTransport>();
                if (kcp != null)
                {
                    kcp.port = (ushort)port;
                }
            }

            int watchdogSeconds = mode == Mode.Slow ? 150 : 90;
            StartCoroutine(Watchdog(watchdogSeconds));

            try
            {
                manager.StartHost();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[S4A] S4A_SAVE_START_FAIL type={ex.GetType().Name} msg={ex.Message} port={port}");
                Application.Quit(1);
                return;
            }

            StartCoroutine(RunOnline());
        }

        /// <summary>
        /// 兜底闸门：任何一条路径漏了退出（StartHost 抛异常、协程被卡死），
        /// 都必须让进程自己死掉并给出非零退出码。没有这个，批测会静默挂住而不是报错。
        /// </summary>
        private IEnumerator Watchdog(int seconds)
        {
            yield return new WaitForSecondsRealtime(seconds);
            Debug.LogError($"[S4A] S4A_SAVE_WATCHDOG_TIMEOUT mode={mode} seconds={seconds}");
            Application.Quit(3);
        }

        private void Fail(string reason)
        {
            failures++;
            Debug.LogError($"[S4A] S4A_SAVE_TEST_FAIL reason={reason}");
        }

        // ---------------- 离线：校验 / 破坏 ----------------

        private void RunOffline()
        {
            if (mode == Mode.Corrupt || mode == Mode.CorruptAll)
            {
                int broken = Corrupt();
                Debug.Log($"[S4A] S4A_SAVE_CORRUPT_DONE mode={mode} broken={broken} dir={dir}");
                Application.Quit(broken > 0 ? 0 : 1);
                return;
            }

            Verify();
        }

        private void Verify()
        {
            if (string.IsNullOrEmpty(S4ACli.Value("-savedir=")))
            {
                Debug.LogError("[S4A] S4A_SAVE_VERIFY_FAIL reason=savedir_required");
                Application.Quit(1);
                return;
            }

            if (!Directory.Exists(dir))
            {
                // 校验进程不许为了「有个目录可读」而把目录建出来：那样 dir 一存在，
                // 后面的 no_save 判断就分不清「真的没有存档」还是「刚被自己造出来」。
                Debug.LogError($"[S4A] S4A_SAVE_VERIFY_FAIL reason=dir_missing dir={dir}");
                Application.Quit(1);
                return;
            }

            GameObject go = new("S4ASaveVerify");
            P0SaveService svc = go.AddComponent<P0SaveService>();
            svc.Initialize(dir, false);

            // seedWhenEmpty=false：校验进程绝不往盘上补写空档，
            // 否则「读不到存档」会被自己刚写的空档掩盖成「读到了 txn=0 的存档」。
            svc.LoadOrCreate(false);

            TeamSaveData data = svc.Data;
            if (data == null || svc.LoadedFrom == "new")
            {
                Debug.LogError($"[S4A] S4A_SAVE_VERIFY_FAIL reason=no_save from={svc.LoadedFrom} dir={dir}");
                Application.Quit(1);
                return;
            }

            long sum = data.SumFromTxns();
            int dup = CountDuplicateCargo(data);
            bool structureOk = data.txnCount == data.txns.Count && data.balance == sum && dup == 0;

            // expected 必须由外部传入（脚本里写死手算值），否则就是「自己跟自己核对」。
            int expected = S4ACli.IntValue("-expect=", (int)sum);
            long delta = data.balance - expected;

            Debug.Log($"[S4A] S4A_SAVE_VERIFY_OK txn={data.txnCount} dup={dup} balance={data.balance} sumCommitted={sum} from={svc.LoadedFrom}");
            Debug.Log($"[S4A] GATE_WALLET expected={expected} actual={data.balance} delta={delta} path={Path.Combine(dir, P0SaveService.SavName)} persisted=True");

            if (svc.LoadedFrom == "bak" || svc.LoadedFrom == "bak2")
            {
                Debug.Log($"[S4A] S4A_SAVE_RECOVERED from={svc.LoadedFrom} balance={data.balance} txn={data.txnCount}");
            }

            if (!structureOk)
            {
                Debug.LogError($"[S4A] S4A_SAVE_VERIFY_FAIL reason=structure ok={structureOk} txn={data.txnCount} listCount={data.txns.Count} balance={data.balance} sum={sum} dup={dup}");
                Application.Quit(1);
                return;
            }

            if (delta != 0)
            {
                Debug.LogError($"[S4A] S4A_SAVE_VERIFY_FAIL reason=amount expected={expected} actual={data.balance} delta={delta}");
                Application.Quit(1);
                return;
            }

            Debug.Log("[S4A] S4A_SAVE_VERIFY_PASS");
            Application.Quit(0);
        }

        private static int CountDuplicateCargo(TeamSaveData data)
        {
            HashSet<string> seen = new();
            int dup = 0;
            foreach (SaveTxnRecord t in data.txns)
            {
                if (t == null || string.IsNullOrEmpty(t.cargoId))
                {
                    continue;
                }

                if (!seen.Add(t.cargoId))
                {
                    dup++;
                }
            }

            return dup;
        }

        private int Corrupt()
        {
            int broken = 0;
            broken += CorruptOne(P0SaveService.SavName);

            if (mode == Mode.CorruptAll)
            {
                broken += CorruptOne(P0SaveService.BakName);
                broken += CorruptOne(P0SaveService.Bak2Name);
            }

            return broken;
        }

        private int CorruptOne(string name)
        {
            string path = Path.Combine(dir, name);
            if (!File.Exists(path))
            {
                Debug.LogWarning($"[S4A] S4A_SAVE_CORRUPT_SKIP file={name} reason=missing");
                return 0;
            }

            File.WriteAllText(path, "S4A_CORRUPT_INJECTED\nthis is not a valid save payload");
            Debug.Log($"[S4A] S4A_SAVE_CORRUPT_INJECTED file={name} path={path} sha256={P0SaveService.Sha256OfFile(path)}");
            return 1;
        }

        // ---------------- 在线：需要 Host ----------------

        private IEnumerator RunOnline()
        {
            yield return WaitFor(() => NetworkClient.localPlayer != null, 20f, "no_local_player");
            yield return WaitFor(() => TeamAccount.Instance != null, 20f, "no_team_account");

            TeamAccount account = TeamAccount.Instance;
            P0SaveService save = P0SaveService.Instance;

            if (save == null)
            {
                Fail("no_save_service");
                Application.Quit(1);
                yield break;
            }

            Debug.Log($"[S4A] S4A_SAVE_PID pid={System.Diagnostics.Process.GetCurrentProcess().Id} state={save.State} txn={save.TxnCount} balance={account.Balance} dir={save.SaveDir}");
            Debug.Log($"[S4A] S4A_SAVE_PATH path={Path.Combine(save.SaveDir, P0SaveService.SavName)} persisted={TeamAccount.Persisted} loaded_from={save.LoadedFrom}");

            switch (mode)
            {
                case Mode.Double:
                    yield return RunDouble(account, save);
                    break;
                case Mode.FailIo:
                    yield return RunFailIo(account, save);
                    break;
                case Mode.Kill:
                    yield return RunKill(account, save);
                    break;
                case Mode.Slow:
                    yield return RunSlow(account, save);
                    break;
                default:
                    yield return RunSave(account, save);
                    break;
            }
        }

        private IEnumerator RunSave(TeamAccount account, P0SaveService save)
        {
            long startBalance = account.Balance;
            int[] values = { Value1, Value2, Value3 };

            foreach (int v in values)
            {
                string cargoId = $"s4asave-{Guid.NewGuid():N}";
                long before = account.Balance;

                bool ok = account.DepositId(cargoId, v);
                yield return null;

                if (!ok || account.Balance != before + v)
                {
                    Fail($"deposit_failed value={v} ok={ok} before={before} after={account.Balance}");
                    continue;
                }

                // CD1：不等退出、不等下一帧存盘，立刻从磁盘读回。
                // 只要有人把「写盘」挪到退出时统一处理，这一条会立刻红。
                string savPath = Path.Combine(dir, P0SaveService.SavName);
                if (!P0SaveService.TryReadFile(savPath, out TeamSaveData disk, out string readError))
                {
                    Fail($"disk_read_failed value={v} error={readError}");
                    continue;
                }

                if (disk.balance != account.Balance)
                {
                    Fail($"disk_lag memory={account.Balance} disk={disk.balance} value={v}");
                }
                else
                {
                    Debug.Log($"[S4A] S4A_SAVE_IMMEDIATE value={v} disk_balance={disk.balance} txn={disk.txnCount} match=True");
                }
            }

            int expectedTxn = save.TxnCount;
            long expectedBalance = account.Balance;
            bool clean = failures == 0;

            Debug.Log($"[S4A] S4A_SAVE_OK txn={expectedTxn} balance={expectedBalance} delta={expectedBalance - startBalance} failures={failures} ok={clean}");

            Application.Quit(clean ? 0 : 1);
            yield break;
        }

        private IEnumerator RunSlow(TeamAccount account, P0SaveService save)
        {
            int stopAfter = S4ACli.IntValue("-stopafter=", 0);
            Debug.Log($"[S4A] S4A_SAVE_PID pid={System.Diagnostics.Process.GetCurrentProcess().Id} note=await_external_taskkill stopafter={stopAfter}");

            int i = 0;
            while (i < 60)
            {
                i++;
                account.DepositId($"s4aslow-{i}", Value1);
                Debug.Log($"[S4A] S4A_SAVE_PROGRESS seq={i} txn={save.TxnCount} balance={account.Balance}");

                if (stopAfter > 0 && i >= stopAfter)
                {
                    // 停在指定事务点不动，等外部 taskkill。
                    // 这样「强杀」和「进程内退出」可以停在完全相同的一笔上做逐字比对；
                    // 否则两边各自自洽，证明不了 ProcessExit 有没有偷偷刷盘。
                    Debug.Log($"[S4A] S4A_SAVE_HOLD txn={save.TxnCount} balance={account.Balance} note=awaiting_external_kill");
                    while (true)
                    {
                        yield return new WaitForSecondsRealtime(1f);
                    }
                }

                yield return new WaitForSeconds(0.8f);
            }

            Application.Quit(0);
        }

        private IEnumerator RunKill(TeamAccount account, P0SaveService save)
        {
            int killAfter = Mathf.Max(1, S4ACli.IntValue("-killafter=", 2));

            for (int i = 1; i <= killAfter; i++)
            {
                account.DepositId($"s4akill-{i}", Value1);
                yield return new WaitForSeconds(0.4f);
            }

            // 对照组：进程内退出会跑 ProcessExit / OnApplicationQuit 托管回调。
            // 若有人在回调里刷盘，这条路径的 txn 会比 taskkill 多，对照就会露馅。
            Debug.Log($"[S4A] S4A_SAVE_EXIT_INPROC txn={save.TxnCount} balance={account.Balance}");
            Application.Quit(0);
        }

        private IEnumerator RunFailIo(TeamAccount account, P0SaveService save)
        {
            string savPath = Path.Combine(dir, P0SaveService.SavName);
            long before = account.Balance;
            int txnBefore = save.TxnCount;
            string hashBefore = P0SaveService.Sha256OfFile(savPath);

            save.InjectIoFailures(1);
            bool ok = account.DepositId($"s4afailio-{Guid.NewGuid():N}", Value1);
            yield return null;

            string hashAfter = P0SaveService.Sha256OfFile(savPath);
            bool rejected = !ok;
            bool memorySame = account.Balance == before;
            bool diskSame = string.Equals(hashBefore, hashAfter, StringComparison.Ordinal);
            bool txnSame = save.TxnCount == txnBefore;

            Debug.Log($"[S4A] S4A_SAVE_FAILIO_RESULT rejected={rejected} memory_same={memorySame} disk_same={diskSame} txn_same={txnSame} balance={account.Balance} txn={save.TxnCount}");

            if (!rejected || !memorySame || !diskSame || !txnSame)
            {
                Fail($"failio_not_atomic rejected={rejected} memory_same={memorySame} disk_same={diskSame} txn_same={txnSame}");
            }
            else
            {
                Debug.Log($"[S4A] S4A_SAVE_FAILIO_OK balance={account.Balance} sha256={hashAfter}");
            }

            Application.Quit(failures == 0 ? 0 : 1);
            yield break;
        }

        private IEnumerator RunDouble(TeamAccount account, P0SaveService save)
        {
            bool locked = save.State == SaveState.Locked;
            long before = account.Balance;

            bool ok = account.DepositId($"s4adouble-{Guid.NewGuid():N}", Value1);
            yield return null;

            bool rejected = !ok && account.Balance == before;
            Debug.Log($"[S4A] S4A_SAVE_DOUBLE_RESULT locked={locked} rejected={rejected} balance={account.Balance} state={save.State}");

            if (!locked || !rejected)
            {
                Fail($"double_not_rejected locked={locked} rejected={rejected}");
            }
            else
            {
                Debug.Log("[S4A] S4A_SAVE_DOUBLE_OK second_process_readonly=1");
            }

            Application.Quit(failures == 0 ? 0 : 1);
            yield break;
        }

        private IEnumerator WaitFor(Func<bool> condition, float seconds, string failReason)
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
    }
}
