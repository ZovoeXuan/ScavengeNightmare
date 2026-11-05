using System.Collections.Generic;
using UnityEngine;

namespace ScavengeNightmare.Gameplay
{
    /// <summary>
    /// D6 窄门卡位 V2 升级版：
    /// 1) 修复多子碰撞体物件在同一物理帧多次触发 OnTriggerStay 导致 moved 被清零误判的 Bug；
    /// 2) 注册 NarrowZones 时乘上 lossyScale，保证缩放后的门洞范围准确。
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public sealed class NarrowDoorWatcher : MonoBehaviour
    {
        private sealed class Tracked
        {
            public Vector3 LastPosition;
            public float StuckSeconds;
            public bool Reported;
            public int LastFixedFrame;
        }

        private readonly Dictionary<GrabbableProp, Tracked> tracked = new();
        private BoxCollider zone;

        private void Awake()
        {
            zone = GetComponent<BoxCollider>();
            zone.isTrigger = true;
        }

        private void Start()
        {
            Vector3 s = transform.lossyScale;
            Vector3 halfExtent = new Vector3(
                Mathf.Abs(zone.size.x * s.x) * 0.5f,
                Mathf.Abs(zone.size.y * s.y) * 0.5f,
                Mathf.Abs(zone.size.z * s.z) * 0.5f);
            Traits.NarrowZones.Register(transform, halfExtent);
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

            int frame = Time.frameCount;
            if (!tracked.TryGetValue(prop, out Tracked info))
            {
                info = new Tracked
                {
                    LastPosition = prop.transform.position,
                    LastFixedFrame = frame
                };
                tracked[prop] = info;
                return;
            }

            if (info.LastFixedFrame == frame)
            {
                return;
            }

            info.LastFixedFrame = frame;

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
