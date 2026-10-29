using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace ScavengeNightmare.Save
{
    public enum SaveState
    {
        NotLoaded = 0,
        Ready = 1,
        Recovered = 2,
        CorruptRecovered = 3,
        Locked = 4,
        Failed = 5
    }

    /// <summary>
    /// P0 存档服务：整个工程里**唯一**允许碰磁盘的类。玩法代码一律不得 File.Write*。
    ///
    /// 三条不可动摇的纪律：
    /// 1) 先落盘、再改内存、最后才广播。反过来就会在写失败时留下「内存里有钱、盘上没有」的假账。
    /// 2) 绝不在 OnApplicationQuit / ProcessExit 里统一刷盘。V3.0 17.4 明令禁止；
    ///    一旦这么写，强杀测试会漂亮地通过，而真实断电照样丢钱。
    /// 3) 备份轮换必须先 bak→bak2 再 File.Replace(tmp, sav, bak)，
    ///    顺序颠倒会把唯一的好档挤掉。
    /// </summary>
    public sealed class P0SaveService : MonoBehaviour
    {
        public static P0SaveService Instance { get; private set; }

        public const string SavName = "wallet.sav";
        public const string BakName = "wallet.bak";
        public const string Bak2Name = "wallet.bak2";
        public const string LockName = "wallet.lock";

        private const string DefaultFolder = "S4A";
        private const string HashPrefix = "SHA256:";

        private string dir;
        private string savPath;
        private string bakPath;
        private string bak2Path;
        private string lockPath;

        private FileStream lockStream;
        private int injectedIoFailures = -1;

        public TeamSaveData Data { get; private set; }
        public SaveState State { get; private set; } = SaveState.NotLoaded;
        public string LastError { get; private set; } = string.Empty;
        public string SaveDir => dir;

        /// <summary>sav / bak / bak2 / new —— 校验报告必须能说清「这条余额是从哪一档读出来的」。</summary>
        public string LoadedFrom { get; private set; } = "none";
        public bool HasWriteLock => lockStream != null;
        public bool Loaded => Data != null;
        public int TxnCount => Data != null ? Data.txnCount : 0;

        /// <summary>HUD 用的短状态：SAVED / LOCKED / RECOVERED / CORRUPT / —。</summary>
        public static string HudToken
        {
            get
            {
                P0SaveService svc = Instance;
                if (svc == null)
                {
                    return "-";
                }

                return svc.State switch
                {
                    SaveState.Ready => "SAVED",
                    SaveState.Recovered => "RECOVERED",
                    SaveState.CorruptRecovered => "CORRUPT",
                    SaveState.Locked => "LOCKED",
                    SaveState.Failed => "FAILED",
                    _ => "-"
                };
            }
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                // 同进程出现第二份存档服务：两份锁两份内存，谁写谁覆盖，必须炸出来。
                Debug.LogError($"[S4A] S4A_SAVE_DUPLICATE existing_dir={Instance.dir} new_dir={dir}");
                return;
            }

            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }

            ReleaseLock();
        }

        /// <summary>
        /// 只释放锁，绝不刷盘。看见这段的人不要「顺手保存一下」——
        /// 那会让 taskkill 与 Environment.Exit 的对照测试失去意义。
        /// </summary>
        private void OnApplicationQuit()
        {
            ReleaseLock();
        }

        public void Initialize(string overrideDir, bool acquireLock)
        {
            dir = string.IsNullOrEmpty(overrideDir)
                ? Path.Combine(Application.persistentDataPath, DefaultFolder)
                : overrideDir;

            savPath = Path.Combine(dir, SavName);
            bakPath = Path.Combine(dir, BakName);
            bak2Path = Path.Combine(dir, Bak2Name);
            lockPath = Path.Combine(dir, LockName);

            if (acquireLock)
            {
                if (!EnsureDir(out string dirError))
                {
                    LastError = dirError;
                    State = SaveState.Failed;
                    Debug.LogError($"[S4A] S4A_SAVE_FAIL reason={dirError} phase=init detail=dir_unavailable dir={dir}");
                    return;
                }

                if (!AcquireLock())
                {
                    // 同机双开：第二进程必须明确被拒，不能静默降级成「各写各的」。
                    State = SaveState.Locked;
                    LastError = "locked";
                    Debug.LogError($"[S4A] S4A_SAVE_LOCKED dir={dir} note=another_process_holds_wallet");
                }
            }

            Debug.Log($"[S4A] S4A_SAVE_INIT dir={dir} lock={HasWriteLock} state={State}");
        }

        public bool LoadOrCreate(bool seedWhenEmpty = true)
        {
            if (State == SaveState.Locked || State == SaveState.Failed)
            {
                return false;
            }

            if (!EnsureDir(out string dirError))
            {
                LastError = dirError;
                State = SaveState.Failed;
                Debug.LogError($"[S4A] S4A_SAVE_FAIL reason={dirError} phase=load detail=dir_unavailable dir={dir}");
                return false;
            }

            if (TryReadFile(savPath, out TeamSaveData fromSav, out string savError))
            {
                Data = fromSav;
                State = SaveState.Ready;
                LoadedFrom = "sav";
                Debug.Log($"[S4A] S4A_SAVE_LOADED from=sav txn={Data.txnCount} balance={Data.balance} legProgress={Data.legProgress} legIndex={Data.legIndex}");
                return true;
            }

            int existing = (File.Exists(savPath) ? 1 : 0) + (File.Exists(bakPath) ? 1 : 0) + (File.Exists(bak2Path) ? 1 : 0);

            if (TryReadFile(bakPath, out TeamSaveData fromBak, out string bakError))
            {
                Data = fromBak;
                State = SaveState.Recovered;
                LoadedFrom = "bak";
                PreserveCorrupt(savPath, "sav", savError);
                Debug.LogWarning($"[S4A] S4A_SAVE_RECOVERED from=bak reason={savError} txn={Data.txnCount} balance={Data.balance}");
                return true;
            }

            if (TryReadFile(bak2Path, out TeamSaveData fromBak2, out string bak2Error))
            {
                Data = fromBak2;
                State = SaveState.Recovered;
                LoadedFrom = "bak2";
                PreserveCorrupt(savPath, "sav", savError);
                PreserveCorrupt(bakPath, "bak", bakError);
                Debug.LogWarning($"[S4A] S4A_SAVE_RECOVERED from=bak2 reason={savError}|{bakError} txn={Data.txnCount} balance={Data.balance}");
                return true;
            }

            Data = TeamSaveData.NewEmpty(NewRunId(), 1800);
            LoadedFrom = "new";

            if (existing > 0)
            {
                // 三档全废：坏档必须留证，否则事后无法判断是磁盘坏、还是我们写坏。
                PreserveCorrupt(savPath, "sav", savError);
                PreserveCorrupt(bakPath, "bak", bakError);
                PreserveCorrupt(bak2Path, "bak2", bak2Error);
                State = SaveState.CorruptRecovered;
                Debug.LogError($"[S4A] S4A_SAVE_CORRUPT_RECOVERED files={existing} reasons={savError}|{bakError}|{bak2Error} new_balance=0");
            }
            else
            {
                State = SaveState.Ready;
                Debug.Log("[S4A] S4A_SAVE_CREATED empty=1");
            }

            if (!seedWhenEmpty)
            {
                // 只读校验模式：不许为了「让它看起来正常」而往盘上写一个空档。
                return true;
            }

            // 落一个合法空档，避免下次启动重复走损坏分支。写不进去（只读盘）不算致命：
            // 真正致命的是「假装写成功了」，所以这里只报错不改状态。
            if (!TryWriteAtomic(Data, false, out string seedError))
            {
                Debug.LogError($"[S4A] S4A_SAVE_FAIL reason={seedError} phase=seed_empty");
            }

            return true;
        }

        /// <summary>
        /// 提交一次入账。next 里的业务字段（balance / legProgress / quotaReached）由调用方算好，
        /// txnCount / sumCommitted / seq 由本服务统一维护，避免两边各算一份、对不上账。
        /// </summary>
        public bool TryCommit(TeamSaveData next, SaveTxnRecord record, out string error)
        {
            error = string.Empty;

            if (next == null || record == null)
            {
                error = "null_payload";
                return false;
            }

            if (State == SaveState.Locked)
            {
                error = "locked";
                LastError = error;
                Debug.LogError($"[S4A] S4A_SAVE_REJECT reason=locked cargoId={record.cargoId} value={record.value} balance={Data?.balance}");
                return false;
            }

            int baseTxn = Data != null ? Data.txnCount : 0;
            long baseSum = Data != null ? Data.sumCommitted : 0;

            next.txnCount = baseTxn + 1;
            next.sumCommitted = baseSum + record.value;
            record.seq = next.txnCount;
            next.txns ??= new List<SaveTxnRecord>();
            next.txns.Add(record);

            if (!TryWriteAtomic(next, true, out error))
            {
                LastError = error;
                Debug.LogError($"[S4A] S4A_SAVE_FAIL reason={error} phase=commit cargoId={record.cargoId} value={record.value} balance_unchanged={Data?.balance ?? -1} txn_unchanged={baseTxn}");
                return false;
            }

            Data = next;
            State = SaveState.Ready;
            Debug.Log($"[S4A] S4A_SAVE_COMMIT txn={Data.txnCount} balance={Data.balance} legProgress={Data.legProgress} sumCommitted={Data.sumCommitted} cargoId={record.cargoId} value={record.value}");
            return true;
        }

        /// <summary>非入账的状态变更（换关、改过关额）。不产生事务，但同样必须先落盘。</summary>
        public bool TryCommitState(TeamSaveData next, string reason, out string error)
        {
            error = string.Empty;

            if (next == null)
            {
                error = "null_payload";
                return false;
            }

            if (State == SaveState.Locked)
            {
                error = "locked";
                LastError = error;
                Debug.LogError($"[S4A] S4A_SAVE_REJECT reason=locked phase={reason}");
                return false;
            }

            next.txnCount = Data != null ? Data.txnCount : 0;
            next.sumCommitted = Data != null ? Data.sumCommitted : 0;

            if (!TryWriteAtomic(next, true, out error))
            {
                LastError = error;
                Debug.LogError($"[S4A] S4A_SAVE_FAIL reason={error} phase={reason}");
                return false;
            }

            Data = next;
            State = SaveState.Ready;
            Debug.Log($"[S4A] S4A_SAVE_STATE reason={reason} txn={Data.txnCount} balance={Data.balance} legIndex={Data.legIndex} legProgress={Data.legProgress}");
            return true;
        }

        /// <summary>测试钩子：让接下来 N 次写盘在落 tmp 之前抛 IO 异常。</summary>
        public void InjectIoFailures(int times)
        {
            injectedIoFailures = times;
            Debug.Log($"[S4A] S4A_SAVE_FAILIO_ARMED times={times}");
        }

        private bool TryWriteAtomic(TeamSaveData data, bool rotateBackups, out string error)
        {
            error = string.Empty;

            try
            {
                if (injectedIoFailures == 0)
                {
                    injectedIoFailures = -1;
                }

                if (injectedIoFailures > 0)
                {
                    injectedIoFailures--;
                    throw new IOException("injected_io_failure");
                }

                WriteAtomic(data, rotateBackups);
                return true;
            }
            catch (Exception ex)
            {
                error = Classify(ex);
                Debug.LogError($"[S4A] S4A_SAVE_IO_ERROR type={ex.GetType().Name} msg={ex.Message} path={savPath}");
                return false;
            }
        }

        private void WriteAtomic(TeamSaveData data, bool rotateBackups)
        {
            Directory.CreateDirectory(dir);

            byte[] bytes = Serialize(data);
            string tmpPath = savPath + ".tmp";

            using (FileStream fs = new(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                fs.Write(bytes, 0, bytes.Length);
                // flushToDisk:true 是这条链路里最容易被删掉、删掉后又最难发现的一行。
                fs.Flush(true);
            }

            if (rotateBackups)
            {
                // 顺序死规定：先腾 bak2，再把 bak 降级成 bak2，最后 Replace 用旧 sav 生成新 bak。
                if (File.Exists(bak2Path))
                {
                    File.Delete(bak2Path);
                }

                if (File.Exists(bakPath))
                {
                    File.Move(bakPath, bak2Path);
                }
            }

            if (File.Exists(savPath))
            {
                try
                {
                    File.Replace(tmpPath, savPath, bakPath);
                }
                catch (PlatformNotSupportedException)
                {
                    // 玩家把存档放在 FAT 移动盘时会走到这里。窗口极短但确实存在，必须诚实记录，
                    // 不能假装还是原子的。
                    NonAtomicReplace(tmpPath);
                    Debug.LogWarning($"[S4A] S4A_SAVE_FALLBACK_NONATOMIC reason=platform_not_supported path={savPath}");
                }
            }
            else
            {
                File.Move(tmpPath, savPath);
            }
        }

        private void NonAtomicReplace(string tmpPath)
        {
            if (File.Exists(savPath))
            {
                File.Delete(savPath);
            }

            File.Move(tmpPath, savPath);
        }

        private bool EnsureDir(out string error)
        {
            error = string.Empty;
            try
            {
                Directory.CreateDirectory(dir);
                return true;
            }
            catch (Exception ex)
            {
                // 分类只给「io_error」这种可断言的口径，具体类型交给日志的 detail 字段。
                // 之前把 ":IOException" 拼进来，测试脚本按 reason=io_error 匹配就会漏。
                error = Classify(ex);
                return false;
            }
        }

        private bool AcquireLock()
        {
            try
            {
                // FileShare.None 是跨进程独占：第二个进程拿不到句柄，也就没有「两份内存互相覆盖」的窗口。
                lockStream = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                return true;
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }

        private void ReleaseLock()
        {
            if (lockStream == null)
            {
                return;
            }

            try
            {
                lockStream.Dispose();
            }
            catch (Exception)
            {
                // 释放锁失败不该影响进程退出，也不该触发任何写盘。
            }

            lockStream = null;
        }

        private void PreserveCorrupt(string path, string tag, string reason)
        {
            if (!File.Exists(path))
            {
                return;
            }

            try
            {
                string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
                string dest = Path.Combine(dir, $"corrupt-{stamp}-{tag}.sav");
                File.Copy(path, dest, true);
                Debug.LogWarning($"[S4A] S4A_SAVE_CORRUPT_KEPT from={tag} reason={reason} dest={dest}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[S4A] S4A_SAVE_CORRUPT_KEEP_FAIL from={tag} type={ex.GetType().Name}");
            }
        }

        private static string Classify(Exception ex)
        {
            if (ex is UnauthorizedAccessException)
            {
                return "io_error";
            }

            if (ex is IOException)
            {
                return "io_error";
            }

            if (ex is PlatformNotSupportedException)
            {
                return "platform_unsupported";
            }

            return "unknown";
        }

        private static int NewRunId()
        {
            return new System.Random(Guid.NewGuid().GetHashCode()).Next(1, int.MaxValue);
        }

        private static byte[] Serialize(TeamSaveData data)
        {
            string json = JsonUtility.ToJson(data);
            string text = HashPrefix + Sha256Hex(Encoding.UTF8.GetBytes(json)) + "\n" + json;
            return Encoding.UTF8.GetBytes(text);
        }

        /// <summary>
        /// 校验和放在首行单独存放，不塞进 JSON 内部：
        /// 这样不依赖 JsonUtility 的字段输出顺序，也不会出现「算校验和时要先清空校验和字段」的自指问题。
        /// </summary>
        public static bool TryParse(byte[] bytes, out TeamSaveData data, out string error)
        {
            data = null;
            error = string.Empty;

            if (bytes == null || bytes.Length == 0)
            {
                error = "empty_file";
                return false;
            }

            string text;
            try
            {
                text = new UTF8Encoding(false, true).GetString(bytes);
            }
            catch (Exception ex)
            {
                error = "decode_" + ex.GetType().Name;
                return false;
            }

            int nl = text.IndexOf('\n');
            if (nl <= HashPrefix.Length)
            {
                error = "no_header";
                return false;
            }

            string header = text.Substring(0, nl).Trim();
            string json = text.Substring(nl + 1);

            if (!header.StartsWith(HashPrefix, StringComparison.Ordinal))
            {
                error = "bad_header";
                return false;
            }

            string expect = header.Substring(HashPrefix.Length).Trim();
            string actual = Sha256Hex(Encoding.UTF8.GetBytes(json));

            if (!string.Equals(expect, actual, StringComparison.OrdinalIgnoreCase))
            {
                error = "checksum_mismatch";
                return false;
            }

            try
            {
                data = JsonUtility.FromJson<TeamSaveData>(json);
            }
            catch (Exception ex)
            {
                error = "json_" + ex.GetType().Name;
                return false;
            }

            if (data == null)
            {
                error = "json_null";
                return false;
            }

            if (data.version <= 0)
            {
                error = "bad_version";
                return false;
            }

            data.txns ??= new List<SaveTxnRecord>();
            return true;
        }

        public static bool TryReadFile(string path, out TeamSaveData data, out string error)
        {
            data = null;
            error = string.Empty;

            try
            {
                if (!File.Exists(path))
                {
                    error = "missing";
                    return false;
                }

                byte[] bytes = File.ReadAllBytes(path);
                return TryParse(bytes, out data, out error);
            }
            catch (Exception ex)
            {
                error = "read_" + ex.GetType().Name;
                return false;
            }
        }

        public static string Sha256OfFile(string path)
        {
            try
            {
                if (!File.Exists(path))
                {
                    return "missing";
                }

                using FileStream fs = File.OpenRead(path);
                using SHA256 sha = SHA256.Create();
                return ToHex(sha.ComputeHash(fs));
            }
            catch (Exception ex)
            {
                return "error_" + ex.GetType().Name;
            }
        }

        public static string Sha256Hex(byte[] bytes)
        {
            using SHA256 sha = SHA256.Create();
            return ToHex(sha.ComputeHash(bytes));
        }

        private static string ToHex(byte[] hash)
        {
            StringBuilder sb = new(hash.Length * 2);
            foreach (byte b in hash)
            {
                sb.Append(b.ToString("x2"));
            }

            return sb.ToString();
        }
    }
}
