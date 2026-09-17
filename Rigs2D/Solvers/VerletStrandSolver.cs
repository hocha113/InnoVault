using InnoVault.Rigs2D.Data;
using InnoVault.Rigs2D.Runtime;
using Microsoft.Xna.Framework;
using System;
using Terraria;

namespace InnoVault.Rigs2D.Solvers
{
    /// <summary>
    /// 世界坐标 verlet 触角 / 绳：质点积分 + 距离约束迭代，首端钉在首节父骨骼；
    /// 弱重力 + 静息伸展力 + 双频水流谐波（越靠末端摆幅越大），头部运动自然甩尾
    /// <br/>骨骼：<c>[节1 … 节n]</c>（n 节 = n + 1 质点）；参数：<c>gravity</c> 0.028、<c>damping</c> 0.9、<c>iterations</c> 3、
    /// <c>restForce</c> 0.16、<c>sway</c> 0.22、<c>swayFreq</c> [1.7, 0.61]、<c>swayStep</c> [0.8, 0.42]、<c>swaySecond</c> 0.5、<c>warmDist</c> 400
    /// <br/>两端绳：<c>pinEnd</c> false（真则末端钉在静息尖端，运行时 <see cref="EndTarget"/> 非空即钉到该点，两者都给以 EndTarget 为准）、
    /// <c>fitLength</c> false（末端钉住时每节静息长改为 锚–末端距离 × <c>fitFactor</c> 1 / 节数，绳永远刚好跨满两端）
    /// <br/>稳定性：<c>substeps</c> 1（每帧积分次数；锚点与末端在上帧值与本帧值之间插值，高速宿主下防抽长）、
    /// <c>tileCollide</c> false（质点撞实心物块时截掉进入方向的位移）
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
        private bool pinEnd;
        private bool fitLength;
        private float fitFactor;
        private int substeps;
        private bool tileCollide;
        private Vector2[] pos = [];
        private Vector2[] old = [];
        private bool warmed;
        private Vector2 prevAnchor;
        private Vector2? prevEnd;
        private bool hasPrev;
        private float fitSegLen = float.NaN;

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
        /// 末端钉住点（世界）；非空即两端绳（本体到另一实体的连接绳一类），每帧由消费方写入已同步的量
        /// </summary>
        public Vector2? EndTarget { get; set; }
        /// <summary>
        /// 节长运行时倍率（乘在静息长上；<c>fitLength</c> 生效时被覆盖）
        /// </summary>
        public float LengthScale { get; set; } = 1f;
        /// <summary>
        /// 本帧生效的末端钉住点（含 <c>pinEnd</c> 的静息尖端回落），无则 <see langword="null"/>
        /// </summary>
        public Vector2? PinnedEnd { get; private set; }
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
            pinEnd = def.GetBool("pinEnd", false);
            fitLength = def.GetBool("fitLength", false);
            fitFactor = def.GetFloat("fitFactor", 1f);
            substeps = Math.Max(def.GetInt("substeps", 1), 1);
            tileCollide = def.GetBool("tileCollide", false);
            int n = bones.Length + 1;
            if (pos.Length != n) {
                pos = new Vector2[n];
                old = new Vector2[n];
                warmed = false;
                hasPrev = false;
            }
        }

        private Vector2 Anchor() => bones.Length > 0 ? RestPosition(0) : Rig.RootPosition;

        private Vector2 ResolveRestDir() => RestDir ?? RestForward(0);

        //第 i 节本帧生效的节长：贴合模式下为 锚–末端距离 × fitFactor / 节数，否则 静息长 × LengthScale
        private float SegLength(int i) => float.IsNaN(fitSegLen) ? RestLength(i) * LengthScale : fitSegLen;

        private float TotalRestLength() {
            float total = 0f;
            for (int i = 0; i < bones.Length; i++) {
                total += RestLength(i) * LengthScale;
            }
            return total;
        }

        private Vector2? ResolveEnd(Vector2 anchor, Vector2 restDir) {
            if (EndTarget.HasValue) {
                return EndTarget;
            }
            return pinEnd ? anchor + restDir * TotalRestLength() : null;
        }

        private void RefreshFit(Vector2 anchor, Vector2? end) {
            fitSegLen = fitLength && end.HasValue && bones.Length > 0
                ? Vector2.Distance(anchor, end.Value) * fitFactor / bones.Length
                : float.NaN;
        }

        /// <summary>
        /// 沿静息方向摆好初始落位
        /// </summary>
        public void WarmStart(Vector2 anchor, Vector2 restDir) => WarmStart(anchor, restDir, null);

        /// <summary>
        /// 摆好初始落位：有末端钉住点时在两端之间等分铺开，否则沿静息方向按节长铺开
        /// </summary>
        public void WarmStart(Vector2 anchor, Vector2 restDir, Vector2? end) {
            RefreshFit(anchor, end);
            pos[0] = old[0] = anchor;
            if (end.HasValue && pos.Length > 1) {
                for (int i = 1; i < pos.Length; i++) {
                    pos[i] = old[i] = Vector2.Lerp(anchor, end.Value, i / (float)(pos.Length - 1));
                }
            }
            else {
                Vector2 p = anchor;
                for (int i = 1; i < pos.Length; i++) {
                    p += restDir * SegLength(i - 1);
                    pos[i] = old[i] = p;
                }
            }
            warmed = true;
            hasPrev = false;
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
            Vector2 anchor = Anchor();
            Vector2 restDir = ResolveRestDir();
            Vector2? end = ResolveEnd(anchor, restDir);
            PinnedEnd = end;
            WarmStart(anchor, restDir, end);
            WriteBones();
        }

        /// <inheritdoc/>
        public override void Step(float dt) {
            if (bones.Length == 0) {
                return;
            }
            Vector2 anchor = Anchor();
            Vector2 restDir = ResolveRestDir();
            Vector2? end = ResolveEnd(anchor, restDir);
            PinnedEnd = end;
            float warm = warmDist * Scale;
            if (!warmed || Vector2.DistanceSquared(pos[0], anchor) > warm * warm) {
                WarmStart(anchor, restDir, end);
            }

            //子步：锚点与末端从上帧值插到本帧值，每一小步各自积分一次
            int steps = substeps;
            Vector2 fromAnchor = hasPrev ? prevAnchor : anchor;
            Vector2 fromEnd = hasPrev && prevEnd.HasValue && end.HasValue ? prevEnd.Value : (end ?? Vector2.Zero);
            float subDt = dt / steps;
            for (int s = 1; s <= steps; s++) {
                float f = s / (float)steps;
                Vector2 a = steps == 1 ? anchor : Vector2.Lerp(fromAnchor, anchor, f);
                Vector2? e = end.HasValue ? (steps == 1 ? end.Value : Vector2.Lerp(fromEnd, end.Value, f)) : null;
                Integrate(a, e, restDir, subDt);
            }
            prevAnchor = anchor;
            prevEnd = end;
            hasPrev = true;
            WriteBones();
        }

        //一次积分 + 约束：dt 为 1 时与单步语义逐字一致（重力 / 伸展力按 dt²、阻尼按 dt 次幂、谐波位移按 dt 缩放）
        private void Integrate(Vector2 anchor, Vector2? end, Vector2 restDir, float dt) {
            RefreshFit(anchor, end);
            int n = pos.Length;
            float gravity = (float.IsNaN(Gravity) ? paramGravity : Gravity) * dt * dt;
            float dampingBase = float.IsNaN(Damping) ? paramDamping : Damping;
            float damping = dt == 1f ? dampingBase : MathF.Pow(Math.Max(dampingBase, 0f), dt);
            float pull = restForce * dt * dt;
            float time = Rig.Time * Spring2D.FrameSeconds;
            float phase = float.IsNaN(Phase) ? Rig.Seed : Phase;
            Vector2 side = new(-restDir.Y, restDir.X);
            float swayAmp = sway * SwayGain * dt;
            int last = n - 1;

            for (int i = 1; i < n; i++) {
                if (end.HasValue && i == last) {
                    //钉住的末端不积分，只跟目标
                    old[i] = pos[i];
                    pos[i] = end.Value;
                    continue;
                }
                Vector2 vel = (pos[i] - old[i]) * damping;
                old[i] = pos[i];
                //静息伸展力：让触角保持前扬而不是全程下垂
                float reach = i / (float)(n - 1);
                Vector2 pullVec = restDir * (pull * (1f - reach));
                Vector2 swayVec = Vector2.Zero;
                if (swayAmp != 0f) {
                    float s = MathF.Sin(time * swayFreq1 + phase + i * swayStep1)
                        + MathF.Sin(time * swayFreq2 + phase * 1.3f + i * swayStep2) * swaySecond;
                    swayVec = side * (s * swayAmp * reach);
                }
                if (tileCollide) {
                    //碰撞模式：先合成整段位移再截，被挡的分量整体归零
                    Vector2 delta = vel;
                    delta.Y += gravity;
                    delta += pullVec + swayVec;
                    pos[i] += Collide(pos[i], delta);
                }
                else {
                    //无碰撞：逐项累加，与单步版本逐字同序
                    pos[i] += vel;
                    pos[i].Y += gravity;
                    pos[i] += pullVec;
                    if (swayAmp != 0f) {
                        pos[i] += swayVec;
                    }
                }
            }

            for (int k = 0; k < iterations; k++) {
                pos[0] = anchor;
                if (end.HasValue) {
                    pos[last] = end.Value;
                }
                for (int i = 0; i < n - 1; i++) {
                    float segLen = SegLength(i);
                    Vector2 delta = pos[i + 1] - pos[i];
                    float len = delta.Length();
                    if (len < 0.0001f) {
                        continue;
                    }
                    float diff = (len - segLen) / len;
                    bool headFixed = i == 0;
                    bool tailFixed = end.HasValue && i + 1 == last;
                    if (headFixed && tailFixed) {
                        //单节两端全钉：长度由两端决定，无可修正
                        continue;
                    }
                    if (headFixed) {
                        Vector2 move = -delta * diff;
                        pos[i + 1] += tileCollide ? Collide(pos[i + 1], move) : move;
                    }
                    else if (tailFixed) {
                        Vector2 move = delta * diff;
                        pos[i] += tileCollide ? Collide(pos[i], move) : move;
                    }
                    else {
                        Vector2 corr = delta * (diff * 0.5f);
                        if (tileCollide) {
                            pos[i] += Collide(pos[i], corr);
                            pos[i + 1] += Collide(pos[i + 1], -corr);
                        }
                        else {
                            pos[i] += corr;
                            pos[i + 1] -= corr;
                        }
                    }
                }
            }
            pos[0] = anchor;
            if (end.HasValue) {
                pos[last] = end.Value;
            }
        }

        //物块碰撞：以质点为中心的 6×6 小盒试探位移，被物块挡住的分量归零（与经典绳索实现同法）
        private static Vector2 Collide(Vector2 position, Vector2 delta) {
            if (delta.LengthSquared() < 0.000001f) {
                return delta;
            }
            Vector2 allowed = Collision.noSlopeCollision(position - new Vector2(3f), delta, 6, 6, true, true);
            Vector2 result = delta;
            if (Math.Abs(allowed.X) < Math.Abs(delta.X)) {
                result.X = 0f;
            }
            if (Math.Abs(allowed.Y) < Math.Abs(delta.Y)) {
                result.Y = 0f;
            }
            return result;
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
