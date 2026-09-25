using InnoVault.Rigs2D.Data;
using InnoVault.Rigs2D.Runtime;
using InnoVault.Rigs2D.Solvers;
using Microsoft.Xna.Framework;
using System;

namespace InnoVault.Rigs2D.Animation
{
    /// <summary>
    /// 运动层：按体速与相位出双脚（或四脚）目标、足角、盆骨起伏与前倾，写进动画机输出。
    /// <br/>支撑期足目标相对锚点按体速后退（锚点随体前移时世界里不滑步），摆越期抬膝前送、落地前脚尖上翘脚跟先着；
    /// 走在双支撑最低、跑在支撑中段最低（由各档 <see cref="Gait2DMode.BobOffset"/> 定）。多档按 <see cref="Blend"/> 逐项插值。
    /// <br/>相位是本类唯一的时间状态：消费方可以每帧 <see cref="Advance"/>，也可以直接写 <see cref="Phase"/>（已同步的计时折算），各端同输入同输出。
    /// <br/>地形适配（<see cref="ApplyTerrain"/>）：逐脚探脚下地面相对锚点的高差，脚目标跟着落，盆骨跟着低的那只脚沉、两脚都高就抬
    /// </summary>
    public sealed class Rig2DGait
    {
        private bool[] planted = [];
        private bool[] justPlanted = [];
        private float[] ground = [];

        /// <summary>
        /// 所属实例
        /// </summary>
        public Rig2DInstance Rig { get; }
        /// <summary>
        /// 定义
        /// </summary>
        public Gait2DDef Def { get; private set; }
        /// <summary>
        /// 周期相位（0~1）
        /// </summary>
        public float Phase { get; set; }
        /// <summary>
        /// 体速（骨架单位 / 帧，恒正）
        /// </summary>
        public float Speed { get; set; }
        /// <summary>
        /// 档混合：0 = 第一档，1 = 第二档…（小数插值）
        /// </summary>
        public float Blend { get; set; }
        /// <summary>
        /// 脚目标缩放（x / y 直接同乘）。注意支撑脚会随之按 (1 − 倍率) 的比例相对世界滑动；想步子小又不滑用 <see cref="StrideScale"/>
        /// </summary>
        public float StepScale { get; set; } = 1f;
        /// <summary>
        /// 步幅缩放（蹲着走 / 小碎步）：步幅、落脚前伸、抬脚同乘，周期跟着变短，支撑段后退速度仍等于体速，不滑步
        /// </summary>
        public float StrideScale { get; set; } = 1f;
        /// <summary>
        /// 写进输出时的权重（0 = 不出力，站定时渐回底姿用）
        /// </summary>
        public float Weight { get; set; } = 1f;
        /// <summary>
        /// 地形适配权重（0 = 关；空中、砸地腾空时关）
        /// </summary>
        public float TerrainWeight { get; set; }
        /// <summary>
        /// 探地器（空 = <see cref="Rig2DGround.DefaultProbe"/>：游戏里物块射线，离线宿主给高度表）
        /// </summary>
        public Rig2DGroundProbe Probe { get; set; }
        /// <summary>
        /// 各腿本帧是否在支撑期（最近一次 <see cref="Advance"/> 之后）
        /// </summary>
        public ReadOnlySpan<bool> Planted => planted;
        /// <summary>
        /// 各腿是否在最近一次 <see cref="Advance"/> 里刚着地（落步事件：尘、震屏、脚步声）
        /// </summary>
        public ReadOnlySpan<bool> JustPlanted => justPlanted;
        /// <summary>
        /// 各腿最近一次地形适配的地面高差（世界像素，正 = 更低，已乘权重）
        /// </summary>
        public ReadOnlySpan<float> Ground => ground;
        /// <summary>
        /// 最近一次地形适配给盆骨的下沉量（世界像素，正 = 下）
        /// </summary>
        public float TerrainDrop { get; private set; }

        /// <summary>
        /// 按定义建运动层
        /// </summary>
        public Rig2DGait(Rig2DInstance rig, Gait2DDef def) {
            Rig = rig;
            Bind(def);
        }

        /// <summary>
        /// 按名取骨架里的运动层定义；缺失时定义为空（不出力）
        /// </summary>
        public Rig2DGait(Rig2DInstance rig, string name) : this(rig, rig?.Definition?.GaitValue(name)) {
        }

        /// <summary>
        /// 换定义（热重载后按名重取时用），相位保留
        /// </summary>
        public void Bind(Gait2DDef def) {
            Def = def;
            int n = def?.Legs.Count ?? 0;
            if (planted.Length != n) {
                planted = new bool[n];
                justPlanted = new bool[n];
                ground = new float[n];
            }
        }

        //==================== 档插值 ====================

        private struct Mix
        {
            public float Stance, Stride, CycleMin, CycleMax, SpeedSlow, SpeedFast, ReachBias, FarOffset, Skew, Toe;
            public bool Centered;
            public Gait2DMode A, B;
            public float T;
        }

        private Mix Current() {
            Mix m = default;
            int count = Def.Modes.Count;
            if (count == 0) {
                m.A = m.B = new Gait2DMode();
            }
            else {
                float b = MathHelper.Clamp(Blend, 0f, count - 1);
                //始终落在相邻两档之间（末档取 t = 1 而不是自插自）：与"两档参数先插值再求值"的写法逐位一致
                int i0 = count >= 2 ? Math.Min((int)MathF.Floor(b), count - 2) : 0;
                int i1 = Math.Min(i0 + 1, count - 1);
                m.A = Def.Modes[i0];
                m.B = Def.Modes[i1];
                m.T = i1 == i0 ? 0f : b - i0;
            }
            Gait2DMode a = m.A, c = m.B;
            float t = m.T;
            m.Stance = MathHelper.Lerp(a.Stance, c.Stance, t);
            m.Stride = MathHelper.Lerp(a.Stride, c.Stride, t);
            m.CycleMin = MathHelper.Lerp(a.CycleMin, c.CycleMin, t);
            m.CycleMax = MathHelper.Lerp(a.CycleMax, c.CycleMax, t);
            if (StrideScale != 1f) {
                float k = MathF.Max(StrideScale, 0.05f);
                m.Stride *= k;
                m.CycleMin *= k;
                m.CycleMax *= k;
            }
            m.SpeedSlow = MathHelper.Lerp(a.SpeedSlow, c.SpeedSlow, t);
            m.SpeedFast = MathHelper.Lerp(a.SpeedFast, c.SpeedFast, t);
            m.ReachBias = MathHelper.Lerp(a.ReachBias, c.ReachBias, t);
            m.FarOffset = MathHelper.Lerp(a.FarOffset, c.FarOffset, t);
            m.Skew = MathHelper.Lerp(a.Skew, c.Skew, t);
            m.Toe = MathHelper.Lerp(a.Toe, c.Toe, t);
            m.Centered = t < 0.5f ? a.Centered : c.Centered;
            return m;
        }

        private static float PerLeg(float[] arr, float scalar, int leg) => arr != null && leg < arr.Length ? arr[leg] : scalar;

        private static float LegValue(in Mix m, Func<Gait2DMode, float[]> arr, Func<Gait2DMode, float> scalar, int leg)
            => MathHelper.Lerp(PerLeg(arr(m.A), scalar(m.A), leg), PerLeg(arr(m.B), scalar(m.B), leg), m.T);

        private float LegPhase(in Mix m, int leg) {
            float own = Def.Legs[leg].Phase;
            return MathHelper.Lerp(PerLeg(m.A.Phases, own, leg), PerLeg(m.B.Phases, own, leg), m.T);
        }

        private static float Smooth(float x) {
            x = MathHelper.Clamp(x, 0f, 1f);
            return x * x * (3f - 2f * x);
        }

        //==================== 求值 ====================

        /// <summary>
        /// 一整个周期的帧数：按步幅（步幅 / 体速，钳在上下限）或按体速区间插值
        /// </summary>
        public float Cycle(float speed) {
            if (Def == null) {
                return 1f;
            }
            return Cycle(Current(), speed);
        }

        private static float Cycle(in Mix m, float speed) {
            if (m.SpeedSlow != 0f || m.SpeedFast != 0f) {
                float span = m.SpeedFast - m.SpeedSlow;
                float k = span != 0f ? (speed - m.SpeedSlow) / span : 1f;
                return MathHelper.Lerp(m.CycleMax, m.CycleMin, Smooth(k));
            }
            if (speed < 0.05f) {
                return m.CycleMax;
            }
            return MathHelper.Clamp(m.Stride / speed, m.CycleMin, m.CycleMax);
        }

        /// <summary>
        /// 按体速推进相位，并刷新各腿支撑状态与落步事件
        /// </summary>
        public void Advance(float speed, float dt = 1f) {
            Speed = speed;
            if (Def == null) {
                return;
            }
            Phase += dt / MathF.Max(Cycle(speed), Def.MinCycle);
            Phase -= MathF.Floor(Phase);
            RefreshPlanted();
        }

        /// <summary>
        /// 按当前相位重算支撑状态（直接写 <see cref="Phase"/> 之后调，落步事件同样按"本次支撑、上次不支撑"判）
        /// </summary>
        public void RefreshPlanted() {
            if (Def == null) {
                return;
            }
            for (int i = 0; i < planted.Length; i++) {
                Foot(i, out _, out _, out _, out bool now);
                justPlanted[i] = now && !planted[i];
                planted[i] = now;
            }
        }

        /// <summary>
        /// 第 <paramref name="leg"/> 条腿在当前相位的足目标（锚点系，骨架单位，未乘 <see cref="StepScale"/>）、足角与是否支撑
        /// </summary>
        public void Foot(int leg, out float x, out float y, out float a, out bool isPlanted) {
            Mix m = Current();
            Gait2DDef d = Def;
            float cycle = Cycle(m, Speed);
            float u = Phase - LegPhase(m, leg);
            u -= MathF.Floor(u);
            float travel = Speed * m.Stance * cycle;
            float reachAt = LegValue(m, g => g.Reach, g => g.ReachBias, leg);
            float off = d.Legs[leg].Offset * m.FarOffset;
            if (StrideScale != 1f) {
                float k = MathF.Max(StrideScale, 0.05f);
                reachAt *= k;
                off *= k;
            }
            float reach = m.Centered ? travel * 0.5f + reachAt : reachAt;
            if (u < m.Stance) {
                float s = u / m.Stance;
                x = reach - travel * s + off;
                y = 0f;
                a = 0f;
                if (s < d.HeelSpan) {
                    a = d.Heel * (1f - s / d.HeelSpan);
                }
                else if (s > d.ToeOffStart) {
                    a = d.ToeOff * Smooth((s - d.ToeOffStart) / d.ToeOffSpan);
                }
                isPlanted = true;
                return;
            }
            float w = (u - m.Stance) / (1f - m.Stance);
            float lift = LegValue(m, g => g.Lifts, g => g.Lift, leg);
            if (StrideScale != 1f) {
                lift *= MathF.Max(StrideScale, 0.05f);
            }
            x = MathHelper.Lerp(reach - travel, reach, Smooth(w)) + off;
            //float 下 sin(MathF.PI) ≈ −8.7e-8：负底数的非整数次幂是 NaN，末段先钳到 0
            float arc = MathF.Max(MathF.Sin(MathF.PI * MathF.Min(MathF.Pow(w, m.Skew) * d.LiftStretch, 1f)), 0f);
            y = -lift * MathF.Pow(arc, d.LiftPow);
            a = MathHelper.Lerp(m.Toe, d.SwingToe, Smooth(w));
            isPlanted = false;
        }

        /// <summary>
        /// 盆骨起伏（正 = 下沉）与前倾：各档分别求值再按 <see cref="Blend"/> 插
        /// </summary>
        public void Body(out float bob, out float lean) {
            if (Def == null) {
                bob = lean = 0f;
                return;
            }
            Mix m = Current();
            float k = MathF.Min(Speed / Def.BobSpeed, 1f);
            ModeBody(m.A, k, out float bobA, out float leanA);
            ModeBody(m.B, k, out float bobB, out float leanB);
            bob = MathHelper.Lerp(bobA, bobB, m.T);
            lean = MathHelper.Lerp(leanA, leanB, m.T);
        }

        private void ModeBody(Gait2DMode g, float k, out float bob, out float lean) {
            float offset = float.IsNaN(g.BobOffset) ? g.Stance * 0.5f : g.BobOffset;
            float wave = 0.5f + 0.5f * MathF.Cos(4f * MathF.PI * (Phase - offset));
            if (g.BobPow != 1f) {
                wave = MathF.Pow(wave, g.BobPow);
            }
            bob = (g.SpeedScaled ? g.Bob * k : g.Bob) * wave + g.BobBase;
            lean = g.SpeedScaled ? g.Lean * k : g.Lean;
            if (g.LeanWave != 0f) {
                lean += g.LeanWave * MathF.Sin(4f * MathF.PI * (Phase - g.LeanWaveOffset));
            }
        }

        /// <summary>
        /// 写进一副姿态：足目标与足角按 <paramref name="weight"/> 插向步态值，盆骨起伏与前倾按权重加上去
        /// </summary>
        public void Sample(Rig2DPose pose, float weight) {
            if (Def == null || pose == null || weight <= 0f) {
                return;
            }
            for (int i = 0; i < Def.Legs.Count; i++) {
                Gait2DLeg leg = Def.Legs[i];
                Foot(i, out float x, out float y, out float a, out _);
                if (leg.ChannelIndex >= 0) {
                    Vector2 target = new(x * StepScale, y * StepScale);
                    pose.SetVector(leg.ChannelIndex, weight >= 1f ? target : Vector2.Lerp(pose.Vector(leg.ChannelIndex), target, weight));
                }
                if (leg.AngleIndex >= 0) {
                    pose[leg.AngleIndex] = weight >= 1f ? a : MathHelper.Lerp(pose[leg.AngleIndex], a, weight);
                }
            }
            Body(out float bob, out float lean);
            AddHip(pose, bob * weight);
            if (Def.TiltIndex >= 0) {
                pose[Def.TiltIndex] += lean * weight;
            }
        }

        private void AddHip(Rig2DPose pose, float amount) {
            int hi = Def.HipIndex;
            if (hi < 0) {
                return;
            }
            if (Rig.Definition.Channels[hi].IsScalar) {
                pose[hi] += amount;
                return;
            }
            Vector2 v = pose.Vector(hi);
            if (Def.HipAxis == 0) {
                v.X += amount;
            }
            else {
                v.Y += amount;
            }
            pose.SetVector(hi, v);
        }

        /// <summary>
        /// 地形适配：逐脚探脚下地面相对锚点的高差（钳在 [−StepUp, StepDown] × Scale），脚目标跟着落；
        /// 盆骨下沉 = max(0, 最低脚) × <see cref="Gait2DDef.SinkLow"/> + min(0, (最高脚 + 最低脚) / 2) × <see cref="Gait2DDef.RaiseBoth"/>
        /// + min(0, 最低脚) × <see cref="Gait2DDef.RaiseLow"/>（高差正 = 更低）
        /// </summary>
        public void ApplyTerrain(Rig2DPose pose, float weight = 1f) {
            TerrainDrop = 0f;
            Array.Clear(ground);
            float tw = TerrainWeight * weight;
            if (Def == null || pose == null || tw <= 0f || Def.Legs.Count == 0) {
                return;
            }
            float s = MathF.Max(Rig.Scale, 0.001f);
            Vector2 anchor = Rig.Anchor;
            float up = Def.StepUp * s;
            float down = Def.StepDown * s;
            Rig2DGroundProbe probe = Probe ?? Rig2DGround.DefaultProbe;
            float low = float.MinValue, high = float.MaxValue;
            for (int i = 0; i < Def.Legs.Count; i++) {
                int ci = Def.Legs[i].ChannelIndex;
                if (ci < 0) {
                    continue;
                }
                Vector2 v = pose.Vector(ci);
                float wx = anchor.X + v.X * s * Rig.MirrorSign;
                float off = 0f;
                if (probe(new Vector2(wx, anchor.Y - up), Vector2.UnitY, up + down, out Vector2 hit)) {
                    off = MathHelper.Clamp(hit.Y - anchor.Y, -up, down);
                }
                off *= tw;
                ground[i] = off;
                pose.SetVector(ci, new Vector2(v.X, v.Y + off / s));
                low = MathF.Max(low, off);
                high = MathF.Min(high, off);
            }
            if (low == float.MinValue) {
                return;
            }
            float drop = MathF.Max(0f, low) * Def.SinkLow + MathF.Min(0f, (high + low) * 0.5f) * Def.RaiseBoth;
            if (Def.RaiseLow != 0f) {
                drop += MathF.Min(0f, low) * Def.RaiseLow;
            }
            TerrainDrop = drop;
            AddHip(pose, drop / s);
        }
    }
}
