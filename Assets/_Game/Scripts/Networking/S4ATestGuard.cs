using ScavengeNightmare.Save;
using UnityEngine;

namespace ScavengeNightmare.Networking
{
    /// <summary>
    /// 自动化用例的异常哨兵。
    ///
    /// 之前踩过的坑：协程中途抛异常会**静默终止**，进程照常跑完、日志里没有终局标记，
    /// 事后只能看到「exit=0 但什么都没验」。这里把未处理异常转成可 grep 的码并固定退出码 4，
    /// 与批处理的退出码分类对齐（0=通过 / 1=业务断言失败 / 4=运行时异常）。
    ///
    /// 只对 <see cref="LogType.Exception"/> 生效：用例自己的断言失败走的是 LogError，
    /// 不能被当成异常提前掐断，否则失败信息还没打完进程就没了。
    /// </summary>
    public sealed class S4ATestGuard : MonoBehaviour
    {
        private bool aborted;

        private void OnEnable()
        {
            if (!S4ACli.IsTestRun)
            {
                return;
            }

            Application.logMessageReceived += OnLog;
        }

        private void OnDisable()
        {
            Application.logMessageReceived -= OnLog;
        }

        private void OnLog(string condition, string stackTrace, LogType type)
        {
            if (type != LogType.Exception || aborted)
            {
                return;
            }

            aborted = true;
            Debug.LogError($"[S4A] S4A_TEST_EXCEPTION msg={condition}");
            Debug.LogError($"[S4A] S4A_TEST_ABORT reason=unhandled_exception");
            Application.Quit(4);
        }
    }
}
