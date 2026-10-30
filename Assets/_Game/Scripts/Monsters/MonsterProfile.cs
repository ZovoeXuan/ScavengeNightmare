using UnityEngine;

namespace ScavengeNightmare.Monsters
{
    /// <summary>
    /// 单只怪的数值表（P0 §4.3）。改数值只改 asset，不改状态机。
    /// 听觉半径不在这里：它由全局噪音档位决定（4/10/16m），避免每只怪自己一套听觉口径。
    /// </summary>
    [CreateAssetMenu(menuName = "拾荒惊魂/怪物档", fileName = "MonsterProfile")]
    public sealed class MonsterProfile : ScriptableObject
    {
        [Header("身份")]
        public string id = "holloweye";
        public string displayName = "空眼";

        [Header("移动")]
        public float patrolSpeed = 0.9f;
        public float investigateSpeed = 1.0f;
        public float chaseSpeed = 3.2f;
        [Tooltip("到达目标点的判定距离")]
        public float arriveDistance = 0.6f;

        [Header("感知")]
        public float sightRange = 12f;
        [Tooltip("水平视锥总角度（度）")]
        public float sightAngle = 90f;

        [Header("攻击")]
        public float attackRange = 1.8f;
        [Tooltip("攻击前摇（秒）")]
        public float attackWindup = 0.8f;
        public int attackDamage = 50;
        public float attackCooldown = 2.5f;

        [Header("丢失 / 搜索")]
        [Tooltip("脱离视线后多久判定丢失（P0 口径 4s，不超过 6s）")]
        public float loseTargetSeconds = 4f;
        public float searchSeconds = 3f;

        [Header("克制")]
        [Tooltip("手电直射多少秒进入僵直；0 = 不吃手电")]
        public float flashlightStunSeconds = 1.5f;
        public float stunSeconds = 2f;

        [Header("外观（占位胶囊）")]
        public Color bodyColor = new(0.55f, 0.55f, 0.58f, 1f);
    }
}
