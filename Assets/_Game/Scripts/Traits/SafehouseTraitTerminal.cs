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
