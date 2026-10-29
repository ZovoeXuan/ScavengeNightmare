using System;
using System.Collections.Generic;

namespace ScavengeNightmare.Save
{
    /// <summary>
    /// 批处理参数读取。所有测试钩子都从这里取，避免每个脚本各写一份解析、口径还不一致。
    /// </summary>
    public static class S4ACli
    {
        public static bool Has(string flag)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], flag, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        public static string Value(string prefix)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] != null && args[i].StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return args[i].Substring(prefix.Length);
                }
            }

            return null;
        }

        public static int IntValue(string prefix, int fallback)
        {
            string raw = Value(prefix);
            return int.TryParse(raw, out int v) ? v : fallback;
        }

        public static bool Any(params string[] flags)
        {
            foreach (string f in flags)
            {
                if (Has(f))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 测试进程在 StartHost 之前可以把它指到临时目录，避免把开发机上的真实存档写成测试垃圾。
        /// 显式传 -savedir= 时以命令行为准。
        /// </summary>
        public static string SaveDirOverride;

        public static string ResolveSaveDir()
        {
            string fromCli = Value("-savedir=");
            return !string.IsNullOrEmpty(fromCli) ? fromCli : SaveDirOverride;
        }
    }
}
