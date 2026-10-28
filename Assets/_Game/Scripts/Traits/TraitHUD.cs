using UnityEngine;

namespace ScavengeNightmare.Traits
{
    /// <summary>
    /// D8 HUD：左上角显示「我是谁 + 这一局的缺点 + 补偿收益」。
    ///
    /// 补偿收益必须显示，这是 D8 的验收口径之一：玩家要知道自己亏了什么、赚了什么，
    /// 否则缺点牌只会读作「被削弱」而不是「有取舍」。
    /// 冲突组合额外标红——那是笑点最可能出现的地方，实测时要重点盯。
    /// </summary>
    public sealed class TraitHUD : MonoBehaviour
    {
        [SerializeField] private PlayerTraits traits;

        private GUIStyle style;
        private GUIStyle conflictStyle;

        private void Awake()
        {
            if (traits == null)
            {
                traits = GetComponent<PlayerTraits>();
            }
        }

        private bool IsLocal
        {
            get
            {
                if (traits == null)
                {
                    return false;
                }

                // NetworkIdentity.isLocalPlayer 只在本地玩家的复制体上为 true
                Mirror.NetworkIdentity id = traits.GetComponent<Mirror.NetworkIdentity>();
                return id != null && id.isLocalPlayer;
            }
        }

        private void OnGUI()
        {
            if (traits == null || !IsLocal)
            {
                return;
            }

            PlayerLoadout load = traits.Loadout;
            if (!load.HasIdentity)
            {
                return;
            }

            EnsureStyles();

            string identity = load.Identity.displayName;
            string card = load.HasCard ? load.Card.displayName : "无";
            string comp = load.HasCard ? load.Card.compensationText : "—";
            string notes = load.HasCard ? load.Card.comedyNotes : string.Empty;

            string text = $"<b>身份：{identity}</b>    <b>缺点：{card}</b>\n" +
                          $"补偿：{comp}\n" +
                          $"<size=10>{notes}</size>";

            Rect box = new Rect(12f, 12f, 340f, 74f);
            GUI.Label(box, text, load.IsConflict ? conflictStyle : style);

            if (load.IsConflict)
            {
                GUI.Label(new Rect(12f, 88f, 340f, 18f), "<b><color=#FF6B6B>⚠ 冲突组合</color></b>", style);
            }

            if (load.ValueScent)
            {
                string scent = ValueScentText();
                if (scent != null)
                {
                    GUI.Label(new Rect(12f, load.IsConflict ? 106f : 88f, 340f, 18f), scent, style);
                }
            }
        }

        /// <summary>
        /// D8「闻香定位值钱货」的补偿收益：给出最贵物件的方位与距离。
        /// 这是把「缺点」兑换成「有用信息」的典型做法——近视眼看不清小件，但知道钱在哪。
        /// </summary>
        private string ValueScentText()
        {
            Gameplay.GrabbableProp best = null;
            int bestValue = 0;

            foreach (Gameplay.GrabbableProp prop in Object.FindObjectsByType<Gameplay.GrabbableProp>(FindObjectsSortMode.None))
            {
                if (prop.Profile == null || !prop.Profile.isFragile || prop.Profile.value <= bestValue)
                {
                    continue;
                }

                bestValue = prop.Profile.value;
                best = prop;
            }

            if (best == null)
            {
                return null;
            }

            Vector3 to = best.transform.position - transform.position;
            float distance = to.magnitude;
            if (distance < 0.01f)
            {
                return $"<color=#9BE89B>值 {bestValue} 的货就在脚下</color>";
            }

            Vector3 local = transform.InverseTransformDirection(to.normalized);
            string dir = Mathf.Abs(local.z) > Mathf.Abs(local.x)
                ? (local.z > 0f ? "前方" : "后方")
                : (local.x > 0f ? "右侧" : "左侧");

            return $"<color=#9BE89B>闻到值 {bestValue} 的货：{dir} {distance:F0}m</color>";
        }

        private void EnsureStyles()
        {
            if (style == null)
            {
                style = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 13,
                    richText = true,
                    wordWrap = true,
                    alignment = TextAnchor.UpperLeft,
                    normal = { textColor = new Color(1f, 1f, 1f, 0.92f) }
                };
            }

            if (conflictStyle == null)
            {
                conflictStyle = new GUIStyle(style)
                {
                    normal = { textColor = new Color(1f, 0.85f, 0.6f, 0.95f) }
                };
            }
        }
    }
}
