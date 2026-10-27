using ScavengeNightmare.Gameplay;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ScavengeNightmare.UI
{
    /// <summary>
    /// D5 手感方案切换器：数字键 1/2/3 在「轻快 / 稳重 / 沉重」之间热切换，屏幕左下角显示当前方案。
    /// 只对本地玩家生效；方案属于本地手感参数，不通过网络同步（同局玩家可以各用各的）。
    /// </summary>
    public sealed class FeelSwitchHUD : MonoBehaviour
    {
        [Header("方案（顺序对应数字键 1..N）")]
        [SerializeField] private FeelScheme[] schemes;

        [Tooltip("启动时默认选中的下标，1 = 稳重")]
        [SerializeField] private int defaultIndex = 1;

        [Tooltip("仅本地玩家生效：远端复制体不切方案")]
        [SerializeField] private GrabController localGate;

        [SerializeField] private bool showPanel = true;

        private int current = -1;
        private GUIStyle style;

        public FeelScheme Current => current >= 0 && schemes != null && current < schemes.Length ? schemes[current] : null;
        public string CurrentId => Current != null ? Current.schemeId : "default";

        private bool IsLocal
        {
            get
            {
                if (localGate == null)
                {
                    localGate = GetComponent<GrabController>();
                }

                return localGate == null || localGate.IsLocalControl;
            }
        }

        private void Awake()
        {
            if (localGate == null)
            {
                localGate = GetComponent<GrabController>();
            }
        }

        private void Start()
        {
            if (!IsLocal)
            {
                return;
            }

            if (schemes != null && schemes.Length > 0)
            {
                Select(Mathf.Clamp(defaultIndex, 0, schemes.Length - 1));
            }
        }

        private void Update()
        {
            if (!IsLocal || schemes == null)
            {
                return;
            }

            Keyboard kb = Keyboard.current;
            if (kb == null)
            {
                return;
            }

            if (kb.digit1Key.wasPressedThisFrame) Select(0);
            if (kb.digit2Key.wasPressedThisFrame) Select(1);
            if (kb.digit3Key.wasPressedThisFrame) Select(2);
            if (kb.digit4Key.wasPressedThisFrame) Select(3);
        }

        public void Select(int index)
        {
            if (schemes == null || index < 0 || index >= schemes.Length)
            {
                return;
            }

            current = index;
            FeelSettings.Set(schemes[index]);
            Debug.Log($"[D5] D5_FEEL_APPLIED scheme={CurrentId} name={(schemes[index] != null ? schemes[index].displayName : "-")}");
        }

        /// <summary>按 schemeId 选方案，供自动化测试使用。</summary>
        public bool SelectById(string schemeId)
        {
            if (schemes == null || string.IsNullOrEmpty(schemeId))
            {
                return false;
            }

            for (int i = 0; i < schemes.Length; i++)
            {
                if (schemes[i] != null && schemes[i].schemeId == schemeId)
                {
                    Select(i);
                    return true;
                }
            }

            Debug.LogWarning($"[D5] 找不到手感方案 schemeId={schemeId}");
            return false;
        }

        private void OnGUI()
        {
            if (!showPanel || !IsLocal)
            {
                return;
            }

            FeelScheme scheme = Current;
            if (scheme == null)
            {
                return;
            }

            EnsureStyle();

            float height = 66f;
            Rect box = new Rect(12f, Screen.height - height - 12f, 380f, height);
            GUI.Label(box, $"<b>手感方案：{scheme.displayName}</b>（{current + 1}/{schemes.Length}，数字键切换）\n笑点侧重：{scheme.comedyNotes}", style);
        }

        private void EnsureStyle()
        {
            if (style != null)
            {
                return;
            }

            style = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                richText = true,
                wordWrap = true,
                alignment = TextAnchor.LowerLeft
            };
        }
    }
}
