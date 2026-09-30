using InnoVault.Rigs2D.Runtime;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;

namespace InnoVault.Rigs2D.Physics
{
    /// <summary>
    /// 接触场：给一个圆（<paramref name="center"/>, <paramref name="radius"/>）找它与地形的接触。
    /// 有接触返回 <see langword="true"/>：法线从地面指向圆心一侧，穿深为正（已嵌入）；没有接触返回 <see langword="false"/>
    /// <br/>整身刚体（<see cref="Rig2DBody"/>）把每个胶囊拆成一串圆来问它；口径可插拔：只认顶面的地表（巨物，虚体穿岩）、完整物块（小体型）、平地（沙盒 / 图鉴舞台）
    /// </summary>
    public delegate bool Rig2DContactField(Vector2 center, float radius, out Rig2DContact contact);

    /// <summary>
    /// 接触场原件：平地、折线、高度场、物块格，以及取最深接触的组合。游戏宿主分部另给物块版（<c>Tiles</c> / <c>TileSurface</c>）
    /// </summary>
    public static partial class Rig2DContactFields
    {
        /// <summary>
        /// 没有地（自由落体测试、纯空中表演）
        /// </summary>
        public static bool None(Vector2 center, float radius, out Rig2DContact contact) {
            contact = default;
            return false;
        }

        /// <summary>
        /// 水平地面 y = <paramref name="groundY"/>（向下为正），地面以下全实
        /// </summary>
        public static Rig2DContactField Flat(float groundY) {
            return (Vector2 center, float radius, out Rig2DContact contact) => {
                float depth = center.Y + radius - groundY;
                contact = new Rig2DContact(new Vector2(center.X, groundY), -Vector2.UnitY, depth);
                return depth > 0f;
            };
        }

        /// <summary>
        /// 高度场：<paramref name="groundAt"/>(x, 参考 y) → 该列在参考高度以下的第一个地面 y（没有返回 <see cref="float.NaN"/>）。
        /// 在圆的水平跨度上按 <paramref name="sampleStep"/> 采样成折线，取圆心到折线的最近点：圆心在线下 = 已嵌入（穿深 = 半径 + 距离），
        /// 否则穿深 = 半径 − 距离。台阶的陡边自然成为墙面；参考高度 = 圆顶再往上 <paramref name="lookUp"/>，所以头顶之上的岩层不参与（虚体穿岩）
        /// </summary>
        /// <param name="groundAt">(x, 参考 y) → 地面 y</param>
        /// <param name="sampleStep">采样间距（像素）</param>
        /// <param name="lookUp">参考高度在圆顶之上的余量（像素）</param>
        public static Rig2DContactField HeightField(Func<float, float, float> groundAt, float sampleStep = 8f, float lookUp = 16f) {
            if (groundAt == null) {
                return None;
            }
            sampleStep = MathF.Max(sampleStep, 1f);
            return (Vector2 center, float radius, out Rig2DContact contact) => HeightQuery(groundAt, sampleStep, lookUp, center, radius, out contact);
        }

        /// <summary>
        /// 静态折线地面（点按 x 升序；两端之外按端点高度水平延伸）：沙盒里的斜坡、台阶
        /// </summary>
        public static Rig2DContactField Polyline(IReadOnlyList<Vector2> points, float sampleStep = 4f) {
            if (points == null || points.Count == 0) {
                return None;
            }
            Vector2[] pts = new Vector2[points.Count];
            for (int i = 0; i < pts.Length; i++) {
                pts[i] = points[i];
            }
            return HeightField((x, _) => PolylineHeight(pts, x), sampleStep, 16f);
        }

        /// <summary>
        /// 折线在 x 处的高度（端点外水平延伸；竖直段取靠后的点）
        /// </summary>
        public static float PolylineHeight(Vector2[] pts, float x) {
            if (x <= pts[0].X) {
                return pts[0].Y;
            }
            for (int i = 1; i < pts.Length; i++) {
                if (x <= pts[i].X) {
                    float span = pts[i].X - pts[i - 1].X;
                    if (span <= 1e-4f) {
                        return pts[i].Y;
                    }
                    return MathHelper.Lerp(pts[i - 1].Y, pts[i].Y, (x - pts[i - 1].X) / span);
                }
            }
            return pts[^1].Y;
        }

        /// <summary>
        /// 物块格：<paramref name="solid"/>(tx, ty) 为实心格，<paramref name="platform"/>(tx, ty) 为只有顶面、自上而下才挡的平台格（可空）。
        /// 圆对每个重叠格求最近点，圆心落进格内时从暴露的那条边推出去；取最深的一处
        /// </summary>
        /// <param name="solid">实心格判定</param>
        /// <param name="tileSize">格边长（像素）</param>
        /// <param name="platform">平台格判定（可空）</param>
        public static Rig2DContactField TileGrid(Func<int, int, bool> solid, float tileSize = 16f, Func<int, int, bool> platform = null) {
            if (solid == null) {
                return None;
            }
            tileSize = MathF.Max(tileSize, 1f);
            return (Vector2 center, float radius, out Rig2DContact contact) => GridQuery(solid, platform, tileSize, center, radius, out contact);
        }

        /// <summary>
        /// 组合：逐个问，取穿深最大的一处
        /// </summary>
        public static Rig2DContactField Combine(params Rig2DContactField[] fields) {
            if (fields == null || fields.Length == 0) {
                return None;
            }
            if (fields.Length == 1) {
                return fields[0] ?? None;
            }
            return (Vector2 center, float radius, out Rig2DContact contact) => {
                contact = default;
                bool any = false;
                for (int i = 0; i < fields.Length; i++) {
                    if (fields[i] != null && fields[i](center, radius, out Rig2DContact c) && (!any || c.Depth > contact.Depth)) {
                        contact = c;
                        any = true;
                    }
                }
                return any;
            };
        }

        //==================== 高度场 ====================

        private const int MaxHeightSamples = 48;

        private static bool HeightQuery(Func<float, float, float> groundAt, float step, float lookUp, Vector2 center, float radius, out Rig2DContact contact) {
            contact = default;
            float refY = center.Y - radius - lookUp;
            float x0 = center.X - radius - step;
            float span = 2f * (radius + step);
            int n = (int)MathF.Ceiling(span / step) + 1;
            if (n > MaxHeightSamples) {
                n = MaxHeightSamples;
                step = span / (n - 1);
            }
            Span<float> xs = stackalloc float[n];
            Span<float> ys = stackalloc float[n];
            for (int k = 0; k < n; k++) {
                float x = x0 + step * k;
                xs[k] = x;
                ys[k] = groundAt(x, refY);
            }
            //圆心正下方的那一段决定"在线上还是线下"
            bool inside = false;
            Vector2 insideNormal = -Vector2.UnitY;
            for (int k = 0; k < n - 1; k++) {
                if (center.X < xs[k] || center.X > xs[k + 1]) {
                    continue;
                }
                if (float.IsFinite(ys[k]) && float.IsFinite(ys[k + 1])) {
                    float t = (center.X - xs[k]) / MathF.Max(xs[k + 1] - xs[k], 1e-4f);
                    float y = MathHelper.Lerp(ys[k], ys[k + 1], t);
                    inside = center.Y > y;
                    insideNormal = UpNormal(new Vector2(xs[k], ys[k]), new Vector2(xs[k + 1], ys[k + 1]));
                }
                break;
            }
            float best = float.MaxValue;
            Vector2 closest = center;
            Vector2 segNormal = -Vector2.UnitY;
            for (int k = 0; k < n - 1; k++) {
                if (!float.IsFinite(ys[k]) || !float.IsFinite(ys[k + 1])) {
                    continue;
                }
                Vector2 a = new(xs[k], ys[k]);
                Vector2 b = new(xs[k + 1], ys[k + 1]);
                Vector2 ab = b - a;
                float len2 = ab.LengthSquared();
                float t = len2 < 1e-6f ? 0f : MathHelper.Clamp(Vector2.Dot(center - a, ab) / len2, 0f, 1f);
                Vector2 p = a + ab * t;
                float d = Vector2.DistanceSquared(center, p);
                if (d < best) {
                    best = d;
                    closest = p;
                    segNormal = UpNormal(a, b);
                }
            }
            if (best == float.MaxValue) {
                return false;
            }
            float dist = MathF.Sqrt(best);
            Vector2 normal;
            float depth;
            if (inside) {
                normal = dist > 1e-3f ? (closest - center) / dist : insideNormal;
                //最近点反而在圆心下方（折线在圆心两侧都更高的窄沟）：按正下方那段的法线推
                if (normal.Y > 0f) {
                    normal = insideNormal;
                }
                depth = radius + dist;
            }
            else {
                normal = dist > 1e-3f ? (center - closest) / dist : segNormal;
                depth = radius - dist;
            }
            contact = new Rig2DContact(closest, normal, depth);
            return depth > 0f;
        }

        //折线段（左 → 右）的朝上法线（屏幕 y 向下，上 = −Y）
        private static Vector2 UpNormal(Vector2 a, Vector2 b) {
            Vector2 t = b - a;
            if (t.X < 0f) {
                t = -t;
            }
            float len = t.Length();
            if (len < 1e-5f) {
                return -Vector2.UnitY;
            }
            t /= len;
            return new Vector2(t.Y, -t.X);
        }

        //==================== 物块格 ====================

        private static bool GridQuery(Func<int, int, bool> solid, Func<int, int, bool> platform, float size, Vector2 center, float radius, out Rig2DContact contact) {
            contact = default;
            int tx0 = (int)MathF.Floor((center.X - radius) / size);
            int tx1 = (int)MathF.Floor((center.X + radius) / size);
            int ty0 = (int)MathF.Floor((center.Y - radius) / size);
            int ty1 = (int)MathF.Floor((center.Y + radius) / size);
            bool any = false;
            for (int tx = tx0; tx <= tx1; tx++) {
                for (int ty = ty0; ty <= ty1; ty++) {
                    Rig2DContact c;
                    if (solid(tx, ty)) {
                        if (!SolidCell(solid, size, tx, ty, center, radius, out c)) {
                            continue;
                        }
                    }
                    else if (platform != null && platform(tx, ty)) {
                        float top = ty * size;
                        //平台只挡从上面来的：圆心在顶面之上才算
                        if (center.Y > top || center.X < tx * size - radius || center.X > (tx + 1) * size + radius) {
                            continue;
                        }
                        float px = MathHelper.Clamp(center.X, tx * size, (tx + 1) * size);
                        Vector2 p = new(px, top);
                        float d = Vector2.Distance(center, p);
                        if (d >= radius) {
                            continue;
                        }
                        c = new Rig2DContact(p, d > 1e-3f ? (center - p) / d : -Vector2.UnitY, radius - d);
                        if (c.Normal.Y > -0.3f) {
                            c.Normal = -Vector2.UnitY;
                        }
                    }
                    else {
                        continue;
                    }
                    if (!any || c.Depth > contact.Depth) {
                        contact = c;
                        any = true;
                    }
                }
            }
            return any;
        }

        private static bool SolidCell(Func<int, int, bool> solid, float size, int tx, int ty, Vector2 center, float radius, out Rig2DContact contact) {
            float left = tx * size, top = ty * size, right = left + size, bottom = top + size;
            bool insideX = center.X >= left && center.X <= right;
            bool insideY = center.Y >= top && center.Y <= bottom;
            if (insideX && insideY) {
                //圆心在格内：从暴露的边里挑最近的推出去（四边都被邻格堵住时按顶面）
                float best = float.MaxValue;
                Vector2 n = -Vector2.UnitY;
                Vector2 p = new(center.X, top);
                TryEdge(!solid(tx, ty - 1), center.Y - top, -Vector2.UnitY, new Vector2(center.X, top), ref best, ref n, ref p);
                TryEdge(!solid(tx, ty + 1), bottom - center.Y, Vector2.UnitY, new Vector2(center.X, bottom), ref best, ref n, ref p);
                TryEdge(!solid(tx - 1, ty), center.X - left, -Vector2.UnitX, new Vector2(left, center.Y), ref best, ref n, ref p);
                TryEdge(!solid(tx + 1, ty), right - center.X, Vector2.UnitX, new Vector2(right, center.Y), ref best, ref n, ref p);
                if (best == float.MaxValue) {
                    best = center.Y - top;
                }
                contact = new Rig2DContact(p, n, radius + best);
                return true;
            }
            Vector2 q = new(MathHelper.Clamp(center.X, left, right), MathHelper.Clamp(center.Y, top, bottom));
            float d = Vector2.Distance(center, q);
            if (d >= radius) {
                contact = default;
                return false;
            }
            Vector2 normal = d > 1e-3f ? (center - q) / d : -Vector2.UnitY;
            //落在与实心邻格共享的内缝上（地面由多格拼成）：法线换成暴露面的方向，免得被缝挡住
            if (normal.Y < -0.5f && solid(tx, ty - 1) || normal.Y > 0.5f && solid(tx, ty + 1)
                || normal.X < -0.5f && solid(tx - 1, ty) || normal.X > 0.5f && solid(tx + 1, ty)) {
                contact = default;
                return false;
            }
            contact = new Rig2DContact(q, normal, radius - d);
            return true;
        }

        private static void TryEdge(bool exposed, float dist, Vector2 normal, Vector2 point, ref float best, ref Vector2 n, ref Vector2 p) {
            if (exposed && dist < best) {
                best = dist;
                n = normal;
                p = point;
            }
        }
    }
}
