using InnoVault.Rigs2D.Data;
using InnoVault.Rigs2D.Runtime;
using Microsoft.Xna.Framework;
using System;

namespace InnoVault.Rigs2D.Solvers
{
    /// <summary>
    /// 脊链跟随：每节按"位置跟随（自然拖拽）"与"姿态期望（卷曲 + 行波）"按卷曲强度混合，
    /// 相邻节相对角硬钳制防折叠，节向角平滑趋近，节位落在前节后方一个节距处
    /// <br/>骨骼：<c>[节1, 节2, …]</c>，链头 = 节1 的父骨骼（或实例根）；每节的 <c>Pos</c> 是节中心，<c>Dir</c> 指向前进方向
    /// <br/>参数：<c>maxBend</c> 0.5、<c>turnRate</c> 0.38、<c>poseWeightBase</c> 0.3、<c>poseWeightCurl</c> 0.6、<c>curlGain</c> 1.6、
    /// <c>curlPerJoint</c> 0.44、<c>waveAmp</c> 0.085、<c>waveStep</c> 1.15、<c>waveAdvance</c> 0.05、<c>speedNorm</c> 9、
    /// <c>speedMin</c> 0.15、<c>speedMax</c> 1.4、<c>downSign</c> 1、<c>gaps</c>（节距数组，缺省用各节骨长）
    /// </summary>
    public sealed class ChainFollowSolver : Rig2DSolver
    {
        private float maxBend;
        private float turnRate;
        private float poseWeightBase;
        private float poseWeightCurl;
        private float curlGain;
        private float curlPerJoint;
        private float waveAmp;
        private float waveStep;
        private float waveAdvance;
        private float speedNorm;
        private float speedMin;
        private float speedMax;
        private float downSign;
        private float[] gaps = [];

        /// <summary>
        /// 卷曲 −1..1：负 = 向 downSign 反侧卷，正 = 同侧卷，0 自然
        /// </summary>
        public float Curl { get; set; }
        /// <summary>
        /// 行波增益 0..2
        /// </summary>
        public float WaveGain { get; set; } = 1f;
        /// <summary>
        /// 当前行进速度（px/帧），决定行波幅度与相位推进
        /// </summary>
        public float Speed { get; set; }
        /// <summary>
        /// 行波相位（<see cref="AutoAdvanceWave"/> 时按路程自动推进）
        /// </summary>
        public float WavePhase { get; set; }
        /// <summary>
        /// 是否按 <see cref="Speed"/> 自动推进相位（停住波也停）
        /// </summary>
        public bool AutoAdvanceWave { get; set; } = true;

        /// <inheritdoc/>
        protected override void Configure(Solver2DDef def) {
            maxBend = def.GetAngle("maxBend", 0.5f);
            turnRate = def.GetFloat("turnRate", 0.38f);
            poseWeightBase = def.GetFloat("poseWeightBase", 0.3f);
            poseWeightCurl = def.GetFloat("poseWeightCurl", 0.6f);
            curlGain = def.GetFloat("curlGain", 1.6f);
            curlPerJoint = def.GetAngle("curlPerJoint", 0.44f);
            waveAmp = def.GetAngle("waveAmp", 0.085f);
            waveStep = def.GetAngle("waveStep", 1.15f);
            waveAdvance = def.GetFloat("waveAdvance", 0.05f);
            speedNorm = def.GetFloat("speedNorm", 9f);
            speedMin = def.GetFloat("speedMin", 0.15f);
            speedMax = def.GetFloat("speedMax", 1.4f);
            downSign = def.GetFloat("downSign", 1f) >= 0f ? 1f : -1f;
            gaps = def.GetFloatArray("gaps");
        }

        private float Gap(int i) => i < gaps.Length ? gaps[i] * Scale : RestLength(i);

        private void Leader(out Vector2 pos, out float dir) {
            pos = ParentPos(0);
            dir = ParentDir(0);
        }

        /// <inheritdoc/>
        public override void Snap() {
            if (bones.Length == 0) {
                return;
            }
            Leader(out Vector2 prevPos, out float dir);
            Vector2 fwd = new(MathF.Cos(dir), MathF.Sin(dir));
            for (int i = 0; i < bones.Length; i++) {
                ref Bone2D node = ref B(i);
                float gap = Gap(i);
                node.Dir = dir;
                node.Pos = prevPos - fwd * gap;
                node.Length = gap;
                prevPos = node.Pos;
            }
        }

        /// <inheritdoc/>
        public override void Step(float dt) {
            if (bones.Length == 0) {
                return;
            }
            if (AutoAdvanceWave) {
                WavePhase += Speed * waveAdvance * WaveGain * dt;
            }
            float curl = MathHelper.Clamp(Curl, -1f, 1f);
            float poseWeight = poseWeightBase + poseWeightCurl * Math.Min(1f, Math.Abs(curl) * curlGain);
            float speedFactor = MathHelper.Clamp(Speed / Math.Max(speedNorm, 0.001f), speedMin, speedMax);
            float rate = Spring2D.RateForDt(turnRate, dt);

            Leader(out Vector2 frontPos, out float frontDir);
            for (int i = 0; i < bones.Length; i++) {
                ref Bone2D node = ref B(i);
                float gap = Gap(i);

                Vector2 toFront = frontPos - node.Pos;
                float natural = toFront.LengthSquared() < 0.01f ? frontDir : MathF.Atan2(toFront.Y, toFront.X);

                float curlOff = curl * curlPerJoint * downSign;
                float waveOff = MathF.Sin(WavePhase - (i + 1) * waveStep) * waveAmp * WaveGain * speedFactor;
                float posed = frontDir + curlOff + waveOff;

                float blended = natural + MathHelper.WrapAngle(posed - natural) * poseWeight;
                float rel = MathHelper.Clamp(MathHelper.WrapAngle(blended - frontDir), -maxBend, maxBend);
                float wantDir = frontDir + rel;

                node.Dir = Spring2D.AngleLerp(node.Dir, wantDir, rate);
                node.Pos = frontPos - new Vector2(MathF.Cos(node.Dir), MathF.Sin(node.Dir)) * gap;
                node.Length = gap;

                frontPos = node.Pos;
                frontDir = node.Dir;
            }
        }
    }
}
