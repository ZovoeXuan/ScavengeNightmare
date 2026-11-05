using Mirror;
using ScavengeNightmare.Gameplay;
using UnityEngine;

namespace ScavengeNightmare.Curses
{
    public enum CurseKind : byte
    {
        None = 0,
        Doll = 1,
        Tablet = 2,
    }

    /// <summary>
    /// 诅咒物件 V2 升级版：
    /// 1) 修复 IsCarriedByLocalHand() 未校验 holderNetId 导致一人拿牌位全队客户端都在抖动的 Bug (CUR-01)；
    /// 2) 新增 [SyncVar] isSealed 与 ServerSealCurse()：在净化祭坛完成封印后永久消除发笑/手抖/难脱手负面效果，
    ///    并将售出价格从 60% 折损提升至 100% 原价（甚至可获得净化溢价）。
    /// </summary>
    public sealed class CursedItem : NetworkBehaviour
    {
        public const string DollId = "curse_doll";
        public const string TabletId = "curse_tablet";
        public const float UnsealedValueRate = 0.6f;

        [Header("身份")]
        [SerializeField] private CurseKind kind = CurseKind.Doll;
        [SerializeField] private string curseId = DollId;
        [SerializeField] private string displayName = "笑面偶";

        [Header("笑面偶：发笑")]
        [SerializeField] private float laughIntervalMin = 12f;
        [SerializeField] private float laughIntervalMax = 20f;
        [SerializeField] private float laughWindup = 1.5f;
        [SerializeField] private float laughNoise = 8f;

        [Header("无字牌位：放下")]
        [SerializeField] private float requiredReleaseHold = 0f;
        [SerializeField] private float handShakeAmplitude = 0f;

        [SyncVar] private bool dormant;
        [SyncVar] private bool isSealed;
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
        public float RequiredReleaseHold => isSealed ? 0f : requiredReleaseHold;
        public float HandShakeAmplitude => isSealed ? 0f : handShakeAmplitude;
        public bool IsDormant => dormant || isSealed;
        public uint HolderNetId => holderNetId;
        public uint CartOwnerNetId => cartOwnerNetId;
        public float LaughCountdown => laughCountdown;
        public int LaughCount => laughCount;
        public bool Sealed => isSealed;

        public GrabbableProp Prop => prop != null ? prop : (prop = GetComponent<GrabbableProp>());

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
            visual = transform.Find("visual");

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

        [Server]
        public void ServerSealCurse()
        {
            if (isSealed)
            {
                return;
            }

            isSealed = true;
            dormant = true;
            laughCountdown = 0f;
            Debug.Log($"[S4B] GATE_CURSE_SEALED id={curseId} holder={holderNetId} value={CurrentValue}");
        }

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

        [ClientRpc]
        public void RpcLaughWindup()
        {
            shakeUntil = Time.time + laughWindup;
        }

        [ClientRpc]
        public void RpcLaugh()
        {
            shakeUntil = Time.time + 0.35f;
        }

        private void ApplyDormantVisual()
        {
            if (visual == null || dormantVisualApplied == IsDormant)
            {
                return;
            }

            dormantVisualApplied = IsDormant;
            visual.localScale = IsDormant ? visualRestScale * 0.82f : visualRestScale;
        }

        private void ApplyShake()
        {
            if (visual == null)
            {
                return;
            }

            bool winding = !isSealed && Time.time < shakeUntil;
            bool shakes = winding || (!isSealed && handShakeAmplitude > 0f && !dormant && IsCarriedByLocalHand());

            if (!shakes)
            {
                visual.localPosition = visualRestPosition;
                return;
            }

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
            NetworkIdentity local = NetworkClient.localPlayer;
            if (local != null && holderNetId != 0)
            {
                return holderNetId == local.netId;
            }

            GrabbableProp self = Prop;
            return self != null && self.IsHeld;
        }
    }
}
