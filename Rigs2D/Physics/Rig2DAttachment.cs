using InnoVault.Rigs2D.Runtime;
using InnoVault.Rigs2D.Solvers;
using Microsoft.Xna.Framework;
using System;

namespace InnoVault.Rigs2D.Physics
{
    /// <summary>
    /// 世界物体挂到骨骼上：拾起那一刻先保持物体的世界变换（按当时相对骨骼的位姿钉住，手一动它就跟着动，不会一下跳进手里），
    /// 再在若干帧内平滑混到握法（骨骼局部的握点与握角）；扔出时按最近一帧的实际运动给出线速度与角速度，消费方拿去生成飞行物
    /// <br/>骨骼局部系：原点 = 骨骼近端，x 沿骨、y 沿骨的侧向（镜像骨架时侧向取反，握角按镜像取反，与贴图件的镜像规则一致）
    /// <br/>双手持械用 <see cref="WeaponGripSolver"/> 的世界来源（<c>worldWeight</c>）；这里给的是单骨挂接：一手抓起的石块、嵌在身上的斧、被抓住的角色
    /// <br/>纯本地表现量：挂 / 摘的时机来自消费方已同步的状态
    /// </summary>
    public sealed class Rig2DAttachment
    {
        private Vector2 fromOffset;
        private float fromRotation;
        private float blendFrames;
        private float blendTime;
        private Vector2 lastPosition;
        private float lastRotation;
        private bool hasLast;

        /// <summary>挂在哪副骨架上（摘下后保留，便于再挂）</summary>
        public Rig2DInstance Rig { get; private set; }
        /// <summary>挂在哪根骨上；未挂为 -1</summary>
        public int Bone { get; private set; } = -1;
        /// <summary>是否挂着</summary>
        public bool Attached => Bone >= 0 && Rig != null;
        /// <summary>物体位置（骨架空间，世界骨架即世界）；未挂时由消费方写</summary>
        public Vector2 Position { get; set; }
        /// <summary>物体朝向（弧度）；未挂时由消费方写</summary>
        public float Rotation { get; set; }
        /// <summary>目标握点（骨骼局部，Scale 为 1 的像素）</summary>
        public Vector2 GripOffset { get; set; }
        /// <summary>目标握角（相对骨轴，弧度）</summary>
        public float GripRotation { get; set; }
        /// <summary>最近一帧的线速度（像素 / 帧）：扔出时的初速度</summary>
        public Vector2 Velocity { get; private set; }
        /// <summary>最近一帧的角速度（弧度 / 帧）：扔出时的自转</summary>
        public float AngularVelocity { get; private set; }
        /// <summary>混入握法的进度（0 = 刚抓住时的相对位姿，1 = 握法）</summary>
        public float Blend => blendFrames <= 0f ? 1f : MathHelper.Clamp(blendTime / blendFrames, 0f, 1f);

        /// <summary>
        /// 直接放置（未挂时）：消费方每帧写物体在世界里的位姿，速度按帧差更新
        /// </summary>
        public void SetWorld(Vector2 position, float rotation) {
            Position = position;
            Rotation = rotation;
            Track();
        }

        /// <summary>
        /// 挂上：记下物体此刻相对骨骼的位姿，在 <paramref name="blendFrames"/> 帧内混到 (<paramref name="gripOffset"/>, <paramref name="gripRotation"/>)
        /// </summary>
        public void Attach(Rig2DInstance rig, int bone, Vector2 gripOffset, float gripRotation, int blendFrames = 8) {
            if (rig == null || (uint)bone >= (uint)rig.Bones.Length) {
                return;
            }
            Rig = rig;
            Bone = bone;
            GripOffset = gripOffset;
            GripRotation = gripRotation;
            ToLocal(Position, Rotation, out fromOffset, out fromRotation);
            this.blendFrames = Math.Max(blendFrames, 0);
            blendTime = 0f;
        }

        /// <summary>
        /// 按名挂上
        /// </summary>
        public void Attach(Rig2DInstance rig, string bone, Vector2 gripOffset, float gripRotation, int blendFrames = 8)
            => Attach(rig, rig?.Bone(bone) ?? -1, gripOffset, gripRotation, blendFrames);

        /// <summary>
        /// 摘下：位姿停在最后一帧，<see cref="Velocity"/> / <see cref="AngularVelocity"/> 留着给飞行物当初速度
        /// </summary>
        public void Detach() => Bone = -1;

        /// <summary>
        /// 每帧在 <c>rig.Step()</c> 之后调用：挂着时按骨骼位姿（混合中的局部位姿）算出世界位姿，并更新速度
        /// </summary>
        public void Update(float dt = 1f) {
            if (Attached) {
                if (dt > 0f) {
                    blendTime += dt;
                }
                float e = Spring2D.SmoothStep01(Blend);
                Vector2 offset = Vector2.Lerp(fromOffset, GripOffset, e);
                float rot = fromRotation + MathHelper.WrapAngle(GripRotation - fromRotation) * e;
                FromLocal(offset, rot, out Vector2 p, out float r);
                Position = p;
                Rotation = r;
            }
            if (dt > 0f) {
                Track(dt);
            }
        }

        private void Track(float dt = 1f) {
            if (hasLast && dt > 0f) {
                Velocity = (Position - lastPosition) / dt;
                AngularVelocity = MathHelper.WrapAngle(Rotation - lastRotation) / dt;
            }
            lastPosition = Position;
            lastRotation = Rotation;
            hasLast = true;
        }

        /// <summary>
        /// 骨骼局部位姿 → 骨架空间
        /// </summary>
        public void FromLocal(Vector2 offset, float localRotation, out Vector2 position, out float rotation) {
            ref Bone2D b = ref Rig.Bones[Bone];
            float sign = Rig.MirrorSign;
            float s = MathF.Max(Rig.Scale, 0.001f);
            Vector2 ex = Rig2DMath.Dir(b.Dir);
            Vector2 ey = new Vector2(-ex.Y, ex.X) * sign;
            position = b.Pos + (ex * offset.X + ey * offset.Y) * s;
            rotation = b.Dir + localRotation * sign;
        }

        /// <summary>
        /// 骨架空间位姿 → 骨骼局部
        /// </summary>
        public void ToLocal(Vector2 position, float rotation, out Vector2 offset, out float localRotation) {
            ref Bone2D b = ref Rig.Bones[Bone];
            float sign = Rig.MirrorSign;
            float s = MathF.Max(Rig.Scale, 0.001f);
            Vector2 ex = Rig2DMath.Dir(b.Dir);
            Vector2 ey = new Vector2(-ex.Y, ex.X) * sign;
            Vector2 d = (position - b.Pos) / s;
            offset = new Vector2(Vector2.Dot(d, ex), Vector2.Dot(d, ey));
            localRotation = MathHelper.WrapAngle(rotation - b.Dir) * sign;
        }
    }
}
