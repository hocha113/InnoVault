using InnoVault.Rigs2D.Data;
using InnoVault.Rigs2D.Runtime;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;

namespace InnoVault.Rigs2D.Solvers
{
    /// <summary>
    /// 悬链：从锚（首节静息近端）到 <see cref="Target"/> 沿一条下垂的二次贝塞尔弧把 N 节铺开——链越松垂度越大，拉满自然绷直；
    /// 可选绷直颤动（拉满瞬间链条打的那个战栗）。吊挂的工具 / 铰链 / 缆绳一类"两端受控、中间只管垂"的链用它
    /// <br/>骨骼：<c>[link1 … linkN]</c>；各节长按弧上等分弦长写入（贴图用 <c>Axis</c> 拉伸即可）
    /// <br/>参数：<c>restLength</c>（链的松弛长度，像素；缺省 = 节静息长和 × <c>restFactor</c> 1.18）、<c>sagBase</c> 10、<c>sagGain</c> 0.55、<c>sagMax</c> 120、
    /// <c>sagDir</c> (0, 1)（垂向，世界系）、<c>vibrateAmp</c> 6、<c>vibrateFreq</c> 62（rad/s）
    /// </summary>
    public sealed class HangChainSolver : Rig2DSolver
    {
        private float restLength;
        private float restFactor;
        private float sagBase;
        private float sagGain;
        private float sagMax;
        private Vector2 sagDir;
        private float vibrateAmp;
        private float vibrateFreq;
        private Vector2[] joints = [];
        private bool valid;

        /// <summary>
        /// 链末端（世界）
        /// </summary>
        public Vector2 Target { get; set; }
        /// <summary>
        /// 绷直颤动强度 0..1（拉满瞬间给 1 再自行衰减）
        /// </summary>
        public float Vibrate { get; set; }
        /// <summary>
        /// 锚位置（本帧）
        /// </summary>
        public Vector2 Mount { get; private set; }
        /// <summary>
        /// 本帧垂度（像素）
        /// </summary>
        public float Sag { get; private set; }
        /// <summary>
        /// 关节数（节数 + 1）
        /// </summary>
        public int JointCount => joints.Length;

        /// <summary>
        /// 第 j 个关节位置（0 = 锚，N = 末端）
        /// </summary>
        public Vector2 Joint(int j) => j >= 0 && j < joints.Length ? joints[j] : Mount;

        /// <inheritdoc/>
        protected override void Configure(Solver2DDef def) {
            int n = bones.Length;
            valid = n >= 1;
            for (int i = 0; i < n && valid; i++) {
                valid = bones[i] >= 0;
            }
            if (!valid) {
                VaultMod.LoggerError($"[Rig2D:{Rig?.Name}/{Name}]", "HangChain needs at least one bone [link1 … linkN]");
                return;
            }
            restLength = def.GetFloat("restLength", 0f);
            restFactor = def.GetFloat("restFactor", 1.18f);
            sagBase = def.GetFloat("sagBase", 10f);
            sagGain = def.GetFloat("sagGain", 0.55f);
            sagMax = def.GetFloat("sagMax", 120f);
            sagDir = def.GetVector2("sagDir", Vector2.UnitY);
            if (sagDir.LengthSquared() < 0.0001f) {
                sagDir = Vector2.UnitY;
            }
            sagDir.Normalize();
            vibrateAmp = def.GetFloat("vibrateAmp", 6f);
            vibrateFreq = def.GetFloat("vibrateFreq", 62f);
            if (joints.Length != n + 1) {
                joints = new Vector2[n + 1];
            }
        }

        /// <inheritdoc/>
        public override void Snap() => Solve();

        /// <inheritdoc/>
        public override void Step(float dt) => Solve();

        private void Solve() {
            if (!valid) {
                return;
            }
            int n = bones.Length;
            Mount = RestPosition(0);
            float rest = restLength > 0f ? restLength * Scale : 0f;
            if (rest <= 0f) {
                for (int i = 0; i < n; i++) {
                    rest += RestLength(i);
                }
                rest *= restFactor;
            }
            float dist = Vector2.Distance(Mount, Target);
            //rest 与 dist 都已是世界量（含 Scale），松弛项不再乘 Scale；只有像素常量 sagBase 需要
            Sag = sagBase * Scale + MathHelper.Clamp(rest - dist, 0f, sagMax * Scale) * sagGain;
            Vector2 mid = (Mount + Target) * 0.5f + sagDir * Sag;
            if (Vibrate > 0.001f && dist > 0.001f) {
                Vector2 along = (Target - Mount) / dist;
                Vector2 perp = new(-along.Y, along.X);
                float t = Rig.Time * Spring2D.FrameSeconds;
                mid += perp * (MathF.Sin(t * vibrateFreq + Rig.Seed) * MathHelper.Clamp(Vibrate, 0f, 1f) * vibrateAmp * Scale);
            }
            for (int k = 0; k <= n; k++) {
                float u = k / (float)n;
                joints[k] = Vector2.Lerp(Vector2.Lerp(Mount, mid, u), Vector2.Lerp(mid, Target, u), u);
            }
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
            if (!valid) {
                return;
            }
            Texture2D px = VaultAsset.placeholder2.Value;
            for (int j = 0; j < joints.Length; j++) {
                sb.Draw(px, toScreen(joints[j]), new Rectangle(0, 0, 1, 1), Color.SkyBlue, 0f, new Vector2(0.5f), 4f, SpriteEffects.None, 0f);
            }
            sb.Draw(px, toScreen(Target), new Rectangle(0, 0, 1, 1), Color.LimeGreen, 0f, new Vector2(0.5f), 6f, SpriteEffects.None, 0f);
        }
    }
}
