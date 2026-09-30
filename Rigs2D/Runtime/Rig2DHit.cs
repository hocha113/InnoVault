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

        /// <summary>
        /// 把某组胶囊本帧的世界形状追加进 <paramref name="into"/>（<see cref="Rig2DCapsule.Index"/> = 组内序号），返回追加个数。
        /// 受击盒 / 攻击体积 / 整身刚体都从这里取形状
        /// </summary>
        public static int GatherGroup(Rig2DInstance rig, string group, System.Collections.Generic.List<Rig2DCapsule> into,
            Func<Vector2, Vector2> transform = null, float radiusScale = 1f) {
            System.Collections.Generic.List<Data.Hitbox2DDef> list = rig?.Definition?.HitboxGroup(group);
            if (list == null || into == null) {
                return 0;
            }
            int added = 0;
            for (int i = 0; i < list.Count; i++) {
                if (Capsule(rig, list[i], out Vector2 a, out Vector2 b, out float r, transform, radiusScale)) {
                    into.Add(new Rig2DCapsule(a, b, r, i));
                    added++;
                }
            }
            return added;
        }

        //==================== 胶囊对胶囊 / 胶囊对矩形（带接触信息） ====================

        /// <summary>
        /// 两线段的最近点对（Ericson 5.1.9）：返回距离平方，<paramref name="s"/> / <paramref name="t"/> 为两段上的参数，
        /// <paramref name="c1"/> / <paramref name="c2"/> 为对应的点。退化线段按点处理
        /// </summary>
        public static float ClosestPointsSegmentSegment(Vector2 p1, Vector2 q1, Vector2 p2, Vector2 q2,
            out float s, out float t, out Vector2 c1, out Vector2 c2) {
            const float eps = 1e-6f;
            Vector2 d1 = q1 - p1;
            Vector2 d2 = q2 - p2;
            Vector2 r = p1 - p2;
            float a = Vector2.Dot(d1, d1);
            float e = Vector2.Dot(d2, d2);
            float f = Vector2.Dot(d2, r);
            if (a <= eps && e <= eps) {
                s = t = 0f;
            }
            else if (a <= eps) {
                s = 0f;
                t = MathHelper.Clamp(f / e, 0f, 1f);
            }
            else {
                float c = Vector2.Dot(d1, r);
                if (e <= eps) {
                    t = 0f;
                    s = MathHelper.Clamp(-c / a, 0f, 1f);
                }
                else {
                    float b = Vector2.Dot(d1, d2);
                    float denom = a * e - b * b;
                    s = denom > eps ? MathHelper.Clamp((b * f - c * e) / denom, 0f, 1f) : 0f;
                    t = (b * s + f) / e;
                    if (t < 0f) {
                        t = 0f;
                        s = MathHelper.Clamp(-c / a, 0f, 1f);
                    }
                    else if (t > 1f) {
                        t = 1f;
                        s = MathHelper.Clamp((b - c) / a, 0f, 1f);
                    }
                }
            }
            c1 = p1 + d1 * s;
            c2 = p2 + d2 * t;
            return Vector2.DistanceSquared(c1, c2);
        }

        /// <summary>
        /// 两线段是否相交（含端点接触、共线重叠）
        /// </summary>
        public static bool SegmentsIntersect(Vector2 p1, Vector2 q1, Vector2 p2, Vector2 q2)
            => ClosestPointsSegmentSegment(p1, q1, p2, q2, out _, out _, out _, out _) <= 1e-8f;

        /// <summary>
        /// 两胶囊是否重叠
        /// </summary>
        public static bool CapsuleIntersects(in Rig2DCapsule a, in Rig2DCapsule b) {
            float r = a.Radius + b.Radius;
            return ClosestPointsSegmentSegment(a.A, a.B, b.A, b.B, out _, out _, out _, out _) <= r * r;
        }

        /// <summary>
        /// 两胶囊的接触：<paramref name="contact"/> 的法线从 <paramref name="b"/> 指向 <paramref name="a"/>，
        /// 接触点取两表面之间的中点，穿深 = 半径和 − 轴距（不重叠时为负、返回 <see langword="false"/>）。
        /// 两轴线相交（轴距为 0）时法线取 <paramref name="b"/> 轴的法向、朝 <paramref name="a"/> 的中点一侧
        /// </summary>
        public static bool CapsuleContact(in Rig2DCapsule a, in Rig2DCapsule b, out Rig2DContact contact) {
            float d2 = ClosestPointsSegmentSegment(a.A, a.B, b.A, b.B, out _, out _, out Vector2 ca, out Vector2 cb);
            float dist = MathF.Sqrt(d2);
            Vector2 n;
            if (dist > 1e-4f) {
                n = (ca - cb) / dist;
            }
            else {
                n = CrossingNormal(a, b);
            }
            float depth = a.Radius + b.Radius - dist;
            //两表面上的对应点：a 表面 = ca − n·ra，b 表面 = cb + n·rb，取中点
            Vector2 point = (ca - n * a.Radius + cb + n * b.Radius) * 0.5f;
            contact = new Rig2DContact(point, n, depth);
            return depth >= 0f;
        }

        private static Vector2 CrossingNormal(in Rig2DCapsule a, in Rig2DCapsule b) {
            Vector2 axis = b.B - b.A;
            Vector2 toA = a.Center - b.Center;
            if (axis.LengthSquared() > 1e-6f) {
                Vector2 perp = new(-axis.Y, axis.X);
                perp.Normalize();
                return Vector2.Dot(perp, toA) >= 0f ? perp : -perp;
            }
            if (toA.LengthSquared() > 1e-6f) {
                toA.Normalize();
                return toA;
            }
            return -Vector2.UnitY;
        }

        /// <summary>
        /// 胶囊与矩形的接触：法线从矩形指向胶囊，接触点取矩形上离胶囊轴最近的点（轴线穿过矩形时取轴在矩形内那一截的中点），
        /// 穿深 = 半径 − 轴到矩形的距离
        /// </summary>
        public static bool CapsuleRectContact(in Rig2DCapsule c, Rectangle rect, out Rig2DContact contact) {
            Vector2 center = new(rect.X + rect.Width * 0.5f, rect.Y + rect.Height * 0.5f);
            if (ClipSegment(c.A, c.B, rect, out float t0, out float t1)) {
                Vector2 mid = Vector2.Lerp(c.A, c.B, (t0 + t1) * 0.5f);
                Vector2 away = mid - center;
                Vector2 n = away.LengthSquared() > 1e-6f ? Vector2.Normalize(away) : -Vector2.UnitY;
                contact = new Rig2DContact(mid, n, c.Radius);
                return true;
            }
            //不相交：比两端点到矩形、四角到线段，取最近的一对
            float best = float.MaxValue;
            Vector2 onSeg = c.A, onRect = c.A;
            Consider(c.A, ClampToRect(c.A, rect), ref best, ref onSeg, ref onRect);
            Consider(c.B, ClampToRect(c.B, rect), ref best, ref onSeg, ref onRect);
            ConsiderCorner(new Vector2(rect.Left, rect.Top), c, ref best, ref onSeg, ref onRect);
            ConsiderCorner(new Vector2(rect.Right, rect.Top), c, ref best, ref onSeg, ref onRect);
            ConsiderCorner(new Vector2(rect.Left, rect.Bottom), c, ref best, ref onSeg, ref onRect);
            ConsiderCorner(new Vector2(rect.Right, rect.Bottom), c, ref best, ref onSeg, ref onRect);
            float dist = MathF.Sqrt(best);
            Vector2 normal = dist > 1e-4f ? (onSeg - onRect) / dist : -Vector2.UnitY;
            contact = new Rig2DContact(onRect, normal, c.Radius - dist);
            return dist <= c.Radius;
        }

        private static Vector2 ClampToRect(Vector2 p, Rectangle rect)
            => new(MathHelper.Clamp(p.X, rect.Left, rect.Right), MathHelper.Clamp(p.Y, rect.Top, rect.Bottom));

        private static void Consider(Vector2 seg, Vector2 rectPoint, ref float best, ref Vector2 onSeg, ref Vector2 onRect) {
            float d = Vector2.DistanceSquared(seg, rectPoint);
            if (d < best) {
                best = d;
                onSeg = seg;
                onRect = rectPoint;
            }
        }

        private static void ConsiderCorner(Vector2 corner, in Rig2DCapsule c, ref float best, ref Vector2 onSeg, ref Vector2 onRect) {
            Vector2 ab = c.B - c.A;
            float len2 = ab.LengthSquared();
            float t = len2 < 1e-6f ? 0f : MathHelper.Clamp(Vector2.Dot(corner - c.A, ab) / len2, 0f, 1f);
            Consider(c.A + ab * t, corner, ref best, ref onSeg, ref onRect);
        }

        //线段在矩形内的参数区间（Liang–Barsky）
        private static bool ClipSegment(Vector2 a, Vector2 b, Rectangle rect, out float t0, out float t1) {
            t0 = 0f;
            t1 = 1f;
            float dx = b.X - a.X, dy = b.Y - a.Y;
            return Clip(-dx, a.X - rect.Left, ref t0, ref t1)
                && Clip(dx, rect.Right - a.X, ref t0, ref t1)
                && Clip(-dy, a.Y - rect.Top, ref t0, ref t1)
                && Clip(dy, rect.Bottom - a.Y, ref t0, ref t1);
        }

        /// <summary>
        /// 两组胶囊之间最深的一处接触（法线从 <paramref name="b"/> 组指向 <paramref name="a"/> 组）；
        /// 返回值 = 是否有重叠，<see cref="Rig2DGroupContact.IndexA"/> / <see cref="Rig2DGroupContact.IndexB"/> 为两侧的数组下标
        /// </summary>
        public static bool CapsulesContact(ReadOnlySpan<Rig2DCapsule> a, ReadOnlySpan<Rig2DCapsule> b, out Rig2DGroupContact hit) {
            hit = default;
            hit.IndexA = hit.IndexB = -1;
            bool any = false;
            float deepest = float.MinValue;
            for (int i = 0; i < a.Length; i++) {
                for (int j = 0; j < b.Length; j++) {
                    if (CapsuleContact(in a[i], in b[j], out Rig2DContact c) && c.Depth > deepest) {
                        deepest = c.Depth;
                        hit.Contact = c;
                        hit.IndexA = i;
                        hit.IndexB = j;
                        any = true;
                    }
                }
            }
            return any;
        }

        /// <summary>
        /// 一组胶囊与矩形最深的一处接触（法线从矩形指向胶囊），<paramref name="index"/> 为数组下标
        /// </summary>
        public static bool CapsulesRectContact(ReadOnlySpan<Rig2DCapsule> capsules, Rectangle rect, out Rig2DContact contact, out int index) {
            contact = default;
            index = -1;
            float deepest = float.MinValue;
            for (int i = 0; i < capsules.Length; i++) {
                if (CapsuleRectContact(in capsules[i], rect, out Rig2DContact c) && c.Depth > deepest) {
                    deepest = c.Depth;
                    contact = c;
                    index = i;
                }
            }
            return index >= 0;
        }

        /// <summary>
        /// 一组胶囊里是否有与矩形相交的，命中返回数组下标
        /// </summary>
        public static bool CapsulesIntersect(ReadOnlySpan<Rig2DCapsule> capsules, Rectangle rect, out int index) {
            for (int i = 0; i < capsules.Length; i++) {
                if (CapsuleIntersects(capsules[i].A, capsules[i].B, capsules[i].Radius, rect)) {
                    index = i;
                    return true;
                }
            }
            index = -1;
            return false;
        }

        /// <summary>
        /// 两副骨架各一组胶囊之间最深的一处接触（法线从 B 指向 A，序号为各自组内序号）。
        /// 两侧各有自己的空间变换与半径倍率（画布空间骨架换到世界）
        /// </summary>
        public static bool GroupContact(Rig2DInstance rigA, string groupA, Rig2DInstance rigB, string groupB, out Rig2DGroupContact hit,
            Func<Vector2, Vector2> transformA = null, float radiusScaleA = 1f, Func<Vector2, Vector2> transformB = null, float radiusScaleB = 1f) {
            hit = default;
            hit.IndexA = hit.IndexB = -1;
            System.Collections.Generic.List<Data.Hitbox2DDef> la = rigA?.Definition?.HitboxGroup(groupA);
            System.Collections.Generic.List<Data.Hitbox2DDef> lb = rigB?.Definition?.HitboxGroup(groupB);
            if (la == null || lb == null) {
                return false;
            }
            bool any = false;
            float deepest = float.MinValue;
            for (int i = 0; i < la.Count; i++) {
                if (!Capsule(rigA, la[i], out Vector2 a0, out Vector2 a1, out float ra, transformA, radiusScaleA)) {
                    continue;
                }
                Rig2DCapsule ca = new(a0, a1, ra, i);
                for (int j = 0; j < lb.Count; j++) {
                    if (!Capsule(rigB, lb[j], out Vector2 b0, out Vector2 b1, out float rb, transformB, radiusScaleB)) {
                        continue;
                    }
                    Rig2DCapsule cb = new(b0, b1, rb, j);
                    if (CapsuleContact(in ca, in cb, out Rig2DContact c) && c.Depth > deepest) {
                        deepest = c.Depth;
                        hit.Contact = c;
                        hit.IndexA = i;
                        hit.IndexB = j;
                        any = true;
                    }
                }
            }
            return any;
        }
    }
}
