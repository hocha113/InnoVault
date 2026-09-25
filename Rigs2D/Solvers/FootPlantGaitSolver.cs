using InnoVault.Rigs2D.Data;
using InnoVault.Rigs2D.Runtime;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;

namespace InnoVault.Rigs2D.Solvers
{
    /// <summary>
    /// 世界落足步态：足端钉在世界固定点，身体从上面驶过；足落在休息位前方半步幅，随体位移漂到后方半步幅时抬腿换步
    /// <br/>本求解器不写骨骼，只产出每条腿的足端目标（实现 <see cref="IRig2DTargetSource"/>），腿的骨链交给
    /// <see cref="ThreeBoneLegSolver"/> / <see cref="TwoBoneIKSolver"/> / <see cref="PointAtSolver"/> 用 <c>targetSolver</c> 引用
    /// <br/>骨骼：每条腿一根髋骨（多为该腿的第一节）；髋 = 该骨静息近端，静息方向（法线）= 该骨静息轴向，
    /// 体前向 = 该骨父骨骼轴向
    /// <br/>地面探测、落步回调、犁沙出口全部是委托，框架只拥有换步状态机（探测缺省走物块射线）
    /// <br/>体载步面（<c>stance: "body"</c>）：没有真实地面可踩的躯体（蛇形 / 腾空 / 攀附）给每条腿一条随身体走的虚拟地面——
    /// 过「站心（髋骨父骨骼近端）+ 朝下法线 × <c>stanceDepth</c>」、沿体轴倾斜的一条直线；休息位正好落在线上，
    /// 身体沿自身轴前进就让足端落后并换步，爬行与腾空是同一套迈步。体轴与地面向夹角超过 <c>stanceSlopeMax</c>（近垂直爬升 / 俯冲）
    /// 视为无地，腿退回腾空划桨。此模式完全不读 <see cref="Probe"/>；真实物块只该在消费方的表现层（沙尘、瘫软贴地）露面
    /// <br/>参数（JSON 键 → 默认值）：
    /// <list type="bullet">
    /// <item><c>stance</c> "probe"：<c>probe</c> 走 <see cref="Probe"/>（缺省物块射线）/ <c>body</c> 体载步面；<c>stanceDepth</c> 0（像素，0 = 自动：|髋−站心 沿朝下法线| + <c>reach·restReach</c>）；
    /// <c>stanceSlopeMax</c> / <c>stanceSlopeMaxDeg</c> 60°；运行时 <see cref="Stance"/> 可逐帧切换</item>
    /// <item><c>teleportPx</c> 0：髋单帧位移超此值（乘 Scale）视为传送，足端 / 落点锚 / 摆越两端整体平移同一位移，不走应急摆越；0 关闭</item>
    /// <item><c>reach</c> 100：全肢触及（像素，乘 Scale）；<c>restReach</c> 0.56：休息半径占触及比例；<c>strideAccent</c> [1]：逐腿步幅性格差</item>
    /// <item><c>stride</c> 96：步幅（像素）；<c>strideLead</c> 0：休息位沿行进向的前探；<c>stepThreshold</c> 0：足-休息位偏离超此值即想换步（0 关闭，Shrimp 式）</item>
    /// <item><c>rhythm</c> "window"：<c>window</c> 节律窗（<c>stepWindow</c> 0.5、<c>stationLag</c> 1.382、<c>phaseOffsets</c>[]、同站对腿不同时抬）/ <c>group</c> 分组交替（<c>groups</c>[]，对侧组全落地才抬）</item>
    /// <item><c>stations</c> []：逐腿站号（缺省 i / 2）；<c>emergencyStretch</c> 0.9：伸展超此比例无视节律强制换步；<c>stepDown</c> 18：落差超此值触发换步</item>
    /// <item><c>envelopeMin</c> 0.25、<c>envelopeMax</c> 0.84、<c>envelopeSwing</c> 1.0：落足目标可达包络（半径窗 + 相对法线摆角窗）</item>
    /// <item><c>stepFrames</c> 0：固定摆越时长（0 = 按周期推算：<c>swingFraction</c> 0.42、<c>swingMin</c> 5、<c>swingMax</c> 12）；<c>stepClearance</c> 26、<c>pressFraction</c> 0.18、<c>pressPx</c> 3</item>
    /// <item><c>probeLift</c> 46：探地起扫高度；<c>buriedDepth</c> 10：髋没入地下超此值自动收拢；<c>skateStart</c> 15、<c>skateFull</c> 26：滑刹渐入 / 全开速度</item>
    /// <item><c>airRadius</c> 0.42、<c>airRadiusWave</c> 0.11、<c>airBaseTilt</c> 0（法线向体后的常偏）、<c>airTilt</c> 0.62（摆幅）、<c>airRate</c> 0.18、<c>airPhaseRate</c> 0.16、<c>airPhaseSpeed</c> 0.012、<c>airPhaseStep</c> 1.05：腾空划桨 / 抓挠（正倾角一律朝体后）</item>
    /// <item><c>tuckBack</c> 28、<c>tuckStep</c> 6、<c>tuckSide</c> 7、<c>tuckRate</c> 0.28：收拢贴体；<c>collapseDrop</c> 0.9、<c>collapseSide</c> 22、<c>collapseRate</c> 0.16、<c>limpRise</c> 0.055、<c>limpFall</c> 0.05：失力垂软</item>
    /// <item><c>holdRate</c> 0.16、<c>holdStepThreshold</c> 30：外部目标模式的跟随率与换抓阈值；<c>groundnessBand</c> 0.6：走地权重的过渡带</item>
    /// </list>
    /// </summary>
    public sealed class FootPlantGaitSolver : Rig2DSolver, IRig2DTargetSource
    {
        /// <summary>
        /// 单腿姿态模式
        /// </summary>
        public enum LegMode
        {
            /// <summary>
            /// 世界落足步行（够不着地时自动转腾空）
            /// </summary>
            Walk,
            /// <summary>
            /// 腾空：划桨 / 抓挠空气
            /// </summary>
            Air,
            /// <summary>
            /// 收拢贴体（钻沙 / 掠冲）
            /// </summary>
            Tuck,
            /// <summary>
            /// 失力垂软（死亡演出）
            /// </summary>
            Collapse,
            /// <summary>
            /// 跟随外部目标（立起、抓柱等消费方自算的姿态）
            /// </summary>
            Hold,
        }

        /// <summary>
        /// 步面来源
        /// </summary>
        public enum StanceMode
        {
            /// <summary>
            /// 探测器：<see cref="Probe"/>（缺省 <see cref="Rig2DGround.TileProbe(Vector2, Vector2, float, out Vector2)"/> 物块射线）
            /// </summary>
            Probe,
            /// <summary>
            /// 体载步面：每条腿脚下一条随身体走、沿体轴倾斜的虚拟地面（见类说明）
            /// </summary>
            Body,
        }

        /// <summary>
        /// 换步节律
        /// </summary>
        public enum Rhythm
        {
            /// <summary>
            /// 节律窗：每腿一个相位槽，窗内且同站对腿落地才许抬（蜈蚣波）
            /// </summary>
            Window,
            /// <summary>
            /// 分组交替：对侧组全部落地才许抬（三角步态）
            /// </summary>
            Group,
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
            /// 落点锚（落地时足端钉在这里）
            /// </summary>
            public Vector2 PlantPos;
            /// <summary>
            /// 本帧髋位置
            /// </summary>
            public Vector2 Hip;
            /// <summary>
            /// 本帧法线（静息方向，单位向量）
            /// </summary>
            public Vector2 Normal;
            /// <summary>
            /// 摆越起点 / 终点
            /// </summary>
            public Vector2 SwingFrom, SwingTo;
            /// <summary>
            /// 摆越进度 0..1（未摆越时无意义）
            /// </summary>
            public float SwingT;
            /// <summary>
            /// 摆越时长（帧）与离地余隙
            /// </summary>
            public float SwingDur, SwingClearance;
            /// <summary>
            /// 落地中 / 摆越中 / 足下有承托
            /// </summary>
            public bool Planted, Swinging, Grounded;
            /// <summary>
            /// 已初始化
            /// </summary>
            public bool Inited;
            /// <summary>
            /// 是否参与（站宿主缺失时消费方关掉）
            /// </summary>
            public bool Visible;
            /// <summary>
            /// 走地权重 0..1（法线朝地面程度）
            /// </summary>
            public float Groundness;
            /// <summary>
            /// 失力度 0..1
            /// </summary>
            public float Limp;
            /// <summary>
            /// 犁沙热度 0..1
            /// </summary>
            public float DragHeat;
            /// <summary>
            /// 已落地的步数（每次摆越落地自增；确定性哈希的盐，各端一致）
            /// </summary>
            public int StepCount;
            /// <summary>
            /// 本帧生效的模式
            /// </summary>
            public LegMode Mode;
            /// <summary>
            /// 站号 / 分组 / 相位偏移 / 步幅性格
            /// </summary>
            public int Station, Group;
            /// <summary>
            /// 相位偏移（弧度）
            /// </summary>
            public float PhaseOffset;
            /// <summary>
            /// 步幅性格差
            /// </summary>
            public float StrideAccent;
            /// <summary>
            /// 休息位附加世界偏移（蹲伏时前后站沿行进向撑开一类），由 <see cref="SetLegRestOffset"/> 写
            /// </summary>
            public Vector2 RestOffset;
            internal LegMode? Override;
            internal Vector2 HoldTarget;
            internal bool HoldStep;
            /// <summary>上一次步进的髋位置（传送判定用）</summary>
            internal Vector2 PrevHip;
            internal bool HasPrevHip;
        }

        /// <summary>摆越时长下限（帧）：更短的摆越读不出抬落，<see cref="RequestStep"/> 与常规换步都按它钳</summary>
        public const float MinSwingFrames = 4f;

        private LegState[] legs = [];
        private float stanceDepth;
        private float stanceSlopeMax;
        private float teleportPx;
        private float reach;
        private float restReach;
        private float stride;
        private float strideLead;
        private float stepThreshold;
        private Rhythm rhythm;
        private float stepWindow;
        private float stationLag;
        private float emergencyStretch;
        private float stepDown;
        private float envelopeMin;
        private float envelopeMax;
        private float envelopeSwing;
        private float stepFrames;
        private float swingFraction;
        private float swingMin;
        private float swingMax;
        private float stepClearance;
        private float pressFraction;
        private float pressPx;
        private float probeLift;
        private float buriedDepth;
        private float skateStart;
        private float skateFull;
        private float airRadius;
        private float airRadiusWave;
        private float airBaseTilt;
        private float airTilt;
        private float airRate;
        private float airPhaseRate;
        private float airPhaseSpeed;
        private float airPhaseStep;
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
        private float holdStepThreshold;
        private float groundnessBand;

        private Vector2 travel = Vector2.UnitX;
        private float travelSign = 1f;
        private float airPhase;
        private bool anyAir;

        /// <summary>
        /// 宿主速度（px/帧），行进向、步幅时钟与滑刹都读它
        /// </summary>
        public Vector2 Velocity { get; set; }
        /// <summary>
        /// 步态时钟相位（弧度）；<see cref="AutoPhase"/> 时按路程自动推进（一个步幅 = 一个周期）
        /// </summary>
        public float Phase { get; set; }
        /// <summary>
        /// 是否按 <see cref="Velocity"/> 沿行进向的位移自动推进 <see cref="Phase"/>
        /// </summary>
        public bool AutoPhase { get; set; } = true;
        /// <summary>
        /// 指向地面的单位向量（世界步行为 (0, 1)；贴附体给体腹侧）
        /// </summary>
        public Vector2 GroundDir { get; set; } = Vector2.UnitY;
        /// <summary>
        /// 地面探测（<see langword="null"/> 走 <see cref="Rig2DGround.TileProbe(Vector2, Vector2, float, out Vector2)"/>）；
        /// <see cref="Stance"/> 为 <see cref="StanceMode.Body"/> 时不读
        /// </summary>
        public Rig2DGroundProbe Probe { get; set; }
        /// <summary>
        /// 步面来源（逐帧可改；Configure 时重置为参数 <c>stance</c>）：战斗端体载步面、图鉴平沙线 / 瘫软贴真实沙面时切回探测器
        /// </summary>
        public StanceMode Stance { get; set; }
        /// <summary>
        /// 全局模式（逐腿可用 <see cref="SetLegMode"/> 覆盖）
        /// </summary>
        public LegMode Mode { get; set; } = LegMode.Walk;
        /// <summary>
        /// 是否贴附于地面；假时全部腿腾空划桨
        /// </summary>
        public bool Attached { get; set; } = true;
        /// <summary>
        /// 失力站数（<see cref="LegMode.Collapse"/> 下站号小于此值的腿逐站瘫软，奇侧等偶侧先软）
        /// </summary>
        public int CollapsedStations { get; set; } = int.MaxValue;
        /// <summary>
        /// 休息半径逐帧倍率（蹲伏站距外扩 1.14 一类），乘在参数 <c>restReach</c> 上
        /// </summary>
        public float RestReachScale { get; set; } = 1f;
        /// <summary>
        /// 步幅逐帧倍率（蹲伏碎步 0.5 一类）
        /// </summary>
        public float StrideScale { get; set; } = 1f;
        /// <summary>
        /// 是否允许高速滑刹（蹲伏时关掉）
        /// </summary>
        public bool AllowSkate { get; set; } = true;
        /// <summary>
        /// 落步回调 (腿号, 足端, 权重)：偶侧 1、奇侧 0.7
        /// </summary>
        public Action<int, Vector2, float> OnPlant { get; set; }
        /// <summary>
        /// 滑刹回调 (腿号, 落点锚, 滑刹量 0..1)，每帧滑刹中都会调
        /// </summary>
        public Action<int, Vector2, float> OnDrag { get; set; }
        /// <summary>
        /// 落点过滤 (腿号, 已落地步数, 规划落点) → 实际落点：常规换步选好落点、探地之前调用，
        /// 用来做踉跄落短、逐腿落点偏置一类确定性修饰（返回值会再投到地面）；<see langword="null"/> 不过滤
        /// </summary>
        public Func<int, int, Vector2, Vector2> StepTargetFilter { get; set; }

        /// <summary>
        /// 腿数
        /// </summary>
        public int LegCount => legs.Length;
        /// <summary>
        /// 平滑行进方向（单位向量）
        /// </summary>
        public Vector2 Travel => travel;
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
        /// 世界步幅（含 Scale）
        /// </summary>
        public float Stride => stride * Scale;

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
        /// 给某腿外部目标并切到 <see cref="LegMode.Hold"/>；<paramref name="step"/> 为真时偏离超阈值走摆越换抓而非平滑跟随
        /// </summary>
        public void SetLegHold(int index, Vector2 target, bool step = false) {
            if (index >= 0 && index < legs.Length) {
                legs[index].Override = LegMode.Hold;
                legs[index].HoldTarget = target;
                legs[index].HoldStep = step;
            }
        }

        /// <summary>
        /// 设置某腿休息位的附加世界偏移
        /// </summary>
        public void SetLegRestOffset(int index, Vector2 offset) {
            if (index >= 0 && index < legs.Length) {
                legs[index].RestOffset = offset;
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
                legs[i].DragHeat = 0f;
            }
        }

        /// <summary>
        /// 该腿的节律槽相位 0..1
        /// </summary>
        public float SlotPhase01(int index) {
            float phase = Phase + legs[index].PhaseOffset;
            phase %= MathHelper.TwoPi;
            if (phase < 0f) {
                phase += MathHelper.TwoPi;
            }
            return phase / MathHelper.TwoPi;
        }

        /// <inheritdoc/>
        public override ReadOnlySpan<int> DrivenBones => ReadOnlySpan<int>.Empty;

        /// <inheritdoc/>
        protected override void Configure(Solver2DDef def) {
            int n = bones.Length;
            Stance = def.GetString("stance", "probe").ToLowerInvariant() == "body" ? StanceMode.Body : StanceMode.Probe;
            stanceDepth = def.GetFloat("stanceDepth", 0f);
            stanceSlopeMax = def.GetAngle("stanceSlopeMax", MathHelper.ToRadians(60f));
            teleportPx = def.GetFloat("teleportPx", 0f);
            reach = def.GetFloat("reach", 100f);
            restReach = def.GetFloat("restReach", 0.56f);
            stride = def.GetFloat("stride", 96f);
            strideLead = def.GetFloat("strideLead", 0f);
            stepThreshold = def.GetFloat("stepThreshold", 0f);
            rhythm = def.GetString("rhythm", "window").ToLowerInvariant() == "group" ? Rhythm.Group : Rhythm.Window;
            stepWindow = def.GetFloat("stepWindow", 0.5f);
            stationLag = def.GetAngle("stationLag", MathHelper.TwoPi * 0.22f);
            emergencyStretch = def.GetFloat("emergencyStretch", 0.9f);
            stepDown = def.GetFloat("stepDown", 18f);
            envelopeMin = def.GetFloat("envelopeMin", 0.25f);
            envelopeMax = def.GetFloat("envelopeMax", 0.84f);
            envelopeSwing = def.GetAngle("envelopeSwing", 1.0f);
            stepFrames = def.GetFloat("stepFrames", 0f);
            swingFraction = def.GetFloat("swingFraction", 0.42f);
            swingMin = def.GetFloat("swingMin", 5f);
            swingMax = def.GetFloat("swingMax", 12f);
            stepClearance = def.GetFloat("stepClearance", 26f);
            pressFraction = def.GetFloat("pressFraction", 0.18f);
            pressPx = def.GetFloat("pressPx", 3f);
            probeLift = def.GetFloat("probeLift", 46f);
            buriedDepth = def.GetFloat("buriedDepth", 10f);
            skateStart = def.GetFloat("skateStart", 15f);
            skateFull = def.GetFloat("skateFull", 26f);
            airRadius = def.GetFloat("airRadius", 0.42f);
            airRadiusWave = def.GetFloat("airRadiusWave", 0.11f);
            airBaseTilt = def.GetAngle("airBaseTilt", 0f);
            airTilt = def.GetAngle("airTilt", 0.62f);
            airRate = def.GetFloat("airRate", 0.18f);
            airPhaseRate = def.GetFloat("airPhaseRate", 0.16f);
            airPhaseSpeed = def.GetFloat("airPhaseSpeed", 0.012f);
            airPhaseStep = def.GetFloat("airPhaseStep", 1.05f);
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
            holdStepThreshold = def.GetFloat("holdStepThreshold", 30f);
            groundnessBand = def.GetFloat("groundnessBand", 0.6f);

            int[] stations = def.GetIntArray("stations", n, -1);
            int[] groups = def.GetIntArray("groups", n, -1);
            float[] offsets = def.GetFloatArray("phaseOffsets", n, float.NaN);
            bool hasOffsets = def.Has("phaseOffsets");
            float[] accents = def.GetFloatArray("strideAccent", n, 1f);

            if (legs.Length != n) {
                legs = new LegState[n];
            }
            for (int i = 0; i < n; i++) {
                ref LegState leg = ref legs[i];
                leg.Station = stations[i] >= 0 ? stations[i] : i / 2;
                leg.Group = groups[i] >= 0 ? groups[i] : (leg.Station + (i & 1)) % 2;
                leg.PhaseOffset = hasOffsets && !float.IsNaN(offsets[i])
                    ? offsets[i]
                    : -leg.Station * stationLag + ((i & 1) == 1 ? MathHelper.Pi : 0f);
                leg.StrideAccent = accents[i];
                if (!leg.Inited) {
                    leg.Visible = true;
                }
            }
        }

        /// <summary>
        /// 第 <paramref name="i"/> 条腿的地面探测：体载步面模式按该腿自家站的虚拟地面求交，否则走 <see cref="Probe"/>
        /// </summary>
        private bool ProbeGround(int i, Vector2 from, Vector2 dir, float maxDistance, out Vector2 hit) {
            if (Stance == StanceMode.Body) {
                return BodyPlaneProbe(i, from, dir, maxDistance, out hit);
            }
            if (Probe != null) {
                return Probe(from, dir, maxDistance, out hit);
            }
            return Rig2DGround.DefaultProbe(from, dir, maxDistance, out hit);
        }

        /// <summary>
        /// 某腿的体载步面：过「站心 + 朝下法线 × 深度」、沿体轴倾斜的直线。体轴过陡返回假（无地）
        /// </summary>
        /// <param name="i">腿号</param>
        /// <param name="anchor">直线上的锚点</param>
        /// <param name="axis">直线方向（体轴单位向量）</param>
        /// <param name="down">朝下法线（体轴两侧法线里与 <see cref="GroundDir"/> 同向的一支）</param>
        private bool BodyPlane(int i, out Vector2 anchor, out Vector2 axis, out Vector2 down) {
            float bodyDir = ParentDir(i);
            axis = new Vector2(MathF.Cos(bodyDir), MathF.Sin(bodyDir));
            //近垂直爬升 / 俯冲：两侧腿都朝侧，本就不该迈步
            if (Math.Abs(Vector2.Dot(axis, GroundDir)) > MathF.Sin(stanceSlopeMax)) {
                anchor = default;
                down = default;
                return false;
            }
            down = new Vector2(-axis.Y, axis.X);
            if (Vector2.Dot(down, GroundDir) < 0f) {
                down = -down;
            }
            Vector2 station = ParentPos(i);
            float depth = stanceDepth > 0f
                ? stanceDepth * Scale
                : Math.Abs(Vector2.Dot(legs[i].Hip - station, down)) + Reach * restReach;
            anchor = station + down * depth;
            return true;
        }

        /// <summary>
        /// 体载步面探测：从 <paramref name="from"/> 沿 <paramref name="dir"/> 与步面直线求交；返回约定镜像
        /// <see cref="Rig2DGround.FromHeight"/>（交点在 <paramref name="maxDistance"/> 内即命中，负距离——步面在起点之上——也算命中）
        /// </summary>
        private bool BodyPlaneProbe(int i, Vector2 from, Vector2 dir, float maxDistance, out Vector2 hit) {
            if (!BodyPlane(i, out Vector2 anchor, out _, out Vector2 down)) {
                hit = from + dir * maxDistance;
                return false;
            }
            float denom = Vector2.Dot(dir, down);
            if (Math.Abs(denom) < 0.0001f) {
                hit = from + dir * maxDistance;
                return false;
            }
            float t = Vector2.Dot(anchor - from, down) / denom;
            hit = from + dir * t;
            return t <= maxDistance;
        }

        /// <inheritdoc/>
        public override void Snap() {
            for (int i = 0; i < legs.Length; i++) {
                ref LegState leg = ref legs[i];
                leg.Hip = RestPosition(i);
                leg.Normal = RestForward(i);
                LegMode mode = leg.Override ?? Mode;
                if (!Attached && mode == LegMode.Walk) {
                    mode = LegMode.Air;
                }
                Vector2 f0;
                if (mode == LegMode.Air) {
                    //腾空：直接摆在划桨半径上，不去探地
                    f0 = leg.Hip + leg.Normal * (Reach * airRadius);
                    leg.Grounded = false;
                    leg.Planted = false;
                }
                else {
                    f0 = leg.Hip + leg.Normal * (Reach * restReach * leg.StrideAccent);
                    leg.Grounded = false;
                    if (ProbeGround(i, f0 - GroundDir * (probeLift * Scale), GroundDir, probeLift * Scale + Reach, out Vector2 g)) {
                        //足端不许落到地面之下
                        if (Vector2.Dot(f0 - g, GroundDir) > 0f) {
                            f0 = g;
                        }
                        leg.Grounded = true;
                    }
                    leg.Planted = true;
                }
                leg.Foot = f0;
                leg.PlantPos = f0;
                leg.Swinging = false;
                leg.Limp = 0f;
                leg.DragHeat = 0f;
                leg.Mode = mode;
                leg.PrevHip = leg.Hip;
                leg.HasPrevHip = true;
                leg.Inited = true;
            }
        }

        /// <inheritdoc/>
        public override void Step(float dt) {
            if (legs.Length == 0) {
                return;
            }
            float speed = Velocity.Length();
            if (speed > 1.2f) {
                Vector2 dir = Velocity / speed;
                travel = Vector2.Lerp(travel, dir, Spring2D.RateForDt(0.1f, dt));
                if (travel.LengthSquared() < 0.0001f) {
                    travel = dir;
                }
                travel.Normalize();
                //行进符号：沿地面前向（地面朝下即 +X）为正
                float forward = Vector2.Dot(Velocity, ForwardDir);
                if (Math.Abs(forward) > 1.2f) {
                    travelSign = MathHelper.Lerp(travelSign, Math.Sign(forward), Spring2D.RateForDt(0.08f, dt));
                }
            }
            if (AutoPhase && Stride > 0.01f) {
                Phase += Vector2.Dot(Velocity, travel) / Stride * MathHelper.TwoPi * dt;
            }
            if (anyAir) {
                airPhase += (airPhaseRate + speed * airPhaseSpeed) * dt;
            }
            anyAir = false;

            for (int i = 0; i < legs.Length; i++) {
                ref LegState leg = ref legs[i];
                if (!leg.Visible) {
                    continue;
                }
                leg.Hip = RestPosition(i);
                leg.Normal = RestForward(i);
                leg.Groundness = MathHelper.Clamp((Vector2.Dot(leg.Normal, GroundDir) + groundnessBand) / (2f * groundnessBand), 0f, 1f);

                if (!leg.Inited) {
                    InitLeg(ref leg, i);
                }
                else if (teleportPx > 0f && leg.HasPrevHip) {
                    //传送：髋单帧跳过阈值，足端 / 落点锚 / 摆越两端整体搬走，不让钉在旧世界点的脚走应急摆越拉丝
                    Vector2 delta = leg.Hip - leg.PrevHip;
                    float tp = teleportPx * Scale;
                    if (delta.LengthSquared() > tp * tp) {
                        leg.Foot += delta;
                        leg.PlantPos += delta;
                        leg.SwingFrom += delta;
                        leg.SwingTo += delta;
                    }
                }
                leg.PrevHip = leg.Hip;
                leg.HasPrevHip = true;

                LegMode mode = leg.Override ?? Mode;
                if (!Attached && mode == LegMode.Walk) {
                    mode = LegMode.Air;
                }
                bool limpDecay = true;
                switch (mode) {
                    case LegMode.Collapse when StationCollapsed(i):
                        UpdateCollapse(ref leg, i, dt);
                        limpDecay = false;
                        break;
                    case LegMode.Tuck:
                        UpdateTuck(ref leg, i, dt);
                        break;
                    case LegMode.Air:
                        UpdateAir(ref leg, i, dt);
                        break;
                    case LegMode.Hold:
                        UpdateHold(ref leg, i, dt);
                        break;
                    default:
                        mode = UpdateWalk(ref leg, i, speed, dt);
                        break;
                }
                leg.Mode = mode;
                if (limpDecay) {
                    leg.Limp = MathHelper.Clamp(leg.Limp - limpFall * dt, 0f, 1f);
                }
            }
        }

        private void InitLeg(ref LegState leg, int i) {
            Vector2 f0 = leg.Hip + leg.Normal * (Reach * restReach * leg.StrideAccent);
            if (ProbeGround(i, f0 - GroundDir * (probeLift * Scale), GroundDir, probeLift * Scale + Reach, out Vector2 g)
                && Vector2.Dot(f0 - g, GroundDir) > 0f) {
                f0 = g;
            }
            leg.Foot = f0;
            leg.PlantPos = f0;
            leg.Planted = true;
            leg.Swinging = false;
            leg.Limp = 0f;
            leg.PrevHip = leg.Hip;
            leg.HasPrevHip = true;
            leg.Inited = true;
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

        private LegMode UpdateWalk(ref LegState leg, int i, float speed, float dt) {
            float reachPx = Reach;
            float s = Scale;
            Vector2 lead = speed > 0.4f ? travel * (strideLead * s) : Vector2.Zero;
            Vector2 restProbe = leg.Hip + leg.Normal * (reachPx * restReach * RestReachScale * leg.StrideAccent) + lead + leg.RestOffset;
            Vector2 probeFrom = restProbe - GroundDir * (probeLift * s);
            bool hit = ProbeGround(i, probeFrom, GroundDir, probeLift * s + reachPx * 0.9f + 10f * s, out Vector2 ground);

            if (!hit) {
                UpdateAir(ref leg, i, dt);
                return LegMode.Air;
            }

            //髋没入地下：自动收拢（钻沙途中残留步行指令的兜底）
            float groundDist = Vector2.Dot(ground - leg.Hip, GroundDir);
            if (groundDist < -buriedDepth * s) {
                UpdateTuck(ref leg, i, dt);
                return LegMode.Tuck;
            }

            bool plantable = leg.Groundness > 0.3f && groundDist < reachPx * 0.9f && groundDist >= -10f * s;
            if (!plantable) {
                UpdateAir(ref leg, i, dt);
                return LegMode.Air;
            }

            Vector2 rest = ground;
            float skate = AllowSkate ? MathHelper.Clamp((speed - skateStart) / Math.Max(skateFull - skateStart, 0.001f), 0f, 1f) : 0f;

            if (leg.Swinging) {
                AdvanceSwing(ref leg, i, dt);
                return LegMode.Walk;
            }

            if (!leg.Planted) {
                //从空中 / 其他姿态回到步行：远则快摆落位（防瞬移贴地），近则就地落桩
                if (Vector2.Distance(leg.Foot, rest) > 14f * s) {
                    Vector2 landing = ClampToEnvelope(leg.Hip, leg.Normal, rest);
                    landing = ProjectToGround(i, landing);
                    BeginSwing(ref leg, landing, 7f, 14f * s);
                    return LegMode.Walk;
                }
                leg.PlantPos = rest;
                leg.Planted = true;
                leg.Grounded = true;
            }

            //滑刹：锚点随体滑移（部分抓地），滑差犁出连续痕
            if (skate > 0.01f) {
                leg.PlantPos += Velocity * (skate * 0.8f * dt);
                leg.DragHeat = MathHelper.Clamp(leg.DragHeat + 0.12f * dt, 0f, 1f);
                OnDrag?.Invoke(i, leg.PlantPos, skate);
            }
            else {
                leg.DragHeat = MathHelper.Clamp(leg.DragHeat - 0.08f * dt, 0f, 1f);
            }

            //地形跟随：小落差贴、大落差触发应急换步
            float groundGap = 0f;
            if (ProbeGround(i, leg.PlantPos - GroundDir * (probeLift * s), GroundDir, probeLift * s + reachPx, out Vector2 plantGround)) {
                groundGap = Math.Abs(Vector2.Dot(plantGround - leg.PlantPos, GroundDir));
                if (groundGap < stepDown * s) {
                    leg.PlantPos = plantGround;
                    groundGap = 0f;
                }
            }

            float strideI = Stride * leg.StrideAccent * StrideScale;
            float along = Vector2.Dot(leg.PlantPos - rest, travel);
            float stretch = Vector2.Distance(leg.Hip, leg.PlantPos) / Math.Max(reachPx, 0.001f);
            bool emergency = stretch > emergencyStretch || groundGap >= stepDown * s;
            bool behind = along < -0.5f * strideI * (1f + skate * 0.8f);
            bool drift = stepThreshold > 0f && Vector2.Distance(leg.PlantPos, rest) > stepThreshold * s;
            bool wantStep = behind || drift || emergency;

            if (wantStep && (emergency || RhythmAllows(i))) {
                //落点：休息位前方半步幅 + 少量速度前瞻，钳进预测髋的可达包络
                Vector2 target = rest + travel * (0.5f * strideI) + Velocity * 2f;
                Vector2 hipFuture = leg.Hip + Velocity * 3f;
                target = ClampToEnvelope(hipFuture, leg.Normal, target);
                if (StepTargetFilter != null) {
                    target = StepTargetFilter(i, leg.StepCount, target);
                }
                target = ProjectToGround(i, target);

                float dur;
                if (stepFrames > 0f) {
                    dur = Math.Max(stepFrames, MinSwingFrames);
                }
                else {
                    float cycle = strideI / Math.Max(speed, 3f);
                    dur = MathHelper.Clamp(cycle * swingFraction, Math.Max(swingMin, MinSwingFrames), Math.Max(swingMax, MinSwingFrames));
                }
                float clearance = stepClearance * s * (0.8f + 0.25f * leg.StrideAccent) * (1f + skate * 0.4f);
                BeginSwing(ref leg, target, dur, clearance);
                return LegMode.Walk;
            }

            leg.Foot = leg.PlantPos;
            leg.Grounded = true;
            return LegMode.Walk;
        }

        private bool RhythmAllows(int i) {
            if (rhythm == Rhythm.Group) {
                int other = 1 - legs[i].Group;
                for (int k = 0; k < legs.Length; k++) {
                    if (legs[k].Visible && legs[k].Group == other && legs[k].Swinging) {
                        return false;
                    }
                }
                return true;
            }
            bool windowOpen = SlotPhase01(i) < stepWindow;
            int partner = i ^ 1;
            bool partnerSwinging = partner < legs.Length && legs[partner].Visible && legs[partner].Swinging;
            return windowOpen && !partnerSwinging;
        }

        private Vector2 ClampToEnvelope(Vector2 hip, Vector2 normal, Vector2 target) {
            float reachPx = Reach;
            return ReachEnvelope.ClampSwing(hip, normal, target, reachPx * envelopeMin, reachPx * envelopeMax, envelopeSwing);
        }

        private Vector2 ProjectToGround(int i, Vector2 point) {
            float s = Scale;
            if (ProbeGround(i, point - GroundDir * (probeLift * s), GroundDir, probeLift * s + Reach, out Vector2 g)) {
                return g;
            }
            return point;
        }

        private static void BeginSwing(ref LegState leg, Vector2 target, float dur, float clearance) {
            leg.SwingFrom = leg.Foot;
            leg.SwingTo = target;
            leg.SwingT = 0f;
            leg.SwingDur = Math.Max(dur, MinSwingFrames);
            leg.SwingClearance = clearance;
            leg.Swinging = true;
            leg.Planted = false;
            leg.Grounded = false;
        }

        /// <summary>
        /// 摆越推进：预备下压 → 主摆（水平缓动 + 抛物离地）→ 落地
        /// </summary>
        private void AdvanceSwing(ref LegState leg, int i, float dt) {
            leg.SwingT += dt / Math.Max(leg.SwingDur, MinSwingFrames);
            float t = Math.Min(leg.SwingT, 1f);
            float s = Scale;

            Vector2 pos;
            if (t < pressFraction) {
                float press = MathF.Sin(t / pressFraction * MathHelper.Pi) * pressPx * s;
                pos = leg.SwingFrom + GroundDir * press;
            }
            else {
                float m = (t - pressFraction) / (1f - pressFraction);
                float horiz = Spring2D.SmoothStep01(m);
                pos = Vector2.Lerp(leg.SwingFrom, leg.SwingTo, horiz);
                pos -= GroundDir * (MathF.Sin(m * MathHelper.Pi) * leg.SwingClearance);
            }

            //摆越途中不许穿地
            if (ProbeGround(i, pos - GroundDir * (60f * s), GroundDir, 60f * s, out Vector2 g)
                && Vector2.Dot(pos - g, GroundDir) > 0f) {
                pos = g;
            }
            leg.Foot = pos;

            if (leg.SwingT >= 1f) {
                leg.Swinging = false;
                leg.Planted = true;
                leg.Grounded = true;
                leg.PlantPos = leg.SwingTo;
                leg.Foot = leg.SwingTo;
                leg.StepCount++;
                //回调里允许 RequestStep 立刻接一步（踉跄补步）：它直接改 legs[i]，与这里的 ref 同一块内存
                OnPlant?.Invoke(i, leg.Foot, (i & 1) == 0 ? 1f : 0.7f);
            }
        }

        /// <summary>
        /// 让某腿立刻起一步摆越到 <paramref name="target"/>（会投到地面），无视节律窗；
        /// 典型用法是在 <see cref="OnPlant"/> 里给落短的脚接一记快速补步。腿不可见或不在步行 / 外部目标模式时忽略
        /// </summary>
        /// <param name="index">腿号</param>
        /// <param name="target">落点（世界）</param>
        /// <param name="frames">摆越时长（帧，下限 <see cref="MinSwingFrames"/>）</param>
        /// <param name="clearance">离地余隙（像素，Scale 为 1 的量）</param>
        public void RequestStep(int index, Vector2 target, float frames = 6f, float clearance = 9f) {
            if (index < 0 || index >= legs.Length) {
                return;
            }
            ref LegState leg = ref legs[index];
            if (!leg.Visible || !leg.Inited || leg.Mode is LegMode.Air or LegMode.Tuck or LegMode.Collapse) {
                return;
            }
            BeginSwing(ref leg, ProjectToGround(index, target), frames, clearance * Scale);
        }

        /// <summary>
        /// 腾空划桨 / 抓挠：法线向体后倾一个常偏再正弦摆动，半径双频异速调制，幅度压在包络摆角窗内；
        /// 倾角符号按"法线顺转 90° 是否指向体后"自动取，两侧腿解剖对称
        /// </summary>
        private void UpdateAir(ref LegState leg, int i, float dt) {
            anyAir = true;
            leg.Planted = false;
            leg.Swinging = false;
            leg.Grounded = false;
            leg.DragHeat = MathHelper.Clamp(leg.DragHeat - 0.08f * dt, 0f, 1f);
            float ph = airPhase + i * airPhaseStep + Rig.Seed;
            float bodyDir = ParentDir(i);
            Vector2 back = new(-MathF.Cos(bodyDir), -MathF.Sin(bodyDir));
            Vector2 normalCw = new(-leg.Normal.Y, leg.Normal.X);
            float backSign = Vector2.Dot(normalCw, back) >= 0f ? 1f : -1f;
            float tilt = MathHelper.Clamp(airBaseTilt + MathF.Sin(ph) * airTilt, -envelopeSwing, envelopeSwing) * backSign;
            float radius = Reach * (airRadius + airRadiusWave * MathF.Sin(ph * 2f + leg.Station * 1.3f));
            float cos = MathF.Cos(tilt);
            float sin = MathF.Sin(tilt);
            Vector2 dir = new(leg.Normal.X * cos - leg.Normal.Y * sin, leg.Normal.X * sin + leg.Normal.Y * cos);
            Vector2 target = leg.Hip + dir * radius;
            leg.Foot = Vector2.Lerp(leg.Foot, target, Spring2D.RateForDt(airRate, dt));
        }

        /// <summary>
        /// 收拢贴体：沿体轴向后掠平
        /// </summary>
        private void UpdateTuck(ref LegState leg, int i, float dt) {
            leg.Planted = false;
            leg.Swinging = false;
            leg.Grounded = false;
            float s = Scale;
            float bodyDir = ParentDir(i);
            Vector2 forward = new(MathF.Cos(bodyDir), MathF.Sin(bodyDir));
            Vector2 fold = leg.Hip - forward * ((tuckBack + leg.Station * tuckStep + (i & 1) * 8f) * s) + leg.Normal * (tuckSide * s);
            leg.Foot = Vector2.Lerp(leg.Foot, fold, Spring2D.RateForDt(tuckRate, dt));
        }

        /// <summary>
        /// 失力垂软：向地面方向瘫散、拖在行进方向之后（死腿跟不上身体），轻微摇晃
        /// </summary>
        private void UpdateCollapse(ref LegState leg, int i, float dt) {
            leg.Limp = MathHelper.Clamp(leg.Limp + limpRise * ((i & 1) == 1 ? 0.9f : 1f) * dt, 0f, 1f);
            leg.Planted = false;
            leg.Swinging = false;
            float s = Scale;
            float sway = MathF.Sin(Rig.Time * Spring2D.FrameSeconds * 2.2f + i * 1.7f + Rig.Seed) * 6f * s;
            Vector2 dangle = leg.Hip
                + ForwardDir * (-travelSign * ((i & 1) == 1 ? collapseSide * 0.8f : collapseSide) * s + sway)
                + GroundDir * (Reach * collapseDrop);
            dangle = ClampAboveGround(i, dangle);
            leg.Foot = Vector2.Lerp(leg.Foot, dangle, Spring2D.RateForDt(collapseRate, dt));
            leg.Grounded = false;
        }

        /// <summary>
        /// 外部目标：平滑跟随，或偏离超阈值时摆越换抓（抓柱攀爬一类）
        /// </summary>
        private void UpdateHold(ref LegState leg, int i, float dt) {
            leg.DragHeat = MathHelper.Clamp(leg.DragHeat - 0.08f * dt, 0f, 1f);
            if (!leg.HoldStep) {
                leg.Planted = false;
                leg.Swinging = false;
                leg.Foot = Vector2.Lerp(leg.Foot, leg.HoldTarget, Spring2D.RateForDt(holdRate, dt));
                return;
            }
            if (leg.Swinging) {
                AdvanceSwing(ref leg, i, dt);
                return;
            }
            if (!leg.Planted) {
                leg.PlantPos = leg.Foot;
                leg.Planted = true;
            }
            float drift = Vector2.Distance(leg.PlantPos, leg.HoldTarget);
            float stretch = Vector2.Distance(leg.Hip, leg.PlantPos) / Math.Max(Reach, 0.001f);
            if (drift > holdStepThreshold * Scale || stretch > emergencyStretch) {
                BeginSwing(ref leg, leg.HoldTarget, 8f, 12f * Scale);
                return;
            }
            leg.Foot = leg.PlantPos;
        }

        private Vector2 ClampAboveGround(int i, Vector2 point) {
            float s = Scale;
            if (ProbeGround(i, point - GroundDir * (probeLift * s + Reach), GroundDir, probeLift * s + Reach, out Vector2 g)
                && Vector2.Dot(point - g, GroundDir) > 0f) {
                return g;
            }
            return point;
        }

        /// <inheritdoc/>
        public override void DebugDraw(SpriteBatch sb, Func<Vector2, Vector2> toScreen) {
            Texture2D px = Rig2DDebugDraw.Pixel;
            if (px == null) {
                return;
            }
            for (int i = 0; i < legs.Length; i++) {
                ref LegState leg = ref legs[i];
                if (!leg.Inited || !leg.Visible) {
                    continue;
                }
                Color c = leg.Swinging ? Color.Yellow : leg.Planted ? Color.LimeGreen : Color.LightGray;
                Rig2DDebugDraw.Line(sb, toScreen(leg.Hip), toScreen(leg.Foot), c * 0.5f, 1f);
                sb.Draw(px, toScreen(leg.Foot), new Rectangle(0, 0, 1, 1), c, 0f, new Vector2(0.5f), 5f, SpriteEffects.None, 0f);
                if (leg.Mode == LegMode.Walk) {
                    Vector2 rest = leg.Hip + leg.Normal * (Reach * restReach * leg.StrideAccent);
                    sb.Draw(px, toScreen(rest), new Rectangle(0, 0, 1, 1), Color.Cyan * 0.6f, 0f, new Vector2(0.5f), 3f, SpriteEffects.None, 0f);
                }
                //体载步面：画出该腿脚下那条虚拟地面（过陡时不画，腿本就在划桨）
                if (Stance == StanceMode.Body && (i & 1) == 0 && BodyPlane(i, out Vector2 anchor, out Vector2 axis, out _)) {
                    Vector2 half = axis * (Reach * 0.8f);
                    Rig2DDebugDraw.Line(sb, toScreen(anchor - half), toScreen(anchor + half), Color.SandyBrown * 0.7f, 1f);
                }
            }
            if (legs.Length > 0 && legs[0].Inited) {
                Rig2DDebugDraw.Circle(sb, toScreen, legs[0].Hip, Reach * envelopeMax, Color.LimeGreen * 0.2f);
            }
        }
    }
}
