using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;

namespace InnoVault.Vectors
{
    /// <summary>
    /// Canvas2D 风格的路径流式构造器：记录 M / L / Q / C / A 等指令，<see cref="Build"/> 时按平坦度统一展平成 <see cref="VectorPath"/>
    /// <br/>坐标一律为绝对坐标（相对形式由 <see cref="SvgPathParser"/> 在解析时折算）；角度一律为弧度
    /// </summary>
    public sealed class VectorPathBuilder
    {
        private enum SegmentKind : byte
        {
            Line,
            Quadratic,
            Cubic,
        }

        private struct Segment
        {
            public SegmentKind Kind;
            //Line: P1 = 终点；Quadratic: P1 = 控制点, P2 = 终点；Cubic: P1/P2 = 控制点, P3 = 终点
            public Vector2 P1;
            public Vector2 P2;
            public Vector2 P3;

            public readonly Vector2 End => Kind switch {
                SegmentKind.Line => P1,
                SegmentKind.Quadratic => P2,
                _ => P3,
            };
        }

        private sealed class SubPathRecord
        {
            public Vector2 Start;
            public readonly List<Segment> Segments = [];
            public bool Closed;
        }

        private readonly List<SubPathRecord> subPaths = [];
        private SubPathRecord current;
        private Vector2 cursor;
        private Vector2 subPathStart;
        //上一条曲线段的最后一个控制点与类型，供 S / T 平滑指令做镜像
        private Vector2 lastControl;
        private SegmentKind lastKind = SegmentKind.Line;

        /// <summary>绝对平坦度容差（路径单位）；大于 0 时优先于 <see cref="RelativeTolerance"/></summary>
        public float Tolerance { get; set; }
        /// <summary>相对平坦度容差：实际容差 = 该值 × 全路径控制点包围盒的长边。默认 1/400，即 400 像素大的图形最多偏差 1 像素</summary>
        public float RelativeTolerance { get; set; } = VectorFlatten.RelativeTolerance;
        /// <summary>每条曲线最少切几段</summary>
        public int MinCurveSegments { get; set; } = VectorFlatten.MinCurveSegments;
        /// <summary>每条曲线最多切几段</summary>
        public int MaxCurveSegments { get; set; } = VectorFlatten.MaxCurveSegments;
        /// <summary>当前笔尖位置</summary>
        public Vector2 CurrentPoint => cursor;
        /// <summary>是否还没有任何指令</summary>
        public bool IsEmpty => subPaths.Count == 0 && current == null;

        /// <summary>开始一条新的子路径</summary>
        public VectorPathBuilder MoveTo(Vector2 point) {
            //连续两个 M 只保留后者
            if (current != null && current.Segments.Count == 0) {
                current.Start = point;
            }
            else {
                current = new SubPathRecord { Start = point };
                subPaths.Add(current);
            }
            cursor = subPathStart = point;
            lastKind = SegmentKind.Line;
            return this;
        }

        /// <summary>开始一条新的子路径</summary>
        public VectorPathBuilder MoveTo(float x, float y) => MoveTo(new Vector2(x, y));

        /// <summary>直线到</summary>
        public VectorPathBuilder LineTo(Vector2 point) {
            EnsureSubPath();
            current.Segments.Add(new Segment { Kind = SegmentKind.Line, P1 = point });
            cursor = point;
            lastKind = SegmentKind.Line;
            return this;
        }

        /// <summary>直线到</summary>
        public VectorPathBuilder LineTo(float x, float y) => LineTo(new Vector2(x, y));

        /// <summary>水平直线到 x</summary>
        public VectorPathBuilder HorizontalTo(float x) => LineTo(new Vector2(x, cursor.Y));

        /// <summary>竖直直线到 y</summary>
        public VectorPathBuilder VerticalTo(float y) => LineTo(new Vector2(cursor.X, y));

        /// <summary>二次贝塞尔到</summary>
        public VectorPathBuilder QuadTo(Vector2 control, Vector2 end) {
            EnsureSubPath();
            current.Segments.Add(new Segment { Kind = SegmentKind.Quadratic, P1 = control, P2 = end });
            cursor = end;
            lastControl = control;
            lastKind = SegmentKind.Quadratic;
            return this;
        }

        /// <summary>平滑二次贝塞尔（SVG <c>T</c>）：控制点取上一条二次曲线控制点关于当前点的镜像，上一条不是二次曲线时取当前点</summary>
        public VectorPathBuilder SmoothQuadTo(Vector2 end) {
            Vector2 control = lastKind == SegmentKind.Quadratic ? cursor * 2f - lastControl : cursor;
            return QuadTo(control, end);
        }

        /// <summary>三次贝塞尔到</summary>
        public VectorPathBuilder CubicTo(Vector2 control1, Vector2 control2, Vector2 end) {
            EnsureSubPath();
            current.Segments.Add(new Segment { Kind = SegmentKind.Cubic, P1 = control1, P2 = control2, P3 = end });
            cursor = end;
            lastControl = control2;
            lastKind = SegmentKind.Cubic;
            return this;
        }

        /// <summary>平滑三次贝塞尔（SVG <c>S</c>）：第一控制点取上一条三次曲线第二控制点关于当前点的镜像，上一条不是三次曲线时取当前点</summary>
        public VectorPathBuilder SmoothCubicTo(Vector2 control2, Vector2 end) {
            Vector2 control1 = lastKind == SegmentKind.Cubic ? cursor * 2f - lastControl : cursor;
            return CubicTo(control1, control2, end);
        }

        /// <summary>
        /// SVG <c>A</c> 端点参数化椭圆弧：从当前点到 <paramref name="end"/>，半径 <paramref name="rx"/>/<paramref name="ry"/>，
        /// 长轴旋转 <paramref name="xAxisRotation"/>（弧度），<paramref name="largeArc"/> 选大弧，<paramref name="sweep"/> 选角度增加方向
        /// <br/>半径不足以连接两点时按规范等比放大；任一半径为 0 时退化为直线
        /// </summary>
        public VectorPathBuilder ArcTo(float rx, float ry, float xAxisRotation, bool largeArc, bool sweep, Vector2 end) {
            EnsureSubPath();
            Vector2 start = cursor;
            if (Vector2.DistanceSquared(start, end) < 1e-12f) {
                return this;
            }
            rx = MathF.Abs(rx);
            ry = MathF.Abs(ry);
            if (rx < 1e-6f || ry < 1e-6f) {
                return LineTo(end);
            }
            //SVG 实现说明 F.6.5：端点参数 → 圆心参数
            float cosPhi = MathF.Cos(xAxisRotation);
            float sinPhi = MathF.Sin(xAxisRotation);
            float dx2 = (start.X - end.X) * 0.5f;
            float dy2 = (start.Y - end.Y) * 0.5f;
            float x1p = cosPhi * dx2 + sinPhi * dy2;
            float y1p = -sinPhi * dx2 + cosPhi * dy2;
            float lambda = x1p * x1p / (rx * rx) + y1p * y1p / (ry * ry);
            if (lambda > 1f) {
                float s = MathF.Sqrt(lambda);
                rx *= s;
                ry *= s;
            }
            float rx2 = rx * rx;
            float ry2 = ry * ry;
            float denom = rx2 * y1p * y1p + ry2 * x1p * x1p;
            float sq = denom > 0f ? MathF.Max(0f, (rx2 * ry2 - rx2 * y1p * y1p - ry2 * x1p * x1p) / denom) : 0f;
            float coef = (largeArc != sweep ? 1f : -1f) * MathF.Sqrt(sq);
            float cxp = coef * (rx * y1p / ry);
            float cyp = coef * -(ry * x1p / rx);
            Vector2 center = new(
                cosPhi * cxp - sinPhi * cyp + (start.X + end.X) * 0.5f,
                sinPhi * cxp + cosPhi * cyp + (start.Y + end.Y) * 0.5f);
            Vector2 u = new((x1p - cxp) / rx, (y1p - cyp) / ry);
            Vector2 v = new((-x1p - cxp) / rx, (-y1p - cyp) / ry);
            float theta1 = MathF.Atan2(u.Y, u.X);
            float delta = MathF.Atan2(u.X * v.Y - u.Y * v.X, Vector2.Dot(u, v));
            if (!sweep && delta > 0f) {
                delta -= MathHelper.TwoPi;
            }
            else if (sweep && delta < 0f) {
                delta += MathHelper.TwoPi;
            }
            AppendArcCubics(center, new Vector2(rx, ry), xAxisRotation, theta1, delta);
            //数值误差会让最后一段的终点略偏，强制钉回给定终点
            if (current.Segments.Count > 0) {
                Segment last = current.Segments[^1];
                if (last.Kind == SegmentKind.Cubic) {
                    last.P3 = end;
                    current.Segments[^1] = last;
                }
            }
            cursor = end;
            lastKind = SegmentKind.Line;
            return this;
        }

        /// <summary>
        /// 圆心参数化圆弧（Canvas2D <c>arc</c>）：圆心、半径、起始角、扫过角（正为角度增加方向）。
        /// 当前子路径已有笔迹时先直线连到弧起点，否则以弧起点开新子路径
        /// </summary>
        public VectorPathBuilder Arc(Vector2 center, float radius, float startAngle, float sweepAngle)
            => Arc(center, new Vector2(radius), 0f, startAngle, sweepAngle);

        /// <summary>圆心参数化椭圆弧，<paramref name="rotation"/> 为长短轴旋转（弧度）</summary>
        public VectorPathBuilder Arc(Vector2 center, Vector2 radii, float rotation, float startAngle, float sweepAngle) {
            Vector2 start = VectorFlatten.EllipsePoint(center, radii, rotation, startAngle);
            if (current == null) {
                MoveTo(start);
            }
            else if (Vector2.DistanceSquared(cursor, start) > 1e-10f) {
                LineTo(start);
            }
            if (MathF.Abs(sweepAngle) < 1e-7f) {
                return this;
            }
            AppendArcCubics(center, radii, rotation, startAngle, sweepAngle);
            cursor = VectorFlatten.EllipsePoint(center, radii, rotation, startAngle + sweepAngle);
            lastKind = SegmentKind.Line;
            return this;
        }

        /// <summary>闭合当前子路径并把笔尖移回其起点；之后不经 <see cref="MoveTo(Vector2)"/> 的绘制指令会从该起点开新子路径</summary>
        public VectorPathBuilder Close() {
            if (current != null) {
                current.Closed = true;
                current = null;
            }
            cursor = subPathStart;
            lastKind = SegmentKind.Line;
            return this;
        }

        /// <summary>清空全部指令，复用实例</summary>
        public VectorPathBuilder Clear() {
            subPaths.Clear();
            current = null;
            cursor = subPathStart = Vector2.Zero;
            lastKind = SegmentKind.Line;
            return this;
        }

        /// <summary>按当前容差把全部指令展平成不可变路径；构造器内容保留，可继续追加后再次构建</summary>
        public VectorPath Build() {
            if (subPaths.Count == 0) {
                return VectorPath.Empty;
            }
            float tol = Tolerance > 0f ? Tolerance : RelativeTolerance * MathF.Max(ControlExtent(), 1e-6f);
            int minSeg = Math.Max(1, MinCurveSegments);
            int maxSeg = Math.Max(minSeg, MaxCurveSegments);
            //相邻点距离小于这个值视为重合
            float dedupe = MathF.Max(tol * 0.05f, 1e-6f);
            float dedupeSq = dedupe * dedupe;

            List<VectorSubPath> parts = new(subPaths.Count);
            List<Vector2> pts = new(64);
            foreach (SubPathRecord sub in subPaths) {
                pts.Clear();
                pts.Add(sub.Start);
                Vector2 from = sub.Start;
                foreach (Segment seg in sub.Segments) {
                    switch (seg.Kind) {
                        case SegmentKind.Line:
                            pts.Add(seg.P1);
                            break;
                        case SegmentKind.Quadratic: {
                            int n = Math.Clamp(VectorFlatten.QuadraticSegments(from, seg.P1, seg.P2, tol), minSeg, maxSeg);
                            VectorFlatten.AppendQuadratic(pts, from, seg.P1, seg.P2, n);
                            break;
                        }
                        default: {
                            int n = Math.Clamp(VectorFlatten.CubicSegments(from, seg.P1, seg.P2, seg.P3, tol), minSeg, maxSeg);
                            VectorFlatten.AppendCubic(pts, from, seg.P1, seg.P2, seg.P3, n);
                            break;
                        }
                    }
                    from = seg.End;
                }
                //去掉重合点；闭合子路径末点若回到首点也去掉（回合段隐含）
                int w = 1;
                for (int i = 1; i < pts.Count; i++) {
                    if (Vector2.DistanceSquared(pts[i], pts[w - 1]) > dedupeSq) {
                        pts[w++] = pts[i];
                    }
                }
                if (sub.Closed && w > 1 && Vector2.DistanceSquared(pts[w - 1], pts[0]) <= dedupeSq) {
                    w--;
                }
                Vector2[] arr = new Vector2[w];
                pts.CopyTo(0, arr, 0, w);
                parts.Add(new VectorSubPath(arr, sub.Closed));
            }
            return new VectorPath(parts);
        }

        //==================== 内部 ====================

        private void EnsureSubPath() {
            if (current == null) {
                current = new SubPathRecord { Start = cursor };
                subPaths.Add(current);
                subPathStart = cursor;
            }
        }

        //把圆心参数化的椭圆弧拆成每段不超过 90° 的三次贝塞尔
        private void AppendArcCubics(Vector2 center, Vector2 radii, float rotation, float startAngle, float sweep) {
            int pieces = Math.Max(1, (int)MathF.Ceiling(MathF.Abs(sweep) / MathHelper.PiOver2 - 1e-4f));
            float step = sweep / pieces;
            //单段圆弧的三次贝塞尔控制柄长度系数
            float k = 4f / 3f * MathF.Tan(step * 0.25f);
            float a = startAngle;
            for (int i = 0; i < pieces; i++) {
                float b = a + step;
                Vector2 p0 = VectorFlatten.EllipsePoint(center, radii, rotation, a);
                Vector2 p3 = VectorFlatten.EllipsePoint(center, radii, rotation, b);
                Vector2 d0 = VectorFlatten.EllipseDerivative(radii, rotation, a);
                Vector2 d3 = VectorFlatten.EllipseDerivative(radii, rotation, b);
                current.Segments.Add(new Segment {
                    Kind = SegmentKind.Cubic,
                    P1 = p0 + d0 * k,
                    P2 = p3 - d3 * k,
                    P3 = p3,
                });
                a = b;
            }
            lastControl = current.Segments[^1].P2;
        }

        private float ControlExtent() {
            Vector2 min = new(float.MaxValue);
            Vector2 max = new(float.MinValue);
            foreach (SubPathRecord sub in subPaths) {
                Expand(ref min, ref max, sub.Start);
                foreach (Segment seg in sub.Segments) {
                    Expand(ref min, ref max, seg.P1);
                    if (seg.Kind != SegmentKind.Line) {
                        Expand(ref min, ref max, seg.P2);
                    }
                    if (seg.Kind == SegmentKind.Cubic) {
                        Expand(ref min, ref max, seg.P3);
                    }
                }
            }
            Vector2 size = max - min;
            return MathF.Max(size.X, size.Y);
        }

        private static void Expand(ref Vector2 min, ref Vector2 max, Vector2 p) {
            min = Vector2.Min(min, p);
            max = Vector2.Max(max, p);
        }
    }

    /// <summary>
    /// 曲线展平的公共数学：Wang 公式估算段数、贝塞尔 / 椭圆弧 / Catmull-Rom 采样
    /// </summary>
    internal static class VectorFlatten
    {
        /// <summary>默认相对容差：偏差不超过图形长边的 1/400</summary>
        public const float RelativeTolerance = 1f / 400f;
        /// <summary>曲线最少段数</summary>
        public const int MinCurveSegments = 2;
        /// <summary>曲线最多段数</summary>
        public const int MaxCurveSegments = 64;

        //圆弧按弦弓高 r(1 - cos(step/2)) ≤ r/400 取角步长，整圆约 44 段
        private static readonly float arcStep = 2f * MathF.Acos(1f - RelativeTolerance);

        /// <summary>扫过 <paramref name="sweep"/> 弧度需要的段数（默认精度）</summary>
        public static int ArcSegments(float sweep) => Math.Max(1, (int)MathF.Ceiling(MathF.Abs(sweep) / arcStep - 1e-4f));

        /// <summary>四个点包围盒的长边</summary>
        public static float Extent(Vector2 a, Vector2 b, Vector2 c, Vector2 d) {
            Vector2 min = Vector2.Min(Vector2.Min(a, b), Vector2.Min(c, d));
            Vector2 max = Vector2.Max(Vector2.Max(a, b), Vector2.Max(c, d));
            Vector2 size = max - min;
            return MathF.Max(MathF.Max(size.X, size.Y), 1e-6f);
        }

        /// <summary>Wang 公式：三次贝塞尔在容差 <paramref name="tolerance"/> 下需要的均匀段数（未夹范围）</summary>
        public static int CubicSegments(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float tolerance) {
            float dd = MathF.Max((p0 - 2f * p1 + p2).Length(), (p1 - 2f * p2 + p3).Length());
            return Math.Max(1, (int)MathF.Ceiling(MathF.Sqrt(0.75f * dd / MathF.Max(tolerance, 1e-6f))));
        }

        /// <summary>Wang 公式：二次贝塞尔在容差 <paramref name="tolerance"/> 下需要的均匀段数（未夹范围）</summary>
        public static int QuadraticSegments(Vector2 p0, Vector2 p1, Vector2 p2, float tolerance) {
            float dd = (p0 - 2f * p1 + p2).Length();
            return Math.Max(1, (int)MathF.Ceiling(MathF.Sqrt(0.25f * dd / MathF.Max(tolerance, 1e-6f))));
        }

        /// <summary>均匀采样三次贝塞尔，追加 <paramref name="segments"/> 个点（不含起点，含终点）</summary>
        public static void AppendCubic(List<Vector2> into, Vector2 p0, Vector2 c1, Vector2 c2, Vector2 p1, int segments) {
            segments = Math.Max(segments, 1);
            for (int s = 1; s <= segments; s++) {
                float t = s / (float)segments;
                float u = 1f - t;
                into.Add(u * u * u * p0 + 3f * u * u * t * c1 + 3f * u * t * t * c2 + t * t * t * p1);
            }
        }

        /// <summary>均匀采样二次贝塞尔，追加 <paramref name="segments"/> 个点（不含起点，含终点）</summary>
        public static void AppendQuadratic(List<Vector2> into, Vector2 p0, Vector2 control, Vector2 p1, int segments) {
            segments = Math.Max(segments, 1);
            for (int s = 1; s <= segments; s++) {
                float t = s / (float)segments;
                float u = 1f - t;
                into.Add(u * u * p0 + 2f * u * t * control + t * t * p1);
            }
        }

        /// <summary>均匀采样椭圆弧；<paramref name="includeStart"/> 决定是否追加起点</summary>
        public static void AppendArc(List<Vector2> into, Vector2 center, Vector2 radii, float rotation, float startAngle, float sweep, int segments, bool includeStart) {
            segments = Math.Max(segments, 1);
            int first = includeStart ? 0 : 1;
            for (int s = first; s <= segments; s++) {
                float a = startAngle + sweep * s / segments;
                into.Add(EllipsePoint(center, radii, rotation, a));
            }
        }

        /// <summary>椭圆上角度 <paramref name="angle"/> 处的点</summary>
        public static Vector2 EllipsePoint(Vector2 center, Vector2 radii, float rotation, float angle) {
            float x = MathF.Cos(angle) * radii.X;
            float y = MathF.Sin(angle) * radii.Y;
            if (rotation == 0f) {
                return center + new Vector2(x, y);
            }
            float c = MathF.Cos(rotation);
            float s = MathF.Sin(rotation);
            return center + new Vector2(x * c - y * s, x * s + y * c);
        }

        /// <summary>椭圆参数方程对角度的导数（未归一）</summary>
        public static Vector2 EllipseDerivative(Vector2 radii, float rotation, float angle) {
            float x = -MathF.Sin(angle) * radii.X;
            float y = MathF.Cos(angle) * radii.Y;
            if (rotation == 0f) {
                return new Vector2(x, y);
            }
            float c = MathF.Cos(rotation);
            float s = MathF.Sin(rotation);
            return new Vector2(x * c - y * s, x * s + y * c);
        }

        /// <summary>Catmull-Rom 样条一段上的点</summary>
        public static Vector2 CatmullRom(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t) {
            float t2 = t * t;
            float t3 = t2 * t;
            return 0.5f * (2f * p1
                + (-p0 + p2) * t
                + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2
                + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
        }
    }
}
