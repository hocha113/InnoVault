using InnoVault.Rigs2D.Data;
using InnoVault.Rigs2D.Runtime;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;

namespace InnoVault.Rigs2D.Solvers
{
    /// <summary>
    /// 次级运动跟随：每根骨的近端用一个阻尼弹簧追自己的「应在位置」（关键帧 / 静息传播给出的位置 + 风 + 湍流），
    /// 于是被关键帧来回拖动的点有惯性、拐弯时略甩过头再回落，宿主整体挪动时它落在后面，阵风一来先被吹偏、回弹几下再稳住
    /// <br/>典型用途：木偶网格件的发梢 / 衣角 / 飘带控制点（零长骨），也可挂在任何关键帧驱动的末端骨上；只改骨的位置，不改朝向与长度
    /// <br/>骨骼：<c>[骨1 … 骨n]</c>，各自独立。参数（数组按骨广播，单值即全体）：
    /// <c>frequency</c> 1.4（Hz，弹簧固有频率，越低越软越拖）、<c>damping</c> 0.35（阻尼比，1 = 临界不过冲）、
    /// <c>windWeight</c> 1（这根骨吃风的比例）、<c>turbulence</c> 0（湍流幅度，像素，Scale 1）、<c>turbulenceFreq</c> 0.35（Hz）、
    /// <c>turbulenceY</c> 0.5（纵向湍流相对横向的比例）、<c>maxLag</c> 0（偏离应在位置的上限，像素，0 = 不限）、
    /// <c>space</c> "world"（"parent"：弹簧活在父骨局部系里，父骨的挪动直接带走、只有局部动画有惯性）
    /// <br/>风：运行时 <see cref="Wind"/>（世界像素，权重 1 的骨被推开的距离，乘 Scale）× <see cref="WindGain"/>；
    /// 通道属性 <c>wind</c>（矢量）/ <c>windGain</c> / <c>turbulenceGain</c>。湍流是三路不可公度的正弦叠加，相位来自实例种子与骨序号，各端一致
    /// <br/>时间：属次级运动（<see cref="IsSecondary"/>），步长取实例的次级步长；步长 ≤ 0 时定格
    /// </summary>
    public sealed class SpringFollowSolver : Rig2DSolver
    {
        private float[] frequency = [];
        private float[] damping = [];
        private float[] windWeight = [];
        private float[] turbulence = [];
        private float[] turbulenceFreq = [];
        private float turbulenceY;
        private float maxLag;
        private bool parentSpace;
        //弹簧状态：世界空间时是世界坐标，父空间时是父骨局部坐标（Scale 1）
        private Vector2[] pos = [];
        private Vector2[] vel = [];
        private Vector2[] goal = [];
        private bool seeded;

        /// <summary>
        /// 风（世界像素，权重 1 的骨被推开的距离；乘实例 Scale）
        /// </summary>
        public Vector2 Wind { get; set; }
        /// <summary>
        /// 风的运行时倍率
        /// </summary>
        public float WindGain { get; set; } = 1f;
        /// <summary>
        /// 湍流的运行时倍率（0 关掉）
        /// </summary>
        public float TurbulenceGain { get; set; } = 1f;

        /// <inheritdoc/>
        public override bool IsSecondary => true;

        /// <summary>
        /// 第 i 根骨本帧的应在位置（世界，含风与湍流）
        /// </summary>
        public Vector2 Goal(int i) => (uint)i < (uint)goal.Length ? ToWorld(i, goal[i]) : Vector2.Zero;

        /// <inheritdoc/>
        protected override void Configure(Solver2DDef def) {
            int n = bones.Length;
            frequency = def.GetFloatArray("frequency", n, 1.4f);
            damping = def.GetFloatArray("damping", n, 0.35f);
            windWeight = def.GetFloatArray("windWeight", n, 1f);
            turbulence = def.GetFloatArray("turbulence", n, 0f);
            turbulenceFreq = def.GetFloatArray("turbulenceFreq", n, 0.35f);
            turbulenceY = def.GetFloat("turbulenceY", 0.5f);
            maxLag = def.GetFloat("maxLag", 0f);
            bool wasParent = parentSpace;
            parentSpace = string.Equals(def.GetString("space", "world"), "parent", StringComparison.OrdinalIgnoreCase);
            if (pos.Length != n || wasParent != parentSpace) {
                pos = new Vector2[n];
                vel = new Vector2[n];
                goal = new Vector2[n];
                seeded = false;
            }
        }

        /// <inheritdoc/>
        protected internal override int ChannelProperty(string prop, out bool spatial) {
            spatial = false;
            return prop switch {
                "wind" => 0,
                "windGain" => 1,
                "turbulenceGain" => 2,
                _ => -1,
            };
        }

        /// <inheritdoc/>
        protected internal override void SetChannel(int property, Vector2 value) {
            switch (property) {
                case 0: Wind = value; break;
                case 1: WindGain = value.X; break;
                case 2: TurbulenceGain = value.X; break;
            }
        }

        /// <inheritdoc/>
        protected internal override void OnEnabled() => seeded = false;

        /// <inheritdoc/>
        protected internal override void OnMirrorChanged() => seeded = false;

        /// <inheritdoc/>
        public override void Snap() {
            for (int i = 0; i < bones.Length; i++) {
                goal[i] = ComputeGoal(i);
                pos[i] = goal[i];
                vel[i] = Vector2.Zero;
                Write(i);
            }
            seeded = true;
        }

        /// <inheritdoc/>
        public override void Step(float dt) {
            if (!seeded) {
                Snap();
                return;
            }
            float seconds = dt * Spring2D.FrameSeconds;
            for (int i = 0; i < bones.Length; i++) {
                goal[i] = ComputeGoal(i);
                if (seconds > 0f) {
                    Integrate(i, seconds);
                }
                Write(i);
            }
        }

        //半隐式欧拉，按固有频率切子步保证 ω·h ≤ 0.5
        private void Integrate(int i, float seconds) {
            float omega = MathHelper.TwoPi * Math.Max(frequency[i], 0.01f);
            float zeta = Math.Max(damping[i], 0f);
            int steps = Math.Clamp((int)MathF.Ceiling(omega * seconds / 0.5f), 1, 16);
            float h = seconds / steps;
            Vector2 x = pos[i];
            Vector2 v = vel[i];
            Vector2 g = goal[i];
            for (int s = 0; s < steps; s++) {
                Vector2 a = (g - x) * (omega * omega) - v * (2f * zeta * omega);
                v += a * h;
                x += v * h;
            }
            if (maxLag > 0f) {
                float lim = parentSpace ? maxLag : maxLag * Scale;
                Vector2 d = x - g;
                float len = d.Length();
                if (len > lim) {
                    x = g + d * (lim / len);
                    //撞到上限的那一分量速度清掉，免得贴着边界抖
                    Vector2 n = d / len;
                    float outward = Vector2.Dot(v, n);
                    if (outward > 0f) {
                        v -= n * outward;
                    }
                }
            }
            pos[i] = x;
            vel[i] = v;
        }

        //应在位置：静息传播位置（含关键帧覆写）+ 风 + 湍流；父空间时换到父骨局部系（Scale 1）
        private Vector2 ComputeGoal(int i) {
            Vector2 world = RestPosition(i);
            float scale = Scale;
            Vector2 push = Wind * (WindGain * windWeight[i] * scale);
            float amp = turbulence[i] * TurbulenceGain;
            if (amp != 0f) {
                float t = Rig.Time * Spring2D.FrameSeconds * MathHelper.TwoPi * turbulenceFreq[i];
                float ph = Phase(i);
                float nx = MathF.Sin(t + ph) * 0.62f + MathF.Sin(t * 1.731f + ph * 2.3f) * 0.28f + MathF.Sin(t * 2.917f + ph * 4.1f) * 0.10f;
                float ny = MathF.Sin(t * 0.877f + ph * 1.7f) * 0.62f + MathF.Sin(t * 2.243f + ph * 3.1f) * 0.38f;
                push += new Vector2(nx, ny * turbulenceY) * (amp * scale);
            }
            world += push;
            return parentSpace ? ToLocal(i, world) : world;
        }

        private float Phase(int i) {
            float s = MathF.Sin((Rig.Seed + 1.37f) * 12.9898f + bones[i] * 78.233f) * 43758.547f;
            return (s - MathF.Floor(s)) * MathHelper.TwoPi;
        }

        private void Write(int i) {
            ref Bone2D b = ref B(i);
            b.Pos = ToWorld(i, pos[i]);
        }

        private Vector2 ToLocal(int i, Vector2 world) {
            Vector2 d = world - ParentPos(i);
            float dir = ParentDir(i);
            float c = MathF.Cos(dir);
            float s = MathF.Sin(dir);
            float inv = 1f / Math.Max(Scale, 0.001f);
            return new Vector2((d.X * c + d.Y * s) * inv, (-d.X * s + d.Y * c) * inv * MirrorSign);
        }

        private Vector2 ToWorld(int i, Vector2 p) {
            if (!parentSpace) {
                return p;
            }
            float dir = ParentDir(i);
            float c = MathF.Cos(dir);
            float s = MathF.Sin(dir);
            float scale = Scale;
            float x = p.X * scale;
            float y = p.Y * scale * MirrorSign;
            return ParentPos(i) + new Vector2(c * x - s * y, s * x + c * y);
        }

        /// <inheritdoc/>
        public override void DebugDraw(SpriteBatch sb, Func<Vector2, Vector2> toScreen) {
            for (int i = 0; i < bones.Length; i++) {
                Vector2 g = toScreen(Goal(i));
                Vector2 p = toScreen(B(i).Pos);
                Rig2DDebugDraw.Line(sb, g, p, new Color(255, 200, 90) * 0.8f, 1f);
                Rig2DDebugDraw.Dot(sb, g, 3f, new Color(255, 200, 90));
            }
        }
    }
}
