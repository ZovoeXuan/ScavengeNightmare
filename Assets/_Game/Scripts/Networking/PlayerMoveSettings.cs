using UnityEngine;

namespace ScavengeNightmare.Networking
{
    /// <summary>
    /// 玩家移动参数（D5）。把原本硬编码在 PlayerMotor 字段上的数值外置成数据表，
    /// 对应 D1 冻结的「一切手感参数进数据表，改表即生效」。
    /// </summary>
    [CreateAssetMenu(menuName = "拾荒惊魂/玩家移动参数", fileName = "PlayerMove")]
    public sealed class PlayerMoveSettings : ScriptableObject
    {
        [Header("速度")]
        public float walkSpeed = 3.2f;
        public float sprintSpeed = 5.0f;
        [Tooltip("加速度：越大起步越利落，越小越「肉」")]
        public float acceleration = 24f;

        [Header("跳跃与重力")]
        public float jumpSpeed = 4.2f;
        public float gravity = -18f;

        [Header("视角")]
        [Tooltip("每像素鼠标位移对应的角度")]
        public float lookSensitivity = 0.12f;

        [Header("负重")]
        [Tooltip("负重减速分母（方案未激活时的默认值）：越大惩罚越轻")]
        public float carryDivisor = 70f;
    }
}
