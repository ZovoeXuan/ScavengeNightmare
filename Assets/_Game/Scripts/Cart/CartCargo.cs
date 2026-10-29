using System.Collections.Generic;
using System.Text;
using ScavengeNightmare.Gameplay;
using UnityEngine;

namespace ScavengeNightmare.Cart
{
    /// <summary>
    /// 推车承载（挂车物体上）：载重汇总、板上货物清单、滑落判定。
    ///
    /// 货物是**独立刚体真实躺在板上**，不是子物体。做成子物体的话测的是 Transform 跟随，
    /// 而不是物理稳定性——A4「满载推行 30m 不掉货」会变成一条永远通过的假断言。
    ///
    /// 但完整摩擦求解在 30Hz 网络物理下会抖，所以板对货物用「平台带动」近似：
    /// 把板本帧的水平位移直接叠加到货物上，竖直方向仍交给物理。
    /// 这是简化，不是真实摩擦——代价是急转弯时货物不会因惯性甩出（比真实更稳），
    /// 好处是结果可断言、可复现。已记入 Story 3 风险。
    /// </summary>
    public sealed class CartCargo : MonoBehaviour
    {
        private readonly List<GrabbableProp> items = new();
        private readonly Dictionary<GrabbableProp, Vector3> anchors = new();

        private CartProfile profile;
        private Transform deckTransform;
        private Vector3 lastDeckPosition;

        public int Count => items.Count;
        public float LoadKg { get; private set; }
        public IReadOnlyList<GrabbableProp> Items => items;

        public void Configure(CartProfile cartProfile, Transform deck)
        {
            profile = cartProfile;
            deckTransform = deck;

            if (deckTransform != null)
            {
                lastDeckPosition = deckTransform.position;
            }
        }

        /// <summary>服务端：装一件货上板。件数上限是硬闸门，载重超限只提示不禁止。</summary>
        public bool TryLoad(GrabbableProp prop, out string reason)
        {
            reason = string.Empty;

            if (prop == null)
            {
                reason = "null_prop";
                return false;
            }

            if (items.Contains(prop))
            {
                reason = "already_loaded";
                return false;
            }

            if (profile != null && items.Count >= profile.maxItems)
            {
                reason = "item_limit";
                return false;
            }

            items.Add(prop);
            Recalculate();
            return true;
        }

        public bool Unload(GrabbableProp prop)
        {
            if (prop == null || !items.Remove(prop))
            {
                return false;
            }

            anchors.Remove(prop);
            Recalculate();
            return true;
        }

        /// <summary>整车卸货（公秤提交前的清空）。</summary>
        public List<GrabbableProp> UnloadAll()
        {
            List<GrabbableProp> removed = new(items);
            items.Clear();
            anchors.Clear();
            Recalculate();
            return removed;
        }

        /// <summary>
        /// 服务端每帧：先按板位移带动货物，再做滑落判定。
        /// 顺序不能反——先判定再带动的话，货物会因为「还没跟上板」被误判成滑落。
        /// </summary>
        public void Tick(float deltaTime)
        {
            if (deckTransform == null || profile == null || items.Count == 0)
            {
                if (deckTransform != null)
                {
                    lastDeckPosition = deckTransform.position;
                }

                return;
            }

            float halfX = profile.deckLength * 0.5f;
            float halfZ = profile.deckWidth * 0.5f;
            float margin = profile.cargoFallMargin;

            for (int i = items.Count - 1; i >= 0; i--)
            {
                GrabbableProp prop = items[i];

                if (prop == null || prop.Body == null)
                {
                    items.RemoveAt(i);
                    continue;
                }

                Rigidbody body = prop.Body;
                Vector3 local = deckTransform.InverseTransformPoint(body.position);

                if (!anchors.TryGetValue(prop, out Vector3 anchor))
                {
                    anchor = new Vector3(local.x, local.y, local.z);
                    anchors[prop] = anchor;
                }

                // 水平软约束：把货物的水平位置锚定在装车点，竖直方向完全交给物理。
                //
                // 为什么必须这么做：纯靠摩擦求解时，60kg 重件与板面发生穿透修正，
                // 求解器会给它一个横向推力，推行一段后重件就被推出板外（实测 DROP 的
                // x 到了 0.81，板半长只有 0.65），连带把旁边的轻件一起顶下去。
                //
                // 代价是货物在板上不会因惯性滑动——比真实物理更稳，属于稳定性优先的取舍。
                // 因此「货物甩落」这条噪音来源在推车场景下不会自然发生，
                // 4B 若要它必须靠撞击/翻车的显式事件，不能指望摩擦把它甩出去。
                Vector3 wantLocal = new Vector3(anchor.x, local.y, anchor.z);
                Vector3 wantWorld = deckTransform.TransformPoint(wantLocal);
                body.MovePosition(new Vector3(wantWorld.x, body.position.y, wantWorld.z));

                // 判定只看竖直掉落：水平方向已由上面的锚点约束保证恒等于装车点，
                // 而 MovePosition 是延迟生效的，立刻重读 body.position 拿到的还是旧值——
                // 在车被瞬移的那一帧会读到一个几十米外的坐标，把好货全判成滑落。
                bool outside = local.y < -0.5f;

                if (outside)
                {
                    items.RemoveAt(i);
                    anchors.Remove(prop);
                    Debug.Log($"[S4A] GATE_CART_DROP id={(prop.Profile != null ? prop.Profile.id : "?")} mass={prop.Mass:F1} local=({local.x:F2},{local.z:F2})");
                }
            }

            Recalculate();
        }

        public string IdsJoined()
        {
            StringBuilder sb = new();
            for (int i = 0; i < items.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append(',');
                }

                sb.Append(items[i] != null && items[i].Profile != null ? items[i].Profile.id : "?");
            }

            return sb.ToString();
        }

        public bool IsOverloaded()
        {
            return profile != null && LoadKg > profile.overloadWarnKg;
        }

        public bool IsOverCapacity()
        {
            return profile != null && profile.IsOverCapacity(LoadKg);
        }

        private void Recalculate()
        {
            float sum = 0f;
            for (int i = items.Count - 1; i >= 0; i--)
            {
                if (items[i] == null)
                {
                    items.RemoveAt(i);
                    continue;
                }

                sum += items[i].Mass;
            }

            LoadKg = sum;
        }
    }
}
