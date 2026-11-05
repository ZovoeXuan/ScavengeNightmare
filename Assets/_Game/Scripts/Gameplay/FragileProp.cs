using ScavengeNightmare.Audio;
using UnityEngine;

namespace ScavengeNightmare.Gameplay
{
    /// <summary>
    /// D6 易碎 V2 升级版：
    /// 修复破碎生成的纯视觉碎块带有激活 BoxCollider 导致车板内相邻易碎件连锁碎裂或卡住车轮的 Bug。
    /// </summary>
    [RequireComponent(typeof(GrabbableProp))]
    public sealed class FragileProp : MonoBehaviour
    {
        private GrabbableProp grabbable;
        private NetworkedProp networked;
        private Renderer[] renderers;

        public bool Broken { get; private set; }

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
            networked = GetComponent<NetworkedProp>();
            renderers = GetComponentsInChildren<Renderer>(true);
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (Broken || !FrictionEvents.Tuning.enableFragile)
            {
                return;
            }

            if (networked != null && !networked.isServer)
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

            if (networked != null)
            {
                networked.ShatterAuthoritative(collision.relativeVelocity.magnitude);
            }
            else
            {
                ApplyBreak(collision.relativeVelocity.magnitude);
            }
        }

        public void Shatter(float impactSpeed = 0f)
        {
            ApplyBreak(impactSpeed);
        }

        public void ApplyBreak(float impactSpeed = 0f)
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

            AudioBus.Play(AudioCue.GlassBreak, transform.position, 1f, $"speed={impactSpeed:F1}");
            FrictionEvents.Report("易碎", $"{name} 碎了 ({impactSpeed:F1} m/s)，价值归零", 520f);
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

                Collider shardCol = shard.GetComponent<Collider>();
                if (shardCol != null)
                {
                    shardCol.enabled = false;
                }

                Rigidbody body = shard.AddComponent<Rigidbody>();
                body.mass = 0.05f;
                body.linearVelocity = Random.onUnitSphere * 2.5f + Vector3.up * 1.5f;
                body.angularVelocity = Random.onUnitSphere * 6f;

                Renderer r = shard.GetComponent<Renderer>();
                if (r != null)
                {
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
