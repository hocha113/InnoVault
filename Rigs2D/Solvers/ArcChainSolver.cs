using InnoVault.Rigs2D.Data;
using InnoVault.Rigs2D.Runtime;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;

namespace InnoVault.Rigs2D.Solvers
{
    /// <summary>
    /// 常曲率弧链：从锚（首节静息近端）到 <see cref="Target"/> 把 N 节铺成一段圆弧——两端钉死的定长链，弦越短弧弯得越厉害；
    /// 总弯角按弦长二分收敛（<c>chordTolerance</c> 内），末端再整体旋转对齐弦向。<see cref="Curl"/> 的符号选弯向，绝对值只作弯角初值
    /// <br/>逐关节拖拽（越靠尖越滞后 → 尖端甩鞭），<see cref="SnapAmount"/> 越大越跟手；可选痉挛微颤（从 <c>jitterFrom</c> 关节起按 j/N 放大）
    /// <br/>骨骼：<c>[seg1 … segN]</c>；节长取各骨静息长，触及 = 节长和；<c>turnWeights</c> 决定弯曲在各关节间怎么分（基节僵、中段柔一类）
    /// <br/>参数：<c>turnWeights</c> [N]（逐关节转角权重，自动归一，默认均匀）、<c>lagRates</c> [N+1]（逐关节每帧跟随率，默认 1 = 不拖）、
    /// <c>thetaGain</c> 4.4、<c>curlFloor</c> 0.45、<c>thetaMin</c> 0.02、<c>thetaMax</c> 5.4、<c>minChord</c> 34、<c>maxChordFactor</c> 0.99、
    /// <c>iterations</c> 10、<c>chordTolerance</c> 5、<c>bendSide</c> 1（整肢弯向极性，镜像肢给 -1）、<c>snapLagBlend</c> 0.7、<c>jitterFrom</c> 3
    /// </summary>
    public sealed class ArcChainSolver : Rig2DSolver
    {
        private float[] turnWeights = [];
        private float[] lagRates = [];
        private float thetaGain;
        private float curlFloor;
        private float thetaMin;
        private float thetaMax;
        private float minChord;
        private float maxChordFactor;
        private int iterations;
        private float chordTolerance;
        private float bendSide;
        private float snapLagBlend;
        private int jitterFrom;
        private Vector2[] joints = [];
        private Vector2[] solved = [];
        private float[] segLen = [];
        private bool valid;
        private bool inited;

        /// <summary>
        /// 尖端目标（世界）
        /// </summary>
        public Vector2 Target { get; set; }
        /// <summary>
        /// 卷曲 -1..1：符号选弯向（乘 <c>bendSide</c>）；绝对值只作弯角初值（定长链的弯曲量由弦长决定）
        /// </summary>
        public float Curl { get; set; }
        /// <summary>
        /// 跟手度 0..1：把逐关节拖拽率向 1 拉（猛推 / 瞬发时给高）
        /// </summary>
        public float SnapAmount { get; set; }
        /// <summary>
        /// 痉挛微颤振幅（像素，Scale 为 1 的量）；0 关闭
        /// </summary>
        public float Jitter { get; set; }

        /// <summary>
        /// 锚位置（本帧）
        /// </summary>
        public Vector2 Mount { get; private set; }
        /// <summary>
        /// 拖拽后的尖端（绘制 / 判定用）
        /// </summary>
        public Vector2 Tip => joints.Length > 0 ? joints[^1] : Mount;
        /// <summary>
        /// 无拖拽的本帧解尖端（与 <see cref="Tip"/> 之差即尖端滞后量，拖丝一类特效读它）
        /// </summary>
        public Vector2 SolvedTip => solved.Length > 0 ? solved[^1] : Mount;
        /// <summary>
        /// 全链触及（节长和，含 Scale）
        /// </summary>
        public float Reach { get; private set; }
        /// <summary>
        /// 关节数（节数 + 1）
        /// </summary>
        public int JointCount => joints.Length;

        /// <summary>
        /// 第 j 个关节位置（0 = 锚，N = 尖端），越界返回锚
        /// </summary>
        public Vector2 Joint(int j) => j >= 0 && j < joints.Length ? joints[j] : Mount;

        /// <inheritdoc/>
        protected internal override int ChannelProperty(string prop, out bool spatial) {
            spatial = prop == "target";
            return prop switch {
                "target" => 0,
                "curl" => 1,
                "snap" => 2,
                "jitter" => 3,
                _ => -1,
            };
        }

        /// <inheritdoc/>
        protected internal override void SetChannel(int property, Vector2 value) {
            switch (property) {
                case 0: Target = value; break;
                case 1: Curl = value.X; break;
                case 2: SnapAmount = value.X; break;
                case 3: Jitter = value.X; break;
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
                Rig2DPlatform.LogError($"[Rig2D:{Rig?.Name}/{Name}]", "ArcChain needs at least one bone [seg1 … segN]");
                return;
            }
            turnWeights = def.GetFloatArray("turnWeights", n, 1f / n);
            float sum = 0f;
            for (int i = 0; i < n; i++) {
                sum += Math.Max(turnWeights[i], 0f);
            }
            for (int i = 0; i < n; i++) {
                turnWeights[i] = sum > 0.0001f ? Math.Max(turnWeights[i], 0f) / sum : 1f / n;
            }
            lagRates = def.GetFloatArray("lagRates", n + 1, 1f);
            thetaGain = def.GetFloat("thetaGain", 4.4f);
            curlFloor = MathHelper.Clamp(def.GetFloat("curlFloor", 0.45f), 0f, 1f);
            thetaMin = def.GetFloat("thetaMin", 0.02f);
            thetaMax = def.GetFloat("thetaMax", 5.4f);
            minChord = def.GetFloat("minChord", 34f);
            maxChordFactor = def.GetFloat("maxChordFactor", 0.99f);
            iterations = Math.Max(def.GetInt("iterations", 10), 1);
            chordTolerance = def.GetFloat("chordTolerance", 5f);
            bendSide = def.GetFloat("bendSide", 1f) >= 0f ? 1f : -1f;
            snapLagBlend = MathHelper.Clamp(def.GetFloat("snapLagBlend", 0.7f), 0f, 1f);
            jitterFrom = Math.Max(def.GetInt("jitterFrom", 3), 0);
            if (joints.Length != n + 1) {
                joints = new Vector2[n + 1];
                solved = new Vector2[n + 1];
                inited = false;
            }
            segLen = new float[n];
        }

        /// <inheritdoc/>
        public override void Snap() {
            if (!valid) {
                return;
            }
            SolveArc();
            Array.Copy(solved, joints, solved.Length);
            inited = true;
            WriteBones();
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
            SolveArc();
            int n = segLen.Length;
            float snap = MathHelper.Clamp(SnapAmount, 0f, 1f);
            for (int j = 1; j <= n; j++) {
                float rate = MathHelper.Lerp(MathHelper.Clamp(lagRates[j], 0f, 1f), 1f, snap * snapLagBlend);
                joints[j] = Vector2.Lerp(joints[j], solved[j], Spring2D.RateForDt(rate, dt));
            }
            joints[0] = Mount;
            if (Jitter > 0.0001f) {
                float t = Rig.Time * Spring2D.FrameSeconds;
                float salt = Rig.Seed + (Rig.Definition?.SolverIndex(Name) ?? 0) * 1.9f;
                float amp = Jitter * Scale;
                for (int j = Math.Max(jitterFrom, 1); j <= n; j++) {
                    float k = j / (float)n;
                    joints[j] += new Vector2(
                        MathF.Sin(t * 9.1f + j * 2.3f + salt),
                        MathF.Sin(t * 11.3f + j * 1.7f + salt)) * (amp * k);
                }
            }
            WriteBones();
        }

        //==================== 弧链解算 ====================

        private void SolveArc() {
            int n = segLen.Length;
            Mount = RestPosition(0);
            float reach = 0f;
            for (int i = 0; i < n; i++) {
                segLen[i] = RestLength(i);
                reach += segLen[i];
            }
            Reach = reach;
            if (reach < 0.001f) {
                Array.Fill(solved, Mount);
                return;
            }

            Vector2 d = Target - Mount;
            float dist = MathHelper.Clamp(d.Length(), Math.Min(minChord * Scale, reach * maxChordFactor), reach * maxChordFactor);
            float chordAng = d.LengthSquared() > 0.0001f ? MathF.Atan2(d.Y, d.X) : ParentDir(0);
            //弯向是父骨骼局部系里的左右选择，骨架镜像时跟着翻
            float bend = (Curl >= 0f ? 1f : -1f) * bendSide * MirrorSign;

            //初值：松弛度 × 增益（|Curl| 只影响初值；两端钉死的定长链，弯曲量最终由弦长决定）
            float slack = 1f - dist / reach;
            float theta = slack * thetaGain * (curlFloor + (1f - curlFloor) * Math.Min(Math.Abs(Curl), 1f));
            theta = MathHelper.Clamp(theta, thetaMin, thetaMax);

            //弦长校正：弦长随总弯角单调递减（thetaMax < 2π），二分收敛
            float tol = chordTolerance * Scale;
            float lo = thetaMin;
            float hi = thetaMax;
            BuildArc(chordAng, theta, bend);
            for (int iter = 0; iter < iterations; iter++) {
                float got = (solved[n] - Mount).Length();
                if (Math.Abs(got - dist) < tol) {
                    break;
                }
                if (got > dist) {
                    lo = theta;
                }
                else {
                    hi = theta;
                }
                theta = (lo + hi) * 0.5f;
                BuildArc(chordAng, theta, bend);
            }

            Vector2 end = solved[n] - Mount;
            if (end.LengthSquared() > 0.0001f) {
                float corr = MathHelper.WrapAngle(chordAng - MathF.Atan2(end.Y, end.X));
                if (Math.Abs(corr) > 0.001f) {
                    float cos = MathF.Cos(corr);
                    float sin = MathF.Sin(corr);
                    for (int j = 1; j <= n; j++) {
                        Vector2 r = solved[j] - Mount;
                        solved[j] = Mount + new Vector2(r.X * cos - r.Y * sin, r.X * sin + r.Y * cos);
                    }
                }
            }
        }

        private void BuildArc(float chordAng, float theta, float bend) {
            int n = segLen.Length;
            float ang = chordAng + bend * theta * 0.5f;
            solved[0] = Mount;
            for (int i = 0; i < n; i++) {
                solved[i + 1] = solved[i] + new Vector2(MathF.Cos(ang), MathF.Sin(ang)) * segLen[i];
                ang -= bend * theta * turnWeights[i];
            }
        }

        private void WriteBones() {
            int n = segLen.Length;
            for (int i = 0; i < n; i++) {
                ref Bone2D b = ref B(i);
                Vector2 v = joints[i + 1] - joints[i];
                float len = v.Length();
                b.Pos = joints[i];
                b.Dir = len > 0.0001f ? MathF.Atan2(v.Y, v.X) : (i > 0 ? B(i - 1).Dir : ParentDir(0));
                b.Length = len > 0.0001f ? len : segLen[i];
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
                sb.Draw(px, toScreen(joints[j]), new Rectangle(0, 0, 1, 1), Color.Orange, 0f, new Vector2(0.5f), 4f, SpriteEffects.None, 0f);
            }
            sb.Draw(px, toScreen(Target), new Rectangle(0, 0, 1, 1), Color.LimeGreen, 0f, new Vector2(0.5f), 6f, SpriteEffects.None, 0f);
            Rig2DDebugDraw.Circle(sb, toScreen, Mount, Reach, Color.Orange * 0.25f);
        }
    }
}
