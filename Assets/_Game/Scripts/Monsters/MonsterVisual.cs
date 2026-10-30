using UnityEngine;

namespace ScavengeNightmare.Monsters
{
    /// <summary>
    /// 占位胶囊染色 + 前兆脉冲。客户端只读 SyncVar State，不参与任何判定。
    /// 正式模型/动画属于美术阶段（技术债 T3），这里只保证「玩家能一眼看出它在干什么」。
    /// </summary>
    public sealed class MonsterVisual : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        [SerializeField] private MonsterBrain brain;
        [SerializeField] private Renderer bodyRenderer;
        [SerializeField] private Transform body;

        private MaterialPropertyBlock block;
        private static readonly Color ChaseColor = new(0.85f, 0.12f, 0.12f, 1f);
        private static readonly Color InvestigateColor = new(0.92f, 0.78f, 0.2f, 1f);
        private static readonly Color AttackColor = new(1f, 0.25f, 0.1f, 1f);
        private static readonly Color SearchColor = new(0.85f, 0.5f, 0.15f, 1f);
        private static readonly Color StunnedColor = new(0.25f, 0.7f, 0.95f, 1f);
        private static readonly Color SpawnColor = new(0.35f, 0.35f, 0.38f, 1f);

        private void Awake()
        {
            if (brain == null)
            {
                brain = GetComponent<MonsterBrain>();
            }

            if (body == null)
            {
                body = transform.Find("Body");
            }

            if (bodyRenderer == null && body != null)
            {
                bodyRenderer = body.GetComponent<Renderer>();
            }

            block = new MaterialPropertyBlock();
        }

        private void Update()
        {
            if (brain == null || bodyRenderer == null)
            {
                return;
            }

            Color color = ResolveColor(brain.State);
            block.SetColor(BaseColorId, color);
            block.SetColor(ColorId, color);
            bodyRenderer.SetPropertyBlock(block);

            if (body != null)
            {
                // S4B-3：前兆（导购前摇 / 尖啸罐蓄力）用更快的脉冲，玩家能提前看出「要动了」。
                float pulse;
                if (brain.IsWindingUp)
                {
                    pulse = 1f + 0.09f * Mathf.Sin(Time.time * 24f);
                }
                else if (brain.State == MonsterState.Attack)
                {
                    pulse = 1f + 0.07f * Mathf.Sin(Time.time * 16f);
                }
                else
                {
                    pulse = 1f;
                }

                body.localScale = new Vector3(0.7f * pulse, 0.9f * pulse, 0.7f * pulse);
            }
        }

        private Color ResolveColor(MonsterState state)
        {
            switch (state)
            {
                case MonsterState.Chase:
                    return ChaseColor;
                case MonsterState.Investigate:
                    return InvestigateColor;
                case MonsterState.Attack:
                    return AttackColor;
                case MonsterState.Search:
                    return SearchColor;
                case MonsterState.Stunned:
                    return StunnedColor;
                case MonsterState.Patrol:
                    return brain.Profile != null ? brain.Profile.bodyColor : SpawnColor;
                default:
                    return SpawnColor;
            }
        }
    }
}
