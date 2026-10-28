using Mirror;
using UnityEngine;

namespace ScavengeNightmare.Gameplay
{
    /// <summary>
    /// D6.5 物件联网：位置同步 + 抓取时所有权切换 + 碎裂事件广播。
    ///
    /// 为什么必须「总有一个人持有权威」：Mirror 的 NetworkTransform 以 syncDirection 决定谁发送。
    /// 设成 ClientToServer 时，没人拥有该物件则服务端也不发送，客户端上的物件会冻结。
    /// 所以出生时把权威交给房主（本项目是房主权威 P2P，没有独立服务器），
    /// 被抓时转给抓的人，松手后再交回房主 —— 物件永远有且只有一个模拟者。
    /// </summary>
    [RequireComponent(typeof(NetworkIdentity))]
    [RequireComponent(typeof(GrabbableProp))]
    public sealed class NetworkedProp : NetworkBehaviour
    {
        private Rigidbody body;
        private FragileProp fragile;
        private NetworkTransformReliable netTransform;

        /// <summary>本端是否负责模拟：被自己拥有，或自己是服务端且无人认领。</summary>
        private bool SimulateHere => isOwned || (isServer && connectionToClient == null);

        private void Awake()
        {
            body = GetComponent<Rigidbody>();
            fragile = GetComponent<FragileProp>();
            netTransform = GetComponent<NetworkTransformReliable>();
        }

        public override void OnStartServer()
        {
            // 出生即把权威交给房主，避免出现「无人拥有 → 谁都不发送」的冻结状态
            if (netIdentity.connectionToClient == null && NetworkServer.localConnection != null)
            {
                netIdentity.AssignClientAuthority(NetworkServer.localConnection);
            }

            UpdateSyncDirection();
        }

        public override void OnStartClient()
        {
            ApplySimulation(SimulateHere);
            UpdateSyncDirection();
        }

        public override void OnStartAuthority()
        {
            ApplySimulation(true);
            UpdateSyncDirection();
        }

        public override void OnStopAuthority()
        {
            ApplySimulation(false);
            UpdateSyncDirection();
        }

        /// <summary>
        /// 同步方向必须跟着「谁持有」走：
        /// 房主持有时用 ServerToClient（否则房主既当服务端又当拥有者，不会向其他客户端广播，实测客户端纹丝不动）；
        /// 远程客户端持有时用 ClientToServer（由该客户端发送，服务端转发）。
        /// </summary>
        private void UpdateSyncDirection()
        {
            if (netTransform == null)
            {
                return;
            }

            if (isServer)
            {
                bool hostOwns = netIdentity.connectionToClient == null ||
                                netIdentity.connectionToClient == NetworkServer.localConnection;
                netTransform.syncDirection = hostOwns ? SyncDirection.ServerToClient : SyncDirection.ClientToServer;
            }
            else
            {
                netTransform.syncDirection = SyncDirection.ClientToServer;
            }
        }

        /// <summary>只有持有权威的一端才跑物理，其余端由 NetworkTransform 摆位。</summary>
        private void ApplySimulation(bool on)
        {
            if (body == null)
            {
                return;
            }

            body.isKinematic = !on;
            if (on)
            {
                body.WakeUp();
            }
        }

        public void RequestAuthority()
        {
            if (!isClient || SimulateHere)
            {
                return;
            }

            CmdRequestAuthority();
        }

        public void ReleaseAuthority()
        {
            if (!isOwned)
            {
                return;
            }

            CmdReleaseAuthority();
        }

        [Command(requiresAuthority = false)]
        private void CmdRequestAuthority(NetworkConnectionToClient sender = null)
        {
            if (sender == null)
            {
                return;
            }

            // 已被别人抓住就不抢，避免两人同时抓同一件时反复夺权
            if (netIdentity.connectionToClient != null && netIdentity.connectionToClient != sender)
            {
                return;
            }

            netIdentity.AssignClientAuthority(sender);
        }

        [Command(requiresAuthority = false)]
        private void CmdReleaseAuthority()
        {
            if (NetworkServer.localConnection != null)
            {
                netIdentity.AssignClientAuthority(NetworkServer.localConnection);
            }
            else
            {
                netIdentity.RemoveClientAuthority();
            }
        }

        /// <summary>碎裂：任意端检测到都上报服务端，由服务端广播，保证四端一致。</summary>
        public void ShatterAuthoritative(float speed)
        {
            if (isServer)
            {
                RpcShatter(speed);
            }
            else
            {
                CmdShatter(speed);
            }
        }

        [Command(requiresAuthority = false)]
        private void CmdShatter(float speed)
        {
            RpcShatter(speed);
        }

        [ClientRpc]
        private void RpcShatter(float speed)
        {
            if (fragile != null)
            {
                fragile.ApplyBreak(speed);
            }
        }
    }
}
