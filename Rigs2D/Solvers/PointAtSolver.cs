using InnoVault.Rigs2D.Data;
using InnoVault.Rigs2D.Runtime;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using Terraria;

namespace InnoVault.Rigs2D.Solvers
{
    /// <summary>
    /// 单骨指向：骨骼从近端指向目标点，骨长可按距离拉伸并钳制（配 <c>Piece2DStretch.Uniform / Axis</c> 即"一张贴图拉到足端"的步足画法）
    /// <br/>骨骼：<c>[骨]</c>；参数：<c>minLength</c> 0、<c>maxLength</c> 0（0 = 不钳）、<c>rotateOnly</c> false（真则保持静息骨长）、
    /// <c>turnRate</c> 1（每帧朝向追近比例，1 = 瞬时）、<c>targetSolver</c> / <c>targetIndex</c>
    /// </summary>
    public sealed class PointAtSolver : Rig2DSolver
    {
        private float minLength;
        private float maxLength;
        private bool rotateOnly;
        private float turnRate;
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

        /// <inheritdoc/>
        protected override void Configure(Solver2DDef def) {
            minLength = def.GetFloat("minLength", 0f);
            maxLength = def.GetFloat("maxLength", 0f);
            rotateOnly = def.GetBool("rotateOnly", false);
            turnRate = def.GetFloat("turnRate", 1f);
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
            Texture2D px = VaultAsset.placeholder2.Value;
            sb.Draw(px, toScreen(ResolveTarget()), new Rectangle(0, 0, 1, 1), Color.DeepSkyBlue, 0f, new Vector2(0.5f), 5f, SpriteEffects.None, 0f);
        }
    }
}
