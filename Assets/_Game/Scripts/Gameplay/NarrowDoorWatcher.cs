using System.Collections.Generic;
using UnityEngine;

namespace ScavengeNightmare.Gameplay
{
    /// <summary>
    /// D6 窄门卡位：门框碰撞体负责物理阻挡，本组件只负责「卡住超过阈值就给反馈」。
    /// 判定依据是**位移**而不是速度——被抓物件在卡住时速度仍不为零（速度控制在拽它），
    /// 但位置不动，这才是「卡住」的真实表现。
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public sealed class NarrowDoorWatcher : MonoBehaviour
    {
        private sealed class Tracked
        {
            public Vector3 LastPosition;
            public float StuckSeconds;
            public bool Reported;
        }

        private readonly Dictionary<GrabbableProp, Tracked> tracked = new();
        private BoxCollider zone;

        private void Awake()
        {
            zone = GetComponent<BoxCollider>();
            zone.isTrigger = true;

            // D8「窄门恐惧」需要知道玩家是否站在门洞里：把这个区域的 AABB 登记到全局窄区表
            Traits.NarrowZones.Register(transform, zone.size * 0.5f);
        }

        private void OnDestroy()
        {
            Traits.NarrowZones.Unregister(transform);
        }

        private void OnTriggerStay(Collider other)
        {
            if (!FrictionEvents.Tuning.enableNarrowDoor || other.attachedRigidbody == null)
            {
                return;
            }

            GrabbableProp prop = other.attachedRigidbody.GetComponent<GrabbableProp>();
            if (prop == null)
            {
                return;
            }

            if (!tracked.TryGetValue(prop, out Tracked info))
            {
                info = new Tracked { LastPosition = prop.transform.position };
                tracked[prop] = info;
            }

            float moved = (prop.transform.position - info.LastPosition).magnitude;
            info.LastPosition = prop.transform.position;

            if (moved < 0.05f && prop.IsHeld)
            {
                info.StuckSeconds += Time.fixedDeltaTime;
                if (info.StuckSeconds >= FrictionEvents.Tuning.jamContactSeconds && !info.Reported)
                {
                    info.Reported = true;
                    FrictionEvents.Report("窄门卡位", $"{prop.name} 卡在门洞里，换个角度", 240f);
                }
            }
            else
            {
                info.StuckSeconds = 0f;
                info.Reported = false;
            }
        }

        private void OnTriggerExit(Collider other)
        {
            if (other.attachedRigidbody == null)
            {
                return;
            }

            GrabbableProp prop = other.attachedRigidbody.GetComponent<GrabbableProp>();
            if (prop != null)
            {
                tracked.Remove(prop);
            }
        }
    }
}
