using Microsoft.Xna.Framework;
using System;
using Terraria;

namespace InnoVault.Rigs2D.Runtime
{
    /// <summary>
    /// 从骨骼位姿出判定的几何助手：把一根骨看成"线段 + 半径"的胶囊，给逐节碰撞 / 触碰 / 拾取用
    /// <br/>只读骨骼，不碰实体；判定结果是否作用于 gameplay 由消费方决定（联机下两端骨骼由同输入算出，判定一致）
    /// </summary>
    public static class Rig2DHit
    {
        /// <summary>
        /// 骨骼胶囊的包围矩形（整数像素，向外取整）
        /// </summary>
        /// <param name="bone">骨骼</param>
        /// <param name="radius">胶囊半径（像素）</param>
        public static Rectangle BoneRect(in Bone2D bone, float radius) {
            Vector2 a = bone.Pos;
            Vector2 b = bone.Tip;
            float minX = Math.Min(a.X, b.X) - radius;
            float minY = Math.Min(a.Y, b.Y) - radius;
            float maxX = Math.Max(a.X, b.X) + radius;
            float maxY = Math.Max(a.Y, b.Y) + radius;
            int x = (int)Math.Floor(minX);
            int y = (int)Math.Floor(minY);
            return new Rectangle(x, y, (int)Math.Ceiling(maxX) - x, (int)Math.Ceiling(maxY) - y);
        }

        /// <summary>
        /// 点到骨骼线段的最近距离
        /// </summary>
        public static float DistanceToBone(in Bone2D bone, Vector2 point) {
            Vector2 a = bone.Pos;
            Vector2 ab = bone.Tip - a;
            float len2 = ab.LengthSquared();
            if (len2 < 0.0001f) {
                return Vector2.Distance(a, point);
            }
            float t = MathHelper.Clamp(Vector2.Dot(point - a, ab) / len2, 0f, 1f);
            return Vector2.Distance(a + ab * t, point);
        }

        /// <summary>
        /// 骨骼线段上离 <paramref name="point"/> 最近的点
        /// </summary>
        public static Vector2 ClosestPoint(in Bone2D bone, Vector2 point) {
            Vector2 a = bone.Pos;
            Vector2 ab = bone.Tip - a;
            float len2 = ab.LengthSquared();
            if (len2 < 0.0001f) {
                return a;
            }
            float t = MathHelper.Clamp(Vector2.Dot(point - a, ab) / len2, 0f, 1f);
            return a + ab * t;
        }

        /// <summary>
        /// 骨骼胶囊是否与矩形相交（实体 hitbox 一类）
        /// </summary>
        /// <param name="bone">骨骼</param>
        /// <param name="radius">胶囊半径（像素）</param>
        /// <param name="rect">世界矩形</param>
        public static bool BoneCapsuleIntersects(in Bone2D bone, float radius, Rectangle rect) {
            if (bone.Length < 0.5f) {
                return CircleIntersects(bone.Pos, radius, rect);
            }
            float point = 0f;
            return Collision.CheckAABBvLineCollision(new Vector2(rect.X, rect.Y), new Vector2(rect.Width, rect.Height),
                bone.Pos, bone.Tip, Math.Max(radius * 2f, 1f), ref point);
        }

        /// <summary>
        /// 骨骼胶囊是否包含某点
        /// </summary>
        public static bool BoneCapsuleContains(in Bone2D bone, float radius, Vector2 point)
            => DistanceToBone(in bone, point) <= radius;

        /// <summary>
        /// 一组骨骼里第一根与矩形相交的胶囊（按给定顺序），命中返回其骨骼索引
        /// </summary>
        /// <param name="rig">实例</param>
        /// <param name="bones">要测的骨骼索引</param>
        /// <param name="radius">胶囊半径（Scale 为 1 的量，内部乘实例 Scale）</param>
        /// <param name="rect">世界矩形</param>
        /// <param name="hitBone">命中的骨骼索引，未命中为 -1</param>
        public static bool AnyBoneIntersects(Rig2DInstance rig, ReadOnlySpan<int> bones, float radius, Rectangle rect, out int hitBone) {
            hitBone = -1;
            if (rig == null) {
                return false;
            }
            float r = radius * Math.Max(rig.Scale, 0.001f);
            for (int k = 0; k < bones.Length; k++) {
                int b = bones[k];
                if (b < 0 || b >= rig.Bones.Length) {
                    continue;
                }
                if (BoneCapsuleIntersects(in rig.Bones[b], r, rect)) {
                    hitBone = b;
                    return true;
                }
            }
            return false;
        }

        private static bool CircleIntersects(Vector2 center, float radius, Rectangle rect) {
            float cx = MathHelper.Clamp(center.X, rect.Left, rect.Right);
            float cy = MathHelper.Clamp(center.Y, rect.Top, rect.Bottom);
            float dx = center.X - cx;
            float dy = center.Y - cy;
            return dx * dx + dy * dy <= radius * radius;
        }
    }
}
