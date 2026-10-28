using UnityEngine;

namespace ScavengeNightmare.Gameplay
{
    /// <summary>场景启动时把摩擦表接进全局，避免每个摩擦组件各自引用资产。</summary>
    public sealed class FrictionBootstrap : MonoBehaviour
    {
        [SerializeField] private FrictionTuning tuning;

        private void Awake()
        {
            FrictionEvents.Use(tuning);
        }
    }
}
