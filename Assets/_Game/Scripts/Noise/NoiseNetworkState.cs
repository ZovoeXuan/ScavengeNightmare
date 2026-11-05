using Mirror;
using UnityEngine;

namespace ScavengeNightmare.Noise
{
    /// <summary>
    /// 噪音 HUD 的服务端真值桥 V2 升级版：
    /// 修复 OnStartServer 未设置 Instance = this 导致纯服务端 (Headless) 读取 Instance 为 null 的 Bug。
    /// </summary>
    public sealed class NoiseNetworkState : NetworkBehaviour
    {
        public static NoiseNetworkState Instance { get; private set; }

        [SyncVar] private float level;
        [SyncVar] private string source = "none";
        [SyncVar] private Vector3 sourcePosition;
        [SyncVar] private string band = "quiet";

        private float nextSyncAt;

        public float Level => level;
        public string Source => source;
        public Vector3 SourcePosition => sourcePosition;
        public string Band => band;

        private void Awake()
        {
            Instance = this;
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            Instance = this;
            level = NoiseBus.Level;
            source = NoiseBus.TopSource;
            sourcePosition = NoiseBus.TopSourcePosition;
            band = NoiseBus.ZoneForLevel(level);
            nextSyncAt = Time.time + 0.2f;
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void Update()
        {
            if (!NetworkServer.active)
            {
                return;
            }

            NoiseBus.Decay(Time.deltaTime);

            if (Time.time < nextSyncAt)
            {
                return;
            }

            nextSyncAt = Time.time + 0.2f;
            level = NoiseBus.Level;
            source = NoiseBus.TopSource;
            sourcePosition = NoiseBus.TopSourcePosition;
            band = NoiseBus.ZoneForLevel(level);
        }
    }
}
