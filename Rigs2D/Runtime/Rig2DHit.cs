using Microsoft.Xna.Framework;
using System;

namespace InnoVault.Rigs2D.Runtime
{
    /// <summary>
    /// 从骨骼位姿出判定的几何助手：把一根骨看成"线段 + 半径"的胶囊，给逐节碰撞 / 触碰 / 拾取用
    /// <br/>只读骨骼，不碰实体；判定结果是否作用于 gameplay 由消费方决定（联机下两端骨骼由同输入算出，判定一致）
    /// <br/>胶囊对矩形的相交（<c>BoneCapsuleIntersects</c> / <c>AnyBoneIntersects</c>）沿用原版线段判定，在游戏宿主分部里
    /// </summary>
    public static partial class Rig2DHit
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
        /// 骨骼胶囊是否包含某点
        /// </summary>
        public static bool BoneCapsuleContains(in Bone2D bone, float radius, Vector2 point)
            => DistanceToBone(in bone, point) <= radius;

        private static bool CircleIntersects(Vector2 center, float radius, Rectangle rect) {
            float cx = MathHelper.Clamp(center.X, rect.Left, rect.Right);
            float cy = MathHelper.Clamp(center.Y, rect.Top, rect.Bottom);
            float dx = center.X - cx;
            float dy = center.Y - cy;
            return dx * dx + dy * dy <= radius * radius;
        }

        //==================== 可移植的胶囊几何（不依赖宿主） ====================

        /// <summary>
        /// 点到线段的最近距离
        /// </summary>
        public static float PointSegmentDistance(Vector2 p, Vector2 a, Vector2 b) {
            Vector2 ab = b - a;
            float len2 = ab.LengthSquared();
            if (len2 < 0.0001f) {
                return Vector2.Distance(a, p);
            }
            float t = MathHelper.Clamp(Vector2.Dot(p - a, ab) / len2, 0f, 1f);
            return Vector2.Distance(a + ab * t, p);
        }

        /// <summary>
        /// 线段是否穿过（或落在）矩形内（Liang–Barsky 裁剪）
        /// </summary>
        public static bool SegmentHitsRect(Vector2 a, Vector2 b, Rectangle rect) {
            float t0 = 0f, t1 = 1f;
            float dx = b.X - a.X, dy = b.Y - a.Y;
            return Clip(-dx, a.X - rect.Left, ref t0, ref t1)
                && Clip(dx, rect.Right - a.X, ref t0, ref t1)
                && Clip(-dy, a.Y - rect.Top, ref t0, ref t1)
                && Clip(dy, rect.Bottom - a.Y, ref t0, ref t1);
        }

        private static bool Clip(float p, float q, ref float t0, ref float t1) {
            if (p == 0f) {
                return q >= 0f;
            }
            float r = q / p;
            if (p < 0f) {
                if (r > t1) {
                    return false;
                }
                if (r > t0) {
                    t0 = r;
                }
            }
            else {
                if (r < t0) {
                    return false;
                }
                if (r < t1) {
                    t1 = r;
                }
            }
            return true;
        }

        /// <summary>
        /// 线段到矩形的最近距离（相交为 0）
        /// </summary>
        public static float SegmentRectDistance(Vector2 a, Vector2 b, Rectangle rect) {
            if (SegmentHitsRect(a, b, rect)) {
                return 0f;
            }
            float d = MathF.Min(PointRectDistance(a, rect), PointRectDistance(b, rect));
            d = MathF.Min(d, PointSegmentDistance(new Vector2(rect.Left, rect.Top), a, b));
            d = MathF.Min(d, PointSegmentDistance(new Vector2(rect.Right, rect.Top), a, b));
            d = MathF.Min(d, PointSegmentDistance(new Vector2(rect.Left, rect.Bottom), a, b));
            d = MathF.Min(d, PointSegmentDistance(new Vector2(rect.Right, rect.Bottom), a, b));
            return d;
        }

        private static float PointRectDistance(Vector2 p, Rectangle rect) {
            float dx = p.X - MathHelper.Clamp(p.X, rect.Left, rect.Right);
            float dy = p.Y - MathHelper.Clamp(p.Y, rect.Top, rect.Bottom);
            return MathF.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>
        /// 胶囊（线段 + 半径）是否与矩形相交
        /// </summary>
        public static bool CapsuleIntersects(Vector2 a, Vector2 b, float radius, Rectangle rect) => SegmentRectDistance(a, b, rect) <= radius;

        //==================== 胶囊组 ====================

        /// <summary>
        /// 一个胶囊本帧的两端与半径：沿骨取 [From, To] 段，半径乘实例 Scale；
        /// <paramref name="transform"/> 把骨架空间点换到目标空间（画布空间骨架换到世界），<paramref name="radiusScale"/> 乘半径（落位倍率）
        /// </summary>
        public static bool Capsule(Rig2DInstance rig, Data.Hitbox2DDef h, out Vector2 a, out Vector2 b, out float radius,
            Func<Vector2, Vector2> transform = null, float radiusScale = 1f) {
            a = b = Vector2.Zero;
            radius = 0f;
            if (rig == null || h == null || h.BoneIndex < 0 || h.BoneIndex >= rig.Bones.Length) {
                return false;
            }
            ref Bone2D bone = ref rig.Bones[h.BoneIndex];
            Vector2 tip = bone.Tip;
            a = Vector2.Lerp(bone.Pos, tip, h.From);
            b = Vector2.Lerp(bone.Pos, tip, h.To);
            if (transform != null) {
                a = transform(a);
                b = transform(b);
            }
            radius = h.Radius * Math.Max(rig.Scale, 0.001f) * radiusScale;
            return true;
        }

        /// <summary>
        /// 某组胶囊里第一个与矩形相交的（按定义顺序），命中返回其组内序号
        /// </summary>
        public static bool GroupIntersects(Rig2DInstance rig, string group, Rectangle rect, out int hitIndex,
            Func<Vector2, Vector2> transform = null, float radiusScale = 1f) {
            hitIndex = -1;
            System.Collections.Generic.List<Data.Hitbox2DDef> list = rig?.Definition?.HitboxGroup(group);
            if (list == null) {
                return false;
            }
            for (int i = 0; i < list.Count; i++) {
                if (Capsule(rig, list[i], out Vector2 a, out Vector2 b, out float r, transform, radiusScale)
                    && CapsuleIntersects(a, b, r, rect)) {
                    hitIndex = i;
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 某组胶囊里第一个包含该点的，命中返回其组内序号
        /// </summary>
        public static bool GroupContains(Rig2DInstance rig, string group, Vector2 point, out int hitIndex,
            Func<Vector2, Vector2> transform = null, float radiusScale = 1f) {
            hitIndex = -1;
            System.Collections.Generic.List<Data.Hitbox2DDef> list = rig?.Definition?.HitboxGroup(group);
            if (list == null) {
                return false;
            }
            for (int i = 0; i < list.Count; i++) {
                if (Capsule(rig, list[i], out Vector2 a, out Vector2 b, out float r, transform, radiusScale)
                    && PointSegmentDistance(point, a, b) <= r) {
                    hitIndex = i;
                    return true;
                }
            }
            return false;
        }
    }
}
