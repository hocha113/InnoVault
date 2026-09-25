using InnoVault.Rigs2D.Data;
using InnoVault.Rigs2D.Runtime;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;

namespace InnoVault.Rigs2D.Solvers
{
    /// <summary>
    /// 体态：治"弯腰够东西"。先按链的当前解（通道 / 瞄准链）铺好，逐骨把世界前倾角钳进限位
    /// （朝向系、自竖直向上量，正 = 朝面向一侧倾）；钳掉的前倾会让 <c>keep</c> 骨（肩）离开原位，于是先由下盘补：
    /// 链首（盆骨）按这段差下沉 / 前移，受各腿可达包络约束（腿 IK 目标到髋的距离留在 [legMin, legMax] × 腿长内，已越界的腿不许更糟）；
    /// 补不上的部分按 <c>giveBack</c> 还给脊柱（0 = 硬限位，剩下交给手臂去够）。可选平衡钳制：盆骨水平位置留在两脚之间 ± 余量。
    /// 本轮已被前面的求解器（瞄准链）解过的骨从那份解续写
    /// <br/>声明顺序：瞄准链 → 体态 → 武器握持 → 臂 IK → 腿 IK
    /// <br/>骨骼：<c>[盆骨, …, 头]</c>，父在前
    /// <br/>参数：<c>limits</c> [{ <c>bone</c>, <c>maxDeg</c> / <c>max</c>, <c>minDeg</c> / <c>min</c> }]、<c>keep</c> 骨名（缺省不补偿）、
    /// <c>legs</c> [两骨 IK 求解器名]、<c>legMin</c> 0.55、<c>legMax</c> 0.985、<c>compensate</c> 1、<c>giveBack</c> 0、
    /// <c>balance</c> false、<c>balanceMargin</c> 24、<c>weight</c> 1
    /// <br/>通道属性：<c>weight</c>、<c>compensate</c>、<c>giveBack</c>
    /// </summary>
    public sealed class PostureSolver : Rig2DSolver
    {
        private struct Limit
        {
            public int Chain;
            public float Min;
            public float Max;
        }

        private Limit[] limits = [];
        private int keepBone = -1;
        private int keepAncestor = -1;
        private string[] legNames = [];
        private TwoBoneIKSolver[] legs = [];
        private float legMin;
        private float legMax;
        private bool balance;
        private float balanceMargin;
        private float[] extraWorld = [];
        private float[] lean = [];
        private Vector2[] legGoal = [];
        private float[] legLength = [];
        private float[] legStart = [];
        private readonly Rig2DChainLayout chain = new();

        /// <summary>
        /// 整份修正的权重（0 = 不修，逐帧可改）
        /// </summary>
        public float Weight { get; set; } = 1f;
        /// <summary>
        /// 交给下盘补的比例上限
        /// </summary>
        public float Compensate { get; set; } = 1f;
        /// <summary>
        /// 下盘补不上的部分还给脊柱的比例（0 = 硬限位）
        /// </summary>
        public float GiveBack { get; set; }
        /// <summary>
        /// 本帧下盘实际补上的比例（0~1）
        /// </summary>
        public float Compensation { get; private set; }
        /// <summary>
        /// 本帧链首位移（世界）
        /// </summary>
        public Vector2 PelvisOffset { get; private set; }
        /// <summary>
        /// 本帧钳掉的前倾总量（弧度）
        /// </summary>
        public float Excess { get; private set; }
        /// <summary>
        /// 链内第 i 节本帧最终的前倾角（弧度，朝向系）
        /// </summary>
        public float Lean(int chainIndex) => (uint)chainIndex < (uint)lean.Length ? lean[chainIndex] : 0f;

        /// <inheritdoc/>
        protected override void Configure(Solver2DDef def) {
            int n = bones.Length;
            List<Limit> list = [];
            if (def.Params?["limits"] is JArray arr) {
                foreach (JToken t in arr) {
                    if (t is not JObject o) {
                        continue;
                    }
                    string name = (string)o["bone"];
                    int bi = string.IsNullOrEmpty(name) ? -1 : Rig?.Definition?.BoneIndex(name) ?? -1;
                    int ci = Array.IndexOf(bones, bi);
                    if (ci < 0) {
                        Rig2DPlatform.LogError($"[Rig2D:{Rig?.Name}/{Name}]", $"posture limit bone '{name}' is not in the chain");
                        continue;
                    }
                    float max = Angle(o, "max", MathHelper.Pi);
                    float min = Angle(o, "min", -MathHelper.Pi);
                    list.Add(new Limit { Chain = ci, Min = Math.Min(min, max), Max = Math.Max(min, max) });
                }
            }
            list.Sort((a, b) => a.Chain.CompareTo(b.Chain));
            limits = [.. list];
            keepBone = def.GetBone(Rig?.Definition, "keep");
            keepAncestor = -1;
            if (keepBone >= 0 && Rig?.Definition != null) {
                //keep 骨挂在链内某节下面（可隔几代）：记下那一节，取点前先传播它的后代
                int p = Rig.Definition.Bones[keepBone].ParentIndex;
                bool direct = Array.IndexOf(bones, p) >= 0;
                while (!direct && p >= 0) {
                    int ci = Array.IndexOf(bones, p);
                    if (ci >= 0) {
                        keepAncestor = ci;
                        break;
                    }
                    p = Rig.Definition.Bones[p].ParentIndex;
                }
            }
            legNames = def.GetStringArray("legs");
            legMin = def.GetFloat("legMin", 0.55f);
            legMax = def.GetFloat("legMax", 0.985f);
            balance = def.GetBool("balance", false);
            balanceMargin = def.GetFloat("balanceMargin", 24f);
            Weight = def.GetFloat("weight", 1f);
            Compensate = MathHelper.Clamp(def.GetFloat("compensate", 1f), 0f, 1f);
            GiveBack = MathHelper.Clamp(def.GetFloat("giveBack", 0f), 0f, 1f);
            extraWorld = new float[n];
            lean = new float[n];
            chain.Bind(Rig, bones);
        }

        private static float Angle(JObject o, string key, float fallback) {
            JToken deg = o[key + "Deg"];
            if (deg != null && deg.Type is JTokenType.Float or JTokenType.Integer) {
                return MathHelper.ToRadians((float)deg);
            }
            JToken rad = o[key];
            return rad != null && rad.Type is JTokenType.Float or JTokenType.Integer ? (float)rad : fallback;
        }

        /// <inheritdoc/>
        protected internal override void PostBind() {
            List<TwoBoneIKSolver> found = [];
            foreach (string name in legNames) {
                if (Rig.Solver(name) is TwoBoneIKSolver leg) {
                    found.Add(leg);
                }
                else {
                    Rig2DPlatform.LogError($"[Rig2D:{Rig.Name}/{Name}]", $"posture leg '{name}' is not a TwoBoneIK solver");
                }
            }
            legs = [.. found];
            legGoal = new Vector2[legs.Length];
            legLength = new float[legs.Length];
            legStart = new float[legs.Length];
        }

        /// <inheritdoc/>
        protected internal override int ChannelProperty(string prop, out bool spatial) {
            spatial = false;
            return prop switch {
                "weight" => 0,
                "compensate" => 1,
                "giveBack" => 2,
                _ => -1,
            };
        }

        /// <inheritdoc/>
        protected internal override void SetChannel(int property, Vector2 value) {
            switch (property) {
                case 0:
                    Weight = value.X;
                    break;
                case 1:
                    Compensate = MathHelper.Clamp(value.X, 0f, 1f);
                    break;
                case 2:
                    GiveBack = MathHelper.Clamp(value.X, 0f, 1f);
                    break;
            }
        }

        /// <inheritdoc/>
        public override void Snap() => Solve();

        /// <inheritdoc/>
        public override void Step(float dt) => Solve();

        private void Solve() {
            int n = bones.Length;
            if (n == 0) {
                return;
            }
            chain.Capture(Rig);
            Array.Clear(extraWorld);
            float sign = MirrorSign;
            Vector2 keep0 = KeepPoint();

            //逐骨钳前倾（按链序；前一节的修正后面各节继承，再看是否还越界）
            float excess = 0f;
            for (int i = 0; i < limits.Length; i++) {
                ref Limit lim = ref limits[i];
                if (i > 0) {
                    chain.Lay(Rig, extraWorld, Vector2.Zero);
                }
                float l = Rig2DChainLayout.Lean(B(lim.Chain).Dir, sign);
                float c = MathHelper.Clamp(l, lim.Min, lim.Max);
                if (c != l) {
                    extraWorld[lim.Chain] += (c - l) * sign;
                    excess += MathF.Abs(l - c);
                }
            }
            Excess = excess;

            Vector2 offset = Vector2.Zero;
            float alpha = 0f;
            if (excess > 0f) {
                chain.Lay(Rig, extraWorld, Vector2.Zero);
                if (keepBone >= 0 && Compensate > 0f) {
                    Vector2 d = keep0 - KeepPoint();
                    if (d.LengthSquared() > 0.0001f) {
                        alpha = Feasible(d, Compensate);
                        offset = d * alpha;
                    }
                }
                if (GiveBack > 0f) {
                    float back = 1f - GiveBack * (1f - alpha);
                    for (int k = 0; k < n; k++) {
                        extraWorld[k] *= back;
                    }
                }
            }
            if (balance && legs.Length > 0) {
                GatherLegs();
                float lo = float.MaxValue, hi = float.MinValue;
                for (int i = 0; i < legs.Length; i++) {
                    lo = Math.Min(lo, legGoal[i].X);
                    hi = Math.Max(hi, legGoal[i].X);
                }
                float margin = balanceMargin * Scale;
                float px = chain.Origin.X + offset.X;
                offset.X += MathHelper.Clamp(px, lo - margin, hi + margin) - px;
            }
            float w = MathHelper.Clamp(Weight, 0f, 1f);
            if (w < 1f) {
                for (int k = 0; k < n; k++) {
                    extraWorld[k] *= w;
                }
                offset *= w;
            }
            chain.Lay(Rig, extraWorld, offset);
            Compensation = alpha;
            PelvisOffset = offset;
            for (int k = 0; k < n; k++) {
                lean[k] = bones[k] >= 0 ? Rig2DChainLayout.Lean(B(k).Dir, sign) : 0f;
            }
        }

        private Vector2 KeepPoint() {
            if (keepBone < 0) {
                return Vector2.Zero;
            }
            if (keepAncestor >= 0 && bones[keepAncestor] >= 0) {
                Rig.PropagateDescendants(bones[keepAncestor]);
            }
            return Rig.RestPosition(keepBone);
        }

        private void GatherLegs() {
            for (int i = 0; i < legs.Length; i++) {
                TwoBoneIKSolver leg = legs[i];
                Rig.PushSolverChannels(leg);
                legGoal[i] = leg.TargetSource != null && leg.TargetSource.TryGetTarget(leg.TargetIndex, out Vector2 t) ? t : leg.Target;
                ReadOnlySpan<int> lb = leg.DrivenBones;
                legLength[i] = lb.Length >= 2 ? Rig.RestLength(lb[0]) + Rig.RestLength(lb[1]) : 0f;
            }
        }

        //沿位移 d 找下盘能走到的最大比例：各腿到脚的距离留在包络里（已越界的腿以当前距离为界，不许更糟）
        private float Feasible(Vector2 d, float maxAlpha) {
            if (legs.Length == 0) {
                return maxAlpha;
            }
            GatherLegs();
            for (int i = 0; i < legs.Length; i++) {
                legStart[i] = LegDistance(i, Vector2.Zero);
            }
            if (Fits(d * maxAlpha)) {
                return maxAlpha;
            }
            float lo = 0f, hi = maxAlpha;
            for (int it = 0; it < 12; it++) {
                float mid = (lo + hi) * 0.5f;
                if (Fits(d * mid)) {
                    lo = mid;
                }
                else {
                    hi = mid;
                }
            }
            return lo;
        }

        private bool Fits(Vector2 offset) {
            for (int i = 0; i < legs.Length; i++) {
                if (legLength[i] <= 0f) {
                    continue;
                }
                float dist = LegDistance(i, offset);
                float hi = Math.Max(legMax * legLength[i], legStart[i]);
                float lo = Math.Min(legMin * legLength[i], legStart[i]);
                if (dist > hi + 0.01f || dist < lo - 0.01f) {
                    return false;
                }
            }
            return true;
        }

        private float LegDistance(int i, Vector2 offset) {
            ReadOnlySpan<int> lb = legs[i].DrivenBones;
            if (lb.Length == 0) {
                return 0f;
            }
            chain.Lay(Rig, extraWorld, offset);
            return Vector2.Distance(legGoal[i], Rig.RestPosition(lb[0]));
        }

        /// <inheritdoc/>
        public override void DebugDraw(SpriteBatch sb, Func<Vector2, Vector2> toScreen) {
            if (PelvisOffset == Vector2.Zero || bones.Length == 0 || bones[0] < 0) {
                return;
            }
            Texture2D px = Rig2DDebugDraw.Pixel;
            if (px == null) {
                return;
            }
            Vector2 a = toScreen(chain.Origin);
            Vector2 b = toScreen(B(0).Pos);
            Vector2 d = b - a;
            sb.Draw(px, a, new Rectangle(0, 0, 1, 1), Color.Gold, MathF.Atan2(d.Y, d.X), new Vector2(0f, 0.5f), new Vector2(d.Length(), 2f), SpriteEffects.None, 0f);
        }
    }
}
