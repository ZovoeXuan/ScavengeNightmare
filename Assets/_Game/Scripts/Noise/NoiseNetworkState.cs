using Mirror;
using UnityEngine;

namespace ScavengeNightmare.Noise
{
    /// <summary>
    /// 噪音 HUD 的服务端真值桥（A9）：等级/主要来源/声源位置的 5Hz SyncVar。
    /// HUD 与怪物 AI 必须读同一份数据，否则「玩家看到的来源」和「怪物追的来源」会分家。
    ///
    /// 同时它负责服务端的噪音自然衰减：4A 的 NoiseBus 只有测试会手动 Decay，
    /// 没有逐帧衰减时记忆永不遗忘，怪物会一直追一条早就结束的噪音。
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

        public override void OnStartServer()
        {
            base.OnStartServer();
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
