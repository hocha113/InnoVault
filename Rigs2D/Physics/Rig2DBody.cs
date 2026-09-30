using InnoVault.Rigs2D.Runtime;
using InnoVault.Rigs2D.Solvers;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;

namespace InnoVault.Rigs2D.Physics
{
    /// <summary>
    /// 整身刚体的阶段
    /// </summary>
    public enum Rig2DBodyState
    {
        /// <summary>未接管：骨架由动画 / 步态照常驱动</summary>
        Inactive,
        /// <summary>模拟中：被击飞、翻滚、落地、滑行</summary>
        Simulating,
        /// <summary>已躺稳：速度归零、位姿定住，等消费方选起身</summary>
        Settled,
        /// <summary>交还中：根变换从躺姿插回站立锚点（<see cref="Rig2DBody.Recover"/>）</summary>
        Recovering,
    }

    /// <summary>
    /// 静止时的躺姿
    /// </summary>
    public enum Rig2DRestPose
    {
        /// <summary>站着落地（头朝上）</summary>
        Upright,
        /// <summary>仰躺（正面朝上）</summary>
        Supine,
        /// <summary>俯卧（正面朝下）</summary>
        Prone,
        /// <summary>倒立（头朝下，卡在坑里一类）</summary>
        Inverted,
    }

    /// <summary>
    /// 刚体读骨架「角色系」原点的来处
    /// </summary>
    public enum Rig2DBodyFrameSource
    {
        /// <summary>取 <see cref="Rig2DInstance.Anchor"/>（通道驱动根、锚点钉位的骨架）</summary>
        Anchor,
        /// <summary>取 <see cref="Rig2DInstance.RootPosition"/>（消费方直接写根的骨架）</summary>
        Root,
    }

    /// <summary>
    /// 躺姿分类结果
    /// </summary>
    public struct Rig2DRestInfo
    {
        /// <summary>躺姿</summary>
        public Rig2DRestPose Pose;
        /// <summary>头朝哪边（世界，+1 右 / −1 左）</summary>
        public int HeadSide;
        /// <summary>头是否朝着角色自己的面向一侧</summary>
        public bool HeadTowardFacing;
        /// <summary>角色系转角（世界，回绕到 [−π, π]）</summary>
        public float Rotation;
        /// <summary>质心（世界）</summary>
        public Vector2 CenterOfMass;
    }

    /// <summary>
    /// 整身刚体：把骨架的一组胶囊（缺省 <c>body</c>）当成一个刚体，按作用点接冲量（线速度 + 角速度），
    /// 在可插拔的接触场上积分、反弹、摩擦、接触力矩（肩先着地腿会甩过去），直到躺稳，再把根变换交还给动画。
    /// 不做全身布娃娃：四肢由动画的关键姿态 + <see cref="Rig2DReactions"/> 受击弹簧负责，刚体只管整身的位置与转角
    /// <br/>坐标约定：刚体活在<b>世界</b>里；骨架活在自己的空间（世界或画布）。骨架的「角色系」= 原点（<see cref="FrameSource"/>）
    /// + <see cref="Rig2DInstance.FrameRotation"/>，形状每帧从当前姿态按角色系反变换读出（姿态变了质心跟着变，角动量守恒，收身转得更快），
    /// 再按 <see cref="ShapeScale"/> / <see cref="ShapeMirror"/> 换到世界尺度（画布骨架合成时的倍率与翻转）
    /// <br/>每帧：<see cref="Step"/>（读上一帧的姿态、积分、接触）→ 把 <see cref="Origin"/> / <see cref="RigRotation"/> 交给骨架
    /// （世界骨架直接 <see cref="ApplyFrame"/>；画布骨架写 <c>FrameRotation</c>、合成落位锚点取 <see cref="Origin"/>）→ 动画与 <c>rig.Step()</c>
    /// <br/>联机：纯本地确定性模拟（无随机、无全局时钟），同输入同轨迹；由谁发起、何时发起走消费方已同步的状态
    /// </summary>
    public sealed class Rig2DBody
    {
        private struct LocalCapsule
        {
            public Vector2 A;
            public Vector2 B;
            public float Radius;
        }

        private struct Contact
        {
            public Vector2 Point;
            public Vector2 Normal;
            public float Depth;
            public float Bounce;
            public float AccN;
            public float AccT;
            public float AccB;
        }

        private readonly List<Rig2DCapsule> gathered = new(16);
        private LocalCapsule[] shape = [];
        private int shapeCount;
        private Contact[] contacts = new Contact[64];
        private int contactCount;
        private readonly List<Rig2DContact> contactView = new(64);
        private Vector2 com;
        private float inertia;
        private float boundRadius;
        private float minRadius = 1f;
        private float baseRootRotation;
        private int sleepCounter;
        private Data.Rig2DDefinition appliedDefinition;
        private int airCounter;
        private Vector2 recoverFromOrigin;
        private Vector2 recoverToOrigin;
        private float recoverFromRotation;
        private float recoverDelta;
        private float recoverFrames;
        private float recoverTime;
        private bool loggedEmpty;

        /// <summary>
        /// 为一副骨架建刚体
        /// </summary>
        /// <param name="rig">骨架实例</param>
        /// <param name="hitboxGroup">组成刚体的胶囊组名</param>
        public Rig2DBody(Rig2DInstance rig, string hitboxGroup = "body") {
            Rig = rig ?? throw new ArgumentNullException(nameof(rig));
            Group = hitboxGroup;
            rig.Body = this;
            ApplyDefinition();
        }

        /// <summary>
        /// 从定义的 <c>"body"</c> 参数袋读取调参（没有这个块、或块里没写的键都不动当前值）。构造时调用一次，
        /// 骨架热重载换了定义后在下一次 <see cref="Step"/> 自动再调——JSON 里的数优先于代码里构造后写的数
        /// </summary>
        public void ApplyDefinition() {
            appliedDefinition = Rig.Definition;
            Data.Solver2DDef p = appliedDefinition?.BodyParams;
            if (p?.Params == null) {
                return;
            }
            Group = p.GetString("group", Group);
            string frame = p.GetString("frameSource", null);
            if (frame != null) {
                FrameSource = string.Equals(frame, "root", StringComparison.OrdinalIgnoreCase) ? Rig2DBodyFrameSource.Root : Rig2DBodyFrameSource.Anchor;
            }
            Mass = p.GetFloat("mass", Mass);
            InertiaScale = p.GetFloat("inertiaScale", InertiaScale);
            SpinScale = p.GetFloat("spinScale", SpinScale);
            Gravity = p.GetVector2("gravity", Gravity);
            AirDrag = p.GetFloat("airDrag", AirDrag);
            AngularDrag = p.GetFloat("angularDrag", AngularDrag);
            ContactAngularDamping = p.GetFloat("contactAngularDamping", ContactAngularDamping);
            Restitution = p.GetFloat("restitution", Restitution);
            RestitutionThreshold = p.GetFloat("restitutionThreshold", RestitutionThreshold);
            Friction = p.GetFloat("friction", Friction);
            MaxSpeed = p.GetFloat("maxSpeed", MaxSpeed);
            MaxAngularVelocity = p.GetFloat("maxAngularVelocity", MaxAngularVelocity);
            PositionCorrection = p.GetFloat("positionCorrection", PositionCorrection);
            Slop = p.GetFloat("slop", Slop);
            MaxCorrection = p.GetFloat("maxCorrection", MaxCorrection);
            SolverIterations = p.GetInt("solverIterations", SolverIterations);
            MaxSubsteps = p.GetInt("maxSubsteps", MaxSubsteps);
            SubstepTravel = p.GetFloat("substepTravel", SubstepTravel);
            CircleSpacing = p.GetFloat("circleSpacing", CircleSpacing);
            ConserveAngularMomentum = p.GetBool("conserveAngularMomentum", ConserveAngularMomentum);
            SleepSpeed = p.GetFloat("sleepSpeed", SleepSpeed);
            SleepAngularSpeed = p.GetFloat("sleepAngularSpeed", SleepAngularSpeed);
            SleepFrames = p.GetInt("sleepFrames", SleepFrames);
            SettleTimeout = p.GetInt("settleTimeout", SettleTimeout);
        }

        //==================== 配置 ====================

        /// <summary>所属骨架</summary>
        public Rig2DInstance Rig { get; }
        /// <summary>组成刚体的胶囊组名</summary>
        public string Group { get; set; }
        /// <summary>角色系原点的来处（缺省锚点）</summary>
        public Rig2DBodyFrameSource FrameSource { get; set; } = Rig2DBodyFrameSource.Anchor;
        /// <summary>骨架像素 → 世界像素的倍率（画布骨架 = 合成倍率；世界骨架 1）</summary>
        public float ShapeScale { get; set; } = 1f;
        /// <summary>合成时是否水平翻转（画布骨架永远朝右、合成时镜像的写法）</summary>
        public bool ShapeMirror { get; set; }
        /// <summary>质量：冲量 / 质量 = 质心速度变化（缺省 1，冲量即速度）</summary>
        public float Mass { get; set; } = 1f;
        /// <summary>转动惯量倍率：大于 1 更难被踹得打转</summary>
        public float InertiaScale { get; set; } = 1f;
        /// <summary>重力（像素 / 帧²）</summary>
        public Vector2 Gravity { get; set; } = new(0f, 0.4f);
        /// <summary>空气阻尼（每帧损失的速度比例）</summary>
        public float AirDrag { get; set; } = 0.004f;
        /// <summary>角速度阻尼（每帧损失比例）</summary>
        public float AngularDrag { get; set; } = 0.01f;
        /// <summary>
        /// 躺稳阶段的滚动阻尼（每帧）：只在质心落在接触支撑范围之内（真正躺平）时生效，躺下后不来回晃；
        /// 头着地倒立、单点支撑时不生效，重力力矩照常把身子掀倒
        /// </summary>
        public float ContactAngularDamping { get; set; } = 0.08f;
        /// <summary>
        /// 冲量带来的角速度倍率：&lt; 1 踹得更"平"（身子少转、落地多半是背 / 肩先着），&gt; 1 更容易翻跟头。
        /// 只影响 <see cref="ApplyImpulse"/>，接触力矩不受影响。缺省 0.6：真实刚体（1）被踹胸口会在空中翻过头、头先着地倒立再掀倒，
        /// 巨型角色读起来像纸人；0.6 时一记胸口踹是"后仰飞出 → 背肩着地 → 贴地滑 → 仰躺"
        /// </summary>
        public float SpinScale { get; set; } = 0.6f;
        /// <summary>弹性系数（0 = 不弹）</summary>
        public float Restitution { get; set; } = 0.18f;
        /// <summary>法向接近速度低于此值（像素 / 帧）不反弹</summary>
        public float RestitutionThreshold { get; set; } = 2.5f;
        /// <summary>库仑摩擦系数：决定落地后滑多远（滑行距离 ≈ 速度² / (2 × 摩擦 × 重力)）</summary>
        public float Friction { get; set; } = 0.5f;
        /// <summary>质心速度上限（像素 / 帧）</summary>
        public float MaxSpeed { get; set; } = 64f;
        /// <summary>角速度上限（弧度 / 帧）</summary>
        public float MaxAngularVelocity { get; set; } = 0.4f;
        /// <summary>穿深修正比例（每子步）</summary>
        public float PositionCorrection { get; set; } = 0.5f;
        /// <summary>允许的残余穿深（像素），小于它不修正，防躺地抖动</summary>
        public float Slop { get; set; } = 1f;
        /// <summary>每子步最多修正的穿深（像素）：姿态突变把肢体压进地里时分几帧推出来，不一下弹飞</summary>
        public float MaxCorrection { get; set; } = 10f;
        /// <summary>速度迭代次数</summary>
        public int SolverIterations { get; set; } = 6;
        /// <summary>子步上限：按最快表面点的一帧位移自动分子步，免得高速翻滚穿地</summary>
        public int MaxSubsteps { get; set; } = 8;
        /// <summary>每子步允许的表面位移（占最细胶囊半径的比例）</summary>
        public float SubstepTravel { get; set; } = 0.5f;
        /// <summary>胶囊拆成圆的间距（占半径比例）：越小接触越密</summary>
        public float CircleSpacing { get; set; } = 0.9f;
        /// <summary>姿态变化引起转动惯量变化时守恒角动量（收身转得快、舒展转得慢）</summary>
        public bool ConserveAngularMomentum { get; set; } = true;
        /// <summary>判定躺稳的质心速度（像素 / 帧）</summary>
        public float SleepSpeed { get; set; } = 0.25f;
        /// <summary>判定躺稳的角速度（弧度 / 帧）</summary>
        public float SleepAngularSpeed { get; set; } = 0.006f;
        /// <summary>连续满足静止条件多少帧算躺稳</summary>
        public int SleepFrames { get; set; } = 16;
        /// <summary>模拟超过此帧数：着地就强制躺稳，悬空则置 <see cref="TimedOut"/>（0 = 不限）</summary>
        public int SettleTimeout { get; set; } = 600;

        //==================== 状态 ====================

        /// <summary>阶段</summary>
        public Rig2DBodyState State { get; private set; }
        /// <summary>是否接管中（模拟、躺稳或交还）</summary>
        public bool Active => State != Rig2DBodyState.Inactive;
        /// <summary>是否已躺稳</summary>
        public bool Settled => State == Rig2DBodyState.Settled;
        /// <summary>质心（世界）</summary>
        public Vector2 Position { get; set; }
        /// <summary>角色系转角（世界，弧度，不回绕；0 = 直立的作者姿态）</summary>
        public float Rotation { get; set; }
        /// <summary>质心速度（像素 / 帧）</summary>
        public Vector2 Velocity { get; set; }
        /// <summary>角速度（弧度 / 帧，屏幕顺时针为正）</summary>
        public float AngularVelocity { get; set; }
        /// <summary>写给骨架 <see cref="Rig2DInstance.FrameRotation"/> 的转角（合成翻转时取反）</summary>
        public float RigRotation => ShapeMirror ? -Rotation : Rotation;
        /// <summary>角色系原点（世界）：世界骨架的锚点 / 根，画布骨架的合成落位锚点</summary>
        public Vector2 Origin => Position - Rotate(com, Rotation);
        /// <summary>质心在角色系里的位置（世界尺度、未转）</summary>
        public Vector2 CenterOfMassLocal => com;
        /// <summary>转动惯量（质量 × 像素²）</summary>
        public float Inertia => inertia;
        /// <summary>包围半径（质心到最远表面，世界像素）</summary>
        public float BoundingRadius => boundRadius;
        /// <summary>角色在世界里的面向（+1 右 / −1 左）</summary>
        public int Facing => (Rig.Mirrored ? -1 : 1) * (ShapeMirror ? -1 : 1);
        /// <summary>接管以来的帧数</summary>
        public int ActiveFrames { get; private set; }
        /// <summary>本帧是否有接触</summary>
        public bool Grounded { get; private set; }
        /// <summary>本帧是悬空若干帧后的第一次着地（最重的一拍：顿帧、震地、镜头弹簧）</summary>
        public bool JustLanded { get; private set; }
        /// <summary>本帧新接触里最大的法向接近速度（像素 / 帧）：反弹扬尘、音量</summary>
        public float ImpactSpeed { get; private set; }
        /// <summary>本帧接触的平均点（世界，按穿深加权）</summary>
        public Vector2 ContactPoint { get; private set; }
        /// <summary>本帧接触的平均法线</summary>
        public Vector2 ContactNormal { get; private set; } = -Vector2.UnitY;
        /// <summary>着地时质心的切向速度（像素 / 帧）：犁地痕、刹停的时机</summary>
        public float SlideSpeed { get; private set; }
        /// <summary>是否贴地滑行（切向速度大于 1）</summary>
        public bool Sliding => Grounded && SlideSpeed > 1f;
        /// <summary>超时仍悬空（掉进深坑一类），消费方决定怎么收场</summary>
        public bool TimedOut { get; private set; }
        /// <summary>交还进度（0 → 1）</summary>
        public float RecoverProgress => recoverFrames <= 0f ? 1f : MathHelper.Clamp(recoverTime / recoverFrames, 0f, 1f);
        /// <summary>本帧最后一个子步的接触（世界；法线从地面指向刚体）</summary>
        public IReadOnlyList<Rig2DContact> Contacts => contactView;
        /// <summary>刚体形状（世界胶囊，当前位姿）</summary>
        public int CapsuleCount => shapeCount;

        //==================== 坐标 ====================

        private static Vector2 Rotate(Vector2 v, float angle) {
            if (angle == 0f) {
                return v;
            }
            float c = MathF.Cos(angle), s = MathF.Sin(angle);
            return new Vector2(c * v.X - s * v.Y, s * v.X + c * v.Y);
        }

        private static float Cross(Vector2 a, Vector2 b) => a.X * b.Y - a.Y * b.X;

        //ω × r（二维）
        private static Vector2 CrossW(float w, Vector2 r) => new(-w * r.Y, w * r.X);

        /// <summary>
        /// 角色系点（世界尺度、未转，原点 = 角色系原点）→ 世界
        /// </summary>
        public Vector2 LocalToWorld(Vector2 local) => Position + Rotate(local - com, Rotation);

        /// <summary>
        /// 世界 → 角色系点
        /// </summary>
        public Vector2 WorldToLocal(Vector2 world) => Rotate(world - Position, -Rotation) + com;

        /// <summary>
        /// 第 i 个胶囊的世界形状
        /// </summary>
        public Rig2DCapsule WorldCapsule(int i) {
            LocalCapsule c = shape[i];
            return new Rig2DCapsule(LocalToWorld(c.A), LocalToWorld(c.B), c.Radius, i);
        }

        /// <summary>
        /// 世界点上的速度（线速度 + 角速度 × 力臂）
        /// </summary>
        public Vector2 PointVelocity(Vector2 world) => Velocity + CrossW(AngularVelocity, world - Position);

        private Vector2 FrameOriginRig => FrameSource == Rig2DBodyFrameSource.Root ? Rig.RootPosition : Rig.Anchor;

        private bool WorldSpaceRig => ShapeScale == 1f && !ShapeMirror;

        //==================== 形状 ====================

        //从骨架当前姿态读形状：骨架空间 → 角色系（反转 FrameRotation）→ 镜像 / 倍率 → 世界尺度；并算质心、转动惯量
        private bool RefreshShape() {
            gathered.Clear();
            Rig2DHit.GatherGroup(Rig, Group, gathered);
            int n = gathered.Count;
            if (n == 0) {
                if (!loggedEmpty) {
                    loggedEmpty = true;
                    Rig2DPlatform.LogError($"[Rig2DBody:{Rig.Name}]", $"hitbox group '{Group}' is empty or missing; the body cannot simulate");
                }
                shapeCount = 0;
                return false;
            }
            if (shape.Length < n) {
                shape = new LocalCapsule[n];
            }
            Vector2 o = FrameOriginRig;
            float back = -Rig.FrameRotation;
            float s = MathF.Max(ShapeScale, 0.0001f);
            float mirror = ShapeMirror ? -1f : 1f;
            float area = 0f;
            Vector2 moment = Vector2.Zero;
            minRadius = float.MaxValue;
            for (int i = 0; i < n; i++) {
                Rig2DCapsule c = gathered[i];
                Vector2 a = Rotate(c.A - o, back) * s;
                Vector2 b = Rotate(c.B - o, back) * s;
                a.X *= mirror;
                b.X *= mirror;
                float r = MathF.Max(c.Radius * s, 0.5f);
                shape[i] = new LocalCapsule { A = a, B = b, Radius = r };
                float len = Vector2.Distance(a, b);
                float ai = 2f * r * len + MathHelper.Pi * r * r;
                area += ai;
                moment += (a + b) * 0.5f * ai;
                minRadius = MathF.Min(minRadius, r);
            }
            shapeCount = n;
            Vector2 newCom = moment / MathF.Max(area, 1e-4f);
            float iSum = 0f;
            float bound = 0f;
            for (int i = 0; i < n; i++) {
                LocalCapsule c = shape[i];
                float len = Vector2.Distance(c.A, c.B);
                float r = c.Radius;
                float rect = 2f * r * len;
                float circ = MathHelper.Pi * r * r;
                float own = rect * (len * len + 4f * r * r) / 12f + circ * (r * r * 0.5f + len * len * 0.25f);
                Vector2 mid = (c.A + c.B) * 0.5f;
                iSum += own + (rect + circ) * Vector2.DistanceSquared(mid, newCom);
                bound = MathF.Max(bound, MathF.Max(Vector2.Distance(c.A, newCom), Vector2.Distance(c.B, newCom)) + r);
            }
            float density = MathF.Max(Mass, 1e-4f) / MathF.Max(area, 1e-4f);
            float newInertia = MathF.Max(iSum * density * MathF.Max(InertiaScale, 1e-4f), 1e-3f);
            if (State == Rig2DBodyState.Simulating) {
                //质心在角色系里挪了：世界质心不动（外力才动它），角色系原点反向让位；惯量变了按角动量守恒改角速度
                if (ConserveAngularMomentum && inertia > 0f) {
                    AngularVelocity *= MathHelper.Clamp(inertia / newInertia, 0.5f, 2f);
                }
            }
            com = newCom;
            inertia = newInertia;
            boundRadius = bound;
            return true;
        }

        //==================== 接管 / 冲量 ====================

        /// <summary>
        /// 从骨架当前姿态开始接管（世界骨架：角色系原点就在骨架空间里）
        /// </summary>
        public bool Activate(Vector2 velocity = default, float angularVelocity = 0f) {
            if (!WorldSpaceRig) {
                Rig2DPlatform.LogError($"[Rig2DBody:{Rig.Name}:origin]", "Activate without an origin needs a world-space rig (ShapeScale 1, no ShapeMirror); pass the composited world origin");
            }
            return Activate(FrameOriginRig, velocity, angularVelocity);
        }

        /// <summary>
        /// 从骨架当前姿态开始接管
        /// </summary>
        /// <param name="originWorld">角色系原点此刻在世界里的位置（世界骨架 = 锚点 / 根；画布骨架 = 合成落位锚点）</param>
        /// <param name="velocity">初始质心速度（角色原本在走 / 跑）</param>
        /// <param name="angularVelocity">初始角速度</param>
        public bool Activate(Vector2 originWorld, Vector2 velocity, float angularVelocity = 0f) {
            float theta = Rig.FrameRotation;
            float phi = ShapeMirror ? -theta : theta;
            State = Rig2DBodyState.Inactive;
            if (!RefreshShape()) {
                return false;
            }
            baseRootRotation = Rig.RootRotation - Rig.FrameRotation;
            Rotation = phi;
            Position = originWorld + Rotate(com, phi);
            Velocity = velocity;
            AngularVelocity = angularVelocity;
            State = Rig2DBodyState.Simulating;
            ActiveFrames = 0;
            sleepCounter = 0;
            airCounter = 0;
            TimedOut = false;
            Grounded = false;
            JustLanded = false;
            contactCount = 0;
            contactView.Clear();
            return true;
        }

        /// <summary>
        /// 在世界点 <paramref name="pointWorld"/> 施加冲量（质量 × 像素 / 帧）：质心速度 += 冲量 / 质量，角速度 += 力臂 × 冲量 / 惯量。
        /// 踹在胸口、高于质心 → 向后上飞、后仰翻转、头先走。躺稳后再挨一下会重新醒来
        /// </summary>
        public void ApplyImpulse(Vector2 pointWorld, Vector2 impulse) {
            if (State == Rig2DBodyState.Inactive || State == Rig2DBodyState.Recovering) {
                return;
            }
            if (State == Rig2DBodyState.Settled) {
                State = Rig2DBodyState.Simulating;
                sleepCounter = 0;
            }
            float invM = 1f / MathF.Max(Mass, 1e-4f);
            Velocity += impulse * invM;
            AngularVelocity += Cross(pointWorld - Position, impulse) / inertia * SpinScale;
            ClampVelocity();
        }

        /// <summary>
        /// 纯角冲量（质量 × 像素² / 帧）：编排上想多转半圈、少转半圈时补
        /// </summary>
        public void ApplyAngularImpulse(float angularImpulse) {
            if (State == Rig2DBodyState.Simulating || State == Rig2DBodyState.Settled) {
                State = Rig2DBodyState.Simulating;
                AngularVelocity += angularImpulse / inertia;
                ClampVelocity();
            }
        }

        /// <summary>
        /// 世界骨架的一记击飞：未接管就先从当前姿态接管，再在作用点施加冲量
        /// </summary>
        public bool Launch(Vector2 pointWorld, Vector2 impulse) {
            if (State == Rig2DBodyState.Inactive || State == Rig2DBodyState.Recovering) {
                if (!Activate()) {
                    return false;
                }
            }
            ApplyImpulse(pointWorld, impulse);
            return true;
        }

        /// <summary>
        /// 任意骨架的一记击飞：未接管就以 <paramref name="originWorld"/> 为角色系原点接管，再施加冲量
        /// </summary>
        public bool Launch(Vector2 originWorld, Vector2 pointWorld, Vector2 impulse) {
            if (State == Rig2DBodyState.Inactive || State == Rig2DBodyState.Recovering) {
                if (!Activate(originWorld, Vector2.Zero)) {
                    return false;
                }
            }
            ApplyImpulse(pointWorld, impulse);
            return true;
        }

        /// <summary>
        /// 立刻交还（不插值）：状态归 <see cref="Rig2DBodyState.Inactive"/>。消费方随后自己把 <c>FrameRotation</c> 清零
        /// </summary>
        public void Deactivate() {
            State = Rig2DBodyState.Inactive;
            Velocity = Vector2.Zero;
            AngularVelocity = 0f;
            contactCount = 0;
            contactView.Clear();
            Grounded = false;
            JustLanded = false;
        }

        /// <summary>
        /// 躺稳（或任何时刻）后开始交还：<paramref name="frames"/> 帧内角色系原点平滑插到 <paramref name="targetOriginWorld"/>（站立锚点），
        /// 转角走最短弧插到 <paramref name="targetRotation"/>（缺省 0 = 直立）。与动画机同帧数的 <c>CrossFade</c> 配合：
        /// 起身招式的首帧是作者姿态里的躺姿，根变换和姿态一起从物理躺姿过渡过去
        /// </summary>
        public void Recover(Vector2 targetOriginWorld, int frames, float targetRotation = 0f) {
            if (State == Rig2DBodyState.Inactive) {
                return;
            }
            recoverFromOrigin = Origin;
            recoverToOrigin = targetOriginWorld;
            recoverFromRotation = Rotation;
            recoverDelta = MathHelper.WrapAngle(targetRotation - Rotation);
            recoverFrames = Math.Max(frames, 0);
            recoverTime = 0f;
            Velocity = Vector2.Zero;
            AngularVelocity = 0f;
            State = Rig2DBodyState.Recovering;
            if (recoverFrames <= 0f) {
                FinishRecover();
            }
        }

        private void FinishRecover() {
            Rotation = recoverFromRotation + recoverDelta;
            Position = recoverToOrigin + Rotate(com, Rotation);
            State = Rig2DBodyState.Inactive;
            contactCount = 0;
            contactView.Clear();
        }

        /// <summary>
        /// 把本帧结果写给世界骨架：<see cref="Rig2DInstance.FrameRotation"/> 总会写；
        /// 世界骨架（<see cref="ShapeScale"/> 1、不翻转）另写原点——锚点模式写 <see cref="Rig2DInstance.Anchor"/>，
        /// 根模式写根位置并把根朝向设为「接管时的作者根朝向 + 转角」。画布骨架只写转角，原点由消费方交给合成落位
        /// </summary>
        public void ApplyFrame() {
            Rig.FrameRotation = RigRotation;
            if (!WorldSpaceRig) {
                return;
            }
            if (FrameSource == Rig2DBodyFrameSource.Root) {
                Rig.SetRoot(Origin, baseRootRotation + RigRotation);
            }
            else {
                Rig.Anchor = Origin;
            }
        }

        //==================== 推进 ====================

        /// <summary>
        /// 推进一帧：读骨架上一帧的姿态为形状，重力 → 接触 → 速度求解（反弹、摩擦、接触力矩）→ 积分，自动分子步；
        /// 之后更新着地 / 滑行 / 躺稳。<paramref name="dt"/> 为 0 = 顿帧（原样定住，冲量照攒）
        /// </summary>
        /// <param name="field">接触场（<see cref="Rig2DContactFields"/>）；空 = 无地</param>
        /// <param name="dt">帧步长（1 = 一帧）</param>
        public void Step(Rig2DContactField field, float dt = 1f) {
            if (!ReferenceEquals(appliedDefinition, Rig.Definition)) {
                ApplyDefinition();
            }
            JustLanded = false;
            ImpactSpeed = 0f;
            if (State == Rig2DBodyState.Recovering) {
                if (dt > 0f) {
                    recoverTime += dt;
                }
                float e = Spring2D.SmoothStep01(RecoverProgress);
                RefreshShape();
                Rotation = recoverFromRotation + recoverDelta * e;
                Position = Vector2.Lerp(recoverFromOrigin, recoverToOrigin, e) + Rotate(com, Rotation);
                if (recoverTime >= recoverFrames) {
                    FinishRecover();
                }
                return;
            }
            if (State != Rig2DBodyState.Simulating || dt <= 0f) {
                return;
            }
            //形状跟着上一帧的姿态走（动画在空中换关键姿态）；质心世界位置不动
            if (!RefreshShape()) {
                return;
            }
            field ??= Rig2DContactFields.None;
            ActiveFrames++;

            float surfaceSpeed = Velocity.Length() + MathF.Abs(AngularVelocity) * boundRadius;
            int sub = (int)MathF.Ceiling(surfaceSpeed * dt / MathF.Max(SubstepTravel * minRadius, 0.5f));
            sub = Math.Clamp(sub, 1, Math.Max(MaxSubsteps, 1));
            float h = dt / sub;
            bool touched = false;
            float impact = 0f;
            for (int s = 0; s < sub; s++) {
                Velocity += Gravity * h;
                Velocity *= MathF.Pow(1f - MathHelper.Clamp(AirDrag, 0f, 0.99f), h);
                AngularVelocity *= MathF.Pow(1f - MathHelper.Clamp(AngularDrag, 0f, 0.99f), h);
                BuildContacts(field);
                if (contactCount > 0) {
                    touched = true;
                    for (int c = 0; c < contactCount; c++) {
                        impact = MathF.Max(impact, -ApproachSpeed(c));
                    }
                    if (SupportedOverContacts()) {
                        AngularVelocity *= MathF.Pow(1f - MathHelper.Clamp(ContactAngularDamping, 0f, 0.99f), h);
                    }
                }
                Solve(h, out Vector2 vBias, out float wBias);
                ClampVelocity();
                Position += (Velocity + vBias) * h;
                Rotation += (AngularVelocity + wBias) * h;
            }
            PublishContacts();
            Grounded = touched;
            if (touched) {
                if (airCounter >= 3) {
                    JustLanded = true;
                }
                airCounter = 0;
                ImpactSpeed = MathF.Max(impact, 0f);
                Vector2 n = ContactNormal;
                Vector2 t = new(-n.Y, n.X);
                SlideSpeed = MathF.Abs(Vector2.Dot(Velocity, t));
            }
            else {
                airCounter++;
                SlideSpeed = 0f;
            }

            //躺稳：着地且线 / 角速度都小，连续若干帧
            if (touched && Velocity.Length() < SleepSpeed && MathF.Abs(AngularVelocity) < SleepAngularSpeed) {
                sleepCounter++;
            }
            else {
                sleepCounter = 0;
            }
            if (sleepCounter >= SleepFrames) {
                Settle();
            }
            else if (SettleTimeout > 0 && ActiveFrames >= SettleTimeout) {
                if (touched) {
                    Settle();
                }
                else {
                    TimedOut = true;
                }
            }
        }

        /// <summary>
        /// 立刻躺稳（速度归零、转角回绕），并分类躺姿
        /// </summary>
        public void Settle() {
            if (State != Rig2DBodyState.Simulating) {
                return;
            }
            Velocity = Vector2.Zero;
            AngularVelocity = 0f;
            Rotation = MathHelper.WrapAngle(Rotation);
            State = Rig2DBodyState.Settled;
            sleepCounter = 0;
        }

        //躺平判定：质心沿地面切向的投影落在接触点展开的支撑段里，且支撑段比质心离地的高度还宽（倾覆角 &gt; 45°）。
        //头着地倒立时质心也在头的接触面正上方，但高而窄，不算——重力力矩照常把身子掀倒
        private bool SupportedOverContacts() {
            Vector2 n = Vector2.Zero;
            Vector2 centroid = Vector2.Zero;
            for (int c = 0; c < contactCount; c++) {
                n += contacts[c].Normal;
                centroid += contacts[c].Point;
            }
            if (n.LengthSquared() < 1e-6f) {
                return false;
            }
            n.Normalize();
            centroid /= contactCount;
            Vector2 t = new(-n.Y, n.X);
            float lo = float.MaxValue, hi = float.MinValue;
            for (int c = 0; c < contactCount; c++) {
                float u = Vector2.Dot(contacts[c].Point, t);
                lo = MathF.Min(lo, u);
                hi = MathF.Max(hi, u);
            }
            float com = Vector2.Dot(Position, t);
            float height = Vector2.Dot(Position - centroid, n);
            float span = hi - lo;
            return span > minRadius && span > height && com > lo && com < hi;
        }

        private float ApproachSpeed(int c) {
            ref Contact k = ref contacts[c];
            Vector2 r = k.Point - Position;
            return Vector2.Dot(Velocity + CrossW(AngularVelocity, r), k.Normal);
        }

        private void ClampVelocity() {
            float max = MathF.Max(MaxSpeed, 0f);
            if (max > 0f && Velocity.LengthSquared() > max * max) {
                Velocity = Vector2.Normalize(Velocity) * max;
            }
            float wMax = MathF.Max(MaxAngularVelocity, 0f);
            if (wMax > 0f) {
                AngularVelocity = MathHelper.Clamp(AngularVelocity, -wMax, wMax);
            }
        }

        //把每个胶囊拆成一串圆问接触场；每处接触记下反弹目标（求解前的接近速度决定）
        private void BuildContacts(Rig2DContactField field) {
            contactCount = 0;
            float spacing = MathF.Max(CircleSpacing, 0.2f);
            for (int i = 0; i < shapeCount; i++) {
                LocalCapsule lc = shape[i];
                Vector2 a = LocalToWorld(lc.A);
                Vector2 b = LocalToWorld(lc.B);
                float r = lc.Radius;
                float len = Vector2.Distance(a, b);
                int samples = len < 0.5f ? 1 : (int)MathF.Ceiling(len / (r * spacing)) + 1;
                for (int k = 0; k < samples; k++) {
                    Vector2 p = samples == 1 ? a : Vector2.Lerp(a, b, k / (float)(samples - 1));
                    if (!field(p, r, out Rig2DContact c) || c.Depth <= 0f) {
                        continue;
                    }
                    if (contactCount == contacts.Length) {
                        Array.Resize(ref contacts, contacts.Length * 2);
                    }
                    //作用点：圆面与地面之间的中点
                    Vector2 point = p - c.Normal * (r - c.Depth * 0.5f);
                    Vector2 rr = point - Position;
                    float vn = Vector2.Dot(Velocity + CrossW(AngularVelocity, rr), c.Normal);
                    contacts[contactCount++] = new Contact {
                        Point = point,
                        Normal = c.Normal,
                        Depth = c.Depth,
                        Bounce = vn < -RestitutionThreshold ? -vn * Restitution : 0f,
                    };
                }
            }
        }

        //顺序冲量：法向（不穿、反弹）+ 库仑摩擦；穿深走伪速度（只改位置不改动量）
        private void Solve(float h, out Vector2 vBias, out float wBias) {
            vBias = Vector2.Zero;
            wBias = 0f;
            if (contactCount == 0) {
                return;
            }
            float invM = 1f / MathF.Max(Mass, 1e-4f);
            float invI = 1f / inertia;
            float mu = MathF.Max(Friction, 0f);
            float beta = MathHelper.Clamp(PositionCorrection, 0f, 1f);
            float maxFix = MathF.Max(MaxCorrection, 0f);
            int iterations = Math.Max(SolverIterations, 1);
            for (int it = 0; it < iterations; it++) {
                for (int c = 0; c < contactCount; c++) {
                    ref Contact k = ref contacts[c];
                    Vector2 n = k.Normal;
                    Vector2 r = k.Point - Position;
                    float rn = Cross(r, n);
                    float kN = invM + rn * rn * invI;

                    //法向
                    float vn = Vector2.Dot(Velocity + CrossW(AngularVelocity, r), n);
                    float lambda = -(vn - k.Bounce) / kN;
                    float acc = MathF.Max(k.AccN + lambda, 0f);
                    lambda = acc - k.AccN;
                    k.AccN = acc;
                    Velocity += n * (lambda * invM);
                    AngularVelocity += rn * lambda * invI;

                    //摩擦
                    Vector2 t = new(-n.Y, n.X);
                    float rt = Cross(r, t);
                    float kT = invM + rt * rt * invI;
                    float vt = Vector2.Dot(Velocity + CrossW(AngularVelocity, r), t);
                    float lambdaT = -vt / kT;
                    float limit = mu * k.AccN;
                    float accT = MathHelper.Clamp(k.AccT + lambdaT, -limit, limit);
                    lambdaT = accT - k.AccT;
                    k.AccT = accT;
                    Velocity += t * (lambdaT * invM);
                    AngularVelocity += rt * lambdaT * invI;

                    //穿深修正（伪速度）
                    float fix = MathF.Min(MathF.Max(k.Depth - Slop, 0f), maxFix);
                    if (fix > 0f) {
                        float target = beta * fix / h;
                        float vb = Vector2.Dot(vBias + CrossW(wBias, r), n);
                        float lb = (target - vb) / kN;
                        float accB = MathF.Max(k.AccB + lb, 0f);
                        lb = accB - k.AccB;
                        k.AccB = accB;
                        vBias += n * (lb * invM);
                        wBias += rn * lb * invI;
                    }
                }
            }
        }

        private void PublishContacts() {
            contactView.Clear();
            if (contactCount == 0) {
                return;
            }
            Vector2 sumP = Vector2.Zero;
            Vector2 sumN = Vector2.Zero;
            float sumW = 0f;
            for (int c = 0; c < contactCount; c++) {
                ref Contact k = ref contacts[c];
                contactView.Add(new Rig2DContact(k.Point, k.Normal, k.Depth));
                float w = MathF.Max(k.Depth, 0.01f);
                sumP += k.Point * w;
                sumN += k.Normal * w;
                sumW += w;
            }
            ContactPoint = sumP / sumW;
            ContactNormal = sumN.LengthSquared() > 1e-6f ? Vector2.Normalize(sumN) : -Vector2.UnitY;
        }

        //==================== 躺姿 ====================

        /// <summary>
        /// 按当前转角分类躺姿：角色的「上」（头向）朝上 = 站着、朝下 = 倒立，否则躺着——正面朝上仰、朝下俯，头朝哪边另报。
        /// 起身招式按它选（仰 × 头朝面向 → 鲤鱼打挺，俯 → 拄槊起身……），<see cref="Rig2DRestInfo.Rotation"/> 用来对准起身招式首帧的作者躺姿
        /// </summary>
        public Rig2DRestInfo ClassifyRest() {
            float rot = MathHelper.WrapAngle(Rotation);
            Vector2 up = Rotate(-Vector2.UnitY, rot);
            int facing = Facing;
            Vector2 front = Rotate(new Vector2(facing, 0f), rot);
            Rig2DRestInfo info = new() {
                Rotation = rot,
                CenterOfMass = Position,
                HeadSide = up.X >= 0f ? 1 : -1,
            };
            if (up.Y <= -0.7071f) {
                info.Pose = Rig2DRestPose.Upright;
            }
            else if (up.Y >= 0.7071f) {
                info.Pose = Rig2DRestPose.Inverted;
            }
            else {
                info.Pose = front.Y < 0f ? Rig2DRestPose.Supine : Rig2DRestPose.Prone;
            }
            info.HeadTowardFacing = info.HeadSide == facing;
            return info;
        }

        //==================== 调试 ====================

        /// <summary>
        /// 调试叠层：世界胶囊、质心、速度、接触点与法线
        /// </summary>
        public void DebugDraw(SpriteBatch sb, Func<Vector2, Vector2> toScreen) {
            if (State == Rig2DBodyState.Inactive || shapeCount == 0) {
                return;
            }
            Color body = State switch {
                Rig2DBodyState.Settled => Color.LightGreen,
                Rig2DBodyState.Recovering => Color.Plum,
                _ => Color.Orange,
            };
            for (int i = 0; i < shapeCount; i++) {
                Rig2DCapsule c = WorldCapsule(i);
                Rig2DDebugDraw.Capsule(sb, toScreen, c.A, c.B, c.Radius, body * 0.7f);
            }
            Vector2 p = toScreen(Position);
            Rig2DDebugDraw.Dot(sb, p, 7f, Color.White);
            Rig2DDebugDraw.Line(sb, p, toScreen(Position + Velocity * 6f), Color.Yellow, 2f);
            for (int i = 0; i < contactView.Count; i++) {
                Rig2DContact c = contactView[i];
                Vector2 q = toScreen(c.Point);
                Rig2DDebugDraw.Dot(sb, q, 4f, Color.Red);
                Rig2DDebugDraw.Line(sb, q, toScreen(c.Point + c.Normal * 18f), Color.Red, 1f);
            }
            if (State == Rig2DBodyState.Settled) {
                Rig2DRestInfo rest = ClassifyRest();
                Rig2DDebugDraw.Text(sb, $"{rest.Pose} head {(rest.HeadSide > 0 ? "R" : "L")}", p + new Vector2(10f, -10f), Color.White);
            }
        }
    }
}
