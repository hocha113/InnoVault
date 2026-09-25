using InnoVault.Rigs2D.Data;
using InnoVault.Rigs2D.Runtime;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;

namespace InnoVault.Rigs2D.Solvers
{
    /// <summary>
    /// 多节 FABRIK 链：链根锚在首节父骨骼，链尖追目标，前向 / 后向各扫一遍保骨长；
    /// 可加一个中段最强的侧向弓量（<c>tension</c>）让长臂松垮弓出而不是绷成直线
    /// <br/>骨骼：<c>[节1 … 节n]</c>；参数：<c>iterations</c> 1、<c>reachFactor</c> 0.98、<c>tension</c> 0、<c>bowSide</c> 1、<c>bowPx</c> 16、
    /// <c>targetSolver</c> / <c>targetIndex</c>
    /// </summary>
    public sealed class FabrikChainSolver : Rig2DSolver, IRig2DTargetSource
    {
        private int iterations;
        private float reachFactor;
        private float paramTension;
        private float bowSide;
        private float bowPx;
        private IRig2DTargetSource targetSource;
        private int targetIndex;
        private Vector2[] points = [];
        private float[] lengths = [];
        private bool init;

        /// <summary>
        /// 链尖目标（世界）
        /// </summary>
        public Vector2 Target { get; set; }
        /// <summary>
        /// 目标源
        /// </summary>
        public IRig2DTargetSource TargetSource {
            get => targetSource;
            set => targetSource = value;
        }
        /// <summary>
        /// 目标源序号
        /// </summary>
        public int TargetIndex {
            get => targetIndex;
            set => targetIndex = value;
        }
        /// <summary>
        /// 弓量 0..1（NaN 用参数）：越低越绷直，越高中段越弓出
        /// </summary>
        public float Tension { get; set; } = float.NaN;
        /// <summary>
        /// 弓向 ±1（覆盖参数；0 用参数）
        /// </summary>
        public float BowSide { get; set; }
        /// <summary>
        /// 链尖位置（本帧）
        /// </summary>
        public Vector2 Tip { get; private set; }
        /// <summary>
        /// 链根位置
        /// </summary>
        public Vector2 Root { get; private set; }
        /// <summary>
        /// 全链触及
        /// </summary>
        public float MaxReach { get; private set; }
        /// <summary>
        /// 关节点（下标 0 = 根 … n = 尖）
        /// </summary>
        public ReadOnlySpan<Vector2> Points => points;

        /// <inheritdoc/>
        protected internal override int ChannelProperty(string prop, out bool spatial) {
            spatial = prop == "target";
            return prop switch {
                "target" => 0,
                "tension" => 1,
                _ => -1,
            };
        }

        /// <inheritdoc/>
        protected internal override void SetChannel(int property, Vector2 value) {
            switch (property) {
                case 0: Target = value; break;
                case 1: Tension = value.X; break;
            }
        }

        /// <inheritdoc/>
        protected override void Configure(Solver2DDef def) {
            iterations = Math.Max(def.GetInt("iterations", 1), 1);
            reachFactor = def.GetFloat("reachFactor", 0.98f);
            paramTension = def.GetFloat("tension", 0f);
            bowSide = def.GetFloat("bowSide", 1f) >= 0f ? 1f : -1f;
            bowPx = def.GetFloat("bowPx", 16f);
            targetIndex = def.GetInt("targetIndex", 0);
            if (points.Length != bones.Length + 1) {
                points = new Vector2[bones.Length + 1];
                lengths = new float[bones.Length];
                init = false;
            }
        }

        /// <inheritdoc/>
        protected internal override void PostBind() {
            int idx = Rig?.Definition?.SolverIndex(Name) ?? -1;
            if (idx >= 0) {
                IRig2DTargetSource src = ResolveTargetSource(Rig.Definition.Solvers[idx], out int i);
                if (src != null) {
                    targetSource = src;
                    targetIndex = i;
                }
            }
        }

        /// <inheritdoc/>
        public bool TryGetTarget(int index, out Vector2 target) {
            if (index >= 0 && index < points.Length && init) {
                target = points[index];
                return true;
            }
            target = default;
            return false;
        }

        private Vector2 ResolveTarget() {
            if (targetSource != null && targetSource.TryGetTarget(targetIndex, out Vector2 t)) {
                return t;
            }
            return Target;
        }

        /// <inheritdoc/>
        public override void Snap() {
            if (bones.Length == 0) {
                return;
            }
            Vector2 root = RestPosition(0);
            Vector2 target = ResolveTarget();
            Vector2 dir = target - root;
            if (dir.LengthSquared() < 0.001f) {
                dir = RestForward(0);
            }
            else {
                dir.Normalize();
            }
            points[0] = root;
            for (int i = 0; i < bones.Length; i++) {
                lengths[i] = RestLength(i);
                points[i + 1] = points[i] + dir * lengths[i];
            }
            init = true;
            WriteBones();
        }

        /// <inheritdoc/>
        public override void Step(float dt) {
            if (bones.Length == 0) {
                return;
            }
            if (!init) {
                Snap();
            }
            int n = bones.Length;
            Vector2 root = RestPosition(0);
            float total = 0f;
            for (int i = 0; i < n; i++) {
                lengths[i] = RestLength(i);
                total += lengths[i];
            }
            MaxReach = total;
            Vector2 target = ResolveTarget();
            float reach = total * reachFactor;
            Vector2 toT = target - root;
            if (toT.LengthSquared() > reach * reach) {
                target = root + Vector2.Normalize(toT) * reach;
            }
            float tension = float.IsNaN(Tension) ? paramTension : Tension;
            float side = BowSide != 0f ? Math.Sign(BowSide) : bowSide;
            //弓向是局部左右选择，骨架镜像时跟着翻
            float bow = tension * bowPx * Scale * side * MirrorSign;

            for (int it = 0; it < iterations; it++) {
                //后向：尖端钉在目标，逐节向根收
                points[n] = target;
                for (int i = n - 1; i >= 0; i--) {
                    Vector2 d = points[i] - points[i + 1];
                    Vector2 dir = d.LengthSquared() > 0.0001f ? Vector2.Normalize(d) : -B(i).Forward;
                    float bendFactor = MathF.Sin((i + 1) / (float)(n + 1) * MathHelper.Pi);
                    Vector2 perp = new Vector2(-dir.Y, dir.X) * (bendFactor * bow);
                    points[i] = points[i + 1] + dir * lengths[i] + perp;
                }
                //前向：根钉回锚点，逐节向尖推
                points[0] = root;
                for (int i = 1; i <= n; i++) {
                    Vector2 d = points[i] - points[i - 1];
                    Vector2 dir = d.LengthSquared() > 0.0001f ? Vector2.Normalize(d) : B(i - 1).Forward;
                    float bendFactor = MathF.Sin(i / (float)(n + 1) * MathHelper.Pi);
                    Vector2 perp = new Vector2(-dir.Y, dir.X) * (bendFactor * bow);
                    points[i] = points[i - 1] + dir * lengths[i - 1] + perp;
                }
            }
            WriteBones();
        }

        private void WriteBones() {
            int n = bones.Length;
            for (int i = 0; i < n; i++) {
                ref Bone2D b = ref B(i);
                Vector2 d = points[i + 1] - points[i];
                b.Pos = points[i];
                b.Dir = d.LengthSquared() > 0.0001f ? MathF.Atan2(d.Y, d.X) : b.Dir;
                b.Length = d.Length();
            }
            Root = points[0];
            Tip = points[n];
        }

        /// <inheritdoc/>
        public override void DebugDraw(SpriteBatch sb, Func<Vector2, Vector2> toScreen) {
            Texture2D px = Rig2DDebugDraw.Pixel;
            if (px == null) {
                return;
            }
            sb.Draw(px, toScreen(ResolveTarget()), new Rectangle(0, 0, 1, 1), Color.Violet, 0f, new Vector2(0.5f), 6f, SpriteEffects.None, 0f);
            Rig2DDebugDraw.Circle(sb, toScreen, Root, MaxReach * reachFactor, Color.Violet * 0.3f);
        }
    }
}
