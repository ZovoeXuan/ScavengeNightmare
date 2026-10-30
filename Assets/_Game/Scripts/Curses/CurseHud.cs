using System.Collections.Generic;
using System.Text;
using Mirror;
using UnityEngine;

namespace ScavengeNightmare.Curses
{
    /// <summary>
    /// 诅咒 HUD（挂在玩家预制体上，只读 SyncVar，不持有游戏状态）。
    ///
    /// 三条硬要求落在这一层：
    /// 1. **常驻「未封印 60%」**：只要身上有诅咒，这行字一直在，不能只在结算时才出现。
    ///    P0 不做封印，所以两件恒 60%，玩家必须随时能算出这件货值多少。
    /// 2. **休眠必须明示**：第 2 件不是「默默失效」，HUD 要写出「休眠：xxx（未生效）」，
    ///    否则玩家会以为自己那件牌位白拿了。
    /// 3. 发笑倒计时：让「快笑了」变成可预判的事，而不是随机惊吓。
    ///
    /// 临时 IMGUI 件，正式 UI 在 P1 替换（与 NoiseHud / DownedHud 同批技术债）。
    /// </summary>
    public sealed class CurseHud : MonoBehaviour
    {
        private readonly List<CursedItem> mine = new();
        private readonly StringBuilder builder = new();

        private GUIStyle labelStyle;
        private float refreshIn;

        /// <summary>测试与调试读它：HUD 当前渲染出来的整段文字。</summary>
        public string DisplayedText { get; private set; } = string.Empty;

        /// <summary>身上是否有休眠中的诅咒（休眠不是隐藏的第一判据）。</summary>
        public bool HasDormant { get; private set; }

        /// <summary>生效中的那一件的 id；没有则为 "none"。</summary>
        public string ActiveCurseId { get; private set; } = "none";

        /// <summary>HUD 上写出来的折价口径，恒为 0.6（P0 无封印）。</summary>
        public float DisplayedValueRate => CursedItem.UnsealedValueRate;

        private void Update()
        {
            refreshIn -= Time.unscaledDeltaTime;
            if (refreshIn > 0f)
            {
                return;
            }

            refreshIn = 0.2f;
            Rebuild();
        }

        private void Rebuild()
        {
            mine.Clear();

            NetworkIdentity local = NetworkClient.localPlayer;
            uint localId = local != null ? local.netId : 0u;

            if (localId == 0u)
            {
                DisplayedText = string.Empty;
                HasDormant = false;
                ActiveCurseId = "none";
                return;
            }

            foreach (CursedItem item in FindObjectsByType<CursedItem>(FindObjectsSortMode.None))
            {
                if (item != null && item.HolderNetId == localId)
                {
                    mine.Add(item);
                }
            }

            // 与 CurseRuntime 同一顺序口径：netId 升序，第 0 件生效、其余休眠。
            mine.Sort((a, b) => a.netId.CompareTo(b.netId));

            HasDormant = false;
            ActiveCurseId = "none";
            builder.Length = 0;

            for (int i = 0; i < mine.Count; i++)
            {
                CursedItem item = mine[i];
                bool dormant = item.IsDormant;

                if (dormant)
                {
                    HasDormant = true;
                }
                else if (ActiveCurseId == "none")
                {
                    ActiveCurseId = item.CurseId;
                }

                if (i > 0)
                {
                    builder.Append('\n');
                }

                string state = dormant ? "休眠（未生效）" : "生效";
                builder.Append(item.DisplayName).Append("　").Append(state).Append("　未封印 60%");

                if (!dormant && item.Kind == CurseKind.Doll && item.LaughCountdown > 0.05f)
                {
                    builder.Append("　笑 ").Append(item.LaughCountdown.ToString("F0")).Append('s');
                }
            }

            DisplayedText = builder.ToString();
        }

        private void OnGUI()
        {
            if (mine.Count == 0)
            {
                return;
            }

            EnsureStyles();

            // 左下角：NoiseHud 占左上、DownedHud 占中央，这里再往上叠会互相压字。
            float height = 24f + mine.Count * 22f;
            Rect panel = new(16f, Screen.height - height - 16f, 340f, height);

            GUI.color = new Color(0.16f, 0.05f, 0.2f, 0.55f);
            GUI.DrawTexture(panel, Texture2D.whiteTexture);
            GUI.color = Color.white;

            GUI.Label(new Rect(panel.x + 8f, panel.y + 4f, 330f, 22f), "诅咒（未封印 60% 计价）", labelStyle);
            GUI.Label(new Rect(panel.x + 8f, panel.y + 26f, 330f, height - 26f), DisplayedText, labelStyle);
        }

        private void EnsureStyles()
        {
            labelStyle ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 14,
                normal = { textColor = Color.white }
            };
        }
    }
}
