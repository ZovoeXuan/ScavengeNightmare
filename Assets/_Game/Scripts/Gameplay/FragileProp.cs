using System.Collections.Generic;
using UnityEngine;

namespace ScavengeNightmare.Gameplay
{
    /// <summary>
    /// D6 易碎：碰撞相对速度超过 breakSpeed 即破碎。
    /// 用「相对速度」而不是「碰撞冲量」判定，因为 OnCollisionEnter 里的 impulse 不可靠。
    /// </summary>
    [RequireComponent(typeof(GrabbableProp))]
    public sealed class FragileProp : MonoBehaviour
    {
        private GrabbableProp grabbable;
        private Renderer[] renderers;

        public bool Broken { get; private set; }

        /// <summary>当前价值：碎了归零，供后续结算读取（D13 接经济系统）。</summary>
        public int CurrentValue
        {
            get
            {
                if (Broken || grabbable == null || grabbable.Profile == null)
                {
                    return 0;
                }

                return grabbable.Profile.value;
            }
        }

        private void Awake()
        {
            grabbable = GetComponent<GrabbableProp>();
            renderers = GetComponentsInChildren<Renderer>(true);
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (Broken || !FrictionEvents.Tuning.enableFragile)
            {
                return;
            }

            PropPhysicsProfile profile = grabbable.Profile;
            if (profile == null || !profile.isFragile)
            {
                return;
            }

            if (collision.relativeVelocity.magnitude < profile.breakSpeed)
            {
                return;
            }

            Shatter(collision.relativeVelocity.magnitude);
        }

        public void Shatter(float impactSpeed = 0f)
        {
            if (Broken)
            {
                return;
            }

            Broken = true;

            foreach (Renderer r in renderers)
            {
                if (r != null)
                {
                    r.enabled = false;
                }
            }

            // 外壳冻结在原地，避免碎渣生成后又被惯性带着飞
            if (grabbable.Body != null)
            {
                grabbable.Body.linearVelocity = Vector3.zero;
                grabbable.Body.angularVelocity = Vector3.zero;
                grabbable.Body.isKinematic = true;
            }

            foreach (Collider c in GetComponentsInChildren<Collider>())
            {
                c.enabled = false;
            }

            SpawnFragments();

            FrictionEvents.Report("易碎", $"{name} 碎了（{impactSpeed:F1} m/s），价值归零", 520f);
        }

        private void SpawnFragments()
        {
            int count = Mathf.Max(0, FrictionEvents.Tuning.fragmentCount);
            for (int i = 0; i < count; i++)
            {
                GameObject shard = GameObject.CreatePrimitive(PrimitiveType.Cube);
                shard.name = $"{name}_shard_{i}";
                shard.transform.position = transform.position + Random.insideUnitSphere * 0.15f;
                shard.transform.localScale = Vector3.one * 0.09f;
                shard.transform.rotation = Random.rotation;

                Rigidbody body = shard.AddComponent<Rigidbody>();
                body.mass = 0.1f;
                body.linearVelocity = Random.onUnitSphere * 2.5f + Vector3.up * 1.5f;
                body.angularVelocity = Random.onUnitSphere * 6f;

                Renderer r = shard.GetComponent<Renderer>();
                if (r != null)
                {
                    // 沿用原物件颜色，让玩家一眼看出是刚才那件东西碎的
                    Material source = GetComponent<Renderer>()?.sharedMaterial;
                    if (source != null)
                    {
                        r.sharedMaterial = source;
                    }
                }

                Destroy(shard, FrictionEvents.Tuning.fragmentLifetime);
            }
        }
    }
}
