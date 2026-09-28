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
    /// <br/>不开锁步时相位是本类唯一的时间状态：消费方可以每帧 <see cref="Advance(float, float)"/>，也可以直接写 <see cref="Phase"/>（已同步的计时折算），各端同输入同输出。
    /// <br/>地形适配（<see cref="ApplyTerrain"/>）：逐脚探脚下地面相对锚点的高差，脚目标跟着落，盆骨跟着低的那只脚沉、两脚都高就抬
    /// <br/>锁步（<see cref="Gait2DDef.LockStance"/>）：支撑脚的位置由 <see cref="Advance(float, float)"/> 逐帧按锚点实际位移推，摆越从离地点送到当前落点，
    /// 体速怎么变支撑脚都不滑；离地是事件（相位过离地点那一帧），离了地就按离地时那一档的摆越时长摆完才落，摆越进度只累计相位本身的推进，
    /// 换档改了支撑占比、挪了逐腿相位也不会把支撑脚甩进半空、把摆越脚按回地上、把摆越催快。没推过（或 <see cref="RefreshPlanted"/> 之后）时退回按相位现算，
    /// 探针与直接写相位的用法不受影响。锁步下还有：跨距不到 <see cref="Gait2DDef.MinStep"/> 的一步不迈（脚钉在原地到下一次离地）、
    /// 被拖过 <see cref="Gait2DDef.LateSlack"/> 提前离地、身体波动随迈步活跃度（<see cref="Gait2DDef.StepWaves"/>）、按体速起停收步的 <see cref="Drive"/>
    /// </summary>
    public sealed class Rig2DGait
    {
        private bool[] planted = [];
        private bool[] justPlanted = [];
        private float[] ground = [];
        //锁步状态：支撑脚的足目标 x、本次摆越的离地点 x（锚点系，骨架单位，未乘 StepScale）
        private float[] footX = [];
        private float[] liftX = [];
        //正在摆越、离地后累计推进的相位、这一步的摆越时长（相位，离地时按那一档定）、离地时瞄的落点（摆越中只往前追，不往回缩）
        private bool[] swinging = [];
        private float[] swingAcc = [];
        private float[] swingSpan = [];
        private float[] landX = [];
        //这一步没迈（跨距不到 MinStep）：支撑到下一次离地；上一帧相位落在摆越窗内（离地事件按它判）
        private bool[] hold = [];
        private bool[] swingWindow = [];
        //逐腿相位偏移（周期比例）：拖脚提前离地时偏过去，着地期慢慢收回
        private float[] slip = [];
        //同一对腿（首档相位差约半个周期：前两腿、后两腿、双足的两腿）的另一条，没有 = −1
        private int[] partner = [];
        private bool lockValid;
        private float activity = 1f;

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
        /// 各腿本帧是否在支撑期（最近一次 <see cref="Advance(float, float)"/> 之后）
        /// </summary>
        public ReadOnlySpan<bool> Planted => planted;
        /// <summary>
        /// 各腿是否在最近一次 <see cref="Advance(float, float)"/> 里刚着地（落步事件：尘、震屏、脚步声）
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
        /// 迈步活跃度 0~1（<see cref="Gait2DDef.StepWaves"/> 开时身体波动乘它；没开恒为 1）
        /// </summary>
        public float Activity => Def != null && Def.LockStance && Def.StepWaves ? activity : 1f;
        /// <summary>
        /// <see cref="Drive"/> 的起停状态：在迈步（过了起步门槛，还没落到停步门槛以下）
        /// </summary>
        public bool Striding { get; set; }

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
                footX = new float[n];
                liftX = new float[n];
                swinging = new bool[n];
                swingAcc = new float[n];
                swingSpan = new float[n];
                landX = new float[n];
                hold = new bool[n];
                swingWindow = new bool[n];
                slip = new float[n];
                partner = new int[n];
            }
            lockValid = false;
            Array.Clear(hold);
            Array.Clear(slip);
            for (int i = 0; i < n; i++) {
                partner[i] = -1;
                float best = 0.15f;
                for (int j = 0; j < n; j++) {
                    if (j == i) {
                        continue;
                    }
                    float d = MathF.Abs(BasePhase(def, j) - BasePhase(def, i));
                    d -= MathF.Floor(d);
                    float off = MathF.Abs(d - 0.5f);
                    if (off < best) {
                        best = off;
                        partner[i] = j;
                    }
                }
            }
        }

        private static float BasePhase(Gait2DDef def, int leg) {
            float[] ph = def.Modes.Count > 0 ? def.Modes[0].Phases : null;
            return ph != null && leg < ph.Length ? ph[leg] : def.Legs[leg].Phase;
        }

        //==================== 档插值 ====================

        private struct Mix
        {
            public float Stance, Stride, CycleMin, CycleMax, SpeedSlow, SpeedFast, ReachBias, FarOffset, Skew, Toe;
            /// <summary>支撑段居中的程度（0 = 从落脚点退一整个行程，1 = 前后各半）：相邻两档不同时连续插，换档中途落点不跳</summary>
            public float Center;
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
            m.Center = MathHelper.Lerp(a.Centered ? 1f : 0f, c.Centered ? 1f : 0f, t);
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
        /// 按体速推进相位，并刷新各腿支撑状态与落步事件；锁步时顺带把支撑脚按本帧实际走过的路（<paramref name="speed"/> × <paramref name="dt"/>）往后推
        /// </summary>
        public void Advance(float speed, float dt = 1f) => Advance(speed, dt, speed * dt);

        /// <summary>
        /// 同 <see cref="Advance(float, float)"/>，但锁步的支撑脚按 <paramref name="travel"/>（本帧锚点实际前移，骨架单位，有符号）钉住：
        /// 求值体速与锚点位移不一致时（收步时按 0 求值但身子还在蹭、倒退、坡上只有一部分位移沿骨架前向）支撑脚照样不滑
        /// </summary>
        public void Advance(float speed, float dt, float travel) {
            if (Def != null && Def.LockStance && !lockValid) {
                //从改体速、推相位之前的现算位置接手：此刻 Foot 报的就是它们，接手那一帧不跳
                SeedLocked();
            }
            Speed = speed;
            if (Def == null) {
                return;
            }
            float dPhase = dt / MathF.Max(Cycle(speed), Def.MinCycle);
            Phase += dPhase;
            Phase -= MathF.Floor(Phase);
            if (Def.LockStance) {
                StepLocked(travel, dt, dPhase);
            }
            UpdatePlanted();
            UpdateActivity(dt);
        }

        /// <summary>
        /// 按当前相位重算支撑状态（直接写 <see cref="Phase"/> 之后调，落步事件同样按"本次支撑、上次不支撑"判）；
        /// 锁步状态随之作废，下次 <see cref="Advance(float, float)"/> 时按相位重新落位
        /// </summary>
        public void RefreshPlanted() {
            lockValid = false;
            Array.Clear(hold);
            Array.Clear(slip);
            UpdatePlanted();
        }

        /// <summary>
        /// 锁步状态重建成方步站姿：各腿在站定落点（<see cref="RestX"/>）着地，相位正落在摆越窗里的腿记为这一步没迈（不会一起步就弹进半空）。
        /// 运动层淡出后从底姿起步时用：脚目标与底姿的站位重合，淡入不滑。只在 <see cref="Gait2DDef.LockStance"/> 下有效
        /// </summary>
        public void PlantAtRest() {
            if (Def == null || !Def.LockStance) {
                return;
            }
            Mix m = Current();
            Array.Clear(slip);
            lockValid = true;
            for (int i = 0; i < footX.Length; i++) {
                footX[i] = liftX[i] = RestX(i);
                float u = LegU(m, i);
                bool window = u >= m.Stance;
                swingWindow[i] = window;
                hold[i] = window;
                swinging[i] = false;
            }
            UpdatePlanted();
            Array.Clear(justPlanted);
            activity = 0f;
        }

        /// <summary>
        /// 起停驱动（定义见 <see cref="Gait2DDef.Drive"/>）：体速过起步门槛开始迈步、落到停步门槛以下停步；
        /// 停步后相位加快推（体速越接近 0 越接近 <see cref="Gait2DDrive.SettleRate"/> 倍）收步：离站位远的脚轮到离地就迈回站位，已在站位的配合
        /// <see cref="Gait2DDef.MinStep"/> 不再抬；各腿收齐（<see cref="Settled"/>）后把 <see cref="Weight"/> 淡出。
        /// 运动层已淡出时起步先 <see cref="PlantAtRest"/>，再淡入。
        /// <paramref name="travel"/> = 本帧锚点实际前移（骨架单位，有符号），支撑脚按它钉住。返回本帧是否推进了相位（落步事件只在推进过的帧读）
        /// </summary>
        public bool Drive(float speed, float travel, float dt = 1f) {
            if (Def == null) {
                return false;
            }
            Gait2DDrive d = Def.Drive ?? new Gait2DDrive();
            if (speed > d.StartSpeed) {
                if (!Striding && Weight <= 0.01f) {
                    //运动层已淡出：此刻显示的是底姿站位，从方步站姿接手
                    PlantAtRest();
                }
                Striding = true;
            }
            else if (speed < d.StopSpeed) {
                Striding = false;
            }
            bool advanced = false;
            float target = 0f;
            if (Striding) {
                Advance(speed, dt, travel);
                advanced = true;
                target = 1f;
            }
            else if (Weight > 0.01f && !Settled(d.SettleTolerance)) {
                //按实际体速（已低于停步门槛）求值，推进倍率随体速从 1 倍连续升到收步倍率：跨门槛那一帧落点与抬脚不跳
                float k = d.StopSpeed > 0f ? MathHelper.Clamp(speed / d.StopSpeed, 0f, 1f) : 0f;
                Advance(speed, dt * MathHelper.Lerp(d.SettleRate, 1f, k), travel);
                advanced = true;
                target = 1f;
            }
            else {
                Speed = speed;
            }
            Weight = Approach(Weight, target, (target > Weight ? d.FadeIn : d.FadeOut) * dt);
            return advanced;
        }

        private static float Approach(float v, float target, float step) {
            if (v < target) {
                return MathF.Min(v + step, target);
            }
            return MathF.Max(v - step, target);
        }

        private void UpdatePlanted() {
            if (Def == null) {
                return;
            }
            for (int i = 0; i < planted.Length; i++) {
                Foot(i, out _, out _, out _, out bool now);
                justPlanted[i] = now && !planted[i];
                planted[i] = now;
            }
        }

        private float LegU(in Mix m, int leg) {
            float u = Phase - LegPhase(m, leg);
            if (lockValid && Def.LockStance) {
                u += slip[leg];
            }
            return u - MathF.Floor(u);
        }

        /// <summary>落点（不含远腿错位 <paramref name="off"/>）：落脚点前伸 + 按居中程度补的半个行程</summary>
        private float LandReach(in Mix m, int leg, float travel, out float off) {
            float reachAt = LegValue(m, g => g.Reach, g => g.ReachBias, leg);
            off = Def.Legs[leg].Offset * m.FarOffset;
            if (StrideScale != 1f) {
                float k = MathF.Max(StrideScale, 0.05f);
                reachAt *= k;
                off *= k;
            }
            return reachAt + travel * 0.5f * m.Center;
        }

        /// <summary>按相位现算的支撑脚位置</summary>
        private float StanceX(in Mix m, float cycle, int leg, float u) {
            float travel = Speed * m.Stance * cycle;
            float reach = LandReach(m, leg, travel, out float off);
            return reach - travel * (u / m.Stance) + off;
        }

        /// <summary>
        /// 锁步推一帧：着地的脚（含没迈的）相对锚点退回本帧走过的路（世界里不动），刚着地的落在本帧落点；
        /// 相位刚进摆越窗的是离地事件：跨距不到 <see cref="Gait2DDef.MinStep"/> 就不迈（钉到下一次离地再判），否则记下离地点。
        /// 着地的脚被拖过 <see cref="Gait2DDef.LateSlack"/> 的后沿就把这条腿的相位偏到离地点，本帧离地：
        /// 同一对腿的另一条刚离地（摆越前 30%）时先等它，再被拖过一个余量就不等了（够不着比一对腿同时腾空更难看）
        /// </summary>
        private void StepLocked(float move, float dt, float dPhase) {
            Mix m = Current();
            float cycle = Cycle(m, Speed);
            float minStep = Def.MinStep;
            float slack = Def.LateSlack;
            float stanceTravel = Speed * m.Stance * cycle;
            //偏移收回的速度：相位推进的四分之一（着地期这条腿的节奏慢 / 快四分之一，不会倒走）
            float slipBack = 0.25f * dt / MathF.Max(cycle, Def.MinCycle);
            for (int i = 0; i < footX.Length; i++) {
                float u = LegU(m, i);
                if (swinging[i]) {
                    //摆越时长走满才落地；落点往前追不限，往回缩每帧至多 2（体速突降时空中的脚不往回抽，偏前的由之后的步子收回）
                    float land = LandX(m, cycle, i);
                    landX[i] = land >= landX[i] ? land : MathF.Max(land, landX[i] - 2f * dt);
                    swingAcc[i] += dPhase;
                    if (swingAcc[i] >= swingSpan[i]) {
                        swinging[i] = false;
                        footX[i] = landX[i];
                    }
                    swingWindow[i] = u >= m.Stance;
                    continue;
                }
                if (slack > 0f) {
                    float late = LandX(m, cycle, i) - stanceTravel - slack - (footX[i] - move);
                    int p = partner[i];
                    bool partnerFresh = p >= 0 && SwingProgress(p) is >= 0f and < 0.3f;
                    if (late > slack || late > 0f && !partnerFresh) {
                        //偏过离地点一丝：float 误差不会让它又落回支撑窗
                        slip[i] += m.Stance + 0.0005f - u;
                        slip[i] -= MathF.Round(slip[i]);
                        u = m.Stance + 0.0005f;
                        swingWindow[i] = false;
                        hold[i] = false;
                    }
                }
                bool window = u >= m.Stance;
                if (window && !swingWindow[i]) {
                    //离地事件：跨距不够就这一步不迈
                    if (minStep > 0f && MathF.Abs(LandX(m, cycle, i) - footX[i]) < minStep) {
                        hold[i] = true;
                        footX[i] -= move;
                    }
                    else {
                        hold[i] = false;
                        swinging[i] = true;
                        liftX[i] = footX[i];
                        landX[i] = LandX(m, cycle, i);
                        swingAcc[i] = 0f;
                        swingSpan[i] = MathF.Max(1f - m.Stance, 0.02f);
                    }
                }
                else {
                    footX[i] -= move;
                    if (!window && !hold[i] && slip[i] != 0f) {
                        slip[i] = slip[i] > 0f ? MathF.Max(slip[i] - slipBack, 0f) : MathF.Min(slip[i] + slipBack, 0f);
                    }
                }
                swingWindow[i] = window;
            }
        }

        /// <summary>锁步起点按相位现算：支撑脚落在现算位置，摆越中的脚当作从现算的离地点出发</summary>
        private void SeedLocked() {
            Mix m = Current();
            float cycle = Cycle(m, Speed);
            for (int i = 0; i < footX.Length; i++) {
                float u = LegU(m, i);
                footX[i] = StanceX(m, cycle, i, MathF.Min(u, m.Stance));
                liftX[i] = StanceX(m, cycle, i, m.Stance);
                swingWindow[i] = u >= m.Stance;
                swinging[i] = swingWindow[i];
                swingSpan[i] = MathF.Max(1f - m.Stance, 0.02f);
                swingAcc[i] = swinging[i] ? u - m.Stance : 0f;
                landX[i] = LandX(m, cycle, i);
                hold[i] = false;
                slip[i] = 0f;
            }
            lockValid = true;
        }

        /// <summary>当前体速下这一步的落点（锚点系，含远腿错位）</summary>
        private float LandX(in Mix m, float cycle, int leg) {
            float reach = LandReach(m, leg, Speed * m.Stance * cycle, out float off);
            return reach + off;
        }

        /// <summary>摆越中这一步的抬脚比例（锁步 + <see cref="Gait2DDef.LiftDistance"/> 时按离地点到这一步落点的跨距折算，否则 1）</summary>
        private float LiftFactor(in Mix m, float cycle, int leg) {
            if (!Def.LockStance || !lockValid || Def.LiftDistance <= 0f) {
                return 1f;
            }
            return Smooth(MathF.Abs(landX[leg] - liftX[leg]) / Def.LiftDistance);
        }

        /// <summary>
        /// 第 <paramref name="leg"/> 条腿这一步的抬脚比例 0~1：摆越中按跨距折算（锁步 + <see cref="Gait2DDef.LiftDistance"/>），着地时 1
        /// </summary>
        public float LiftScale(int leg) {
            if (Def == null || leg < 0 || leg >= Def.Legs.Count) {
                return 1f;
            }
            Mix m = Current();
            bool swing = Def.LockStance && lockValid ? swinging[leg] && !hold[leg] : LegU(m, leg) >= m.Stance;
            return swing ? LiftFactor(m, Cycle(m, Speed), leg) : 1f;
        }

        private bool IsHeld(int leg) => Def.LockStance && lockValid && hold[leg];

        /// <summary>迈步活跃度追向摆越中各腿抬脚比例的最大值（没有腿在迈 = 0）</summary>
        private void UpdateActivity(float dt) {
            if (!Def.LockStance || !Def.StepWaves) {
                activity = 1f;
                return;
            }
            Mix m = Current();
            float cycle = Cycle(m, Speed);
            float target = 0f;
            for (int i = 0; i < planted.Length; i++) {
                if (!planted[i]) {
                    target = MathF.Max(target, LiftFactor(m, cycle, i));
                }
            }
            activity = Approach(activity, target, Def.ActivityRate * dt);
        }

        /// <summary>
        /// 第 <paramref name="leg"/> 条腿站定时的落点（体速为 0 的落点，锚点系，骨架单位）：收步时脚要回到这里
        /// </summary>
        public float RestX(int leg) {
            if (Def == null || leg < 0 || leg >= Def.Legs.Count) {
                return 0f;
            }
            float reach = LandReach(Current(), leg, 0f, out float off);
            return reach + off;
        }

        /// <summary>
        /// 各腿都着地（含没迈的）、且都在各自站定落点（<see cref="RestX"/>）的 <paramref name="tolerance"/> 以内（骨架单位）：收步收完了。
        /// 支撑占比低、任何时刻都有腿在摆越的步态（四足慢走）只有配 <see cref="Gait2DDef.MinStep"/>（站位上的脚不再抬）才收得齐
        /// </summary>
        public bool Settled(float tolerance) {
            if (Def == null) {
                return true;
            }
            for (int i = 0; i < Def.Legs.Count; i++) {
                Foot(i, out float x, out _, out _, out bool isPlanted);
                if (!isPlanted || MathF.Abs(x - RestX(i)) > tolerance) {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// 第 <paramref name="leg"/> 条腿在当前相位的足目标（锚点系，骨架单位，未乘 <see cref="StepScale"/>）、足角与是否支撑
        /// </summary>
        public void Foot(int leg, out float x, out float y, out float a, out bool isPlanted) {
            Mix m = Current();
            Gait2DDef d = Def;
            float cycle = Cycle(m, Speed);
            float u = LegU(m, leg);
            float travel = Speed * m.Stance * cycle;
            float reach = LandReach(m, leg, travel, out float off);
            bool locked = d.LockStance && lockValid;
            if (locked && hold[leg]) {
                //没迈的这一步：脚平钉在原地，不做脚跟着地 / 蹬离的滚动
                x = footX[leg];
                y = 0f;
                a = 0f;
                isPlanted = true;
                return;
            }
            if (locked ? !swinging[leg] : u < m.Stance) {
                float s = MathF.Min(u / m.Stance, 1f);
                x = locked ? footX[leg] : reach - travel * s + off;
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
            float w = locked ? LockedSwing(leg) : (u - m.Stance) / (1f - m.Stance);
            float lift = LegValue(m, g => g.Lifts, g => g.Lift, leg);
            if (StrideScale != 1f) {
                lift *= MathF.Max(StrideScale, 0.05f);
            }
            if (locked) {
                //从真实的离地点送到这一步的落点；跨距小（收步、慢走）就少抬
                lift *= LiftFactor(m, cycle, leg);
                x = MathHelper.Lerp(liftX[leg], landX[leg], Smooth(w));
            }
            else {
                x = MathHelper.Lerp(reach - travel, reach, Smooth(w)) + off;
            }
            //float 下 sin(MathF.PI) ≈ −8.7e-8：负底数的非整数次幂是 NaN，末段先钳到 0
            float arc = MathF.Max(MathF.Sin(MathF.PI * MathF.Min(MathF.Pow(w, m.Skew) * d.LiftStretch, 1f)), 0f);
            y = -lift * MathF.Pow(arc, d.LiftPow);
            a = MathHelper.Lerp(m.Toe, d.SwingToe, Smooth(w));
            isPlanted = false;
        }

        /// <summary>
        /// 第 <paramref name="leg"/> 条腿的摆越进度：支撑期 −1，摆越期 0（离地）~ 1（触地）
        /// </summary>
        public float SwingProgress(int leg) {
            if (Def == null || leg < 0 || leg >= Def.Legs.Count || IsHeld(leg)) {
                return -1f;
            }
            Mix m = Current();
            float u = LegU(m, leg);
            if (Def.LockStance && lockValid) {
                return swinging[leg] ? LockedSwing(leg) : -1f;
            }
            return u < m.Stance ? -1f : (u - m.Stance) / MathF.Max(1f - m.Stance, 0.0001f);
        }

        /// <summary>锁步摆越进度：离地后累计推进的相位 / 这一步的摆越时长（换档挪了逐腿相位也不催快、不跳）</summary>
        private float LockedSwing(int leg) => MathHelper.Clamp(swingAcc[leg] / swingSpan[leg], 0f, 1f);

        /// <summary>
        /// 盆骨起伏（正 = 下沉）与前倾：各档分别求值再按 <see cref="Blend"/> 插；起伏波与俯仰波乘 <see cref="Activity"/>（常量偏移不乘）
        /// </summary>
        public void Body(out float bob, out float lean) {
            if (Def == null) {
                bob = lean = 0f;
                return;
            }
            Mix m = Current();
            float k = MathF.Min(Speed / Def.BobSpeed, 1f);
            float act = Activity;
            ModeBody(m.A, k, act, out float bobA, out float leanA);
            ModeBody(m.B, k, act, out float bobB, out float leanB);
            bob = MathHelper.Lerp(bobA, bobB, m.T);
            lean = MathHelper.Lerp(leanA, leanB, m.T);
        }

        private void ModeBody(Gait2DMode g, float k, float act, out float bob, out float lean) {
            float offset = float.IsNaN(g.BobOffset) ? g.Stance * 0.5f : g.BobOffset;
            float wave = 0.5f + 0.5f * MathF.Cos(4f * MathF.PI * (Phase - offset));
            if (g.BobPow != 1f) {
                wave = MathF.Pow(wave, g.BobPow);
            }
            bob = (g.SpeedScaled ? g.Bob * k : g.Bob) * wave * act + g.BobBase;
            lean = g.SpeedScaled ? g.Lean * k : g.Lean;
            if (g.LeanWave != 0f) {
                lean += g.LeanWave * MathF.Sin(4f * MathF.PI * (Phase - g.LeanWaveOffset)) * act;
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
                if (leg.SwingIndex >= 0) {
                    float swing = SwingProgress(i);
                    pose[leg.SwingIndex] = weight >= 1f ? swing : MathHelper.Lerp(pose[leg.SwingIndex], swing, weight);
                }
                if (leg.AutoIndex >= 0) {
                    pose[leg.AutoIndex] = weight >= 1f ? 1f : MathHelper.Lerp(pose[leg.AutoIndex], 1f, weight);
                }
                if (leg.LiftScaleIndex >= 0) {
                    float ls = LiftScale(i);
                    pose[leg.LiftScaleIndex] = weight >= 1f ? ls : MathHelper.Lerp(pose[leg.LiftScaleIndex], ls, weight);
                }
            }
            Body(out float bob, out float lean);
            AddHip(pose, bob * weight);
            if (Def.TiltIndex >= 0) {
                pose[Def.TiltIndex] += lean * weight;
            }
            Mix mix = Current();
            AddWaves(pose, mix.A, (1f - mix.T) * weight);
            if (!ReferenceEquals(mix.A, mix.B)) {
                AddWaves(pose, mix.B, mix.T * weight);
            }
        }

        /// <summary>
        /// 某一档的相位波动按权重加进姿态（矢量通道两个分量都加）
        /// </summary>
        private void AddWaves(Rig2DPose pose, Gait2DMode mode, float weight) {
            if (mode?.Waves == null || mode.Waves.Count == 0 || weight <= 0f) {
                return;
            }
            float speedK = MathF.Min(Speed / MathF.Max(Def.BobSpeed, 0.001f), 1f);
            float act = Activity;
            for (int i = 0; i < mode.Waves.Count; i++) {
                Gait2DWave w = mode.Waves[i];
                int ci = w.ChannelIndex;
                if (ci < 0) {
                    continue;
                }
                float c = MathF.Cos(MathHelper.TwoPi * w.Cycles * (Phase - w.Offset)) * weight;
                if (w.SpeedScaled) {
                    c *= speedK;
                }
                if (w.Cycles != 0f) {
                    c *= act;
                }
                if (Rig.Definition.Channels[ci].IsScalar) {
                    pose[ci] += w.Amplitude.X * c;
                }
                else {
                    pose.SetVector(ci, pose.Vector(ci) + w.Amplitude * c);
                }
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
