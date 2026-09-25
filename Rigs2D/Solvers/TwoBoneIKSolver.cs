using InnoVault.Rigs2D.Data;
using InnoVault.Rigs2D.Runtime;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;

namespace InnoVault.Rigs2D.Solvers
{
    /// <summary>
    /// 二骨解析 IK：肩 → 肘 → 腕，余弦定理精确解
    /// <br/>骨骼：<c>[上臂, 前臂]</c>，肩锚 = 上臂骨骼静息传播出的近端位置（挂在父骨骼上），腕目标 = <see cref="Target"/> 或目标源
    /// <br/>参数（JSON 键 → 默认值）：
    /// <list type="bullet">
    /// <item><c>smoothing</c> → <c>"target"</c>：<c>none</c> 直接解 / <c>target</c> 腕目标挂速度弹簧（滞后 / 过冲 / 余摆）/ <c>angles</c> 弦向角与肘偏角各挂临界阻尼弹簧</item>
    /// <item><c>spring</c> 0.16、<c>damping</c> 0.74：目标弹簧刚度与阻尼（运行时可逐帧改 <see cref="Spring"/> / <see cref="Damping"/>）</item>
    /// <item><c>chordOmega</c> 13、<c>bendOmega</c> 15：角度弹簧角频率 rad/s（运行时 <see cref="Omega"/> 可整体覆盖）</item>
    /// <item><c>hardSnapDist</c> 480：肩锚单帧跳过此距离即硬重建（两种平滑口味都适用）；<c>target</c> 口味下腕目标跳过此距离也重置弹簧，
    /// <c>angles</c> 口味下目标跳变有意不重建——弦向 / 肘偏角弹簧甩过去就是"甩鞭"读数</item>
    /// <item><c>bendSign</c> 1：肘极性（+1 顺时针侧）；<c>autoBend</c> false 时生效</item>
    /// <item><c>autoBend</c> false、<c>hint</c> [0.4, 1]、<c>sideHysteresis</c> 0.2、<c>sideBlendBand</c> 0.45：按父骨骼局部系的提示向量自动选极性，带迟滞与换侧收拢</item>
    /// <item><c>maxBend</c> π：肘偏角硬限位；<c>maxRelative</c> π：前臂相对上臂折叠限位（不许反折贴臂）</item>
    /// <item><c>reachMargin</c> 2、<c>minReachMargin</c> 6：可达域两端余量（防 acos 边界抖动）</item>
    /// <item><c>stretch</c> false、<c>stretchCap</c> 1.3、<c>squashFloor</c> 0.6：允许骨段均匀拉伸 / 贴身压缩</item>
    /// <item><c>targetSolver</c> / <c>targetIndex</c>：从另一个求解器取腕目标</item>
    /// </list>
    /// </summary>
    public sealed class TwoBoneIKSolver : Rig2DSolver
    {
        /// <summary>
        /// 平滑口味
        /// </summary>
        public enum SmoothMode
        {
            /// <summary>
            /// 直接解，无平滑
            /// </summary>
            None,
            /// <summary>
            /// 腕目标位置挂速度弹簧
            /// </summary>
            Target,
            /// <summary>
            /// 弦向角 + 肘偏角挂临界阻尼弹簧
            /// </summary>
            Angles,
        }

        private SmoothMode smoothing;
        private float paramSpring;
        private float paramDamping;
        private float hardSnapDist;
        private float chordOmega;
        private float bendOmega;
        private float bendSign;
        private bool autoBend;
        private Vector2 hint;
        private float sideHysteresis;
        private float sideBlendBand;
        private float maxBend;
        private float maxRelative;
        private float reachMargin;
        private float minReachMargin;
        private bool stretch;
        private float stretchCap;
        private float squashFloor;
        private IRig2DTargetSource targetSource;
        private int targetIndex;

        private Vector2 springPos;
        private Vector2 springVel;
        private bool springInit;
        private float chord;
        private float chordVel;
        private float bend;
        private float bendVel;
        private float desiredSide;
        private bool anglesInit;
        private Vector2 lastShoulder;
        private bool valid;

        /// <summary>
        /// 腕目标（世界）；有目标源时被目标源覆盖
        /// </summary>
        public Vector2 Target { get; set; }
        /// <summary>
        /// 运行时目标源（可代码直接挂，也可由 <c>targetSolver</c> 参数解析）
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
        /// 目标弹簧刚度（逐帧可改；Configure 时重置为参数值）
        /// </summary>
        public float Spring { get; set; }
        /// <summary>
        /// 目标弹簧阻尼（逐帧可改）
        /// </summary>
        public float Damping { get; set; }
        /// <summary>
        /// 角度弹簧角频率整体覆盖（NaN = 用参数；肘偏角取 1.15 倍）
        /// </summary>
        public float Omega { get; set; } = float.NaN;
        /// <summary>
        /// 肘偏角幅度乘子（收拢度；0 近直臂 ~ 1 全弓）
        /// </summary>
        public float BendScale { get; set; } = 1f;

        /// <summary>
        /// 肩位置（本帧解）
        /// </summary>
        public Vector2 Shoulder { get; private set; }
        /// <summary>
        /// 肘位置
        /// </summary>
        public Vector2 Elbow { get; private set; }
        /// <summary>
        /// 腕位置（链尖，必然与肘相连）
        /// </summary>
        public Vector2 Wrist { get; private set; }
        /// <summary>
        /// 上臂方向（单位）
        /// </summary>
        public Vector2 UpperDir { get; private set; }
        /// <summary>
        /// 前臂方向（单位）
        /// </summary>
        public Vector2 ForeDir { get; private set; }
        /// <summary>
        /// 当前弹簧腕目标位置（目标平滑模式）
        /// </summary>
        public Vector2 SpringTarget => springPos;
        /// <summary>
        /// 可达上限（本帧）
        /// </summary>
        public float ReachMax { get; private set; }
        /// <summary>
        /// 可达下限（本帧）
        /// </summary>
        public float ReachMin { get; private set; }
        /// <summary>
        /// 当前肘极性 ±1
        /// </summary>
        public float Side => autoBend ? desiredSide : bendSign * MirrorSign;
        /// <summary>
        /// 当前骨段拉伸倍率（未开 stretch 恒为 1）
        /// </summary>
        public float StretchFactor { get; private set; } = 1f;
        /// <summary>
        /// 本帧实际追的腕目标（目标平滑后）
        /// </summary>
        public Vector2 Goal { get; private set; }
        /// <summary>
        /// 本帧腕与目标的距离（像素）：够不到、折叠限位或弹簧滞后时大于 0，离线检查 <c>IK_UNREACHED</c> 读它
        /// </summary>
        public float Error { get; private set; }

        /// <summary>
        /// 注入一次冲量（出拳弹出 / 后坐余摆），仅目标平滑模式有效
        /// </summary>
        public void Impulse(Vector2 impulse) => springVel += impulse;

        /// <inheritdoc/>
        protected internal override int ChannelProperty(string prop, out bool spatial) {
            spatial = prop == "target";
            return prop switch {
                "target" => 0,
                "bendScale" => 1,
                "omega" => 2,
                "spring" => 3,
                "damping" => 4,
                _ => -1,
            };
        }

        /// <inheritdoc/>
        protected internal override void SetChannel(int property, Vector2 value) {
            switch (property) {
                case 0: Target = value; break;
                case 1: BendScale = value.X; break;
                case 2: Omega = value.X; break;
                case 3: Spring = value.X; break;
                case 4: Damping = value.X; break;
            }
        }

        /// <inheritdoc/>
        protected override void Configure(Solver2DDef def) {
            valid = bones.Length >= 2 && bones[0] >= 0 && bones[1] >= 0;
            if (!valid) {
                Rig2DPlatform.LogError($"[Rig2D:{Rig?.Name}/{Name}]", "TwoBoneIK needs bones [upper, fore]");
            }
            smoothing = def.GetString("smoothing", "target").ToLowerInvariant() switch {
                "none" => SmoothMode.None,
                "angles" => SmoothMode.Angles,
                _ => SmoothMode.Target,
            };
            paramSpring = def.GetFloat("spring", 0.16f);
            paramDamping = def.GetFloat("damping", 0.74f);
            hardSnapDist = def.GetFloat("hardSnapDist", 480f);
            chordOmega = def.GetFloat("chordOmega", 13f);
            bendOmega = def.GetFloat("bendOmega", 15f);
            bendSign = def.GetFloat("bendSign", 1f) >= 0f ? 1f : -1f;
            autoBend = def.GetBool("autoBend", false);
            hint = def.GetVector2("hint", new Vector2(0.4f, 1f));
            sideHysteresis = def.GetFloat("sideHysteresis", 0.2f);
            sideBlendBand = def.GetFloat("sideBlendBand", 0.45f);
            maxBend = def.GetAngle("maxBend", MathHelper.Pi);
            maxRelative = def.GetAngle("maxRelative", MathHelper.Pi);
            reachMargin = def.GetFloat("reachMargin", 2f);
            minReachMargin = def.GetFloat("minReachMargin", 6f);
            stretch = def.GetBool("stretch", false);
            stretchCap = def.GetFloat("stretchCap", 1.3f);
            squashFloor = def.GetFloat("squashFloor", 0.6f);
            Spring = paramSpring;
            Damping = paramDamping;
            targetIndex = def.GetInt("targetIndex", 0);
        }

        /// <inheritdoc/>
        protected internal override void PostBind() {
            if (Rig != null && Rig.Definition != null) {
                int idx = Rig.Definition.SolverIndex(Name);
                if (idx >= 0) {
                    IRig2DTargetSource src = ResolveTargetSource(Rig.Definition.Solvers[idx], out int i);
                    if (src != null) {
                        targetSource = src;
                        targetIndex = i;
                    }
                }
            }
        }

        /// <inheritdoc/>
        public override void Snap() {
            springInit = false;
            anglesInit = false;
            desiredSide = 0f;
            springVel = Vector2.Zero;
            if (valid) {
                Solve(1f, forceSnap: true);
            }
        }

        /// <inheritdoc/>
        protected internal override void OnMirrorChanged() {
            //肘侧迟滞是旧极性下选的，翻身后第一帧重新选边
            desiredSide = 0f;
        }

        /// <inheritdoc/>
        public override void Step(float dt) {
            if (valid) {
                Solve(dt, forceSnap: false);
            }
        }

        private Vector2 ResolveWant() {
            if (targetSource != null && targetSource.TryGetTarget(targetIndex, out Vector2 t)) {
                return t;
            }
            return Target;
        }

        private void Solve(float dt, bool forceSnap) {
            ref Bone2D upper = ref B(0);
            ref Bone2D fore = ref B(1);
            Vector2 shoulder = RestPosition(0);
            float b1 = RestLength(0);
            float b2 = RestLength(1);
            float scale = Math.Max(Scale, 0.001f);
            Vector2 want = ResolveWant();
            float snapDist = hardSnapDist * scale;

            bool snapHard = forceSnap || !anglesInit
                || Vector2.DistanceSquared(lastShoulder, shoulder) > snapDist * snapDist;
            lastShoulder = shoulder;

            Vector2 goal;
            if (smoothing == SmoothMode.Target) {
                //肩锚瞬移（snapHard）也要重置弹簧：否则腕从旧世界位置一路甩过来
                if (!springInit || snapHard || Vector2.DistanceSquared(springPos, want) > snapDist * snapDist) {
                    springPos = want;
                    springVel = Vector2.Zero;
                    springInit = true;
                }
                Spring2D.Velocity(ref springPos, ref springVel, want, Spring, Damping);
                goal = springPos;
            }
            else {
                goal = want;
            }

            //弦向
            Vector2 d = goal - shoulder;
            float len = d.Length();
            Vector2 dN = len > 0.001f ? d / len : RestForward(0);
            len = Math.Max(len, 0.001f);

            //骨段拉伸 / 压缩（均匀）
            StretchFactor = 1f;
            if (stretch) {
                float total = Math.Max(b1 + b2, 0.001f);
                float f = MathHelper.Clamp(len / total, 1f, stretchCap);
                f = Math.Min(f, Math.Max(len * 1.06f / total, squashFloor));
                StretchFactor = f;
                b1 *= f;
                b2 *= f;
            }

            float maxReach = b1 + b2 - reachMargin * scale;
            float minReach = Math.Abs(b1 - b2) + minReachMargin * scale;
            if (minReach > maxReach) {
                minReach = maxReach;
            }
            ReachMax = maxReach;
            ReachMin = minReach;
            float clamped = MathHelper.Clamp(len, minReach, maxReach);

            //肘偏角幅度：余弦定理，封顶 maxBend
            float cosA = (b1 * b1 + clamped * clamped - b2 * b2) / (2f * b1 * clamped);
            float bendMag = Math.Min(MathF.Acos(MathHelper.Clamp(cosA, -1f, 1f)), maxBend);

            //极性：肘向与提示向量的侧向分量都是父骨骼局部系里的左右选择，骨架镜像时跟着翻
            float mirror = MirrorSign;
            float sign = bendSign * mirror;
            float sideBlend = 1f;
            if (autoBend) {
                float parDir = ParentDir(0);
                float cos = MathF.Cos(parDir);
                float sin = MathF.Sin(parDir);
                float hintY = hint.Y * mirror;
                Vector2 hintWorld = new(cos * hint.X - sin * hintY, sin * hint.X + cos * hintY);
                Vector2 n = new(-dN.Y, dN.X);
                float wantSide = Vector2.Dot(n, hintWorld);
                if (snapHard || desiredSide == 0f) {
                    desiredSide = wantSide >= 0f ? 1f : -1f;
                }
                else if (wantSide * desiredSide < -sideHysteresis) {
                    desiredSide = -desiredSide;
                }
                float sb = MathHelper.Clamp(Math.Abs(wantSide) / Math.Max(sideBlendBand, 0.001f), 0f, 1f);
                sideBlend = 0.25f + 0.75f * Spring2D.SmoothStep01(sb);
                sign = desiredSide;
            }
            float bendTarget = bendMag * sideBlend * BendScale * sign;
            float chordTarget = MathF.Atan2(dN.Y, dN.X);

            float upperAng;
            if (smoothing == SmoothMode.Angles) {
                float omegaC = float.IsNaN(Omega) ? chordOmega : Omega;
                float omegaB = float.IsNaN(Omega) ? bendOmega : Omega * 1.15f;
                if (snapHard) {
                    chord = chordTarget;
                    chordVel = 0f;
                    bend = bendTarget;
                    bendVel = 0f;
                }
                else {
                    float sec = dt * Spring2D.FrameSeconds;
                    Spring2D.CriticalAngle(ref chord, ref chordVel, chordTarget, omegaC, sec);
                    Spring2D.Critical(ref bend, ref bendVel, bendTarget, omegaB, sec);
                }
                bend = MathHelper.Clamp(bend, -maxBend, maxBend);
                upperAng = chord + bend;
            }
            else {
                chord = chordTarget;
                bend = bendTarget;
                upperAng = chord + bend;
            }
            anglesInit = true;

            Vector2 upperDir = new(MathF.Cos(upperAng), MathF.Sin(upperAng));
            Vector2 elbow = shoulder + upperDir * b1;

            //肘带腕：前臂指向弦上的腕目标，折叠限位
            Vector2 wristTarget = shoulder + dN * clamped;
            Vector2 toWrist = wristTarget - elbow;
            Vector2 foreDir = toWrist.LengthSquared() > 0.25f ? Vector2.Normalize(toWrist) : upperDir;
            float foreAng = MathF.Atan2(foreDir.Y, foreDir.X);
            float relative = MathHelper.WrapAngle(foreAng - upperAng);
            if (Math.Abs(relative) > maxRelative) {
                float s = Math.Abs(relative) > 3f ? -sign : Math.Sign(relative);
                foreAng = upperAng + maxRelative * s;
                foreDir = new Vector2(MathF.Cos(foreAng), MathF.Sin(foreAng));
            }
            Vector2 wrist = elbow + foreDir * b2;

            upper.Pos = shoulder;
            upper.Dir = upperAng;
            upper.Length = b1;
            fore.Pos = elbow;
            fore.Dir = foreAng;
            fore.Length = b2;

            Shoulder = shoulder;
            Elbow = elbow;
            Wrist = wrist;
            UpperDir = upperDir;
            ForeDir = foreDir;
            Goal = goal;
            Error = Vector2.Distance(wrist, goal);
        }

        /// <inheritdoc/>
        public override void DebugDraw(SpriteBatch sb, Func<Vector2, Vector2> toScreen) {
            if (!valid) {
                return;
            }
            Texture2D px = Rig2DDebugDraw.Pixel;
            if (px == null) {
                return;
            }
            Vector2 t = toScreen(ResolveWant());
            sb.Draw(px, t, new Rectangle(0, 0, 1, 1), Color.OrangeRed, 0f, new Vector2(0.5f), 6f, SpriteEffects.None, 0f);
            if (smoothing == SmoothMode.Target) {
                Vector2 s = toScreen(springPos);
                sb.Draw(px, s, new Rectangle(0, 0, 1, 1), Color.Yellow, 0f, new Vector2(0.5f), 4f, SpriteEffects.None, 0f);
            }
            Rig2DDebugDraw.Circle(sb, toScreen, Shoulder, ReachMax, Color.OrangeRed * 0.35f);
        }
    }
}
