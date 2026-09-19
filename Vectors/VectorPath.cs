using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;

namespace InnoVault.Vectors
{
    /// <summary>
    /// 一条已展平的子路径：折线点列 + 累计弧长表 + 是否闭合
    /// <br/>闭合子路径不重复首点，回合段（末点 → 首点）隐含存在，<see cref="Arcs"/> 因此比 <see cref="Points"/> 多一项
    /// </summary>
    public sealed class VectorSubPath
    {
        /// <summary>折线顶点（路径空间）</summary>
        public Vector2[] Points { get; }
        /// <summary><c>Arcs[i]</c> 为走到第 i 个点的累计弧长；闭合时最后一项为含回合段的总长</summary>
        public float[] Arcs { get; }
        /// <summary>是否闭合</summary>
        public bool Closed { get; }
        /// <summary>子路径总弧长</summary>
        public float Length => Arcs[^1];
        /// <summary>线段数：开放为点数减一，闭合等于点数</summary>
        public int SegmentCount => Points.Length == 0 ? 0 : (Closed ? Points.Length : Points.Length - 1);

        /// <summary>由顶点构造，自动计算弧长表；不足 3 点的闭合请求会退回开放</summary>
        public VectorSubPath(Vector2[] points, bool closed) {
            points ??= [];
            Points = points;
            int n = points.Length;
            Closed = closed && n >= 3;
            Arcs = new float[Closed ? n + 1 : Math.Max(n, 1)];
            for (int i = 1; i < n; i++) {
                Arcs[i] = Arcs[i - 1] + Vector2.Distance(points[i - 1], points[i]);
            }
            if (Closed) {
                Arcs[n] = Arcs[n - 1] + Vector2.Distance(points[n - 1], points[0]);
            }
        }

        /// <summary>第 <paramref name="index"/> 段的起点</summary>
        public Vector2 SegmentStart(int index) => Points[index];

        /// <summary>第 <paramref name="index"/> 段的终点（闭合时最后一段回到首点）</summary>
        public Vector2 SegmentEnd(int index) => Points[(index + 1) % Points.Length];

        /// <summary>
        /// 按绝对弧长定位到某一段；<paramref name="distance"/> 越界时夹到两端
        /// </summary>
        /// <returns>点数不足两点时返回 false</returns>
        public bool TryLocate(float distance, out int segment, out float segmentT) {
            segment = 0;
            segmentT = 0f;
            int segCount = SegmentCount;
            if (segCount <= 0) {
                return false;
            }
            float total = Length;
            if (total <= 0f) {
                return true;
            }
            distance = MathHelper.Clamp(distance, 0f, total);
            int idx = Array.BinarySearch(Arcs, distance);
            if (idx < 0) {
                idx = ~idx - 1;
            }
            idx = Math.Clamp(idx, 0, segCount - 1);
            float segLen = Arcs[idx + 1] - Arcs[idx];
            segment = idx;
            segmentT = segLen > 0f ? MathHelper.Clamp((distance - Arcs[idx]) / segLen, 0f, 1f) : 0f;
            return true;
        }

        /// <summary>归一弧长 <paramref name="t"/>（0~1）处的点</summary>
        public Vector2 PointAt(float t) {
            if (Points.Length == 0) {
                return Vector2.Zero;
            }
            if (!TryLocate(t * Length, out int seg, out float st)) {
                return Points[0];
            }
            return Vector2.Lerp(SegmentStart(seg), SegmentEnd(seg), st);
        }

        /// <summary>归一弧长 <paramref name="t"/> 处的单位切向；退化时返回 <see cref="Vector2.UnitX"/></summary>
        public Vector2 TangentAt(float t) {
            if (!TryLocate(t * Length, out int seg, out _)) {
                return Vector2.UnitX;
            }
            Vector2 d = SegmentEnd(seg) - SegmentStart(seg);
            return d.LengthSquared() > 1e-12f ? Vector2.Normalize(d) : Vector2.UnitX;
        }
    }

    /// <summary>
    /// 一条不可变的矢量路径：若干已展平的子路径 + 全局弧长表 + 包围盒
    /// <br/>构造方式：<see cref="Begin"/> 流式构造、<see cref="FromSvg(string)"/> 解析 SVG <c>d</c> 串、<see cref="FromPoints(ReadOnlySpan{Vector2}, bool)"/>，或本类的基础形状静态方法
    /// <br/>路径本身不含颜色与宽度，绘制时由 <see cref="StrokeStyle"/> / <see cref="FillStyle"/> 与 <see cref="VectorTransform"/> 决定
    /// </summary>
    public sealed class VectorPath
    {
        /// <summary>空路径</summary>
        public static readonly VectorPath Empty = new(Array.Empty<VectorSubPath>());

        private readonly VectorSubPath[] subPaths;
        //每条子路径起点的全局累计弧长
        private readonly float[] subPathStarts;

        /// <summary>全部子路径</summary>
        public IReadOnlyList<VectorSubPath> SubPaths => subPaths;
        /// <summary>子路径数</summary>
        public int SubPathCount => subPaths.Length;
        /// <summary>全部子路径的弧长之和（子路径之间的跳转不计）</summary>
        public float TotalLength { get; }
        /// <summary>顶点总数</summary>
        public int PointCount { get; }
        /// <summary>包围盒左上</summary>
        public Vector2 BoundsMin { get; }
        /// <summary>包围盒右下</summary>
        public Vector2 BoundsMax { get; }
        /// <summary>包围盒中心</summary>
        public Vector2 BoundsCenter => (BoundsMin + BoundsMax) * 0.5f;
        /// <summary>包围盒尺寸</summary>
        public Vector2 BoundsSize => BoundsMax - BoundsMin;
        /// <summary>是否没有任何顶点</summary>
        public bool IsEmpty => subPaths.Length == 0;

        /// <summary>由子路径组装；空引用与无点的子路径会被剔除</summary>
        public VectorPath(IEnumerable<VectorSubPath> parts) {
            List<VectorSubPath> list = [];
            if (parts != null) {
                foreach (VectorSubPath sp in parts) {
                    if (sp != null && sp.Points.Length > 0) {
                        list.Add(sp);
                    }
                }
            }
            subPaths = [.. list];
            subPathStarts = new float[subPaths.Length];
            float total = 0f;
            int count = 0;
            Vector2 min = new(float.MaxValue);
            Vector2 max = new(float.MinValue);
            for (int i = 0; i < subPaths.Length; i++) {
                subPathStarts[i] = total;
                VectorSubPath sp = subPaths[i];
                total += sp.Length;
                count += sp.Points.Length;
                Vector2[] pts = sp.Points;
                for (int k = 0; k < pts.Length; k++) {
                    min = Vector2.Min(min, pts[k]);
                    max = Vector2.Max(max, pts[k]);
                }
            }
            TotalLength = total;
            PointCount = count;
            if (count == 0) {
                min = max = Vector2.Zero;
            }
            BoundsMin = min;
            BoundsMax = max;
        }

        /// <summary>由子路径组装</summary>
        public VectorPath(params VectorSubPath[] parts) : this((IEnumerable<VectorSubPath>)parts) { }

        #region 填充网格缓存

        //按填充规则缓存的三角剖分（路径空间顶点 + 索引三元组）；三角剖分对仿射变换不变，跨帧复用
        private Vector2[] fillVertsNonZero;
        private int[] fillTrisNonZero;
        private Vector2[] fillVertsEvenOdd;
        private int[] fillTrisEvenOdd;
        private bool fillBuiltNonZero;
        private bool fillBuiltEvenOdd;

        /// <summary>
        /// 取本路径按 <paramref name="rule"/> 填充时的三角网格（路径空间）：首次调用时构建并缓存，之后直接返回
        /// <br/>顶点数组可能含桥接产生的重复点，索引三元组指向它
        /// </summary>
        /// <returns>没有可填充区域时返回 false</returns>
        public bool TryGetFillMesh(FillRule rule, out Vector2[] vertices, out int[] triangles) {
            if (rule == FillRule.EvenOdd) {
                if (!fillBuiltEvenOdd) {
                    Tessellation.FillTessellator.BuildFillMesh(this, rule, out fillVertsEvenOdd, out fillTrisEvenOdd);
                    fillBuiltEvenOdd = true;
                }
                vertices = fillVertsEvenOdd;
                triangles = fillTrisEvenOdd;
            }
            else {
                if (!fillBuiltNonZero) {
                    Tessellation.FillTessellator.BuildFillMesh(this, rule, out fillVertsNonZero, out fillTrisNonZero);
                    fillBuiltNonZero = true;
                }
                vertices = fillVertsNonZero;
                triangles = fillTrisNonZero;
            }
            return triangles != null && triangles.Length >= 3;
        }

        #endregion

        /// <summary>开始流式构造一条路径</summary>
        public static VectorPathBuilder Begin() => new();

        /// <summary>第 <paramref name="index"/> 条子路径起点的全局归一弧长</summary>
        public float SubPathStart(int index) => TotalLength > 0f ? subPathStarts[index] / TotalLength : 0f;

        /// <summary>第 <paramref name="index"/> 条子路径终点的全局归一弧长</summary>
        public float SubPathEnd(int index) => TotalLength > 0f ? (subPathStarts[index] + subPaths[index].Length) / TotalLength : 0f;

        /// <summary>
        /// 按全局归一弧长 <paramref name="t"/> 定位到具体的子路径与线段；单点子路径不占弧长，落在其上时退回前一条有线段的子路径末端
        /// </summary>
        /// <returns>找不到任何有线段的子路径时返回 false（此时 <paramref name="subPathIndex"/> 仍指向定位到的单点子路径）</returns>
        public bool TryLocate(float t, out int subPathIndex, out int segmentIndex, out float segmentT) {
            subPathIndex = 0;
            segmentIndex = 0;
            segmentT = 0f;
            if (subPaths.Length == 0) {
                return false;
            }
            float distance = MathHelper.Clamp(t, 0f, 1f) * TotalLength;
            //找到覆盖该弧长的子路径：起点 ≤ distance 的最后一条
            int idx = Array.BinarySearch(subPathStarts, distance);
            if (idx < 0) {
                idx = ~idx - 1;
            }
            idx = Math.Clamp(idx, 0, subPaths.Length - 1);
            //长度为零的子路径（单点）不占弧长，越过它们
            while (idx < subPaths.Length - 1 && distance >= subPathStarts[idx] + subPaths[idx].Length && subPaths[idx].Length <= 0f) {
                idx++;
            }
            subPathIndex = idx;
            if (subPaths[idx].TryLocate(distance - subPathStarts[idx], out segmentIndex, out segmentT)) {
                return true;
            }
            //仍落在单点子路径上（例如末尾的尾随 M）：按弧长语义退回它前面最近一条有线段的子路径的末端；
            //整条路径都是单点时保留该子路径索引并返回 false，调用方取该点本身
            for (int back = idx - 1; back >= 0; back--) {
                if (subPaths[back].SegmentCount > 0) {
                    subPathIndex = back;
                    return subPaths[back].TryLocate(subPaths[back].Length, out segmentIndex, out segmentT);
                }
            }
            return false;
        }

        /// <summary>全局归一弧长 <paramref name="t"/> 处的点；路径只由单点子路径构成时返回定位到的那个点</summary>
        public Vector2 PointAt(float t) {
            if (!TryLocate(t, out int sp, out int seg, out float st)) {
                return subPaths.Length > 0 ? subPaths[sp].Points[0] : Vector2.Zero;
            }
            VectorSubPath sub = subPaths[sp];
            if (sub.SegmentCount == 0) {
                return sub.Points[0];
            }
            return Vector2.Lerp(sub.SegmentStart(seg), sub.SegmentEnd(seg), st);
        }

        /// <summary>全局归一弧长 <paramref name="t"/> 处的单位切向</summary>
        public Vector2 TangentAt(float t) {
            if (!TryLocate(t, out int sp, out int seg, out _)) {
                return Vector2.UnitX;
            }
            VectorSubPath sub = subPaths[sp];
            if (sub.SegmentCount == 0) {
                return Vector2.UnitX;
            }
            Vector2 d = sub.SegmentEnd(seg) - sub.SegmentStart(seg);
            return d.LengthSquared() > 1e-12f ? Vector2.Normalize(d) : Vector2.UnitX;
        }

        /// <summary>
        /// 返回按 <paramref name="transform"/> 变换后的新路径。展平精度不会随之提高，大幅放大请改在绘制时传变换
        /// </summary>
        public VectorPath Transform(in VectorTransform transform) {
            if (transform.IsIdentity) {
                return this;
            }
            VectorSubPath[] parts = new VectorSubPath[subPaths.Length];
            for (int i = 0; i < subPaths.Length; i++) {
                Vector2[] src = subPaths[i].Points;
                Vector2[] dst = new Vector2[src.Length];
                for (int k = 0; k < src.Length; k++) {
                    dst[k] = transform.Apply(src[k]);
                }
                parts[i] = new VectorSubPath(dst, subPaths[i].Closed);
            }
            return new VectorPath(parts);
        }

        /// <summary>返回按矩阵变换后的新路径（只取 XY）</summary>
        public VectorPath Transform(in Matrix matrix) {
            VectorSubPath[] parts = new VectorSubPath[subPaths.Length];
            for (int i = 0; i < subPaths.Length; i++) {
                Vector2[] src = subPaths[i].Points;
                Vector2[] dst = new Vector2[src.Length];
                for (int k = 0; k < src.Length; k++) {
                    dst[k] = Vector2.Transform(src[k], matrix);
                }
                parts[i] = new VectorSubPath(dst, subPaths[i].Closed);
            }
            return new VectorPath(parts);
        }

        /// <summary>把若干路径的子路径按顺序合成一条路径（不改几何）；配合 <see cref="FillRule"/> 可以做带孔填充，例如外圆 + 反向内圆</summary>
        public static VectorPath Combine(params VectorPath[] paths) {
            if (paths == null || paths.Length == 0) {
                return Empty;
            }
            List<VectorSubPath> parts = [];
            foreach (VectorPath p in paths) {
                if (p != null) {
                    parts.AddRange(p.subPaths);
                }
            }
            return new VectorPath(parts);
        }

        /// <summary>返回方向反转的新路径（每条子路径的点序倒置，子路径顺序也倒置）</summary>
        public VectorPath Reverse() {
            VectorSubPath[] parts = new VectorSubPath[subPaths.Length];
            for (int i = 0; i < subPaths.Length; i++) {
                VectorSubPath sp = subPaths[subPaths.Length - 1 - i];
                Vector2[] dst = (Vector2[])sp.Points.Clone();
                Array.Reverse(dst);
                parts[i] = new VectorSubPath(dst, sp.Closed);
            }
            return new VectorPath(parts);
        }

        #region SVG

        /// <summary><see cref="FromSvg(string, float)"/> 缓存的条目上限；超过即整体清空（说明有人在每帧拼新的 d 串，那种用法应改走 <see cref="TryFromSvg(string, out VectorPath, out string)"/> 或 <see cref="VectorPathBuilder"/>）</summary>
        public const int SvgCacheLimit = 512;

        private static readonly Dictionary<(string, float), VectorPath> svgCache = new();

        /// <summary>
        /// 解析 SVG <c>d</c> 串（<c>M L H V C S Q T A Z</c> 及其相对形式），同一字符串只解析一次并缓存（默认展平精度）
        /// <br/>串有语法错误时记录一次日志并返回错误位置之前已解析出的部分，严格用法请改用 <see cref="TryFromSvg(string, out VectorPath, out string)"/>
        /// </summary>
        public static VectorPath FromSvg(string d) => FromSvg(d, VectorFlatten.RelativeTolerance);

        /// <summary>
        /// 解析 SVG <c>d</c> 串并按指定相对精度展平，缓存键含精度
        /// </summary>
        /// <param name="d">路径数据</param>
        /// <param name="relativeTolerance">
        /// 曲线展平容差相对图形长边的比例（默认 1/400）。路径会被放大多少倍绘制，就把它缩小多少：
        /// 目标是「容差 × 绘制倍率 ≤ 0.25 像素」，例如 [-1,1] 字形放到 400 像素 → 长边 2 × 200 倍 → 取 1/1600
        /// </param>
        public static VectorPath FromSvg(string d, float relativeTolerance) {
            if (string.IsNullOrWhiteSpace(d)) {
                return Empty;
            }
            (string, float) key = (d, relativeTolerance);
            lock (svgCache) {
                if (svgCache.TryGetValue(key, out VectorPath cached)) {
                    return cached;
                }
            }
            VectorPathBuilder builder = new();
            if (relativeTolerance > 0f) {
                builder.RelativeTolerance = relativeTolerance;
            }
            if (!SvgPathParser.TryParse(d, builder, out string error)) {
                VaultMod.LoggerError("VectorPath.FromSvg:" + d.GetHashCode(), $"[Vectors] SVG path parse failed: {error}\n  d = \"{d}\"");
            }
            VectorPath path = builder.Build();
            lock (svgCache) {
                if (svgCache.Count >= SvgCacheLimit) {
                    svgCache.Clear();
                    VaultMod.LoggerError("VectorPath.FromSvg:cache", $"[Vectors] FromSvg cache exceeded {SvgCacheLimit} entries and was cleared; building d strings per frame defeats the cache, use VectorPathBuilder or TryFromSvg instead");
                }
                svgCache[key] = path;
            }
            return path;
        }

        /// <summary>严格解析 SVG <c>d</c> 串，不走缓存；失败时 <paramref name="path"/> 为错误位置之前的部分结果</summary>
        public static bool TryFromSvg(string d, out VectorPath path, out string error) => TryFromSvg(d, VectorFlatten.RelativeTolerance, out path, out error);

        /// <summary>严格解析 SVG <c>d</c> 串并按指定相对精度展平，不走缓存</summary>
        public static bool TryFromSvg(string d, float relativeTolerance, out VectorPath path, out string error) {
            VectorPathBuilder builder = new();
            if (relativeTolerance > 0f) {
                builder.RelativeTolerance = relativeTolerance;
            }
            bool ok = SvgPathParser.TryParse(d ?? string.Empty, builder, out error);
            path = builder.Build();
            return ok;
        }

        /// <summary>清空 <see cref="FromSvg(string, float)"/> 的缓存（模组卸载时由框架调用）</summary>
        public static void ClearSvgCache() {
            lock (svgCache) {
                svgCache.Clear();
            }
        }

        #endregion

        #region 基础形状

        /// <summary>由点列直接构成一条子路径（不做任何平滑）</summary>
        public static VectorPath FromPoints(ReadOnlySpan<Vector2> points, bool closed = false) {
            if (points.Length == 0) {
                return Empty;
            }
            return new VectorPath(new VectorSubPath(points.ToArray(), closed));
        }

        /// <summary>由点列直接构成一条子路径（不做任何平滑）</summary>
        public static VectorPath FromPoints(IReadOnlyList<Vector2> points, bool closed = false) {
            if (points == null || points.Count == 0) {
                return Empty;
            }
            Vector2[] arr = new Vector2[points.Count];
            for (int i = 0; i < arr.Length; i++) {
                arr[i] = points[i];
            }
            return new VectorPath(new VectorSubPath(arr, closed));
        }

        /// <summary>两点线段</summary>
        public static VectorPath Line(Vector2 a, Vector2 b) => new(new VectorSubPath([a, b], false));

        /// <summary>开放折线</summary>
        public static VectorPath Polyline(ReadOnlySpan<Vector2> points) => FromPoints(points, false);

        /// <summary>闭合多边形</summary>
        public static VectorPath Polygon(ReadOnlySpan<Vector2> points) => FromPoints(points, true);

        /// <summary>矩形（左上角 + 尺寸），闭合，顺时针</summary>
        public static VectorPath Rect(Vector2 position, Vector2 size) {
            Vector2 br = position + size;
            return new VectorPath(new VectorSubPath([position, new Vector2(br.X, position.Y), br, new Vector2(position.X, br.Y)], true));
        }

        /// <summary>矩形，闭合，顺时针</summary>
        public static VectorPath Rect(Rectangle rect) => Rect(new Vector2(rect.X, rect.Y), new Vector2(rect.Width, rect.Height));

        /// <summary>
        /// 圆角矩形（左上角 + 尺寸），闭合。<paramref name="radius"/> 会被夹到短边的一半；<paramref name="segmentsPerCorner"/> ≤ 0 时按精度自动
        /// </summary>
        public static VectorPath RoundedRect(Vector2 position, Vector2 size, float radius, int segmentsPerCorner = 0) {
            radius = MathHelper.Clamp(radius, 0f, MathF.Min(size.X, size.Y) * 0.5f);
            if (radius <= 0.001f) {
                return Rect(position, size);
            }
            int seg = segmentsPerCorner > 0 ? segmentsPerCorner : VectorFlatten.ArcSegments(MathHelper.PiOver2);
            List<Vector2> pts = new(seg * 4 + 4);
            Vector2 br = position + size;
            //四角圆心，按左上 → 右上 → 右下 → 左下（顺时针）排列，每角扫过 90°
            VectorFlatten.AppendArc(pts, new Vector2(position.X + radius, position.Y + radius), new Vector2(radius), 0f, MathHelper.Pi, MathHelper.PiOver2, seg, true);
            VectorFlatten.AppendArc(pts, new Vector2(br.X - radius, position.Y + radius), new Vector2(radius), 0f, MathHelper.Pi * 1.5f, MathHelper.PiOver2, seg, true);
            VectorFlatten.AppendArc(pts, new Vector2(br.X - radius, br.Y - radius), new Vector2(radius), 0f, 0f, MathHelper.PiOver2, seg, true);
            VectorFlatten.AppendArc(pts, new Vector2(position.X + radius, br.Y - radius), new Vector2(radius), 0f, MathHelper.PiOver2, MathHelper.PiOver2, seg, true);
            return new VectorPath(new VectorSubPath([.. pts], true));
        }

        /// <summary>圆，闭合；<paramref name="segments"/> ≤ 0 时按精度自动</summary>
        public static VectorPath Circle(Vector2 center, float radius, int segments = 0) => Ellipse(center, new Vector2(radius), segments);

        /// <summary>椭圆，闭合；<paramref name="rotation"/> 为长短轴的旋转</summary>
        public static VectorPath Ellipse(Vector2 center, Vector2 radii, int segments = 0, float rotation = 0f) {
            int n = segments > 0 ? segments : VectorFlatten.ArcSegments(MathHelper.TwoPi);
            n = Math.Max(n, 3);
            Vector2[] pts = new Vector2[n];
            float c = MathF.Cos(rotation);
            float s = MathF.Sin(rotation);
            for (int i = 0; i < n; i++) {
                float a = MathHelper.TwoPi * i / n;
                Vector2 p = new(MathF.Cos(a) * radii.X, MathF.Sin(a) * radii.Y);
                pts[i] = center + new Vector2(p.X * c - p.Y * s, p.X * s + p.Y * c);
            }
            return new VectorPath(new VectorSubPath(pts, true));
        }

        /// <summary>圆弧（开放），从 <paramref name="startAngle"/> 扫到 <paramref name="endAngle"/>，角度为弧度、顺屏幕坐标系</summary>
        public static VectorPath Arc(Vector2 center, float radius, float startAngle, float endAngle, int segments = 0) {
            float sweep = endAngle - startAngle;
            if (MathF.Abs(sweep) < 1e-5f) {
                return Empty;
            }
            int n = segments > 0 ? segments : VectorFlatten.ArcSegments(sweep);
            List<Vector2> pts = new(n + 1);
            VectorFlatten.AppendArc(pts, center, new Vector2(radius), 0f, startAngle, sweep, n, true);
            return new VectorPath(new VectorSubPath([.. pts], false));
        }

        /// <summary>
        /// 环形扇区（闭合多边形）：外弧 + 内弧；<paramref name="innerRadius"/> ≤ 0 时退化为饼形。可直接填充，也可描边成轮廊
        /// <br/>扫过整圈（|sweep| ≥ 2π）时返回两条闭合子路径（外环 + 反向内环），填充按 <see cref="FillRule"/> 自动挖孔，描边为两个同心圆
        /// </summary>
        public static VectorPath Sector(Vector2 center, float innerRadius, float outerRadius, float startAngle, float endAngle, int segments = 0) {
            float sweep = endAngle - startAngle;
            if (MathF.Abs(sweep) < 1e-5f || outerRadius <= 0f) {
                return Empty;
            }
            if (MathF.Abs(sweep) >= MathHelper.TwoPi - 1e-4f) {
                int ring = segments > 0 ? segments : VectorFlatten.ArcSegments(MathHelper.TwoPi);
                VectorPath outer = Circle(center, outerRadius, ring);
                if (innerRadius <= 0f) {
                    return outer;
                }
                return Combine(outer, Circle(center, innerRadius, ring).Reverse());
            }
            int n = segments > 0 ? segments : VectorFlatten.ArcSegments(sweep);
            List<Vector2> pts = new(n * 2 + 3);
            VectorFlatten.AppendArc(pts, center, new Vector2(outerRadius), 0f, startAngle, sweep, n, true);
            if (innerRadius > 0f) {
                VectorFlatten.AppendArc(pts, center, new Vector2(innerRadius), 0f, endAngle, -sweep, n, true);
            }
            else {
                pts.Add(center);
            }
            return new VectorPath(new VectorSubPath([.. pts], true));
        }

        /// <summary>星形（闭合），<paramref name="points"/> 个尖角交替外径与内径；默认第一个尖角朝上</summary>
        public static VectorPath Star(Vector2 center, float outerRadius, float innerRadius, int points, float rotation = 0f) {
            points = Math.Max(points, 2);
            Vector2[] pts = new Vector2[points * 2];
            float start = rotation - MathHelper.PiOver2;
            for (int i = 0; i < pts.Length; i++) {
                float a = start + MathHelper.Pi * i / points;
                float r = (i & 1) == 0 ? outerRadius : innerRadius;
                pts[i] = center + new Vector2(MathF.Cos(a), MathF.Sin(a)) * r;
            }
            return new VectorPath(new VectorSubPath(pts, true));
        }

        /// <summary>正多边形（闭合），默认第一个顶点朝上</summary>
        public static VectorPath RegularPolygon(Vector2 center, float radius, int sides, float rotation = 0f) {
            sides = Math.Max(sides, 3);
            Vector2[] pts = new Vector2[sides];
            float start = rotation - MathHelper.PiOver2;
            for (int i = 0; i < sides; i++) {
                float a = start + MathHelper.TwoPi * i / sides;
                pts[i] = center + new Vector2(MathF.Cos(a), MathF.Sin(a)) * radius;
            }
            return new VectorPath(new VectorSubPath(pts, true));
        }

        /// <summary>二次贝塞尔（开放）；<paramref name="segments"/> ≤ 0 时按平坦度自动</summary>
        public static VectorPath Quadratic(Vector2 p0, Vector2 control, Vector2 p1, int segments = 0) {
            int n = segments > 0 ? segments : VectorFlatten.QuadraticSegments(p0, control, p1, VectorFlatten.RelativeTolerance * VectorFlatten.Extent(p0, control, p1, p1));
            List<Vector2> pts = new(n + 1) { p0 };
            VectorFlatten.AppendQuadratic(pts, p0, control, p1, n);
            return new VectorPath(new VectorSubPath([.. pts], false));
        }

        /// <summary>三次贝塞尔（开放）；<paramref name="segments"/> ≤ 0 时按平坦度自动</summary>
        public static VectorPath Cubic(Vector2 p0, Vector2 c1, Vector2 c2, Vector2 p1, int segments = 0) {
            int n = segments > 0 ? segments : VectorFlatten.CubicSegments(p0, c1, c2, p1, VectorFlatten.RelativeTolerance * VectorFlatten.Extent(p0, c1, c2, p1));
            List<Vector2> pts = new(n + 1) { p0 };
            VectorFlatten.AppendCubic(pts, p0, c1, c2, p1, n);
            return new VectorPath(new VectorSubPath([.. pts], false));
        }

        /// <summary>
        /// 过所有控制点的 Catmull-Rom 样条，每两点之间插 <paramref name="subdivisions"/> 个点；端点复用自身作虚拟控制点，闭合时首尾相接
        /// </summary>
        public static VectorPath CatmullRom(ReadOnlySpan<Vector2> points, int subdivisions = 4, bool closed = false) {
            int n = points.Length;
            if (n == 0) {
                return Empty;
            }
            if (n < 3 || subdivisions <= 0) {
                return FromPoints(points, closed);
            }
            List<Vector2> pts = new(n * (subdivisions + 1) + 1);
            int segs = closed ? n : n - 1;
            for (int i = 0; i < segs; i++) {
                Vector2 p0 = closed ? points[(i - 1 + n) % n] : points[Math.Max(i - 1, 0)];
                Vector2 p1 = points[i];
                Vector2 p2 = points[(i + 1) % n];
                Vector2 p3 = closed ? points[(i + 2) % n] : points[Math.Min(i + 2, n - 1)];
                pts.Add(p1);
                for (int s = 1; s <= subdivisions; s++) {
                    float t = s / (float)(subdivisions + 1);
                    pts.Add(VectorFlatten.CatmullRom(p0, p1, p2, p3, t));
                }
            }
            if (!closed) {
                pts.Add(points[n - 1]);
            }
            return new VectorPath(new VectorSubPath([.. pts], closed));
        }

        #endregion
    }
}
