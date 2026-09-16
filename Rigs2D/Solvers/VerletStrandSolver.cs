using InnoVault.Rigs2D.Data;
using InnoVault.Rigs2D.Runtime;
using Microsoft.Xna.Framework;
using System;

namespace InnoVault.Rigs2D.Solvers
{
    /// <summary>
    /// 世界坐标 verlet 触角 / 绳：质点积分 + 距离约束迭代，首端钉在首节父骨骼；
    /// 弱重力 + 静息伸展力 + 双频水流谐波（越靠末端摆幅越大），头部运动自然甩尾
    /// <br/>骨骼：<c>[节1 … 节n]</c>（n 节 = n + 1 质点）；参数：<c>gravity</c> 0.028、<c>damping</c> 0.9、<c>iterations</c> 3、
    /// <c>restForce</c> 0.16、<c>sway</c> 0.22、<c>swayFreq</c> [1.7, 0.61]、<c>swayStep</c> [0.8, 0.42]、<c>swaySecond</c> 0.5、<c>warmDist</c> 400
    /// </summary>
    public sealed class VerletStrandSolver : Rig2DSolver
    {
        private float paramGravity;
        private float paramDamping;
        private int iterations;
        private float restForce;
        private float sway;
        private float swayFreq1;
        private float swayFreq2;
        private float swayStep1;
        private float swayStep2;
        private float swaySecond;
        private float warmDist;
        private Vector2[] pos = [];
        private Vector2[] old = [];
        private bool warmed;

        /// <summary>
        /// 重力覆盖（NaN 用参数；干燥时 0.24、水下 0.028 一类）
        /// </summary>
        public float Gravity { get; set; } = float.NaN;
        /// <summary>
        /// 阻尼覆盖（NaN 用参数）
        /// </summary>
        public float Damping { get; set; } = float.NaN;
        /// <summary>
        /// 静息伸展方向（单位向量；<see langword="null"/> 用首节静息轴向）
        /// </summary>
        public Vector2? RestDir { get; set; }
        /// <summary>
        /// 谐波摆幅增益（0 关掉水流摆）
        /// </summary>
        public float SwayGain { get; set; } = 1f;
        /// <summary>
        /// 谐波相位偏移（NaN 用实例种子）
        /// </summary>
        public float Phase { get; set; } = float.NaN;
        /// <summary>
        /// 质点数
        /// </summary>
        public int Count => pos.Length;
        /// <summary>
        /// 取第 i 个质点
        /// </summary>
        public Vector2 this[int i] => pos[i];
        /// <summary>
        /// 末端质点
        /// </summary>
        public Vector2 Tip => pos.Length > 0 ? pos[^1] : Vector2.Zero;

        /// <inheritdoc/>
        protected override void Configure(Solver2DDef def) {
            paramGravity = def.GetFloat("gravity", 0.028f);
            paramDamping = def.GetFloat("damping", 0.90f);
            iterations = Math.Max(def.GetInt("iterations", 3), 1);
            restForce = def.GetFloat("restForce", 0.16f);
            sway = def.GetFloat("sway", 0.22f);
            float[] freq = def.GetFloatArray("swayFreq", 2, 1.7f);
            if (!def.Has("swayFreq")) {
                freq[1] = 0.61f;
            }
            float[] step = def.GetFloatArray("swayStep", 2, 0.8f);
            if (!def.Has("swayStep")) {
                step[1] = 0.42f;
            }
            swayFreq1 = freq[0];
            swayFreq2 = freq[1];
            swayStep1 = step[0];
            swayStep2 = step[1];
            swaySecond = def.GetFloat("swaySecond", 0.5f);
            warmDist = def.GetFloat("warmDist", 400f);
            int n = bones.Length + 1;
            if (pos.Length != n) {
                pos = new Vector2[n];
                old = new Vector2[n];
                warmed = false;
            }
        }

        private Vector2 Anchor() => bones.Length > 0 ? RestPosition(0) : Rig.RootPosition;

        private Vector2 ResolveRestDir() => RestDir ?? RestForward(0);

        /// <summary>
        /// 沿静息方向摆好初始落位
        /// </summary>
        public void WarmStart(Vector2 anchor, Vector2 restDir) {
            Vector2 p = anchor;
            pos[0] = old[0] = anchor;
            for (int i = 1; i < pos.Length; i++) {
                p += restDir * RestLength(i - 1);
                pos[i] = old[i] = p;
            }
            warmed = true;
        }

        /// <summary>
        /// 末端横向冲量（尾弹 / 受击甩动）
        /// </summary>
        public void Nudge(Vector2 impulse) {
            if (!warmed || pos.Length < 2) {
                return;
            }
            old[^1] -= impulse;
            if (pos.Length > 2) {
                old[^2] -= impulse * 0.5f;
            }
        }

        /// <inheritdoc/>
        public override void Snap() {
            if (bones.Length == 0) {
                return;
            }
            WarmStart(Anchor(), ResolveRestDir());
            WriteBones();
        }

        /// <inheritdoc/>
        public override void Step(float dt) {
            if (bones.Length == 0) {
                return;
            }
            Vector2 anchor = Anchor();
            Vector2 restDir = ResolveRestDir();
            float warm = warmDist * Scale;
            if (!warmed || Vector2.DistanceSquared(pos[0], anchor) > warm * warm) {
                WarmStart(anchor, restDir);
            }

            int n = pos.Length;
            float gravity = float.IsNaN(Gravity) ? paramGravity : Gravity;
            float damping = float.IsNaN(Damping) ? paramDamping : Damping;
            float time = Rig.Time * Spring2D.FrameSeconds;
            float phase = float.IsNaN(Phase) ? Rig.Seed : Phase;
            Vector2 side = new(-restDir.Y, restDir.X);
            float swayAmp = sway * SwayGain;

            for (int i = 1; i < n; i++) {
                Vector2 vel = (pos[i] - old[i]) * damping;
                old[i] = pos[i];
                pos[i] += vel;
                pos[i].Y += gravity;
                //静息伸展力：让触角保持前扬而不是全程下垂
                float reach = i / (float)(n - 1);
                pos[i] += restDir * (restForce * (1f - reach));
                if (swayAmp != 0f) {
                    float s = MathF.Sin(time * swayFreq1 + phase + i * swayStep1)
                        + MathF.Sin(time * swayFreq2 + phase * 1.3f + i * swayStep2) * swaySecond;
                    pos[i] += side * (s * swayAmp * reach);
                }
            }

            for (int k = 0; k < iterations; k++) {
                pos[0] = anchor;
                for (int i = 0; i < n - 1; i++) {
                    float segLen = RestLength(i);
                    Vector2 delta = pos[i + 1] - pos[i];
                    float len = delta.Length();
                    if (len < 0.0001f) {
                        continue;
                    }
                    float diff = (len - segLen) / len;
                    if (i == 0) {
                        pos[i + 1] -= delta * diff;
                    }
                    else {
                        Vector2 corr = delta * (diff * 0.5f);
                        pos[i] += corr;
                        pos[i + 1] -= corr;
                    }
                }
            }
            pos[0] = anchor;
            WriteBones();
        }

        private void WriteBones() {
            for (int i = 0; i < bones.Length; i++) {
                ref Bone2D b = ref B(i);
                Vector2 d = pos[i + 1] - pos[i];
                b.Pos = pos[i];
                b.Dir = d.LengthSquared() > 0.0001f ? MathF.Atan2(d.Y, d.X) : b.Dir;
                b.Length = d.Length();
            }
        }
    }
}
