using InnoVault.Rigs2D.Data;
using InnoVault.Rigs2D.Runtime;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using Terraria;

namespace InnoVault.Rigs2D.Solvers
{
    /// <summary>
    /// 三节腿解析 IK：基节朝足端限幅摆动（近距关节感、全伸时放开直指），腿节 + 胫节双骨余弦解，
    /// 膝弯有效跨距钳在窗内（永不锁直、永不折死），膝极性按偏好向量选取并带迟滞
    /// <br/>骨骼：<c>[基节, 腿节, 胫节]</c>，髋 = 基节静息传播出的近端；静息方向 = 基节静息轴向（或 <see cref="Normal"/> 覆盖）
    /// <br/>参数：<c>coxaSwingMax</c> 0.8、<c>kneeSpanMin</c> 0.12、<c>kneeSpanMax</c> 0.94、<c>kneeHysteresis</c> 0.12、
    /// <c>maxReachFactor</c> 0.995、<c>slack</c> 12、<c>reachTrim</c> 6、<c>targetSolver</c> / <c>targetIndex</c>
    /// </summary>
    public sealed class ThreeBoneLegSolver : Rig2DSolver
    {
        private float coxaSwingMax;
        private float kneeSpanMin;
        private float kneeSpanMax;
        private float kneeHysteresis;
        private float maxReachFactor;
        private float slack;
        private float reachTrim;
        private IRig2DTargetSource targetSource;
        private int targetIndex;
        private int kneeSign;
        private bool valid;

        /// <summary>
        /// 足端目标（世界）；有目标源时被覆盖
        /// </summary>
        public Vector2 Foot { get; set; }
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
        /// 静息方向覆盖（单位向量；<see langword="null"/> 用基节静息轴向）
        /// </summary>
        public Vector2? Normal { get; set; }
        /// <summary>
        /// 膝弯偏好方向（单位向量，默认朝上）
        /// </summary>
        public Vector2 KneePreference { get; set; } = -Vector2.UnitY;
        /// <summary>
        /// 失力度 0..1：基节松脱向重力向垂
        /// </summary>
        public float Limp { get; set; }

        /// <summary>
        /// 髋位置（本帧）
        /// </summary>
        public Vector2 Hip { get; private set; }
        /// <summary>
        /// 基节末端
        /// </summary>
        public Vector2 CoxaTip { get; private set; }
        /// <summary>
        /// 膝位置
        /// </summary>
        public Vector2 Knee { get; private set; }
        /// <summary>
        /// 解算后的足端（跨距钳制后可能与目标不同）
        /// </summary>
        public Vector2 FootPos { get; private set; }
        /// <summary>
        /// 当前膝极性 ±1（0 未定）
        /// </summary>
        public int KneeSign => kneeSign;
        /// <summary>
        /// 全肢触及（本帧，含 Scale）
        /// </summary>
        public float MaxReach { get; private set; }

        /// <inheritdoc/>
        protected override void Configure(Solver2DDef def) {
            valid = bones.Length >= 3 && bones[0] >= 0 && bones[1] >= 0 && bones[2] >= 0;
            if (!valid) {
                VaultMod.LoggerError($"[Rig2D:{Rig?.Name}/{Name}]", "ThreeBoneLeg needs bones [coxa, femur, tibia]");
            }
            coxaSwingMax = def.GetAngle("coxaSwingMax", 0.8f);
            kneeSpanMin = def.GetFloat("kneeSpanMin", 0.12f);
            kneeSpanMax = def.GetFloat("kneeSpanMax", 0.94f);
            kneeHysteresis = def.GetFloat("kneeHysteresis", 0.12f);
            maxReachFactor = def.GetFloat("maxReachFactor", 0.995f);
            slack = def.GetFloat("slack", 12f);
            reachTrim = def.GetFloat("reachTrim", 6f);
            targetIndex = def.GetInt("targetIndex", 0);
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
        public override void Snap() {
            kneeSign = 0;
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

        private Vector2 ResolveFoot() {
            if (targetSource != null && targetSource.TryGetTarget(targetIndex, out Vector2 t)) {
                return t;
            }
            return Foot;
        }

        private void Solve() {
            ref Bone2D coxa = ref B(0);
            ref Bone2D femur = ref B(1);
            ref Bone2D tibia = ref B(2);
            Vector2 hip = RestPosition(0);
            Vector2 normal = Normal ?? RestForward(0);
            float coxaLen = RestLength(0);
            float femurLen = RestLength(1);
            float tibiaLen = RestLength(2);
            float maxReach = coxaLen + femurLen + tibiaLen - reachTrim * Scale;
            MaxReach = maxReach;

            Vector2 foot = ResolveFoot();
            Vector2 d = foot - hip;
            float dist = d.Length();
            if (dist < 1f) {
                d = normal;
                dist = 1f;
            }
            float maxD = maxReach * maxReachFactor;
            if (dist > maxD) {
                foot = hip + d * (maxD / dist);
                d = foot - hip;
                dist = maxD;
            }
            Vector2 dir = d / dist;

            //基节：休息向 = 法线，朝目标限幅摆动；伸展吃紧时放开限幅直指目标
            float baseAng = MathF.Atan2(normal.Y, normal.X);
            float wantAng = MathF.Atan2(dir.Y, dir.X);
            float delta = MathHelper.WrapAngle(wantAng - baseAng);
            float slackPx = slack * Scale;
            float stretch01 = MathHelper.Clamp((dist - (femurLen + tibiaLen - slackPx)) / Math.Max(coxaLen + slackPx, 0.001f), 0f, 1f);
            float swingMax = MathHelper.Lerp(coxaSwingMax, MathHelper.Pi, stretch01);
            float coxaAng = baseAng + MathHelper.Clamp(delta, -swingMax, swingMax);
            if (Limp > 0.05f) {
                coxaAng = Spring2D.AngleLerp(coxaAng, MathHelper.PiOver2, Limp * 0.6f);
            }
            Vector2 coxaTip = hip + new Vector2(MathF.Cos(coxaAng), MathF.Sin(coxaAng)) * coxaLen;

            //腿节 + 胫节双骨：有效跨距钳窗
            Vector2 e = foot - coxaTip;
            float span = femurLen + tibiaLen;
            float eLen = MathHelper.Clamp(e.Length(), span * kneeSpanMin, span * kneeSpanMax);
            float eAng = MathF.Atan2(e.Y, e.X);
            float cosA = MathHelper.Clamp((femurLen * femurLen + eLen * eLen - tibiaLen * tibiaLen) / (2f * femurLen * eLen), -1f, 1f);
            float phi = MathF.Acos(cosA);

            Vector2 pref = KneePreference;
            if (pref.LengthSquared() < 0.0001f) {
                pref = -Vector2.UnitY;
            }
            else {
                pref.Normalize();
            }
            float dotP = Vector2.Dot(new Vector2(MathF.Cos(eAng + phi), MathF.Sin(eAng + phi)), pref);
            float dotM = Vector2.Dot(new Vector2(MathF.Cos(eAng - phi), MathF.Sin(eAng - phi)), pref);
            int want = dotP >= dotM ? 1 : -1;
            if (kneeSign == 0 || want != kneeSign && Math.Abs(dotP - dotM) > kneeHysteresis) {
                kneeSign = want;
            }
            float kneeAng = eAng + kneeSign * phi;
            Vector2 knee = coxaTip + new Vector2(MathF.Cos(kneeAng), MathF.Sin(kneeAng)) * femurLen;
            Vector2 footPos = coxaTip + new Vector2(MathF.Cos(eAng), MathF.Sin(eAng)) * eLen;
            Vector2 shin = footPos - knee;

            coxa.Pos = hip;
            coxa.Dir = coxaAng;
            coxa.Length = coxaLen;
            femur.Pos = coxaTip;
            femur.Dir = kneeAng;
            femur.Length = femurLen;
            tibia.Pos = knee;
            tibia.Dir = shin.LengthSquared() > 0.0001f ? MathF.Atan2(shin.Y, shin.X) : kneeAng;
            tibia.Length = tibiaLen;

            Hip = hip;
            CoxaTip = coxaTip;
            Knee = knee;
            FootPos = footPos;
        }

        /// <inheritdoc/>
        public override void DebugDraw(SpriteBatch sb, Func<Vector2, Vector2> toScreen) {
            if (!valid) {
                return;
            }
            Texture2D px = VaultAsset.placeholder2.Value;
            sb.Draw(px, toScreen(ResolveFoot()), new Rectangle(0, 0, 1, 1), Color.LimeGreen, 0f, new Vector2(0.5f), 6f, SpriteEffects.None, 0f);
            Rig2DDebugDraw.Circle(sb, toScreen, Hip, MaxReach, Color.LimeGreen * 0.3f);
        }
    }
}
