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
        /// <summary>
        /// 是否处于自动化用例进程。用于给「跳过校验」的测试入口加一道运行期闸门：
        /// 正式包永远不会带 -s4a* / -d*test 这类参数，方法体本身也只在
        /// S4A_TEST_BUILD 下才会被编译进来（见 GrabController.ServerForceGrab）。
        /// </summary>
        public static bool IsTestRun
        {
            get
            {
                if (testRunResolved)
                {
                    return testRun;
                }

                testRunResolved = true;
                string[] args = Environment.GetCommandLineArgs();

                foreach (string a in args)
                {
                    if (a == null)
                    {
                        continue;
                    }

                    if (a.StartsWith("-s4a", StringComparison.OrdinalIgnoreCase)
                        || a.StartsWith("-d4test", StringComparison.OrdinalIgnoreCase)
                        || a.StartsWith("-d6test", StringComparison.OrdinalIgnoreCase)
                        || a.StartsWith("-d8test", StringComparison.OrdinalIgnoreCase)
                        || a.StartsWith("-d9", StringComparison.OrdinalIgnoreCase)
                        || a.StartsWith("-d12", StringComparison.OrdinalIgnoreCase)
                        || a.StartsWith("-d13", StringComparison.OrdinalIgnoreCase))
                    {
                        testRun = true;
                        break;
                    }
                }

                return testRun;
            }
        }

        private static bool testRun;
        private static bool testRunResolved;

        public static string SaveDirOverride;

        public static string ResolveSaveDir()
        {
            string fromCli = Value("-savedir=");
            return !string.IsNullOrEmpty(fromCli) ? fromCli : SaveDirOverride;
        }
    }
}
