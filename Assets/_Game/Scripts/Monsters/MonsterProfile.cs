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

        [Header("塑料导购（S4B-3）")]
        [Tooltip("玩家注视它的水平角度（度）：在 25° 内且 18m 内无遮挡就算被看着")]
        public float greeterWatchAngle = 25f;
        public float greeterWatchRange = 18f;
        [Tooltip("全员移开视线后的前摇（秒）")]
        public float greeterWindupSeconds = 0.35f;
        [Tooltip("前摇结束后的突进时长（秒），0.8s 内必须位移 >2m")]
        public float greeterDashSeconds = 0.8f;
        [Tooltip("突进撞墙后的僵直（秒）")]
        public float greeterWallStunSeconds = 1.5f;

        [Header("尖啸罐（S4B-3）")]
        [Tooltip("触发半径：玩家进入 6m 内，或全局噪音 ≥ 40")]
        public float screamPlayerRange = 6f;
        public float screamNoiseThreshold = 40f;
        [Tooltip("尖叫前摇（秒）")]
        public float screamWindupSeconds = 1.5f;
        [Tooltip("尖叫时追加的噪音（+45）")]
        public float screamNoiseAmount = 45f;
        [Tooltip("尖叫眩晕半径与时长（12m / 2s）")]
        public float screamStunRadius = 12f;
        public float screamStunSeconds = 2f;
        [Tooltip("前摇内被投掷物命中：引爆噪音 +15，无掉落")]
        public float screamPreemptNoise = 15f;
        public float screamPreemptRadius = 3.5f;
        [Tooltip("两次尖叫之间的冷却")]
        public float screamCooldownSeconds = 6f;

        [Header("外观（占位胶囊）")]
        public Color bodyColor = new(0.55f, 0.55f, 0.58f, 1f);
    }
}
