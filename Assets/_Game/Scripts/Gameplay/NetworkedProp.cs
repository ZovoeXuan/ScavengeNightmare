using Mirror;
using UnityEngine;

namespace ScavengeNightmare.Gameplay
{
    /// <summary>
    /// D6.5 物件联网（服务端权威）。
    ///
    /// 服务端是唯一的物理模拟者，客户端上的物件是 kinematic 的、位置完全由
    /// NetworkTransform（固定 ServerToClient）驱动。这样就不必在运行时翻转同步方向 ——
    /// 那条路实测会让客户端按旧快照慢爬，同一时刻位置差到 8.67m。
    /// </summary>
    [RequireComponent(typeof(NetworkIdentity))]
    [RequireComponent(typeof(GrabbableProp))]
    public sealed class NetworkedProp : NetworkBehaviour
    {
        private Rigidbody body;
        private FragileProp fragile;

        private void Awake()
        {
            body = GetComponent<Rigidbody>();
            fragile = GetComponent<FragileProp>();
        }

        public override void OnStartClient()
        {
            if (body != null && !isServer)
            {
                body.isKinematic = true;
            }
        }

        /// <summary>碎裂由服务端判定并广播，保证四端同时碎、不重复生成碎块。</summary>
        public void ShatterAuthoritative(float speed)
        {
            if (isServer)
            {
                RpcShatter(speed);
            }
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
