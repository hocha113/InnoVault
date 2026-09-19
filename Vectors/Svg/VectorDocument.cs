using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Text;
using Terraria;
using Terraria.ModLoader;

namespace InnoVault.Vectors.Svg
{
    /// <summary>
    /// SVG 文档里的一个已解析形状：文档空间的几何 + 已解析好的填充 / 描边样式（可为空）
    /// <br/>描边宽度、虚线长度以文档单位计，绘制时随 <see cref="VectorTransform.MeanScale"/> 缩放（SVG 语义）
    /// </summary>
    public sealed class VectorShape
    {
        /// <summary>文档空间几何</summary>
        public VectorPath Path { get; init; }
        /// <summary>填充样式；<c>fill="none"</c> 时为空</summary>
        public FillStyle Fill { get; init; }
        /// <summary>描边样式；没有 stroke 时为空</summary>
        public StrokeStyle Stroke { get; init; }
        /// <summary>元素 <c>id</c>（可为空）</summary>
        public string Id { get; init; }
        /// <summary>解析时累计的整体不透明度（已乘进颜色，这里只作查询）</summary>
        public float Opacity { get; init; } = 1f;
    }

    /// <summary>
    /// 一份解析好的 SVG 文档：形状列表（文档顺序）+ 视框 + 尺寸
    /// <br/>支持：<c>svg g path rect circle ellipse line polyline polygon defs linearGradient radialGradient stop</c>，
    /// 属性 <c>fill stroke stroke-width stroke-linecap stroke-linejoin stroke-miterlimit stroke-dasharray stroke-dashoffset opacity fill-opacity stroke-opacity fill-rule transform style id color</c>，
    /// 颜色见 <see cref="SvgColors"/>，渐变映射到 <see cref="LinearGradientPaint"/> / <see cref="RadialGradientPaint"/>
    /// <br/>不支持（跳过并记入 <see cref="Warnings"/>）：<c>use text image clipPath mask filter pattern symbol marker</c>、<c>&lt;style&gt;</c> 样式表与 CSS 类
    /// <br/>加载：<c>[VaultLoaden("Assets/Vectors/Foo")] static VectorDocument Doc;</c>（路径可省 <c>.svg</c>）或 <see cref="Load"/>
    /// </summary>
    public sealed class VectorDocument
    {
        /// <summary>空文档</summary>
        public static readonly VectorDocument Empty = new([], Vector2.Zero, Vector2.Zero, Vector2.Zero, []);

        //一步式绘制走私有合批：同一组选项、按加入顺序、顶点过 3/4 预算自动开新网格，大文档不会撞 32767 顶点上限
        private static readonly VectorBatch scratchBatch = new();
        private static readonly FillStyle scratchFill = new();
        private static readonly StrokeStyle scratchStroke = new();
        private static readonly TintedPaint scratchFillPaint = new();
        private static readonly TintedPaint scratchStrokePaint = new();
        private static readonly Dictionary<int, float[]> dashScratch = [];

        /// <summary>形状（文档顺序，先出现的先画）</summary>
        public IReadOnlyList<VectorShape> Shapes { get; }
        /// <summary>视框左上（<c>viewBox</c> 的 min-x / min-y；缺省为 0）</summary>
        public Vector2 ViewBoxMin { get; }
        /// <summary>视框尺寸（缺省取 width / height，再缺省取几何包围盒）</summary>
        public Vector2 ViewBoxSize { get; }
        /// <summary>根元素声明的尺寸（缺省与视框尺寸相同）</summary>
        public Vector2 Size { get; }
        /// <summary>解析时跳过的内容说明，空则一切受支持</summary>
        public IReadOnlyList<string> Warnings { get; }
        /// <summary>几何包围盒左上（全部形状）</summary>
        public Vector2 BoundsMin { get; }
        /// <summary>几何包围盒右下</summary>
        public Vector2 BoundsMax { get; }
        /// <summary>视框中心</summary>
        public Vector2 ViewBoxCenter => ViewBoxMin + ViewBoxSize * 0.5f;
        /// <summary>是否没有任何形状</summary>
        public bool IsEmpty => Shapes.Count == 0;

        internal VectorDocument(List<VectorShape> shapes, Vector2 viewBoxMin, Vector2 viewBoxSize, Vector2 size, List<string> warnings) {
            Shapes = shapes;
            Warnings = warnings;
            Vector2 min = new(float.MaxValue);
            Vector2 max = new(float.MinValue);
            foreach (VectorShape s in shapes) {
                if (s.Path == null || s.Path.IsEmpty) {
                    continue;
                }
                min = Vector2.Min(min, s.Path.BoundsMin);
                max = Vector2.Max(max, s.Path.BoundsMax);
            }
            if (min.X > max.X) {
                min = max = Vector2.Zero;
            }
            BoundsMin = min;
            BoundsMax = max;
            if (viewBoxSize.X <= 0f || viewBoxSize.Y <= 0f) {
                if (size.X > 0f && size.Y > 0f) {
                    viewBoxMin = Vector2.Zero;
                    viewBoxSize = size;
                }
                else {
                    viewBoxMin = min;
                    viewBoxSize = max - min;
                }
            }
            ViewBoxMin = viewBoxMin;
            ViewBoxSize = viewBoxSize;
            Size = size.X > 0f && size.Y > 0f ? size : viewBoxSize;
        }

        #region 解析 / 加载

        /// <summary>解析 SVG 文本；语法错误时记录一次日志并返回 <see cref="Empty"/></summary>
        public static VectorDocument Parse(string svgXml, float relativeTolerance = 1f / 400f) {
            if (TryParse(svgXml, out VectorDocument doc, out string error, relativeTolerance)) {
                return doc;
            }
            VaultMod.LoggerError("VectorDocument.Parse:" + (svgXml?.GetHashCode() ?? 0), $"[Vectors] SVG document parse failed: {error}");
            return Empty;
        }

        /// <summary>解析 SVG 文本</summary>
        /// <param name="svgXml">SVG 文本</param>
        /// <param name="document">解析结果（失败为 <see cref="Empty"/>）</param>
        /// <param name="error">失败原因</param>
        /// <param name="relativeTolerance">曲线展平的相对精度（每个形状相对自身长边），文档会被放大多少倍绘制就缩小多少</param>
        public static bool TryParse(string svgXml, out VectorDocument document, out string error, float relativeTolerance = 1f / 400f) {
            document = Empty;
            error = null;
            if (string.IsNullOrWhiteSpace(svgXml)) {
                error = "empty text";
                return false;
            }
            try {
                document = new SvgDocumentParser(relativeTolerance).Parse(svgXml);
                return true;
            } catch (Exception ex) {
                error = ex.GetType().Name + ": " + ex.Message;
                return false;
            }
        }

        /// <summary>
        /// 从模组文件加载（<paramref name="path"/> 可省 <c>.svg</c>）；文件不存在或解析失败时记录日志并返回 <see cref="Empty"/>
        /// </summary>
        public static VectorDocument Load(Mod mod, string path, float relativeTolerance = 1f / 400f) {
            if (mod == null || string.IsNullOrEmpty(path)) {
                return Empty;
            }
            string file = mod.FileExists(path) ? path : path + ".svg";
            if (!mod.FileExists(file)) {
                VaultMod.LoggerError("VectorDocument.Load:" + path, $"[Vectors] SVG file not found: {mod.Name}/{path}(.svg)");
                return Empty;
            }
            byte[] bytes = mod.GetFileBytes(file);
            if (bytes == null || bytes.Length == 0) {
                VaultMod.LoggerError("VectorDocument.Load:" + path, $"[Vectors] SVG file empty: {mod.Name}/{file}");
                return Empty;
            }
            string text = Encoding.UTF8.GetString(bytes);
            if (!TryParse(text, out VectorDocument doc, out string error, relativeTolerance)) {
                VaultMod.LoggerError("VectorDocument.Load:" + path, $"[Vectors] SVG parse failed for {mod.Name}/{file}: {error}");
                return Empty;
            }
            if (doc.Warnings.Count > 0) {
                VaultMod.Instance?.Logger.Warn($"[Vectors] {mod.Name}/{file}: skipped unsupported SVG content: {string.Join("; ", doc.Warnings)}");
            }
            return doc;
        }

        #endregion

        #region 绘制

        /// <summary>
        /// 把视框中心放到 <paramref name="center"/>、视框高度缩放到 <paramref name="height"/> 的变换（保持比例）
        /// </summary>
        public VectorTransform Fit(Vector2 center, float height, float rotation = 0f) {
            float scale = ViewBoxSize.Y > 1e-6f ? height / ViewBoxSize.Y : 1f;
            Vector2 c = ViewBoxCenter * scale;
            if (rotation != 0f) {
                c = c.RotatedBy(rotation);
            }
            return new VectorTransform(center - c, new Vector2(scale), rotation);
        }

        /// <summary>
        /// 把整份文档追加到网格：每个形状先填充再描边，按文档顺序
        /// </summary>
        /// <param name="mesh">目标网格</param>
        /// <param name="transform">文档空间 → 输出空间；描边宽度随 <see cref="VectorTransform.MeanScale"/> 缩放</param>
        /// <param name="alpha">整体不透明度</param>
        /// <param name="tint">整体乘色（可空）</param>
        public void Draw(VectorMesh mesh, in VectorTransform transform, float alpha = 1f, Color? tint = null) {
            if (mesh == null || !BeginDraw(in transform, alpha, tint, out float widthScale, out bool plain, out Color mul)) {
                return;
            }
            for (int i = 0; i < Shapes.Count; i++) {
                VectorShape shape = Shapes[i];
                if (shape.Path == null || shape.Path.IsEmpty) {
                    continue;
                }
                if (shape.Fill != null) {
                    mesh.AppendFill(shape.Path, ResolveFill(shape.Fill, widthScale, plain, mul), in transform);
                }
                if (shape.Stroke != null) {
                    mesh.AppendStroke(shape.Path, ResolveStroke(shape.Stroke, widthScale, plain, mul), in transform);
                }
            }
        }

        /// <summary>
        /// 一步完成：整份文档按文档顺序提交。内部走私有合批，顶点过 3/4 预算时自动开新网格接着攒，所以大文档不会因 32767 顶点上限被静默截断
        /// （代价是多一次提交）；往自己的网格里画请用 <see cref="Draw(VectorMesh, in VectorTransform, float, Color?)"/> 并自行分片
        /// </summary>
        public void Draw(in VectorTransform transform, in VectorDrawOptions options, float alpha = 1f, Color? tint = null) {
            if (!BeginDraw(in transform, alpha, tint, out float widthScale, out bool plain, out Color mul)) {
                return;
            }
            scratchBatch.Begin();
            for (int i = 0; i < Shapes.Count; i++) {
                VectorShape shape = Shapes[i];
                if (shape.Path == null || shape.Path.IsEmpty) {
                    continue;
                }
                if (shape.Fill != null) {
                    scratchBatch.Fill(shape.Path, ResolveFill(shape.Fill, widthScale, plain, mul), in transform, in options);
                }
                if (shape.Stroke != null) {
                    scratchBatch.Stroke(shape.Path, ResolveStroke(shape.Stroke, widthScale, plain, mul), in transform, in options);
                }
            }
            scratchBatch.End();
        }

        //两种绘制入口共用的前置计算：整体倍率、是否原样直画、乘色
        private bool BeginDraw(in VectorTransform transform, float alpha, Color? tint, out float widthScale, out bool plain, out Color mul) {
            widthScale = transform.MeanScale;
            plain = alpha >= 0.999f && (!tint.HasValue || tint.Value == Color.White);
            mul = tint ?? Color.White;
            if (alpha < 0.999f) {
                mul *= alpha;
            }
            return Shapes.Count > 0 && alpha > 0.001f;
        }

        //形状的填充样式按本次绘制解析：细分阈值随缩放、乘色；不需要改动时直接返回原样式，否则返回暂存样式（下一形状前用完）
        private static FillStyle ResolveFill(FillStyle fill, float widthScale, bool plain, Color mul) {
            bool scaleEdge = fill.MaxTriangleEdge > 0f && MathF.Abs(widthScale - 1f) > 1e-4f;
            if (plain && !scaleEdge) {
                return fill;
            }
            scratchFill.CopyFrom(fill);
            if (scaleEdge) {
                scratchFill.MaxTriangleEdge = fill.MaxTriangleEdge * widthScale;
            }
            if (!plain) {
                Tint(scratchFill, mul);
            }
            return scratchFill;
        }

        //形状的描边样式按本次绘制解析：宽度 / 虚线 / 端帽长随缩放（SVG 语义）、乘色
        private static StrokeStyle ResolveStroke(StrokeStyle stroke, float widthScale, bool plain, Color mul) {
            if (plain && MathF.Abs(widthScale - 1f) <= 1e-4f) {
                return stroke;
            }
            scratchStroke.CopyFrom(stroke);
            scratchStroke.Width = stroke.Width * widthScale;
            scratchStroke.DashOffset = stroke.DashOffset * widthScale;
            scratchStroke.CapLength = stroke.CapLength * widthScale;
            if (stroke.Dash != null && stroke.Dash.Length > 0) {
                //Dash 数组按引用共享且长度即模式长度：按长度取一份暂存数组填缩放值
                if (!dashScratch.TryGetValue(stroke.Dash.Length, out float[] scaled)) {
                    scaled = new float[stroke.Dash.Length];
                    dashScratch[stroke.Dash.Length] = scaled;
                }
                for (int k = 0; k < stroke.Dash.Length; k++) {
                    scaled[k] = stroke.Dash[k] * widthScale;
                }
                scratchStroke.Dash = scaled;
            }
            if (!plain) {
                Tint(scratchStroke, mul);
            }
            return scratchStroke;
        }

        private static void Tint(FillStyle style, Color mul) {
            if (style.Paint != null) {
                scratchFillPaint.Inner = style.Paint;
                scratchFillPaint.Multiplier = mul;
                scratchFillPaint.Space = style.Paint.Space;
                style.Paint = scratchFillPaint;
            }
            else {
                style.Color = style.Color.MultiplyRGBA(mul);
            }
        }

        private static void Tint(StrokeStyle style, Color mul) {
            if (style.Paint != null) {
                scratchStrokePaint.Inner = style.Paint;
                scratchStrokePaint.Multiplier = mul;
                scratchStrokePaint.Space = style.Paint.Space;
                style.Paint = scratchStrokePaint;
            }
            else {
                style.Color = style.Color.MultiplyRGBA(mul);
            }
        }

        //把内层画笔的结果按分量乘上一个颜色（整体透明度 / 乘色）
        private sealed class TintedPaint : VectorPaint
        {
            public VectorPaint Inner;
            public Color Multiplier = Color.White;

            public override Color Evaluate(Vector2 position) => Inner == null ? Multiplier : Inner.Evaluate(position).MultiplyRGBA(Multiplier);
        }

        #endregion
    }
}
