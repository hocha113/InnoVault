using InnoVault.Rigs2D.Data;
using InnoVault.Rigs2D.Runtime;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using Terraria;

namespace InnoVault.Rigs2D.Solvers
{
    /// <summary>
    /// 体系踏步步态：足端在髋的局部系里走踏步周期——支撑段沿体轴后退（身体从上面驶过）、摆越段收腿前摆，
    /// 全程不探物块，贴地与腾空是同一套动作。适合没有真实地面可踩、或地面会吞掉世界落足的躯体
    /// （蛇形 / 长链 / 腾空巡曳的多足生物）；要真正钉在地形上换步的走 <see cref="FootPlantGaitSolver"/>
    /// <br/>本求解器不写骨骼，只产出每条腿的足端目标（实现 <see cref="IRig2DTargetSource"/>），腿的骨链交给
    /// <see cref="ThreeBoneLegSolver"/> / <see cref="TwoBoneIKSolver"/> / <see cref="PointAtSolver"/> 用 <c>targetSolver</c> 引用
    /// <br/>骨骼同 <see cref="FootPlantGaitSolver"/>：每腿一根髋骨（多为该腿的第一节）；髋 = 该骨静息近端，法线 = 该骨静息轴向，体前向 = 父骨骼轴向
    /// <br/>沿轴速度由髋位置差分得出（不需要消费方喂 Velocity）；髋单帧位移超过 <c>teleportPx</c> 视为传送，足端整体搬走不拉丝。
    /// 步幅随速拉长守住最短周期，撞上限后节拍封顶、多出的体速变成足端顺滑（重型躯体的滑行读数，不许几帧一步地抽）
    /// <br/>契约同基类：不用 <c>Main.rand</c> 与全局时钟，各端同输入同输出；<see cref="Snap"/> 能从任意脏状态硬重建；<see cref="Configure"/> 可重入（热重载保留运行态）
    /// <br/>参数（JSON 键 → 默认值）：
    /// <list type="bullet">
    /// <item><c>reach</c> 100 全肢触及（像素，乘 Scale）、<c>restReach</c> 0.56 休息半径占比、<c>stride</c> 100 低速步幅、<c>strideMax</c> 170 步幅上限、
    /// <c>minCycle</c> 12 最短周期帧、<c>stanceFraction</c> 0.6 支撑段占比、<c>strideLead</c> 0 休息位前探、<c>clearance</c> 30 摆越收腿量、
    /// <c>strideRate</c> 0.6 足端追近率、<c>blendFrames</c> 8 模式切换的追近率过渡帧数、<c>idleRate</c> 0.02 静止时摆越腿收尾的相位速、
    /// <c>idleSpeed</c> 1 低于此速视为静止、<c>teleportPx</c> 80 传送阈值</item>
    /// <item><c>dorsalScale</c> 1 背侧排步幅倍率；<c>airClearance</c> 1.3、<c>airReach</c> 0.9、<c>airTilt</c> 0.25 腾空风格（收腿更高、半径略收、法线向体后倾），由 <see cref="AirStyle"/> 开关</item>
    /// <item><c>stationLag</c> 1.382 站间滞后、<c>phaseOffsets</c>[] 逐腿覆写、<c>stations</c>[] 站号（缺省 i/2）、<c>strideAccent</c>[] 逐腿步幅性格</item>
    /// <item><c>envelopeMin</c> 0.25、<c>envelopeMax</c> 0.84、<c>envelopeSwing</c> 1.0 落足可达包络；<c>groundnessBand</c> 0.6 走地权重过渡带</item>
    /// <item><c>tuckBack</c> 28、<c>tuckStep</c> 6、<c>tuckSide</c> 7、<c>tuckRate</c> 0.28 收拢贴体；<c>collapseDrop</c> 0.9、<c>collapseSide</c> 22、
    /// <c>collapseRate</c> 0.16、<c>limpRise</c> 0.055、<c>limpFall</c> 0.05 失力垂软；<c>holdRate</c> 0.16 外部目标跟随率</item>
    /// </list>
    /// </summary>
    public sealed class StrideGaitSolver : Rig2DSolver, IRig2DTargetSource
    {
        /// <summary>
        /// 单腿姿态模式
        /// </summary>
        public enum LegMode
        {
            /// <summary>
            /// 踏步（贴地与腾空同一套）
            /// </summary>
            Stride,
            /// <summary>
            /// 收拢贴体（钻沙 / 掠冲）
            /// </summary>
            Tuck,
            /// <summary>
            /// 失力垂软（死亡演出）
            /// </summary>
            Collapse,
            /// <summary>
            /// 跟随外部目标（立起、抓握等消费方自算的姿态）
            /// </summary>
            Hold,
        }

        /// <summary>
        /// 一条腿的运行状态（只读视图由 <see cref="Leg(int)"/> 给出）
        /// </summary>
        public struct LegState
        {
            /// <summary>
            /// 当前足端（世界）
            /// </summary>
            public Vector2 Foot;
            /// <summary>
            /// 本帧髋位置
            /// </summary>
            public Vector2 Hip;
            /// <summary>
            /// 侧法线（静息方向，单位向量）
            /// </summary>
            public Vector2 Normal;
            /// <summary>
            /// 体前向单位向量（父骨轴向）
            /// </summary>
            public Vector2 Forward;
            /// <summary>
            /// 沿体轴的行进符号 ±1（带迟滞）
            /// </summary>
            public float DirSign;
            /// <summary>
            /// 周期相位 0..1，[0, stanceFraction) 为支撑段
            /// </summary>
            public float U;
            /// <summary>
            /// 摆越进度 0..1（支撑段为 0）
            /// </summary>
            public float SwingT;
            /// <summary>
            /// 支撑中 / 摆越中
            /// </summary>
            public bool Planted, Swinging;
            /// <summary>
            /// 已初始化
            /// </summary>
            public bool Inited;
            /// <summary>
            /// 是否参与（站宿主缺失时消费方关掉）
            /// </summary>
            public bool Visible;
            /// <summary>
            /// 走地权重 0..1（法线朝地面程度；消费方压暗 / 排序用）
            /// </summary>
            public float Groundness;
            /// <summary>
            /// 失力度 0..1
            /// </summary>
            public float Limp;
            /// <summary>
            /// 已落步数（每次摆越落地自增；确定性哈希的盐，各端一致）
            /// </summary>
            public int StepCount;
            /// <summary>
            /// 本帧生效的模式
            /// </summary>
            public LegMode Mode;
            /// <summary>
            /// 站号
            /// </summary>
            public int Station;
            /// <summary>
            /// 相位偏移（弧度）
            /// </summary>
            public float PhaseOffset;
            /// <summary>
            /// 步幅性格差
            /// </summary>
            public float StrideAccent;
            /// <summary>
            /// 本步前半步幅倍率（摆越起步时经 <see cref="StrideScaleFilter"/> 写入，落短即踉跄）
            /// </summary>
            public float StepScale;
            internal Vector2 PrevHip;
            internal float ExtraPhase;
            internal float Blend;
            internal LegMode? Override;
            internal Vector2 HoldTarget;
        }

        private LegState[] legs = [];
        private float reach;
        private float restReach;
        private float stride;
        private float strideMax;
        private float minCycle;
        private float stanceFraction;
        private float strideLead;
        private float clearance;
        private float strideRate;
        private float idleRate;
        private float idleSpeed;
        private float teleportPx;
        private float blendFrames;
        private float dorsalScale;
        private float airClearance;
        private float airReach;
        private float airTilt;
        private float stationLag;
        private float envelopeMin;
        private float envelopeMax;
        private float envelopeSwing;
        private float groundnessBand;
        private float tuckBack;
        private float tuckStep;
        private float tuckSide;
        private float tuckRate;
        private float collapseDrop;
        private float collapseSide;
        private float collapseRate;
        private float limpRise;
        private float limpFall;
        private float holdRate;

        private float travelSign = 1f;
        private float currentStride;

        /// <summary>
        /// 步态时钟相位（弧度），按沿轴速度自推进
        /// </summary>
        public float Phase { get; set; }
        /// <summary>
        /// 全局模式（逐腿可用 <see cref="SetLegMode"/> 覆盖）
        /// </summary>
        public LegMode Mode { get; set; } = LegMode.Stride;
        /// <summary>
        /// 腾空风格（收腿更高、法线后倾）；踏步周期本身不变
        /// </summary>
        public bool AirStyle { get; set; }
        /// <summary>
        /// 指向地面的单位向量，只用于走地权重、行进符号与失力垂软
        /// </summary>
        public Vector2 GroundDir { get; set; } = Vector2.UnitY;
        /// <summary>
        /// 失力站数（<see cref="LegMode.Collapse"/> 下站号小于此值的腿逐站瘫软，奇侧等偶侧先软）
        /// </summary>
        public int CollapsedStations { get; set; } = int.MaxValue;
        /// <summary>
        /// 步幅逐帧倍率
        /// </summary>
        public float StrideScale { get; set; } = 1f;
        /// <summary>
        /// 落步回调 (腿号, 足端, 权重)：偶侧 1、奇侧 0.7
        /// </summary>
        public Action<int, Vector2, float> OnPlant { get; set; }
        /// <summary>
        /// 步幅过滤 (腿号, 已落步数) → 本步前半步幅倍率（确定性踉跄 / 补步）；<see langword="null"/> 恒 1
        /// </summary>
        public Func<int, int, float> StrideScaleFilter { get; set; }
        /// <summary>
        /// 失力垂软的地板钳制（死腿不插进地里）；<see langword="null"/> 不钳
        /// </summary>
        public Func<Vector2, Vector2> CollapseFloor { get; set; }

        /// <summary>
        /// 腿数
        /// </summary>
        public int LegCount => legs.Length;
        /// <summary>
        /// 沿地面前向（<see cref="ForwardDir"/>；地面朝下时即世界 +X）的平滑行进符号 ±1：向前向走为 +1
        /// </summary>
        public float TravelSign => travelSign;
        /// <summary>
        /// 地面前向单位向量：<see cref="GroundDir"/> 逆转 90°（地面朝下 (0, 1) 时为 (1, 0)）
        /// </summary>
        public Vector2 ForwardDir => new(GroundDir.Y, -GroundDir.X);
        /// <summary>
        /// 全肢触及（含 Scale）
        /// </summary>
        public float Reach => reach * Scale;
        /// <summary>
        /// 本帧生效步幅（含 Scale，随速拉长）
        /// </summary>
        public float Stride => currentStride;

        /// <summary>
        /// 取某腿状态（只读副本）
        /// </summary>
        public LegState Leg(int index) => index >= 0 && index < legs.Length ? legs[index] : default;

        /// <summary>
        /// 取某腿状态引用（消费方读绘制缓存用；写入请走 Set 系列）
        /// </summary>
        public ref readonly LegState LegRef(int index) => ref legs[index];

        /// <summary>
        /// 某腿足端
        /// </summary>
        public Vector2 Foot(int index) => index >= 0 && index < legs.Length ? legs[index].Foot : Vector2.Zero;

        /// <summary>
        /// 某腿髋位置（本帧）
        /// </summary>
        public Vector2 Hip(int index) => index >= 0 && index < legs.Length ? legs[index].Hip : Vector2.Zero;

        /// <inheritdoc/>
        public bool TryGetTarget(int index, out Vector2 target) {
            if (index >= 0 && index < legs.Length && legs[index].Inited && legs[index].Visible) {
                target = legs[index].Foot;
                return true;
            }
            target = default;
            return false;
        }

        /// <summary>
        /// 覆写某腿模式（<see langword="null"/> 回到全局 <see cref="Mode"/>）
        /// </summary>
        public void SetLegMode(int index, LegMode? mode) {
            if (index >= 0 && index < legs.Length) {
                legs[index].Override = mode;
            }
        }

        /// <summary>
        /// 给某腿外部目标并切到 <see cref="LegMode.Hold"/>（平滑跟随）
        /// </summary>
        public void SetLegHold(int index, Vector2 target) {
            if (index >= 0 && index < legs.Length) {
                legs[index].Override = LegMode.Hold;
                legs[index].HoldTarget = target;
            }
        }

        /// <summary>
        /// 设置某腿是否参与
        /// </summary>
        public void SetLegVisible(int index, bool visible) {
            if (index >= 0 && index < legs.Length) {
                legs[index].Visible = visible;
            }
        }

        /// <summary>
        /// 全腿复位：足端待下一帧重新落位（图鉴循环重启用）
        /// </summary>
        public void ResetLegs() {
            for (int i = 0; i < legs.Length; i++) {
                legs[i].Inited = false;
                legs[i].Swinging = false;
                legs[i].Planted = false;
                legs[i].Limp = 0f;
                legs[i].ExtraPhase = 0f;
            }
        }

        /// <inheritdoc/>
        public override ReadOnlySpan<int> DrivenBones => ReadOnlySpan<int>.Empty;

        /// <inheritdoc/>
        protected override void Configure(Solver2DDef def) {
            int n = bones.Length;
            reach = def.GetFloat("reach", 100f);
            restReach = def.GetFloat("restReach", 0.56f);
            stride = def.GetFloat("stride", 100f);
            strideMax = Math.Max(def.GetFloat("strideMax", 170f), stride);
            minCycle = def.GetFloat("minCycle", 12f);
            stanceFraction = MathHelper.Clamp(def.GetFloat("stanceFraction", 0.6f), 0.2f, 0.9f);
            strideLead = def.GetFloat("strideLead", 0f);
            clearance = def.GetFloat("clearance", 30f);
            strideRate = def.GetFloat("strideRate", 0.6f);
            idleRate = def.GetFloat("idleRate", 0.02f);
            idleSpeed = def.GetFloat("idleSpeed", 1f);
            teleportPx = def.GetFloat("teleportPx", 80f);
            blendFrames = Math.Max(def.GetFloat("blendFrames", 8f), 1f);
            dorsalScale = def.GetFloat("dorsalScale", 1f);
            airClearance = def.GetFloat("airClearance", 1.3f);
            airReach = def.GetFloat("airReach", 0.9f);
            airTilt = def.GetAngle("airTilt", 0.25f);
            stationLag = def.GetAngle("stationLag", MathHelper.TwoPi * 0.22f);
            envelopeMin = def.GetFloat("envelopeMin", 0.25f);
            envelopeMax = def.GetFloat("envelopeMax", 0.84f);
            envelopeSwing = def.GetAngle("envelopeSwing", 1.0f);
            groundnessBand = def.GetFloat("groundnessBand", 0.6f);
            tuckBack = def.GetFloat("tuckBack", 28f);
            tuckStep = def.GetFloat("tuckStep", 6f);
            tuckSide = def.GetFloat("tuckSide", 7f);
            tuckRate = def.GetFloat("tuckRate", 0.28f);
            collapseDrop = def.GetFloat("collapseDrop", 0.9f);
            collapseSide = def.GetFloat("collapseSide", 22f);
            collapseRate = def.GetFloat("collapseRate", 0.16f);
            limpRise = def.GetFloat("limpRise", 0.055f);
            limpFall = def.GetFloat("limpFall", 0.05f);
            holdRate = def.GetFloat("holdRate", 0.16f);

            int[] stations = def.GetIntArray("stations", n, -1);
            float[] offsets = def.GetFloatArray("phaseOffsets", n, float.NaN);
            bool hasOffsets = def.Has("phaseOffsets");
            float[] accents = def.GetFloatArray("strideAccent", n, 1f);

            if (legs.Length != n) {
                legs = new LegState[n];
            }
            for (int i = 0; i < n; i++) {
                ref LegState leg = ref legs[i];
                leg.Station = stations[i] >= 0 ? stations[i] : i / 2;
                leg.PhaseOffset = hasOffsets && !float.IsNaN(offsets[i])
                    ? offsets[i]
                    : -leg.Station * stationLag + ((i & 1) == 1 ? MathHelper.Pi : 0f);
                leg.StrideAccent = accents[i];
                if (!leg.Inited) {
                    leg.Visible = true;
                    leg.StepScale = 1f;
                    leg.DirSign = 1f;
                }
            }
            currentStride = stride * Scale;
        }

        private static float Frac(float v) {
            v %= 1f;
            return v < 0f ? v + 1f : v;
        }

        private void ReadHip(ref LegState leg, int i) {
            leg.Hip = RestPosition(i);
            leg.Normal = RestForward(i);
            float fd = ParentDir(i);
            leg.Forward = new Vector2(MathF.Cos(fd), MathF.Sin(fd));
            leg.Groundness = MathHelper.Clamp((Vector2.Dot(leg.Normal, GroundDir) + groundnessBand) / (2f * groundnessBand), 0f, 1f);
        }

        /// <summary>
        /// 步幅随速拉长守住最短周期，到上限为止
        /// </summary>
        private float EffectiveStride(float speed) {
            float grown = Math.Max(stride, speed * stanceFraction * minCycle);
            return Math.Min(grown, strideMax) * Scale;
        }

        /// <inheritdoc/>
        public override void Snap() {
            for (int i = 0; i < legs.Length; i++) {
                ref LegState leg = ref legs[i];
                ReadHip(ref leg, i);
                leg.PrevHip = leg.Hip;
                LegMode mode = leg.Override ?? Mode;
                leg.Foot = mode switch {
                    LegMode.Tuck => TuckPoint(in leg, i),
                    LegMode.Collapse => DanglePoint(in leg, i),
                    LegMode.Hold => leg.HoldTarget,
                    _ => leg.Hip + leg.Normal * (Reach * restReach * leg.StrideAccent),
                };
                leg.Planted = mode == LegMode.Stride;
                leg.Swinging = false;
                leg.Limp = 0f;
                leg.ExtraPhase = 0f;
                leg.Blend = 1f;
                leg.StepScale = 1f;
                if (leg.DirSign == 0f) {
                    leg.DirSign = 1f;
                }
                leg.Mode = mode;
                leg.Inited = true;
            }
        }

        private void InitLeg(ref LegState leg) {
            leg.PrevHip = leg.Hip;
            leg.Foot = leg.Hip + leg.Normal * (Reach * restReach * leg.StrideAccent);
            leg.Planted = true;
            leg.Swinging = false;
            leg.Limp = 0f;
            leg.ExtraPhase = 0f;
            leg.Blend = 1f;
            leg.StepScale = 1f;
            if (leg.DirSign == 0f) {
                leg.DirSign = 1f;
            }
            leg.Inited = true;
        }

        /// <inheritdoc/>
        public override void Step(float dt) {
            if (legs.Length == 0) {
                return;
            }
            float teleport2 = teleportPx * Scale * teleportPx * Scale;
            Vector2 forwardDir = ForwardDir;

            //髋位姿与沿轴速度（由髋差分得出，坐标传送整体搬足不拉丝）
            float alongSum = 0f;
            float forwardSum = 0f;
            int alongN = 0;
            for (int i = 0; i < legs.Length; i++) {
                ref LegState leg = ref legs[i];
                if (!leg.Visible) {
                    continue;
                }
                ReadHip(ref leg, i);
                if (!leg.Inited) {
                    InitLeg(ref leg);
                    continue;
                }
                Vector2 delta = leg.Hip - leg.PrevHip;
                leg.PrevHip = leg.Hip;
                if (teleportPx > 0f && delta.LengthSquared() > teleport2) {
                    leg.Foot += delta;
                    continue;
                }
                float along = Vector2.Dot(delta, leg.Forward) / Math.Max(dt, 0.001f);
                //行进符号连续过渡：倒向时足端先收回休息位再向另一侧展开，不整排跳位
                float wantSign = along > 1.2f ? 1f : along < -1.2f ? -1f : 0f;
                if (wantSign != 0f) {
                    leg.DirSign = MathHelper.Lerp(leg.DirSign, wantSign, Spring2D.RateForDt(0.12f, dt));
                }
                alongSum += Math.Abs(along);
                forwardSum += Vector2.Dot(delta, forwardDir);
                alongN++;
            }
            float speed = alongN > 0 ? alongSum / alongN : 0f;
            if (alongN > 0 && Math.Abs(forwardSum / alongN) > 1.2f) {
                travelSign = MathHelper.Lerp(travelSign, Math.Sign(forwardSum), Spring2D.RateForDt(0.08f, dt));
            }

            //共享钟：Δu = v·s/L 时支撑段足端在世界系里恰好钉住；步幅随速拉长守最短周期，
            //步幅撞上限后节拍也封顶，多出的体速变成足端顺滑
            currentStride = EffectiveStride(speed);
            float du = currentStride > 0.01f ? speed * stanceFraction / currentStride * dt : 0f;
            du = Math.Min(du, dt / Math.Max(minCycle, 1f));
            Phase = (Phase + du * MathHelper.TwoPi) % MathHelper.TwoPi;
            if (Phase < 0f) {
                Phase += MathHelper.TwoPi;
            }
            bool resting = speed < idleSpeed;

            for (int i = 0; i < legs.Length; i++) {
                ref LegState leg = ref legs[i];
                if (!leg.Visible || !leg.Inited) {
                    continue;
                }
                LegMode mode = leg.Override ?? Mode;
                if (mode == LegMode.Collapse && !StationCollapsed(i)) {
                    mode = LegMode.Stride;
                }
                if (mode != leg.Mode) {
                    leg.Blend = 0f;
                }
                bool limpDecay = true;
                switch (mode) {
                    case LegMode.Collapse:
                        UpdateCollapse(ref leg, i, dt);
                        limpDecay = false;
                        break;
                    case LegMode.Tuck:
                        leg.Planted = false;
                        leg.Swinging = false;
                        leg.Foot = Vector2.Lerp(leg.Foot, TuckPoint(in leg, i), Spring2D.RateForDt(tuckRate, dt));
                        break;
                    case LegMode.Hold:
                        leg.Planted = false;
                        leg.Swinging = false;
                        leg.Foot = Vector2.Lerp(leg.Foot, leg.HoldTarget, Spring2D.RateForDt(holdRate, dt));
                        break;
                    default:
                        UpdateStride(ref leg, i, resting, dt);
                        break;
                }
                leg.Mode = mode;
                if (limpDecay) {
                    leg.Limp = MathHelper.Clamp(leg.Limp - limpFall * dt, 0f, 1f);
                }
            }
        }

        /// <summary>
        /// 本腿所在站是否已失力（偶侧先瘫，奇侧等对腿软下去再跟）
        /// </summary>
        private bool StationCollapsed(int i) {
            if (legs[i].Station >= CollapsedStations) {
                return false;
            }
            return (i & 1) == 0 || i == 0 || legs[i - 1].Limp > 0.35f;
        }

        //==================== 姿态模组 ====================

        /// <summary>
        /// 踏步：支撑段足端沿 -DirSign·Forward 从前半步退到后半步（体在上面驶过），
        /// 摆越段 smoothstep 回到前半步并沿法线收腿；步幅变化只在摆越起步时生效，足端不跳
        /// </summary>
        private void UpdateStride(ref LegState leg, int i, bool resting, float dt) {
            float sFrac = stanceFraction;
            float baseU = Phase / MathHelper.TwoPi + leg.PhaseOffset / MathHelper.TwoPi;

            //静止时摆越中的腿靠余量走完落地，行进中再把余量慢慢还回去恢复站间排布
            if (resting) {
                if (Frac(baseU + leg.ExtraPhase) >= sFrac) {
                    leg.ExtraPhase += idleRate * dt;
                }
            }
            else if (leg.ExtraPhase != 0f) {
                float e = Frac(leg.ExtraPhase);
                float step = 0.002f * dt;
                e = e > 0.5f ? Math.Min(e + step, 1f) : Math.Max(e - step, 0f);
                leg.ExtraPhase = e >= 1f ? 0f : e;
            }

            bool wasPlanted = leg.Planted;
            bool wasSwinging = leg.Swinging;
            float u = Frac(baseU + leg.ExtraPhase);
            leg.U = u;

            bool air = AirStyle;
            float dorsal = MathHelper.Lerp(dorsalScale, 1f, leg.Groundness);
            float fullStride = currentStride * leg.StrideAccent * StrideScale * dorsal;
            float backHalf = 0.5f * fullStride;
            float reachPx = Reach;
            float restDist = reachPx * restReach * leg.StrideAccent * (air ? airReach : 1f);
            float clear = clearance * Scale * (0.8f + 0.25f * leg.StrideAccent) * (air ? airClearance : 1f);

            float a;
            float lift;
            if (u < sFrac) {
                float m = u / sFrac;
                a = MathHelper.Lerp(0.5f * fullStride * leg.StepScale, -backHalf, m);
                lift = 0f;
                leg.Planted = true;
                leg.Swinging = false;
                leg.SwingT = 0f;
            }
            else {
                float m = (u - sFrac) / (1f - sFrac);
                if (wasPlanted || !wasSwinging) {
                    leg.StepScale = StrideScaleFilter?.Invoke(i, leg.StepCount) ?? 1f;
                }
                a = MathHelper.Lerp(-backHalf, 0.5f * fullStride * leg.StepScale, Spring2D.SmoothStep01(m));
                lift = MathF.Sin(m * MathHelper.Pi) * clear;
                leg.Planted = false;
                leg.Swinging = true;
                leg.SwingT = m;
            }

            Vector2 baseDir = leg.Normal;
            if (air && airTilt > 0.001f) {
                //腾空：法线向体后倾，腿拖在身后
                Vector2 tilted = leg.Normal - leg.Forward * (leg.DirSign * MathF.Tan(airTilt));
                if (tilted.LengthSquared() > 0.0001f) {
                    baseDir = Vector2.Normalize(tilted);
                }
            }
            Vector2 target = leg.Hip + baseDir * (restDist - lift)
                + leg.Forward * (leg.DirSign * a + strideLead * Scale);
            target = ReachEnvelope.ClampSwing(leg.Hip, leg.Normal, target,
                reachPx * envelopeMin, reachPx * envelopeMax, envelopeSwing);

            leg.Blend = Math.Min(1f, leg.Blend + dt / blendFrames);
            float rate = MathHelper.Lerp(0.2f, strideRate, leg.Blend);
            leg.Foot = Vector2.Lerp(leg.Foot, target, Spring2D.RateForDt(rate, dt));

            //落步拍：摆越跨回支撑
            if (wasSwinging && leg.Planted) {
                leg.StepCount++;
                OnPlant?.Invoke(i, leg.Foot, (i & 1) == 0 ? 1f : 0.7f);
            }
        }

        /// <summary>
        /// 收拢贴体：沿体轴向后掠平
        /// </summary>
        private Vector2 TuckPoint(in LegState leg, int i) {
            float s = Scale;
            return leg.Hip - leg.Forward * ((tuckBack + leg.Station * tuckStep + (i & 1) * 8f) * s)
                + leg.Normal * (tuckSide * s);
        }

        /// <summary>
        /// 失力垂软点：向地面方向瘫散、拖在行进方向之后（死腿跟不上身体），轻微摇晃
        /// </summary>
        private Vector2 DanglePoint(in LegState leg, int i) {
            float s = Scale;
            float sway = MathF.Sin(Rig.Time * Spring2D.FrameSeconds * 2.2f + i * 1.7f + Rig.Seed) * 6f * s;
            Vector2 dangle = leg.Hip
                + ForwardDir * (-travelSign * ((i & 1) == 1 ? collapseSide * 0.8f : collapseSide) * s + sway)
                + GroundDir * (Reach * collapseDrop);
            return CollapseFloor != null ? CollapseFloor(dangle) : dangle;
        }

        private void UpdateCollapse(ref LegState leg, int i, float dt) {
            leg.Limp = MathHelper.Clamp(leg.Limp + limpRise * ((i & 1) == 1 ? 0.9f : 1f) * dt, 0f, 1f);
            leg.Planted = false;
            leg.Swinging = false;
            leg.Foot = Vector2.Lerp(leg.Foot, DanglePoint(in leg, i), Spring2D.RateForDt(collapseRate, dt));
        }

        /// <inheritdoc/>
        public override void DebugDraw(SpriteBatch sb, Func<Vector2, Vector2> toScreen) {
            Texture2D px = VaultAsset.placeholder2.Value;
            for (int i = 0; i < legs.Length; i++) {
                ref LegState leg = ref legs[i];
                if (!leg.Inited || !leg.Visible) {
                    continue;
                }
                Color c = leg.Mode != LegMode.Stride ? Color.LightGray : leg.Swinging ? Color.Yellow : Color.LimeGreen;
                Rig2DDebugDraw.Line(sb, toScreen(leg.Hip), toScreen(leg.Foot), c * 0.5f, 1f);
                sb.Draw(px, toScreen(leg.Foot), new Rectangle(0, 0, 1, 1), c, 0f, new Vector2(0.5f), 5f, SpriteEffects.None, 0f);
                if (leg.Mode == LegMode.Stride) {
                    Vector2 rest = leg.Hip + leg.Normal * (Reach * restReach * leg.StrideAccent);
                    sb.Draw(px, toScreen(rest), new Rectangle(0, 0, 1, 1), Color.Cyan * 0.6f, 0f, new Vector2(0.5f), 3f, SpriteEffects.None, 0f);
                }
            }
            if (legs.Length > 0 && legs[0].Inited) {
                Rig2DDebugDraw.Circle(sb, toScreen, legs[0].Hip, Reach * envelopeMax, Color.LimeGreen * 0.2f);
            }
        }
    }
}
