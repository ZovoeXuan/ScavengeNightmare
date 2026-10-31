using Mirror;
using ScavengeNightmare.Audio;
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

        /// <summary>
        /// R6 重物落地：客户端物件是 kinematic 的，收不到碰撞回调，
        /// 所以响声必须由服务端判定后用一条 ClientRpc 送到各端，
        /// 否则双人局里客户端整局听不到任何落地声。
        /// </summary>
        public void HeavyDropAuthoritative(float mass)
        {
            if (!isServer)
            {
                return;
            }

            RpcHeavyDrop(mass, transform.position);
        }

        [ClientRpc]
        private void RpcHeavyDrop(float mass, Vector3 at)
        {
            AudioBus.Play(AudioCue.HeavyDrop, at, 1f, $"mass={mass:F1}");
        }
    }
}
