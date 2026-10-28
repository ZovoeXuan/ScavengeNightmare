using System;
using Mirror;
using ScavengeNightmare.Gameplay;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ScavengeNightmare.Gameplay
{
    /// <summary>
    /// D7 实测标记器：挂在玩家预制体上，让每个玩家自己按一下就能记一条。
    ///
    /// 为什么让玩家按、而不是观察者记：朋友局里没人愿意当记录员，
    /// 而「笑的那一刻」只有当事人知道。按键只上报类别，文案与上下文由服务端拼，
    /// 保证四个人按的是同一份数据。
    ///
    /// 键位：F5 笑点 / F6 问题（卡点·迷惑）/ F7 金句（需对着录音说出来）
    /// </summary>
    public sealed class PlaytestMarker : NetworkBehaviour
    {
        private const float ToastSeconds = 2.5f;

        [SerializeField] private bool showHints = true;

        private int laughs;
        private int issues;
        private int quotes;
        private string toast = string.Empty;
        private float toastUntil;
        private GUIStyle hintStyle;
        private GUIStyle toastStyle;

        private void Update()
        {
            if (!isLocalPlayer)
            {
                return;
            }

            Keyboard kb = Keyboard.current;
            if (kb == null)
            {
                return;
            }

            if (kb.f5Key.wasPressedThisFrame)
            {
                MarkFromLocal(PlaytestRecorder.CatLaugh);
            }

            if (kb.f6Key.wasPressedThisFrame)
            {
                MarkFromLocal(PlaytestRecorder.CatIssue);
            }

            if (kb.f7Key.wasPressedThisFrame)
            {
                MarkFromLocal(PlaytestRecorder.CatQuote);
            }
        }

        /// <summary>
        /// 本地标记入口：键位与自动化验证共用。走 Command 上报服务端记录器，
        /// 因此这条路径被自动化覆盖到，就等于覆盖了真人按键的路径。
        /// </summary>
        public void MarkFromLocal(int category)
        {
            switch (category)
            {
                case PlaytestRecorder.CatLaugh:
                    laughs++;
                    toast = $"已记 笑点 #{laughs}";
                    break;
                case PlaytestRecorder.CatIssue:
                    issues++;
                    toast = $"已记 问题 #{issues}";
                    break;
                default:
                    quotes++;
                    toast = $"已记 金句 #{quotes}";
                    break;
            }

            toast += $"  {DateTime.Now:HH:mm:ss}";
            toastUntil = Time.time + ToastSeconds;

            // 本地立刻回显，再发服务端：网络往返不该让玩家怀疑「按了没反应」
            CmdMark(category);
        }

        [Command]
        private void CmdMark(int category)
        {
            PlaytestRecorder recorder = PlaytestRecorder.Instance;
            if (recorder == null)
            {
                Debug.LogWarning("[PT] 记录器不存在，本次标记未落盘（场景里缺 PlaytestRecorder）");
                return;
            }

            recorder.Mark(category, netIdentity, string.Empty);
        }

        private void OnGUI()
        {
            if (!isLocalPlayer)
            {
                return;
            }

            EnsureStyles();

            if (showHints)
            {
                Rect box = new Rect(Screen.width - 300f, Screen.height - 62f, 288f, 50f);
                GUI.Label(box,
                    $"<b>F5 笑点 · F6 问题 · F7 金句</b>\n本次  笑 {laughs}   问 {issues}   金 {quotes}",
                    hintStyle);
            }

            if (Time.time < toastUntil)
            {
                Rect t = new Rect(0f, Screen.height * 0.62f, Screen.width, 32f);
                GUI.Label(t, toast, toastStyle);
            }
        }

        private void EnsureStyles()
        {
            if (hintStyle == null)
            {
                hintStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 12,
                    richText = true,
                    wordWrap = true,
                    alignment = TextAnchor.LowerRight
                };
            }

            if (toastStyle == null)
            {
                toastStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 15,
                    richText = true,
                    alignment = TextAnchor.MiddleCenter
                };
            }
        }
    }
}
