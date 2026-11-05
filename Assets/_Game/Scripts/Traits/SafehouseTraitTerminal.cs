using Mirror;
using ScavengeNightmare.Economy;
using ScavengeNightmare.Networking;
using ScavengeNightmare.Save;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace ScavengeNightmare.Traits
{
    /// <summary>
    /// V2 新增安全屋角色与缺点牌终端 (SafehouseTraitTerminal)：
    /// 将 D8 角色系统（大力士/近视眼/瘸子/灵敏 + 5张缺点补偿牌）正式接入安全屋备战流程！
    /// 玩家在安全屋终端 2.5m 内按 C 键可免费切换角色身份，按 V 键可重新抽取缺点补偿牌（自动化测试期间保持静默）。
    /// </summary>
    public sealed class SafehouseTraitTerminal : NetworkBehaviour
    {
        [SerializeField] private float interactRadius = 2.5f;

        private void Update()
        {
            if (S4ACli.IsTestRun || !NetworkClient.active)
            {
                return;
            }

            NetworkIdentity local = NetworkClient.localPlayer;
            if (local == null)
            {
                return;
            }

            if (Vector3.Distance(transform.position, local.transform.position) > interactRadius)
            {
                return;
            }

#if ENABLE_INPUT_SYSTEM
            Keyboard kb = Keyboard.current;
            if (kb == null)
            {
                return;
            }

            if (kb.cKey.wasPressedThisFrame)
            {
                CmdCycleIdentity(local);
            }
            else if (kb.vKey.wasPressedThisFrame)
            {
                CmdRerollCard(local);
            }
#endif
        }

        private GUIStyle boxStyle;
        private GUIStyle titleStyle;
        private GUIStyle rowStyle;
        private GUIStyle hintStyle;

        private void OnGUI()
        {
            if (S4ACli.IsTestRun || !NetworkClient.active)
            {
                return;
            }

            NetworkIdentity local = NetworkClient.localPlayer;
            if (local == null)
            {
                return;
            }

            if (Vector3.Distance(transform.position, local.transform.position) > interactRadius)
            {
                return;
            }

            PlayerTraits traits = local.GetComponent<PlayerTraits>();
            if (traits == null)
            {
                return;
            }

            EnsureStyles();

            string identityName = traits.Identity != null ? traits.Identity.displayName : "默认";
            string identityDesc = traits.Identity != null ? traits.Identity.description : "";
            string cardName = traits.Card != null ? traits.Card.displayName : "无";
            string cardNotes = traits.Card != null ? traits.Card.comedyNotes : "";
            string cardComp = traits.Card != null ? traits.Card.compensationText : "";

            GUILayout.BeginArea(new Rect(16f, 16f, 400f, 140f), boxStyle);
            GUILayout.Label("安全屋 · 备战更衣室", titleStyle);
            GUILayout.Space(2f);
            GUILayout.Label($"当前身份：<b>【{identityName}】</b> - {identityDesc}", rowStyle);
            GUILayout.Label($"当前缺点：<b>【{cardName}】</b> - {cardNotes}（收益：{cardComp}）", rowStyle);
            GUILayout.Space(4f);
            GUILayout.Label("按 <b>[C]</b> 切换角色身份 (大力士/近视眼/瘸子/灵敏)", hintStyle);
            GUILayout.Label("按 <b>[V]</b> 重新抽取缺点牌 (冲突加权笑点组合)", hintStyle);
            GUILayout.EndArea();
        }

        private void EnsureStyles()
        {
            boxStyle ??= new GUIStyle(GUI.skin.box)
            {
                padding = new RectOffset(12, 12, 10, 10),
                alignment = TextAnchor.UpperLeft
            };

            titleStyle ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 16,
                fontStyle = FontStyle.Bold
            };

            rowStyle ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                wordWrap = true
            };

            hintStyle ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold
            };
        }

        [Command(requiresAuthority = false)]
        private void CmdCycleIdentity(NetworkIdentity playerIdentity)
        {
            if (playerIdentity == null)
            {
                return;
            }

            PlayerTraits traits = playerIdentity.GetComponent<PlayerTraits>();
            traits?.ServerCycleIdentity();
        }

        [Command(requiresAuthority = false)]
        private void CmdRerollCard(NetworkIdentity playerIdentity)
        {
            if (playerIdentity == null)
            {
                return;
            }

            PlayerTraits traits = playerIdentity.GetComponent<PlayerTraits>();
            traits?.ServerRerollCard();
        }
    }
}
