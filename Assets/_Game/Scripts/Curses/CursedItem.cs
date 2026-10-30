using Mirror;
using ScavengeNightmare.Gameplay;
using UnityEngine;

namespace ScavengeNightmare.Curses
{
    /// <summary>
    /// 诅咒种类。用 byte 是为了能直接进 SyncVar（换成 int 每次同步多传 3 字节，
    /// 而它每秒都要随倒计时一起下发）。
    /// </summary>
    public enum CurseKind : byte
    {
        None = 0,
        Doll = 1,
        Tablet = 2,
    }

    /// <summary>
    /// 单件诅咒物（P0 §4.4）。
    ///
    /// 分层铁律：这个组件**只描述自己**——种类、发笑规则、放下规则、未封印折价。
    /// 「同一玩家哪一件生效、哪一件休眠」由 <see cref="CurseRuntime"/> 唯一裁决，
    /// 这里只接收结果。让两件各自判断必然出现「都以为自己生效」的双生效，
    /// 而双生效正是 A7 要堵的东西。
    ///
    /// 它是 NetworkBehaviour：休眠/归属/倒计时都要对四端的 HUD 同步，
    /// 客户端不能自己猜「我手上这件生不生效」。
    /// </summary>
    public sealed class CursedItem : NetworkBehaviour
    {
        public const string DollId = "curse_doll";
        public const string TabletId = "curse_tablet";

        /// <summary>
        /// P0 不做封印，两件恒定按标价 60% 计价（笑面偶 510 / 无字牌位 780）。
        /// 常量写在这里，HUD、结算、测试读同一个数，不会出现三处 0.6 各写一遍。
        /// </summary>
        public const float UnsealedValueRate = 0.6f;

        [Header("身份")]
        [SerializeField] private CurseKind kind = CurseKind.Doll;
        [SerializeField] private string curseId = DollId;
        [SerializeField] private string displayName = "笑面偶";

        [Header("笑面偶：发笑")]
        [Tooltip("发笑间隔下限（秒）")]
        [SerializeField] private float laughIntervalMin = 12f;
        [Tooltip("发笑间隔上限（秒）")]
        [SerializeField] private float laughIntervalMax = 20f;
        [Tooltip("发笑前兆：头部抖动 + 吸气（秒）")]
        [SerializeField] private float laughWindup = 1.5f;
        [Tooltip("发笑噪音。必须走 NoiseBus.Report 带上 source，可归因是 A9 的硬要求")]
        [SerializeField] private float laughNoise = 8f;

        [Header("无字牌位：放下")]
        [Tooltip("放下需长按的秒数。0 = 普通货，松手即脱手（4A 全部货物都是 0）")]
        [SerializeField] private float requiredReleaseHold = 0f;
        [Tooltip("抓握时手抖幅度。0 = 不抖")]
        [SerializeField] private float handShakeAmplitude = 0f;

        [SyncVar] private bool dormant;
        [SyncVar] private uint holderNetId;
        [SyncVar] private uint cartOwnerNetId;
        [SyncVar] private float laughCountdown;
        [SyncVar] private int laughCount;

        private GrabbableProp prop;
        private Transform visual;
        private Vector3 visualRestPosition;
        private Vector3 visualRestScale;
        private float shakeUntil;
        private float shakeSeed;
        private bool dormantVisualApplied;

        public CurseKind Kind => kind;
        public string CurseId => curseId;
        public string DisplayName => displayName;
        public float LaughIntervalMin => laughIntervalMin;
        public float LaughIntervalMax => laughIntervalMax;
        public float LaughWindup => laughWindup;
        public float LaughNoise => laughNoise;
        public float RequiredReleaseHold => requiredReleaseHold;
        public float HandShakeAmplitude => handShakeAmplitude;

        /// <summary>是否休眠（第 2 件起）。休眠不是隐藏：HUD 必须明示，见 CurseHud。</summary>
        public bool IsDormant => dormant;

        /// <summary>归属玩家的 netId；0 = 无主（躺在地上）。</summary>
        public uint HolderNetId => holderNetId;

        /// <summary>
        /// 把它装上车的人。诅咒货在车上时仍算在这个人头上（§4.4 解法「丢车外」），
        /// 归属人是**装车那一刻的装载者**，不是当前推行者、也不是离车最近的人。
        /// 卸下 / 掉落 / 提交后 OnCart 判不到，归属自动解除。
        /// </summary>
        public uint CartOwnerNetId => cartOwnerNetId;

        /// <summary>距离下次发笑还有多久（HUD 倒计时用）。</summary>
        public float LaughCountdown => laughCountdown;

        public int LaughCount => laughCount;

        /// <summary>P0 无封印，恒 false。留着这个查询是为了 HUD 与折价口径将来不用改。</summary>
        public bool Sealed => false;

        public GrabbableProp Prop => prop != null ? prop : (prop = GetComponent<GrabbableProp>());

        /// <summary>
        /// 当前价值：未封印按标价 60%（笑面偶 850→510、牌位 1300→780）。
        /// 结算口径只能从这里来，不许各处自己乘 0.6。
        /// </summary>
        public int CurrentValue
        {
            get
            {
                GrabbableProp self = Prop;
                int listed = self != null && self.Profile != null ? self.Profile.value : 0;
                return Sealed ? listed : Mathf.RoundToInt(listed * UnsealedValueRate);
            }
        }

        private void Awake()
        {
            visual = transform.Find("Visual");

            if (visual != null)
            {
                visualRestPosition = visual.localPosition;
                visualRestScale = visual.localScale;
            }

            shakeSeed = Random.Range(0f, 100f);
        }

        private void Update()
        {
            ApplyDormantVisual();
            ApplyShake();
        }

        // ---------------- 服务端写入口（只有 CurseRuntime 能调） ----------------

        [Server]
        public void ServerSetDormant(bool value)
        {
            dormant = value;
        }

        [Server]
        public void ServerSetHolder(uint value)
        {
            holderNetId = value;
        }

        /// <summary>装车那一刻由 CartController 写入：谁装上去谁承担。</summary>
        [Server]
        public void ServerSetCartOwner(uint value)
        {
            cartOwnerNetId = value;
        }

        [Server]
        public void ServerSetLaughCountdown(float value)
        {
            laughCountdown = value;
        }

        [Server]
        public void ServerSetLaughCount(int value)
        {
            laughCount = value;
        }

        // ---------------- 表现 ----------------

        /// <summary>
        /// 服务端判定「要笑了」先广播前兆：1.5s 头部抖动 + 吸气。
        /// 前兆与发笑分两段，玩家才有「赶紧放下 / 换手」的窗口——
        /// 合在一起就成了没有预警的随机噪音，那是惊吓不是恐怖喜剧。
        /// </summary>
        [ClientRpc]
        public void RpcLaughWindup()
        {
            shakeUntil = Time.time + laughWindup;
        }

        /// <summary>真的笑出来了：短促的一次抖动收尾。</summary>
        [ClientRpc]
        public void RpcLaugh()
        {
            shakeUntil = Time.time + 0.35f;
        }

        /// <summary>休眠不是隐藏：整件缩小一点并缩到蜷起来的姿态，玩家一眼能看出它「没生效」。</summary>
        private void ApplyDormantVisual()
        {
            if (visual == null || dormantVisualApplied == dormant)
            {
                return;
            }

            dormantVisualApplied = dormant;
            visual.localScale = dormant ? visualRestScale * 0.82f : visualRestScale;
        }

        private void ApplyShake()
        {
            if (visual == null)
            {
                return;
            }

            bool winding = Time.time < shakeUntil;
            bool shakes = winding || (handShakeAmplitude > 0f && dormant == false && IsCarriedByLocalHand());

            if (!shakes)
            {
                visual.localPosition = visualRestPosition;
                return;
            }

            // 前兆是「吸气」那种低频抖动；抓握手抖是高频小抖。两条频率分开，
            // 玩家能区分「它要笑了」和「我端不稳」。
            float frequency = winding ? 7f : 13f;
            float amplitude = winding ? 0.05f : handShakeAmplitude * 0.6f;

            float phase = Time.time * frequency + shakeSeed;
            float jitterX = Mathf.PerlinNoise(phase, 0.31f) - 0.5f;
            float jitterY = Mathf.PerlinNoise(0.77f, phase) - 0.5f;

            visual.localPosition = visualRestPosition
                                   + new Vector3(jitterX * amplitude * 2f, jitterY * amplitude * 2f, 0f);
        }

        private bool IsCarriedByLocalHand()
        {
            GrabbableProp self = Prop;
            return self != null && self.IsHeld;
        }
    }
}
