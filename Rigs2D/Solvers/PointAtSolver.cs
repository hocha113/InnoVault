using InnoVault.Rigs2D.Data;
using InnoVault.Rigs2D.Runtime;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;

namespace InnoVault.Rigs2D.Solvers
{
    /// <summary>
    /// 单骨指向：骨骼从近端指向目标点，骨长可按距离拉伸并钳制（配 <c>Piece2DStretch.Uniform / Axis</c> 即"一张贴图拉到足端"的步足画法）
    /// <br/>骨骼：<c>[骨]</c>；参数：<c>minLength</c> 0、<c>maxLength</c> 0（0 = 不钳）、<c>rotateOnly</c> false（真则保持静息骨长）、
    /// <c>turnRate</c> 1（每帧朝向追近比例，1 = 瞬时）、<c>targetSolver</c> / <c>targetIndex</c>
    /// <br/>炮塔限位：<c>maxDeviation</c> 0（弧度，0 关；追近后把朝向钳在参考方向 ± 此值内）、
    /// <c>deviationRef</c> "rest"（<c>rest</c> 以本骨静息轴向为参考 / <c>world</c> 以 <c>refDir</c> 世界角为参考）、<c>refDir</c> 0（世界弧度，<c>world</c> 模式用）
    /// <br/>后坐：<see cref="Kick"/> 注入角速度，每帧加进朝向后按 <c>angularDamping</c> 0.96 衰减；后坐加在限位之后，允许瞬时越出限位（枪口被顶开的那一下）
    /// </summary>
    public sealed class PointAtSolver : Rig2DSolver
    {
        private float minLength;
        private float maxLength;
        private bool rotateOnly;
        private float turnRate;
        private float maxDeviation;
        private bool worldRef;
        private float refDir;
        private float angularDamping;
        private IRig2DTargetSource targetSource;
        private int targetIndex;
        private bool init;

        /// <summary>
        /// 目标点（世界）
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
        /// 近端到目标的原始距离（钳制前）
        /// </summary>
        public float Distance { get; private set; }
        /// <summary>
        /// 当前后坐角速度（弧度 / 帧），每帧加进朝向后衰减；直接写或用 <see cref="Kick"/> 叠加
        /// </summary>
        public float AngularVelocity { get; set; }
        /// <summary>
        /// 本帧是否被限位钳到了边界
        /// </summary>
        public bool Clamped { get; private set; }

        /// <summary>
        /// 注入一次角冲量（开火后坐 / 受击甩枪），正负决定甩向
        /// </summary>
        public void Kick(float angularImpulse) => AngularVelocity += angularImpulse;

        /// <inheritdoc/>
        protected internal override int ChannelProperty(string prop, out bool spatial) {
            spatial = prop == "target";
            return prop == "target" ? 0 : -1;
        }

        /// <inheritdoc/>
        protected internal override void SetChannel(int property, Vector2 value) {
            if (property == 0) {
                Target = value;
            }
        }

        /// <inheritdoc/>
        protected override void Configure(Solver2DDef def) {
            minLength = def.GetFloat("minLength", 0f);
            maxLength = def.GetFloat("maxLength", 0f);
            rotateOnly = def.GetBool("rotateOnly", false);
            turnRate = def.GetFloat("turnRate", 1f);
            maxDeviation = Math.Max(def.GetAngle("maxDeviation", 0f), 0f);
            worldRef = def.GetString("deviationRef", "rest").ToLowerInvariant() == "world";
            refDir = def.GetAngle("refDir", 0f);
            angularDamping = MathHelper.Clamp(def.GetFloat("angularDamping", 0.96f), 0f, 1f);
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

        private Vector2 ResolveTarget() {
            if (targetSource != null && targetSource.TryGetTarget(targetIndex, out Vector2 t)) {
                return t;
            }
            return Target;
        }

        /// <inheritdoc/>
        public override void Snap() {
            init = false;
            AngularVelocity = 0f;
            Solve(1f);
        }

        /// <inheritdoc/>
        public override void Step(float dt) => Solve(dt);

        private void Solve(float dt) {
            if (bones.Length == 0) {
                return;
            }
            ref Bone2D b = ref B(0);
            b.Pos = RestPosition(0);
            Vector2 d = ResolveTarget() - b.Pos;
            float dist = d.Length();
            Distance = dist;
            if (dist > 0.5f) {
                float want = MathF.Atan2(d.Y, d.X);
                b.Dir = !init || turnRate >= 1f ? want : Spring2D.AngleLerp(b.Dir, want, Spring2D.RateForDt(turnRate, dt));
            }
            init = true;

            //炮塔限位：相对静息轴向（随骨架镜像）或世界参考角
            Clamped = false;
            if (maxDeviation > 0f) {
                float reference = worldRef ? refDir : RestDirection(0);
                float rel = MathHelper.WrapAngle(b.Dir - reference);
                if (Math.Abs(rel) > maxDeviation) {
                    b.Dir = MathHelper.WrapAngle(reference + Math.Sign(rel) * maxDeviation);
                    Clamped = true;
                }
            }

            //后坐：角速度累加进朝向再衰减，加在限位之后
            if (AngularVelocity != 0f) {
                b.Dir = MathHelper.WrapAngle(b.Dir + AngularVelocity * dt);
                AngularVelocity *= dt == 1f ? angularDamping : MathF.Pow(angularDamping, dt);
                if (Math.Abs(AngularVelocity) < 0.00001f) {
                    AngularVelocity = 0f;
                }
            }

            if (rotateOnly) {
                b.Length = RestLength(0);
                return;
            }
            float len = dist;
            if (minLength > 0f) {
                len = Math.Max(len, minLength * Scale);
            }
            if (maxLength > 0f) {
                len = Math.Min(len, maxLength * Scale);
            }
            b.Length = len;
        }

        /// <inheritdoc/>
        public override void DebugDraw(SpriteBatch sb, Func<Vector2, Vector2> toScreen) {
            Texture2D px = Rig2DDebugDraw.Pixel;
            if (px == null) {
                return;
            }
            sb.Draw(px, toScreen(ResolveTarget()), new Rectangle(0, 0, 1, 1), Color.DeepSkyBlue, 0f, new Vector2(0.5f), 5f, SpriteEffects.None, 0f);
        }
    }
}
