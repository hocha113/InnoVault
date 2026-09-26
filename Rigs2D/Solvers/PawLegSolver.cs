using InnoVault.Rigs2D.Data;
using InnoVault.Rigs2D.Runtime;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;

namespace InnoVault.Rigs2D.Solvers
{
    /// <summary>
    /// 趾行腿（狗、狼、猫的前后腿）：上段 + 下段两骨解析 IK 解到腕 / 跗，掌骨与趾在其后铺开
    /// <br/>骨骼：<c>[上段, 下段, 掌, 趾]</c>（趾可省）。目标 = 掌球（着地点）；腕 = 掌球沿掌骨方向退一掌长
    /// <br/>掌骨 / 趾的角度有两个来源，按 <see cref="Auto"/> 混合：
    /// <list type="bullet">
    /// <item>显式：<see cref="Pastern"/>（腕 → 掌球）/ <see cref="Toe"/>（掌球 → 趾尖）两个朝向系世界角，姿态库里直接写</item>
    /// <item>程序：支撑期（<see cref="Swing"/> &lt; 0）掌骨从静息方向朝"掌球 → 肩髋"偏 <c>lean</c> 份、趾取站立角；
    /// 摆越期（<see cref="Swing"/> 0~1）按 <c>flick</c> 关键帧翻卷，首尾两端接支撑规则，离地与触地都不跳</item>
    /// </list>
    /// 参数：<c>bendSign</c> 1、<c>lean</c> 0.5、<c>flick</c> <c>[[w, 掌角°, 趾角°], …]</c>（按写出的度数直线插值，不走最短弧：
    /// 趾从 17° 卷到 255° 是往下往后卷，写成 −105° 就会往上翻）、<c>stanceToeDeg</c>（缺省 = 趾骨静息世界角）、
    /// <c>reachMargin</c> 2、<c>minReachMargin</c> 6、<c>maxRelative</c> π
    /// <br/>通道属性：<c>target</c>（空间量）、<c>pastern</c>、<c>toe</c>、<c>auto</c>、<c>swing</c>
    /// </summary>
    public sealed class PawLegSolver : Rig2DSolver, IRig2DLimbReport
    {
        private readonly struct FlickKey
        {
            public readonly float W;
            public readonly float Pastern;
            public readonly float Toe;

            public FlickKey(float w, float pastern, float toe) {
                W = w;
                Pastern = pastern;
                Toe = toe;
            }
        }

        private float bendSign;
        private float lean;
        private float reachMargin;
        private float minReachMargin;
        private float maxRelative;
        private float restUp;
        private float restPastern;
        private float restToe;
        private float stanceToe;
        private FlickKey[] flick = [];
        private bool hasToe;
        private bool valid;
        private IRig2DTargetSource targetSource;
        private int targetIndex;

        /// <summary>
        /// 掌球目标（世界）；有目标源时被覆盖
        /// </summary>
        public Vector2 Target { get; set; }
        /// <summary>
        /// 显式掌骨角（朝向系世界弧度，腕 → 掌球）；NaN = 静息
        /// </summary>
        public float Pastern { get; set; } = float.NaN;
        /// <summary>
        /// 显式趾角（朝向系世界弧度，掌球 → 趾尖）；NaN = 静息
        /// </summary>
        public float Toe { get; set; } = float.NaN;
        /// <summary>
        /// 程序化权重 0~1：0 全用显式角，1 全由支撑 / 翻卷规则现算
        /// </summary>
        public float Auto { get; set; }
        /// <summary>
        /// 摆越进度：负数 = 支撑期，0~1 = 离地到触地
        /// </summary>
        public float Swing { get; set; } = -1f;
        /// <summary>
        /// 目标源（接到另一个求解器的输出上）
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
        /// 肩 / 髋（本帧）
        /// </summary>
        public Vector2 Hip { get; private set; }
        /// <summary>
        /// 肘 / 膝
        /// </summary>
        public Vector2 Knee { get; private set; }
        /// <summary>
        /// 腕 / 跗（两骨 IK 实际到达处）
        /// </summary>
        public Vector2 Wrist { get; private set; }
        /// <summary>
        /// 掌球实际位置（掌骨尖）
        /// </summary>
        public Vector2 Ball { get; private set; }
        /// <summary>
        /// 本帧实际用的掌骨 / 趾角（朝向系）
        /// </summary>
        public float PasternUsed { get; private set; }
        public float ToeUsed { get; private set; }
        /// <inheritdoc/>
        public Vector2 Contact => Ball;
        /// <inheritdoc/>
        public float Error { get; private set; }

        /// <inheritdoc/>
        protected internal override int ChannelProperty(string prop, out bool spatial) {
            spatial = prop == "target";
            return prop switch {
                "target" => 0,
                "pastern" => 1,
                "toe" => 2,
                "auto" => 3,
                "swing" => 4,
                _ => -1,
            };
        }

        /// <inheritdoc/>
        protected internal override void SetChannel(int property, Vector2 value) {
            switch (property) {
                case 0: Target = value; break;
                case 1: Pastern = value.X; break;
                case 2: Toe = value.X; break;
                case 3: Auto = value.X; break;
                case 4: Swing = value.X; break;
            }
        }

        /// <inheritdoc/>
        protected override void Configure(Solver2DDef def) {
            valid = bones.Length >= 3 && bones[0] >= 0 && bones[1] >= 0 && bones[2] >= 0;
            if (!valid) {
                Rig2DPlatform.LogError($"[Rig2D:{Rig?.Name}/{Name}]", "PawLeg needs bones [upper, lower, paw] or [upper, lower, paw, toe]");
                return;
            }
            hasToe = bones.Length >= 4 && bones[3] >= 0;
            bendSign = def.GetFloat("bendSign", 1f) >= 0f ? 1f : -1f;
            lean = MathHelper.Clamp(def.GetFloat("lean", 0.5f), 0f, 1f);
            reachMargin = def.GetFloat("reachMargin", 2f);
            minReachMargin = def.GetFloat("minReachMargin", 6f);
            maxRelative = def.GetAngle("maxRelative", MathHelper.Pi);
            targetIndex = def.GetInt("targetIndex", 0);

            //静息世界角（朝右、根朝向 0）：掌骨指向掌球，"上" = 反过来指向腕
            restPastern = Canon(DefChainDir(bones[2]));
            restUp = restPastern - MathHelper.Pi;
            restToe = hasToe ? Canon(DefChainDir(bones[3])) : restPastern;
            stanceToe = def.Has("stanceToeDeg") ? Canon(MathHelper.ToRadians(def.GetFloat("stanceToeDeg", 0f))) : restToe;

            flick = ReadFlick(def);
        }

        private FlickKey[] ReadFlick(Solver2DDef def) {
            if (def.Params?["flick"] is not Newtonsoft.Json.Linq.JArray arr) {
                return [];
            }
            System.Collections.Generic.List<FlickKey> keys = [];
            foreach (Newtonsoft.Json.Linq.JToken t in arr) {
                if (t is Newtonsoft.Json.Linq.JArray k && k.Count >= 3) {
                    float w = MathHelper.Clamp((float)k[0], 0.001f, 0.999f);
                    keys.Add(new FlickKey(w, MathHelper.ToRadians((float)k[1]), MathHelper.ToRadians((float)k[2])));
                }
            }
            keys.Sort((a, b) => a.W.CompareTo(b.W));
            return [.. keys];
        }

        /// <summary>定义链上的静息世界角（继承旋转的逐级相加，遇到世界绝对角骨骼即止）</summary>
        private float DefChainDir(int bone) {
            Rig2DDefinition d = Rig.Definition;
            float sum = 0f;
            int b = bone;
            while (b >= 0) {
                Bone2DDef bd = d.Bones[b];
                sum += bd.Rotation;
                if (!bd.InheritRotation) {
                    break;
                }
                b = bd.ParentIndex;
            }
            return sum;
        }

        /// <summary>折进 [−π/2, 3π/2)：站立的掌骨（约 40°~110°）、翻卷关键帧与趾角都落在同一段里，直线插值不会绕圈</summary>
        private static float Canon(float a) {
            const float Lo = -MathHelper.PiOver2;
            float span = MathHelper.TwoPi;
            a -= Lo;
            a -= MathF.Floor(a / span) * span;
            return a + Lo;
        }

        /// <inheritdoc/>
        protected internal override void PostBind() {
            if (Rig?.Definition == null) {
                return;
            }
            int idx = Rig.Definition.SolverIndex(Name);
            if (idx >= 0) {
                IRig2DTargetSource src = ResolveTargetSource(Rig.Definition.Solvers[idx], out int i);
                if (src != null) {
                    targetSource = src;
                    targetIndex = i;
                }
            }
        }

        /// <inheritdoc/>
        public override void Snap() {
            if (valid) {
                Solve();
            }
        }

        /// <inheritdoc/>
        public override void Step(float dt) {
            if (valid) {
                Solve();
            }
        }

        private Vector2 ResolveTarget() {
            if (targetSource != null && targetSource.TryGetTarget(targetIndex, out Vector2 t)) {
                return t;
            }
            return Target;
        }

        //朝向系 ↔ 世界：朝左镜像时关于竖直轴对称
        private float ToWorld(float facing) => Rig.Mirrored ? MathHelper.Pi - facing : facing;

        private float ToFacing(float world) => Rig.Mirrored ? MathHelper.Pi - world : world;

        /// <summary>程序化掌骨 / 趾角：支撑按肩髋偏、摆越按关键帧翻卷（首尾接支撑规则）</summary>
        private void Procedural(Vector2 hip, Vector2 ball, out float pastern, out float toe) {
            Vector2 up = hip - ball;
            float toHip = up.LengthSquared() > 0.0001f ? ToFacing(MathF.Atan2(up.Y, up.X)) : restUp;
            float stanceUp = restUp + MathHelper.WrapAngle(toHip - restUp) * lean;
            float stancePastern = Canon(stanceUp + MathHelper.Pi);
            float w = Swing;
            if (w < 0f || flick.Length == 0) {
                pastern = stancePastern;
                toe = stanceToe;
                return;
            }
            w = MathHelper.Clamp(w, 0f, 1f);
            float w0 = 0f, p0 = stancePastern, t0 = stanceToe;
            for (int i = 0; i <= flick.Length; i++) {
                float w1 = i < flick.Length ? flick[i].W : 1f;
                float p1 = i < flick.Length ? flick[i].Pastern : stancePastern;
                float t1 = i < flick.Length ? flick[i].Toe : stanceToe;
                if (w <= w1) {
                    float k = Spring2D.SmoothStep01((w - w0) / MathF.Max(w1 - w0, 0.0001f));
                    pastern = MathHelper.Lerp(p0, p1, k);
                    toe = MathHelper.Lerp(t0, t1, k);
                    return;
                }
                w0 = w1;
                p0 = p1;
                t0 = t1;
            }
            pastern = stancePastern;
            toe = stanceToe;
        }

        private void Solve() {
            ref Bone2D upper = ref B(0);
            ref Bone2D lower = ref B(1);
            ref Bone2D paw = ref B(2);
            float s = MathF.Max(Scale, 0.001f);
            Vector2 hip = RestPosition(0);
            float b1 = RestLength(0);
            float b2 = RestLength(1);
            float bp = RestLength(2);
            Vector2 ball = ResolveTarget();

            Procedural(hip, ball, out float procP, out float procT);
            float explicitP = float.IsNaN(Pastern) ? restPastern : Canon(Pastern);
            float explicitT = float.IsNaN(Toe) ? restToe : Canon(Toe);
            float a = MathHelper.Clamp(Auto, 0f, 1f);
            float pastern = MathHelper.Lerp(explicitP, procP, a);
            float toe = MathHelper.Lerp(explicitT, procT, a);
            PasternUsed = pastern;
            ToeUsed = toe;
            float pasternW = ToWorld(pastern);
            Vector2 pDir = new(MathF.Cos(pasternW), MathF.Sin(pasternW));
            Vector2 goal = ball - pDir * bp;

            //两骨解析 IK（同 TwoBoneIK 的无平滑口味）
            Vector2 d = goal - hip;
            float len = d.Length();
            Vector2 dN = len > 0.001f ? d / len : RestForward(0);
            len = MathF.Max(len, 0.001f);
            float maxReach = b1 + b2 - reachMargin * s;
            float minReach = MathF.Abs(b1 - b2) + minReachMargin * s;
            if (minReach > maxReach) {
                minReach = maxReach;
            }
            float clamped = MathHelper.Clamp(len, minReach, maxReach);
            float cosA = (b1 * b1 + clamped * clamped - b2 * b2) / (2f * b1 * clamped);
            float bendMag = MathF.Acos(MathHelper.Clamp(cosA, -1f, 1f));
            float sign = bendSign * MirrorSign;
            float upperAng = MathF.Atan2(dN.Y, dN.X) + bendMag * sign;
            Vector2 upperDir = new(MathF.Cos(upperAng), MathF.Sin(upperAng));
            Vector2 knee = hip + upperDir * b1;
            Vector2 wristTarget = hip + dN * clamped;
            Vector2 toWrist = wristTarget - knee;
            Vector2 lowerDir = toWrist.LengthSquared() > 0.25f ? Vector2.Normalize(toWrist) : upperDir;
            float lowerAng = MathF.Atan2(lowerDir.Y, lowerDir.X);
            float relative = MathHelper.WrapAngle(lowerAng - upperAng);
            if (MathF.Abs(relative) > maxRelative) {
                float r = MathF.Abs(relative) > 3f ? -sign : MathF.Sign(relative);
                lowerAng = upperAng + maxRelative * r;
                lowerDir = new Vector2(MathF.Cos(lowerAng), MathF.Sin(lowerAng));
            }
            Vector2 wrist = knee + lowerDir * b2;

            upper.Pos = hip;
            upper.Dir = upperAng;
            upper.Length = b1;
            lower.Pos = knee;
            lower.Dir = lowerAng;
            lower.Length = b2;
            paw.Pos = wrist;
            paw.Dir = pasternW;
            paw.Length = bp;
            Vector2 ballAt = wrist + pDir * bp;
            if (hasToe) {
                ref Bone2D toeBone = ref B(3);
                toeBone.Pos = ballAt;
                toeBone.Dir = ToWorld(toe);
                toeBone.Length = RestLength(3);
            }

            Hip = hip;
            Knee = knee;
            Wrist = wrist;
            Ball = ballAt;
            Error = Vector2.Distance(ballAt, ball);
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
            Vector2 t = toScreen(ResolveTarget());
            sb.Draw(px, t, new Rectangle(0, 0, 1, 1), Color.OrangeRed, 0f, new Vector2(0.5f), 6f, SpriteEffects.None, 0f);
            Rig2DDebugDraw.Circle(sb, toScreen, Hip, RestLength(0) + RestLength(1), Color.OrangeRed * 0.3f);
        }
    }
}
