using System.Collections.Generic;
using System.Text;
using ScavengeNightmare.Gameplay;
using UnityEngine;

namespace ScavengeNightmare.Cart
{
    /// <summary>
    /// 推车承载（挂车物体上）：载重汇总、板上货物清单、滑落判定、碰撞颠簸散落与智能空位排布。
    /// V2 升级：
    /// 1) 修复玩家直接从车板上抓走/挂腰带时 CartCargo.Tick 继续 MovePosition 导致刚体拉扯脱手的严重 Bug；
    /// 2) 实现基于区间空隙的 2D 板面排布（FindOpenDeckSlot），解决取走中间货物后新货叠放弹飞的问题；
    /// 3) 新增 ServerJoltCargo：空眼等怪物撞车或高速撞墙时触发颠簸脱锚（拥有打包胶带 packing_tape 可免疫）。
    /// </summary>
    public sealed class CartCargo : MonoBehaviour
    {
        private readonly List<GrabbableProp> items = new();
        private readonly Dictionary<GrabbableProp, Vector3> anchors = new();
        private readonly Dictionary<GrabbableProp, float> halfWidths = new();

        private CartProfile profile;
        private Transform deckTransform;

        public int Count => items.Count;
        public float LoadKg { get; private set; }
        public IReadOnlyList<GrabbableProp> Items => items;

        public void Configure(CartProfile cartProfile, Transform deck)
        {
            profile = cartProfile;
            deckTransform = deck;
        }

        public bool Contains(GrabbableProp prop)
        {
            return prop != null && items.Contains(prop);
        }

        public void RegisterAnchor(GrabbableProp prop, Vector3 local, float settleSeconds = 0.7f)
        {
            if (prop == null)
            {
                return;
            }

            anchors[prop] = local;
        }

        public void RegisterHalfWidth(GrabbableProp prop, float halfWidth)
        {
            if (prop != null)
            {
                halfWidths[prop] = Mathf.Max(0.1f, halfWidth);
            }
        }

        /// <summary>
        /// V2 智能板面空位查找：避免卸下中间货物后新货物直接叠在旧货物锚点上导致物理穿模弹飞。
        /// </summary>
        public bool TryFindOpenDeckPosition(float myHalfWidth, float hullTopY, out Vector3 localAnchor)
        {
            localAnchor = Vector3.zero;
            if (profile == null)
            {
                return false;
            }

            float halfLen = profile.deckLength * 0.5f;
            float cursor = -halfLen;

            // 先按一维顺序扫描（保持与 S4A 用例完全一致的起始坐标），若发生重叠则向后顺延寻找空档
            for (int i = 0; i < items.Count; i++)
            {
                GrabbableProp existing = items[i];
                if (existing == null)
                {
                    continue;
                }

                float w = halfWidths.TryGetValue(existing, out float hw) ? hw : 0.2f;
                cursor += w * 2f + 0.04f;
            }

            float candidateX = cursor + myHalfWidth;
            if (OverlapsExisting(candidateX, myHalfWidth))
            {
                // 从板头到板尾步进搜索无重叠区间
                bool found = false;
                for (float x = -halfLen + myHalfWidth; x <= halfLen - myHalfWidth + 0.001f; x += 0.05f)
                {
                    if (!OverlapsExisting(x, myHalfWidth))
                    {
                        candidateX = x;
                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    return false;
                }
            }

            if (candidateX + myHalfWidth > halfLen + 0.001f)
            {
                return false;
            }

            localAnchor = new Vector3(candidateX, hullTopY + 0.06f, 0f);
            return true;
        }

        private bool OverlapsExisting(float candidateX, float myHalfWidth)
        {
            foreach (GrabbableProp existing in items)
            {
                if (existing == null || !anchors.TryGetValue(existing, out Vector3 anchor))
                {
                    continue;
                }

                float otherHalf = halfWidths.TryGetValue(existing, out float hw) ? hw : 0.2f;
                float minDist = myHalfWidth + otherHalf + 0.02f;
                if (Mathf.Abs(candidateX - anchor.x) < minDist)
                {
                    return true;
                }
            }

            return false;
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
            prop.SetOnCart(true);
            Recalculate();
            return true;
        }

        public bool Unload(GrabbableProp prop)
        {
            if (prop == null || !items.Remove(prop))
            {
                return false;
            }

            prop.SetOnCart(false);
            anchors.Remove(prop);
            halfWidths.Remove(prop);
            Recalculate();
            return true;
        }

        /// <summary>整车卸货（公秤提交前的清空）。</summary>
        public List<GrabbableProp> UnloadAll()
        {
            List<GrabbableProp> removed = new(items);
            foreach (GrabbableProp prop in removed)
            {
                if (prop != null)
                {
                    prop.SetOnCart(false);
                }
            }

            items.Clear();
            anchors.Clear();
            halfWidths.Clear();
            Recalculate();
            return removed;
        }

        /// <summary>
        /// V2 新增：当推车遭遇怪物猛烈撞击（如空眼冲撞）时触发货物颠簸。
        /// 若队伍已购买「打包胶带」(CartSlipScale >= 1.5f)，货物被牢牢固定不会颠落；
        /// 否则最轻的一件货物会被震落车板并产生噪音。
        /// </summary>
        public GrabbableProp ServerJoltCargo(Vector3 hitDirection, float impulse = 3.5f)
        {
            if (items.Count == 0)
            {
                return null;
            }

            if (ScavengeNightmare.Economy.EquippedEffects.CartSlipScale >= 1.5f)
            {
                return null;
            }

            GrabbableProp lightest = null;
            float minMass = float.MaxValue;

            for (int i = 0; i < items.Count; i++)
            {
                GrabbableProp candidate = items[i];
                if (candidate != null && candidate.Mass < minMass)
                {
                    minMass = candidate.Mass;
                    lightest = candidate;
                }
            }

            if (lightest == null)
            {
                return null;
            }

            Unload(lightest);
            if (lightest.Body != null)
            {
                Vector3 ejectDir = (hitDirection.sqrMagnitude > 0.01f ? hitDirection.normalized : Vector3.right)
                                 + Vector3.up * 0.65f;
                lightest.Body.WakeUp();
                lightest.Body.linearVelocity = ejectDir.normalized * Mathf.Clamp(impulse, 1.5f, 4.5f);
            }

            Debug.Log($"[S4A] GATE_CART_DROP id={(lightest.Profile != null ? lightest.Profile.id : "?")} mass={lightest.Mass:F1} reason=monster_jolt");
            return lightest;
        }

        /// <summary>
        /// 服务端每帧：先检查是否有货物被玩家抓走，再按板位移移动货物，最后做滑落判定。
        /// </summary>
        public void Tick(float deltaTime)
        {
            if (deckTransform == null || profile == null || items.Count == 0)
            {
                return;
            }

            for (int i = items.Count - 1; i >= 0; i--)
            {
                GrabbableProp prop = items[i];

                if (prop == null || prop.Body == null)
                {
                    items.RemoveAt(i);
                    continue;
                }

                // 修复 BUG-CART-01：如果玩家用手或工具带把车上的货物拿走了，立即解除车板锚定
                if (prop.IsHeld)
                {
                    prop.SetOnCart(false);
                    items.RemoveAt(i);
                    anchors.Remove(prop);
                    halfWidths.Remove(prop);
                    continue;
                }

                Rigidbody body = prop.Body;
                Vector3 local = deckTransform.InverseTransformPoint(body.position);

                if (!anchors.TryGetValue(prop, out Vector3 anchor))
                {
                    anchor = new Vector3(local.x, local.y, local.z);
                    anchors[prop] = anchor;
                }

                Vector3 wantLocal = new Vector3(anchor.x, local.y, anchor.z);
                Vector3 wantWorld = deckTransform.TransformPoint(wantLocal);

                float stiffness = profile.cargoAnchorStiffness
                    * ScavengeNightmare.Economy.EquippedEffects.CartSlipScale;
                float t = Mathf.Clamp01(stiffness * deltaTime);

                Vector3 current = body.position;
                Vector3 planar = new(
                    Mathf.Lerp(current.x, wantWorld.x, t),
                    current.y,
                    Mathf.Lerp(current.z, wantWorld.z, t));

                body.MovePosition(planar);

                bool outside = local.y < -0.5f;

                if (outside)
                {
                    prop.SetOnCart(false);
                    items.RemoveAt(i);
                    anchors.Remove(prop);
                    halfWidths.Remove(prop);
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
