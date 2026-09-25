using InnoVault.Rigs2D.Data;
using InnoVault.Rigs2D.Runtime;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;

namespace InnoVault.Rigs2D.Solvers
{
    /// <summary>
    /// 双手持械：由「近手握点 + 武器角 + 两处握位比 + 伸展比」定下整根武器，给两条手臂 IK 当腕目标源，
    /// 手臂解完后把两只手铺向杆上握点、把武器骨从杆尾铺到杆尖
    /// <br/>骨骼：<c>[武器, 近手, 远手]</c>（手可省）。声明在两条手臂之后；手臂用 <c>targetSolver</c> 指向本求解器，<c>targetIndex</c> 0 近手 / 1 远手
    /// <br/>通道属性：<c>target</c>（近手握点，空间量）、<c>angle</c>（武器角，0 朝前、负为刃朝上；镜像时按侧视约定翻成 π − 角）、
    /// <c>gripNear</c> / <c>gripFar</c>（近 / 远手握在杆上的位置，自杆尾占全长比）、<c>extent</c>（杆长伸展比，横扫的透视缩短）
    /// <br/>参数：<c>length</c> 武器全长（Scale 为 1）、<c>extentMin</c> 0.2 / <c>extentMax</c> 1、<c>gripNear</c> 0.5 / <c>gripFar</c> 0.3（无通道时的缺省）、
    /// <c>wristBack</c> 8 / <c>wristSide</c> 10（腕目标落在握点靠身一侧、略向杆尾）、<c>palmAhead</c> 6 / <c>palmSide</c> 12（手心朝向点）、
    /// <c>armNear</c> / <c>armFar</c>（可选：手臂求解器名，给了就取其 <c>Wrist</c> 作手的近端，否则取前臂尖）
    /// <br/>靠身侧的法向：杆朝右（x ≥ 0）取顺时针法向，朝左取逆时针法向，握点因此总在杆的同一侧
    /// </summary>
    public sealed class WeaponGripSolver : Rig2DSolver, IRig2DTargetSource
    {
        private float length;
        private float extentMin;
        private float extentMax;
        private float wristBack;
        private float wristSide;
        private float palmAhead;
        private float palmSide;
        private string armNearName;
        private string armFarName;
        private Rig2DSolver armNear;
        private Rig2DSolver armFar;

        /// <summary>
        /// 近手握点（世界）；通道 <c>target</c> 每帧覆盖
        /// </summary>
        public Vector2 Grip { get; set; }
        /// <summary>
        /// 武器角（骨架朝向下的局部角：0 朝前）
        /// </summary>
        public float Angle { get; set; }
        /// <summary>
        /// 近手握位比（自杆尾）
        /// </summary>
        public float GripNearRatio { get; set; } = 0.5f;
        /// <summary>
        /// 远手握位比（自杆尾）
        /// </summary>
        public float GripFarRatio { get; set; } = 0.3f;
        /// <summary>
        /// 杆长伸展比（钳到 <c>extentMin</c> ~ <c>extentMax</c>）
        /// </summary>
        public float Extent { get; set; } = 1f;

        /// <summary>
        /// 本帧武器方向（世界单位向量）
        /// </summary>
        public Vector2 Dir { get; private set; } = Vector2.UnitX;
        /// <summary>
        /// 本帧武器长度（世界像素）
        /// </summary>
        public float Length { get; private set; }
        /// <summary>
        /// 杆尾（世界）
        /// </summary>
        public Vector2 Butt { get; private set; }
        /// <summary>
        /// 杆尖（世界）
        /// </summary>
        public Vector2 Tip => Butt + Dir * Length;
        /// <summary>
        /// 近手握点（本帧解）
        /// </summary>
        public Vector2 NearGrip { get; private set; }
        /// <summary>
        /// 远手握点（本帧解）
        /// </summary>
        public Vector2 FarGrip { get; private set; }

        /// <inheritdoc/>
        public override ReadOnlySpan<int> DrivenBones => bones;

        /// <inheritdoc/>
        protected internal override int ChannelProperty(string prop, out bool spatial) {
            spatial = prop is "target" or "grip";
            return prop switch {
                "target" or "grip" => 0,
                "angle" => 1,
                "gripNear" => 2,
                "gripFar" => 3,
                "extent" => 4,
                _ => -1,
            };
        }

        /// <inheritdoc/>
        protected internal override void SetChannel(int property, Vector2 value) {
            switch (property) {
                case 0: Grip = value; break;
                case 1: Angle = value.X; break;
                case 2: GripNearRatio = value.X; break;
                case 3: GripFarRatio = value.X; break;
                case 4: Extent = value.X; break;
            }
        }

        /// <inheritdoc/>
        protected override void Configure(Solver2DDef def) {
            if (bones.Length == 0 || bones[0] < 0) {
                Rig2DPlatform.LogError($"[Rig2D:{Rig?.Name}/{Name}]", "WeaponGrip needs bones [weapon, handNear, handFar]");
            }
            length = def.GetFloat("length", 100f);
            extentMin = def.GetFloat("extentMin", 0.2f);
            extentMax = def.GetFloat("extentMax", 1f);
            wristBack = def.GetFloat("wristBack", 8f);
            wristSide = def.GetFloat("wristSide", 10f);
            palmAhead = def.GetFloat("palmAhead", 6f);
            palmSide = def.GetFloat("palmSide", 12f);
            GripNearRatio = def.GetFloat("gripNear", 0.5f);
            GripFarRatio = def.GetFloat("gripFar", 0.3f);
            armNearName = def.GetString("armNear", null);
            armFarName = def.GetString("armFar", null);
        }

        /// <inheritdoc/>
        protected internal override void PostBind() {
            armNear = string.IsNullOrEmpty(armNearName) ? null : Rig?.Solver(armNearName);
            armFar = string.IsNullOrEmpty(armFarName) ? null : Rig?.Solver(armFarName);
        }

        /// <summary>
        /// 靠身一侧的法向（握点所在的一侧）
        /// </summary>
        public static Vector2 BodySide(Vector2 dir) => dir.X >= 0f ? new Vector2(-dir.Y, dir.X) : new Vector2(dir.Y, -dir.X);

        /// <summary>
        /// 目标源：0 近手腕目标、1 远手腕目标、2 近手握点、3 远手握点、4 杆尾、5 杆尖。
        /// 被询问时先确保本帧通道已推入，再按此刻的上游位姿现算整根武器（手臂在本求解器之前解算时就是这样取到目标的）
        /// </summary>
        public bool TryGetTarget(int index, out Vector2 target) {
            if (Rig == null || bones.Length == 0) {
                target = default;
                return false;
            }
            Rig.PushSolverChannels(this);
            ComputeLine();
            float s = Scale;
            Vector2 side = BodySide(Dir);
            switch (index) {
                case 0:
                    target = NearGrip - side * (wristSide * s) - Dir * (wristBack * s);
                    return true;
                case 1:
                    target = FarGrip - side * (wristSide * s) - Dir * (wristBack * s);
                    return true;
                case 2:
                    target = NearGrip;
                    return true;
                case 3:
                    target = FarGrip;
                    return true;
                case 4:
                    target = Butt;
                    return true;
                case 5:
                    target = Tip;
                    return true;
            }
            target = default;
            return false;
        }

        private void ComputeLine() {
            float s = Scale;
            float ang = Rig.Mirrored ? MathHelper.Pi - Angle : Angle;
            Vector2 dir = Rig2DMath.Dir(ang);
            float len = length * s * MathHelper.Clamp(Extent, extentMin, extentMax);
            Vector2 butt = Grip - dir * (len * GripNearRatio);
            Dir = dir;
            Length = len;
            NearGrip = Grip;
            Butt = butt;
            FarGrip = butt + dir * (len * GripFarRatio);
        }

        /// <inheritdoc/>
        public override void Snap() => Solve();

        /// <inheritdoc/>
        public override void Step(float dt) => Solve();

        private void Solve() {
            if (bones.Length == 0 || bones[0] < 0) {
                return;
            }
            ComputeLine();
            float s = Scale;
            Vector2 side = BodySide(Dir);
            if (bones.Length > 1 && bones[1] >= 0) {
                LayHand(bones[1], armNear, NearGrip + Dir * (palmAhead * s) + side * (palmSide * s));
            }
            if (bones.Length > 2 && bones[2] >= 0) {
                LayHand(bones[2], armFar, FarGrip + Dir * (palmAhead * s) + side * (palmSide * s));
            }
            //武器骨：两点式，长度与方向都取自杆尾 → 杆尖（与 SetBoneWorld 两点式同式）
            ref Bone2D w = ref Bone(bones[0]);
            Vector2 butt = Butt;
            Vector2 d = butt + Dir * Length - butt;
            float len = d.Length();
            w.Pos = butt;
            w.Dir = len > 0.0001f ? (float)Math.Atan2(d.Y, d.X) : w.Dir;
            w.Length = len;
        }

        //手：近端取手臂解出的腕（没给手臂求解器就取前臂尖），朝手心点
        private void LayHand(int bone, Rig2DSolver arm, Vector2 aim) {
            Vector2 wrist = arm is TwoBoneIKSolver ik ? ik.Wrist : Rig.RestPosition(bone);
            ref Bone2D h = ref Bone(bone);
            h.Pos = wrist;
            h.Dir = MathF.Atan2(aim.Y - wrist.Y, aim.X - wrist.X);
            h.Length = Rig.RestLength(bone);
        }

        /// <inheritdoc/>
        public override void DebugDraw(SpriteBatch sb, Func<Vector2, Vector2> toScreen) {
            Rig2DDebugDraw.Line(sb, toScreen(Butt), toScreen(Tip), Color.Gold * 0.6f, 1f);
            Rig2DDebugDraw.Dot(sb, toScreen(NearGrip), 5f, Color.Gold);
            Rig2DDebugDraw.Dot(sb, toScreen(FarGrip), 4f, Color.Goldenrod);
        }
    }
}
