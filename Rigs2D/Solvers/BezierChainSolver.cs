using InnoVault.Rigs2D.Data;
using InnoVault.Rigs2D.Runtime;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;

namespace InnoVault.Rigs2D.Solvers
{
    /// <summary>
    /// 贝塞尔路径链：从锚（首节静息近端）到 <see cref="Target"/> 沿一条贝塞尔曲线把 N 节铺开——
    /// 起端切线取首节静息轴向、末端切线取 <see cref="TargetDir"/>，两端的控制柄长度决定曲线离开 / 抵达时的鼓胀；
    /// 关节可按参数等分或按弧长等分，并可对曲线采样点做逐帧软跟随（拖着走的尾巴）
    /// <br/>与 <see cref="HangChainSolver"/> 的分工：悬链是"两端受控、中间只管垂"的重力形；本求解器是"两端切线受控"的路径形，
    /// 尾巴甩到某处、触手抵达目标时的进入角都由消费方定
    /// <br/><b>节长不守恒</b>：各节 <c>Length</c> 写实际弦长，件用 <c>Stretch: None</c> 居中挂在关节上，或用 <c>Axis</c> 拉伸铺满
    /// <br/>骨骼：<c>[seg1 … segN]</c>（N 节 = N + 1 关节，关节 N 落在 Target）
    /// <br/>参数：<c>handleA</c> 120（起端控制柄长，像素，Scale 为 1）、<c>handleB</c> 0（末端控制柄长；0 或无 <see cref="TargetDir"/> 时退化为二次曲线，单控制点 = 锚 + 起切线 × handleA）、
    /// <c>spacing</c> "param"（<c>param</c> 参数等分 / <c>arc</c> 弧长等分）、<c>arcSamples</c> 32（弧长表采样数）、
    /// <c>followRate</c> 1（关节向曲线采样点的逐帧追近比例，1 = 瞬时）、
    /// <c>followSpace</c> "world"（软跟随的参照系：<c>world</c> 关节在世界系里追曲线，宿主平移会拖出整条链的滞后；
    /// <c>anchor</c> 先把上一帧的关节随锚点平移再追，只有形状滞后、不随宿主速度拖尾——高速飞行体的尾巴用它）、
    /// <c>targetSolver</c> / <c>targetIndex</c>
    /// </summary>
    public sealed class BezierChainSolver : Rig2DSolver, IRig2DTargetSource
    {
        private float handleA;
        private float handleB;
        private bool arcSpacing;
        private int arcSamples;
        private float paramFollowRate;
        private bool paramAnchorFollow;
        private IRig2DTargetSource targetSource;
        private int targetIndex;
        private Vector2[] joints = [];
        private Vector2[] curve = [];
        private float[] arcTable = [];
        private bool valid;
        private bool inited;
        private Vector2 lastMount;
        private bool hasLastMount;

        /// <summary>
        /// 链尖目标（世界）；有目标源时被目标源覆盖
        /// </summary>
        public Vector2 Target { get; set; }
        /// <summary>
        /// 末端切线（单位向量，曲线抵达目标时的行进方向）；<see langword="null"/> 时退化为二次曲线
        /// </summary>
        public Vector2? TargetDir { get; set; }
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
        /// 软跟随比例覆盖（NaN 用参数）：1 关节贴死曲线，越小拖得越慢
        /// </summary>
        public float FollowRate { get; set; } = float.NaN;
        /// <summary>
        /// 起端控制柄长覆盖（NaN 用参数，像素，Scale 为 1）
        /// </summary>
        public float HandleA { get; set; } = float.NaN;
        /// <summary>
        /// 末端控制柄长覆盖（NaN 用参数）
        /// </summary>
        public float HandleB { get; set; } = float.NaN;
        /// <summary>
        /// 软跟随参照系覆盖（<see langword="null"/> 用参数 <c>followSpace</c>）：真 = 锚点系（关节先随锚点平移再追曲线），假 = 世界系
        /// </summary>
        public bool? AnchorFollow { get; set; }

        /// <summary>
        /// 锚位置（本帧）
        /// </summary>
        public Vector2 Mount { get; private set; }
        /// <summary>
        /// 链尖（跟随后的末关节；<c>followRate</c> 为 1 时即 Target）
        /// </summary>
        public Vector2 Tip => joints.Length > 0 ? joints[^1] : Mount;
        /// <summary>
        /// 本帧曲线弧长（近似）
        /// </summary>
        public float CurveLength { get; private set; }
        /// <summary>
        /// 关节数（节数 + 1）
        /// </summary>
        public int JointCount => joints.Length;
        /// <summary>
        /// 关节点（下标 0 = 锚 … N = 尖）
        /// </summary>
        public ReadOnlySpan<Vector2> Points => joints;

        /// <summary>
        /// 第 j 个关节位置，越界返回锚
        /// </summary>
        public Vector2 Joint(int j) => j >= 0 && j < joints.Length ? joints[j] : Mount;

        /// <inheritdoc/>
        protected internal override int ChannelProperty(string prop, out bool spatial) {
            spatial = prop == "target";
            return prop switch {
                "target" => 0,
                "targetDir" => 1,
                "followRate" => 2,
                "handleA" => 3,
                "handleB" => 4,
                _ => -1,
            };
        }

        /// <inheritdoc/>
        protected internal override void SetChannel(int property, Vector2 value) {
            switch (property) {
                case 0: Target = value; break;
                case 1: TargetDir = value; break;
                case 2: FollowRate = value.X; break;
                case 3: HandleA = value.X; break;
                case 4: HandleB = value.X; break;
            }
        }

        /// <inheritdoc/>
        protected override void Configure(Solver2DDef def) {
            int n = bones.Length;
            valid = n >= 1;
            for (int i = 0; i < n && valid; i++) {
                valid = bones[i] >= 0;
            }
            if (!valid) {
                Rig2DPlatform.LogError($"[Rig2D:{Rig?.Name}/{Name}]", "BezierChain needs at least one bone [seg1 … segN]");
                return;
            }
            handleA = def.GetFloat("handleA", 120f);
            handleB = def.GetFloat("handleB", 0f);
            arcSpacing = def.GetString("spacing", "param").ToLowerInvariant() == "arc";
            arcSamples = Math.Max(def.GetInt("arcSamples", 32), 4);
            paramFollowRate = MathHelper.Clamp(def.GetFloat("followRate", 1f), 0f, 1f);
            paramAnchorFollow = def.GetString("followSpace", "world").ToLowerInvariant() == "anchor";
            targetIndex = def.GetInt("targetIndex", 0);
            if (joints.Length != n + 1) {
                joints = new Vector2[n + 1];
                curve = new Vector2[n + 1];
                inited = false;
            }
            if (arcTable.Length != arcSamples + 1) {
                arcTable = new float[arcSamples + 1];
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
            if (inited && index >= 0 && index < joints.Length) {
                target = joints[index];
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
            if (!valid) {
                return;
            }
            SampleCurve();
            Array.Copy(curve, joints, curve.Length);
            inited = true;
            lastMount = Mount;
            hasLastMount = true;
            WriteBones();
        }

        /// <summary>
        /// 停用期间这些骨被别的求解器（例如跟随链）接管过，旧关节早已过期：从骨骼当前位姿重新播种，软跟随从真实姿态起步
        /// </summary>
        protected internal override void OnEnabled() {
            if (!valid || !inited) {
                return;
            }
            int n = bones.Length;
            for (int i = 0; i < n; i++) {
                joints[i] = B(i).Pos;
            }
            joints[n] = B(n - 1).Tip;
            hasLastMount = false;
        }

        /// <inheritdoc/>
        public override void Step(float dt) {
            if (!valid) {
                return;
            }
            if (!inited) {
                Snap();
                return;
            }
            SampleCurve();
            float rate = float.IsNaN(FollowRate) ? paramFollowRate : MathHelper.Clamp(FollowRate, 0f, 1f);
            if (rate >= 1f) {
                Array.Copy(curve, joints, curve.Length);
            }
            else {
                //锚点系跟随：先让上一帧的关节跟着锚点整体平移，追近只吃形状差，宿主平移不产生拖尾
                bool anchorFollow = AnchorFollow ?? paramAnchorFollow;
                if (anchorFollow && hasLastMount) {
                    Vector2 delta = Mount - lastMount;
                    if (delta != Vector2.Zero) {
                        for (int j = 1; j < joints.Length; j++) {
                            joints[j] += delta;
                        }
                    }
                }
                float r = Spring2D.RateForDt(rate, dt);
                joints[0] = curve[0];
                for (int j = 1; j < joints.Length; j++) {
                    joints[j] = Vector2.Lerp(joints[j], curve[j], r);
                }
            }
            lastMount = Mount;
            hasLastMount = true;
            WriteBones();
        }

        //==================== 曲线采样 ====================

        private void SampleCurve() {
            int n = bones.Length;
            Mount = RestPosition(0);
            Vector2 target = ResolveTarget();
            Vector2 tanA = RestForward(0);
            float scale = Math.Max(Scale, 0.001f);
            float hA = (float.IsNaN(HandleA) ? handleA : HandleA) * scale;
            float hB = (float.IsNaN(HandleB) ? handleB : HandleB) * scale;

            Vector2 p0 = Mount;
            Vector2 p1 = Mount + tanA * hA;
            Vector2 p3 = target;
            bool cubic = TargetDir.HasValue && hB != 0f && TargetDir.Value.LengthSquared() > 0.0001f;
            Vector2 p2 = cubic ? target - Vector2.Normalize(TargetDir.Value) * hB : target;

            if (!arcSpacing) {
                for (int k = 0; k <= n; k++) {
                    curve[k] = Evaluate(p0, p1, p2, p3, cubic, k / (float)n);
                }
                CurveLength = ApproxLength(p0, p1, p2, p3, cubic);
                return;
            }

            //弧长表：等距参数采样累计弦长，再按等弧长反查参数
            int m = arcTable.Length - 1;
            arcTable[0] = 0f;
            Vector2 prev = p0;
            for (int s = 1; s <= m; s++) {
                Vector2 q = Evaluate(p0, p1, p2, p3, cubic, s / (float)m);
                arcTable[s] = arcTable[s - 1] + Vector2.Distance(prev, q);
                prev = q;
            }
            float total = arcTable[m];
            CurveLength = total;
            if (total < 0.001f) {
                Array.Fill(curve, p0);
                return;
            }
            curve[0] = p0;
            curve[n] = p3;
            int cursor = 1;
            for (int k = 1; k < n; k++) {
                float want = total * k / n;
                while (cursor < m && arcTable[cursor] < want) {
                    cursor++;
                }
                float lo = arcTable[cursor - 1];
                float hi = arcTable[cursor];
                float f = hi - lo > 0.0001f ? (want - lo) / (hi - lo) : 0f;
                float t = (cursor - 1 + f) / m;
                curve[k] = Evaluate(p0, p1, p2, p3, cubic, t);
            }
        }

        private static Vector2 Evaluate(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, bool cubic, float t) {
            float u = 1f - t;
            if (!cubic) {
                //二次：p0, p1, p3
                return u * u * p0 + 2f * u * t * p1 + t * t * p3;
            }
            return u * u * u * p0 + 3f * u * u * t * p1 + 3f * u * t * t * p2 + t * t * t * p3;
        }

        private static float ApproxLength(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, bool cubic) {
            const int samples = 16;
            float len = 0f;
            Vector2 prev = p0;
            for (int s = 1; s <= samples; s++) {
                Vector2 q = Evaluate(p0, p1, p2, p3, cubic, s / (float)samples);
                len += Vector2.Distance(prev, q);
                prev = q;
            }
            return len;
        }

        private void WriteBones() {
            int n = bones.Length;
            for (int i = 0; i < n; i++) {
                ref Bone2D b = ref B(i);
                Vector2 v = joints[i + 1] - joints[i];
                float len = v.Length();
                b.Pos = joints[i];
                b.Dir = len > 0.0001f ? MathF.Atan2(v.Y, v.X) : (i > 0 ? B(i - 1).Dir : ParentDir(0));
                b.Length = len;
            }
        }

        /// <inheritdoc/>
        public override void DebugDraw(SpriteBatch sb, Func<Vector2, Vector2> toScreen) {
            if (!valid || !inited) {
                return;
            }
            Texture2D px = Rig2DDebugDraw.Pixel;
            if (px == null) {
                return;
            }
            for (int j = 0; j < joints.Length; j++) {
                sb.Draw(px, toScreen(joints[j]), new Rectangle(0, 0, 1, 1), Color.MediumPurple, 0f, new Vector2(0.5f), 4f, SpriteEffects.None, 0f);
            }
            for (int j = 1; j < curve.Length; j++) {
                Rig2DDebugDraw.Line(sb, toScreen(curve[j - 1]), toScreen(curve[j]), Color.MediumPurple * 0.5f, 1f);
            }
            sb.Draw(px, toScreen(ResolveTarget()), new Rectangle(0, 0, 1, 1), Color.LimeGreen, 0f, new Vector2(0.5f), 6f, SpriteEffects.None, 0f);
        }
    }
}
