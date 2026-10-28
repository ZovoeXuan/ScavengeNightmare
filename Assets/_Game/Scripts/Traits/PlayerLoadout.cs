using UnityEngine;

namespace ScavengeNightmare.Traits
{
    /// <summary>
    /// 生效参数包：把「角色身份 × 缺点牌」合成成一份可直接查询的倍率。
    ///
    /// 为什么要显式合成而不是各组件自己乘：合成只有一处，
    /// 才能验证「代价乘数一律 ≤ 1、补偿一律 ≥ 1、同轴不叠 buff」这三条设计约束。
    /// </summary>
    public readonly struct PlayerLoadout
    {
        public readonly CharacterIdentity Identity;
        public readonly DrawbackCard Card;

        public readonly float SpeedScale;
        public readonly float GrabStiffnessScale;
        public readonly float ThrowScale;
        public readonly float InteractRangeScale;
        public readonly float StunScale;
        public readonly float CarryDivisorScale;

        public readonly bool CanSprint;
        public readonly bool ShakyHands;
        public readonly bool NarrowDoorPanic;
        public readonly bool BlocksSmallItems;
        public readonly bool ValueScent;

        public readonly float SmallItemMassLimit;
        public readonly float ShakeAmplitude;
        public readonly float PanicSpeedScale;
        public readonly Vector3 BodyScale;
        public readonly Color BodyColor;
        public readonly bool IsConflict;

        public bool HasIdentity => Identity != null;
        public bool HasCard => Card != null;

        /// <summary>
        /// 空配置：倍率全 1、可冲刺、无任何缺点。
        /// 未配置数据表或身份尚未下发时使用——不能用 <c>default</c>，
        /// 因为默认构造会把 CanSprint 置为 false，等于凭空禁掉冲刺。
        /// </summary>
        public static PlayerLoadout Neutral { get; } = new PlayerLoadout(null, null);

        public PlayerLoadout(CharacterIdentity identity, DrawbackCard card)
        {
            Identity = identity;
            Card = card;

            SpeedScale = 1f;
            GrabStiffnessScale = 1f;
            ThrowScale = 1f;
            InteractRangeScale = 1f;
            StunScale = 1f;
            CarryDivisorScale = 1f;

            CanSprint = true;
            ShakyHands = false;
            NarrowDoorPanic = false;
            BlocksSmallItems = false;
            ValueScent = false;

            SmallItemMassLimit = 0f;
            ShakeAmplitude = 0f;
            PanicSpeedScale = 1f;
            BodyScale = Vector3.one;
            BodyColor = Color.white;
            IsConflict = false;

            if (identity != null)
            {
                SpeedScale *= identity.speedScale;
                GrabStiffnessScale *= identity.grabStiffnessScale;
                ThrowScale *= identity.throwScale;
                InteractRangeScale *= identity.interactRangeScale;
                StunScale *= identity.stunScale;
                CanSprint = identity.canSprint;
                BodyScale = identity.bodyScale;
                BodyColor = identity.bodyColor;

                if (identity.carryDivisorOverride > 0f)
                {
                    // 分母用「覆盖」而不是相乘：角色体型差异是绝对量，不应被倍率再缩放一次
                    CarryDivisorScale = identity.carryDivisorOverride;
                }
            }

            if (card != null)
            {
                SpeedScale *= card.speedScale;
                GrabStiffnessScale *= card.grabStiffnessScale;
                InteractRangeScale *= card.interactRangeScale;
                StunScale *= card.stunScale;
                CarryDivisorScale *= card.carryDivisorScale;

                // 补偿收益只加不减，且落在与代价不同的轴上
                ThrowScale *= card.compensationThrowScale;
                SpeedScale *= card.compensationSpeedScale;
                GrabStiffnessScale *= card.compensationGrabScale;
                CarryDivisorScale *= card.compensationCarryDivisorScale;

                ShakyHands = card.shakyHands;
                NarrowDoorPanic = card.narrowDoorPanic;
                BlocksSmallItems = card.blocksSmallItems;
                ValueScent = card.valueScent;

                SmallItemMassLimit = card.smallItemMassLimit;
                ShakeAmplitude = card.shakeAmplitude;
                PanicSpeedScale = card.panicSpeedScale;
                IsConflict = TraitSettings.IsConflict(identity, card);
            }
        }

        /// <summary>负重分母：越大越不怕重。角色覆盖值优先，其余走倍率。</summary>
        public float CarryDivisor(float schemeDivisor)
        {
            if (Identity != null && Identity.carryDivisorOverride > 0f)
            {
                return Identity.carryDivisorOverride * (Card != null ? Card.carryDivisorScale : 1f)
                       * (Card != null ? Card.compensationCarryDivisorScale : 1f);
            }

            return schemeDivisor * CarryDivisorScale;
        }

        /// <summary>该物件是否算「小件」（看不清小件用）。</summary>
        public bool IsSmallItem(float mass)
        {
            return BlocksSmallItems && SmallItemMassLimit > 0f && mass <= SmallItemMassLimit;
        }
    }
}
