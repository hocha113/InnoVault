using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Xml;
using System.Xml.Linq;

namespace InnoVault.Vectors.Svg
{
    /// <summary>
    /// SVG 文档解析器：元素树 → <see cref="VectorShape"/> 列表。几何在各元素的用户空间展平后再乘以累计变换（相对精度不受缩放影响），
    /// 样式按 SVG 继承规则合并，渐变解析为 <see cref="GradientPaint"/>（用前置矩阵把文档空间点换回渐变定义空间）
    /// </summary>
    internal sealed class SvgDocumentParser
    {
        private static readonly XNamespace xlink = "http://www.w3.org/1999/xlink";
        private static readonly char[] listSeparators = [' ', ',', '\t', '\n', '\r'];

        private readonly float tolerance;
        private readonly List<VectorShape> shapes = [];
        private readonly List<string> warnings = [];
        private readonly HashSet<string> warned = new(StringComparer.Ordinal);
        private readonly Dictionary<string, XElement> gradients = new(StringComparer.Ordinal);
        private Vector2 viewBoxSize = new(100f, 100f);

        //可继承的呈现属性（opacity 按 SVG 不继承，这里以累乘近似组不透明度）
        private sealed class Style
        {
            public string Fill = "black";
            public string Stroke = "none";
            public float StrokeWidth = 1f;
            public LineCap Cap = LineCap.Butt;
            public LineJoin Join = LineJoin.Miter;
            public float MiterLimit = 4f;
            public float[] Dash;
            public float DashOffset;
            public float Opacity = 1f;
            public float FillOpacity = 1f;
            public float StrokeOpacity = 1f;
            public FillRule Rule = FillRule.NonZero;
            public Color CurrentColor = Color.Black;
            public bool Hidden;

            public Style Clone() => (Style)MemberwiseClone();
        }

        public SvgDocumentParser(float relativeTolerance) {
            tolerance = relativeTolerance > 0f ? relativeTolerance : 1f / 400f;
        }

        public VectorDocument Parse(string xml) {
            XmlReaderSettings settings = new() {
                DtdProcessing = DtdProcessing.Ignore,
                IgnoreComments = true,
                IgnoreProcessingInstructions = true,
                IgnoreWhitespace = true,
                XmlResolver = null,
            };
            XDocument doc;
            using (StringReader sr = new(xml))
            using (XmlReader reader = XmlReader.Create(sr, settings)) {
                doc = XDocument.Load(reader, LoadOptions.None);
            }
            XElement root = doc.Root;
            if (root == null || root.Name.LocalName != "svg") {
                throw new FormatException("root element is not <svg>");
            }
            foreach (XElement e in root.Descendants()) {
                string ln = e.Name.LocalName;
                if (ln == "linearGradient" || ln == "radialGradient") {
                    string id = (string)e.Attribute("id");
                    if (!string.IsNullOrEmpty(id)) {
                        gradients[id] = e;
                    }
                }
            }
            ParseViewBox(root, out Vector2 vbMin, out Vector2 vbSize);
            Vector2 size = new(Length(Attr(root, "width"), 0f), Length(Attr(root, "height"), 0f));
            if (vbSize.X > 0f && vbSize.Y > 0f) {
                viewBoxSize = vbSize;
            }
            else if (size.X > 0f && size.Y > 0f) {
                viewBoxSize = size;
            }
            Style style = ApplyStyle(new Style(), root);
            Matrix m = ParseTransform(Attr(root, "transform"));
            foreach (XElement child in root.Elements()) {
                Walk(child, m, style);
            }
            return new VectorDocument(shapes, vbMin, vbSize, size, warnings);
        }

        //==================== 遍历 ====================

        private void Walk(XElement e, in Matrix parent, Style inherited) {
            string name = e.Name.LocalName;
            switch (name) {
                case "defs":
                case "linearGradient":
                case "radialGradient":
                case "stop":
                case "title":
                case "desc":
                case "metadata":
                    return;
                case "g":
                case "svg":
                case "a":
                case "switch": {
                    Style st = ApplyStyle(inherited.Clone(), e);
                    if (st.Hidden) {
                        return;
                    }
                    Matrix m = ParseTransform(Attr(e, "transform")) * parent;
                    foreach (XElement child in e.Elements()) {
                        Walk(child, in m, st);
                    }
                    return;
                }
                case "path":
                case "rect":
                case "circle":
                case "ellipse":
                case "line":
                case "polyline":
                case "polygon":
                    Shape(e, name, in parent, inherited);
                    return;
                case "style":
                    Warn("<style> stylesheet ignored (use presentation attributes or inline style=)");
                    return;
                default:
                    Warn($"<{name}> unsupported");
                    return;
            }
        }

        private void Shape(XElement e, string name, in Matrix parent, Style inherited) {
            Style st = ApplyStyle(inherited.Clone(), e);
            if (st.Hidden) {
                return;
            }
            Matrix total = ParseTransform(Attr(e, "transform")) * parent;
            VectorPathBuilder b = new() { RelativeTolerance = tolerance };
            bool fillable = true;
            switch (name) {
                case "path": {
                    string d = Attr(e, "d");
                    if (string.IsNullOrWhiteSpace(d)) {
                        return;
                    }
                    if (!SvgPathParser.TryParse(d, b, out string error)) {
                        Warn($"path d error: {error}");
                    }
                    break;
                }
                case "rect":
                    BuildRect(b, e);
                    break;
                case "circle": {
                    float r = Length(Attr(e, "r"), 0f);
                    if (r <= 0f) {
                        return;
                    }
                    b.Arc(new Vector2(Length(Attr(e, "cx"), 0f), Length(Attr(e, "cy"), 0f)), r, 0f, MathHelper.TwoPi).Close();
                    break;
                }
                case "ellipse": {
                    float rx = Length(Attr(e, "rx"), 0f);
                    float ry = Length(Attr(e, "ry"), 0f);
                    if (rx <= 0f || ry <= 0f) {
                        return;
                    }
                    b.Arc(new Vector2(Length(Attr(e, "cx"), 0f), Length(Attr(e, "cy"), 0f)), new Vector2(rx, ry), 0f, 0f, MathHelper.TwoPi).Close();
                    break;
                }
                case "line":
                    fillable = false;
                    b.MoveTo(Length(Attr(e, "x1"), 0f), Length(Attr(e, "y1"), 0f)).LineTo(Length(Attr(e, "x2"), 0f), Length(Attr(e, "y2"), 0f));
                    break;
                case "polyline":
                case "polygon": {
                    if (!BuildPoly(b, Attr(e, "points"), name == "polygon")) {
                        return;
                    }
                    break;
                }
            }
            VectorPath userPath = b.Build();
            if (userPath.IsEmpty) {
                return;
            }
            VectorPath docPath = total == Matrix.Identity ? userPath : userPath.Transform(in total);
            float widthScale = MathF.Sqrt(MathF.Abs(total.M11 * total.M22 - total.M12 * total.M21));
            FillStyle fill = fillable ? BuildFill(st, userPath, in total) : null;
            StrokeStyle stroke = BuildStroke(st, userPath, in total, widthScale);
            if (fill == null && stroke == null) {
                return;
            }
            shapes.Add(new VectorShape {
                Path = docPath,
                Fill = fill,
                Stroke = stroke,
                Id = (string)e.Attribute("id"),
                Opacity = st.Opacity,
            });
        }

        //==================== 几何 ====================

        private void BuildRect(VectorPathBuilder b, XElement e) {
            float x = Length(Attr(e, "x"), 0f);
            float y = Length(Attr(e, "y"), 0f);
            float w = Length(Attr(e, "width"), 0f);
            float h = Length(Attr(e, "height"), 0f);
            if (w <= 0f || h <= 0f) {
                return;
            }
            string rxs = Attr(e, "rx");
            string rys = Attr(e, "ry");
            float rx = Length(rxs, -1f);
            float ry = Length(rys, -1f);
            if (rx < 0f && ry < 0f) {
                rx = ry = 0f;
            }
            else if (rx < 0f) {
                rx = ry;
            }
            else if (ry < 0f) {
                ry = rx;
            }
            rx = MathHelper.Clamp(rx, 0f, w * 0.5f);
            ry = MathHelper.Clamp(ry, 0f, h * 0.5f);
            if (rx <= 1e-6f || ry <= 1e-6f) {
                b.MoveTo(x, y).LineTo(x + w, y).LineTo(x + w, y + h).LineTo(x, y + h).Close();
                return;
            }
            Vector2 radii = new(rx, ry);
            b.MoveTo(x + rx, y)
                .LineTo(x + w - rx, y)
                .Arc(new Vector2(x + w - rx, y + ry), radii, 0f, -MathHelper.PiOver2, MathHelper.PiOver2)
                .LineTo(x + w, y + h - ry)
                .Arc(new Vector2(x + w - rx, y + h - ry), radii, 0f, 0f, MathHelper.PiOver2)
                .LineTo(x + rx, y + h)
                .Arc(new Vector2(x + rx, y + h - ry), radii, 0f, MathHelper.PiOver2, MathHelper.PiOver2)
                .LineTo(x, y + ry)
                .Arc(new Vector2(x + rx, y + ry), radii, 0f, MathHelper.Pi, MathHelper.PiOver2)
                .Close();
        }

        private static bool BuildPoly(VectorPathBuilder b, string points, bool close) {
            if (string.IsNullOrWhiteSpace(points)) {
                return false;
            }
            string[] parts = points.Split(listSeparators, StringSplitOptions.RemoveEmptyEntries);
            int count = 0;
            for (int i = 0; i + 1 < parts.Length; i += 2) {
                if (!float.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out float x)
                    || !float.TryParse(parts[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y)) {
                    break;
                }
                if (count == 0) {
                    b.MoveTo(x, y);
                }
                else {
                    b.LineTo(x, y);
                }
                count++;
            }
            if (count < 2) {
                return false;
            }
            if (close) {
                b.Close();
            }
            return true;
        }

        //==================== 样式 ====================

        private Style ApplyStyle(Style st, XElement e) {
            Dictionary<string, string> inline = ParseInlineStyle(Attr(e, "style", inlineOnly: false, raw: true));
            string Get(string name) {
                if (inline != null && inline.TryGetValue(name, out string v)) {
                    return v;
                }
                return (string)e.Attribute(name);
            }
            string v;
            if ((v = Get("display")) != null && v.Trim().Equals("none", StringComparison.OrdinalIgnoreCase)) {
                st.Hidden = true;
            }
            if ((v = Get("visibility")) != null && v.Trim().Equals("hidden", StringComparison.OrdinalIgnoreCase)) {
                st.Hidden = true;
            }
            if ((v = Get("color")) != null && SvgColors.TryParse(v, out Color cc, out bool ccNone) && !ccNone) {
                st.CurrentColor = cc;
            }
            if ((v = Get("fill")) != null) {
                st.Fill = v.Trim();
            }
            if ((v = Get("stroke")) != null) {
                st.Stroke = v.Trim();
            }
            if ((v = Get("stroke-width")) != null) {
                st.StrokeWidth = Length(v, st.StrokeWidth);
            }
            if ((v = Get("stroke-linecap")) != null) {
                st.Cap = v.Trim().ToLowerInvariant() switch {
                    "round" => LineCap.Round,
                    "square" => LineCap.Square,
                    _ => LineCap.Butt,
                };
            }
            if ((v = Get("stroke-linejoin")) != null) {
                st.Join = v.Trim().ToLowerInvariant() switch {
                    "round" or "arcs" => LineJoin.Round,
                    "bevel" => LineJoin.Bevel,
                    _ => LineJoin.Miter,
                };
            }
            if ((v = Get("stroke-miterlimit")) != null) {
                st.MiterLimit = Length(v, st.MiterLimit);
            }
            if ((v = Get("stroke-dasharray")) != null) {
                st.Dash = ParseDash(v);
            }
            if ((v = Get("stroke-dashoffset")) != null) {
                st.DashOffset = Length(v, st.DashOffset);
            }
            if ((v = Get("opacity")) != null) {
                st.Opacity *= MathHelper.Clamp(Number(v, 1f), 0f, 1f);
            }
            if ((v = Get("fill-opacity")) != null) {
                st.FillOpacity = MathHelper.Clamp(Number(v, 1f), 0f, 1f);
            }
            if ((v = Get("stroke-opacity")) != null) {
                st.StrokeOpacity = MathHelper.Clamp(Number(v, 1f), 0f, 1f);
            }
            if ((v = Get("fill-rule")) != null) {
                st.Rule = v.Trim().Equals("evenodd", StringComparison.OrdinalIgnoreCase) ? FillRule.EvenOdd : FillRule.NonZero;
            }
            return st;
        }

        private FillStyle BuildFill(Style st, VectorPath userPath, in Matrix total) {
            if (!ResolvePaint(st.Fill, st, st.Opacity * st.FillOpacity, userPath, in total, out Color color, out VectorPaint paint)) {
                return null;
            }
            FillStyle fill = new() { Color = color, Paint = paint, Rule = st.Rule };
            if (paint != null) {
                //渐变填充逐顶点插值，内部需要采样点：按形状对角线的 1/6 细分（文档单位，绘制时随缩放）
                Vector2 size = userPath.BoundsSize;
                float diag = MathF.Sqrt(size.X * size.X + size.Y * size.Y);
                fill.MaxTriangleEdge = MathF.Max(diag / 6f, 0.5f);
            }
            return fill;
        }

        private StrokeStyle BuildStroke(Style st, VectorPath userPath, in Matrix total, float widthScale) {
            if (st.StrokeWidth <= 0f || !ResolvePaint(st.Stroke, st, st.Opacity * st.StrokeOpacity, userPath, in total, out Color color, out VectorPaint paint)) {
                return null;
            }
            StrokeStyle s = new() {
                Width = st.StrokeWidth * widthScale,
                Color = color,
                Paint = paint,
                StartCap = st.Cap,
                EndCap = st.Cap,
                Join = st.Join,
                MiterLimit = MathF.Max(st.MiterLimit, 1f),
                DashOffset = st.DashOffset * widthScale,
            };
            if (st.Dash != null && st.Dash.Length > 0) {
                float[] dash = new float[st.Dash.Length];
                for (int i = 0; i < dash.Length; i++) {
                    dash[i] = st.Dash[i] * widthScale;
                }
                s.Dash = dash;
            }
            return s;
        }

        //解析 fill / stroke 的取值：none → false；颜色 → color；url(#id) → 渐变画笔
        private bool ResolvePaint(string spec, Style st, float alpha, VectorPath userPath, in Matrix total, out Color color, out VectorPaint paint) {
            color = Color.White;
            paint = null;
            if (string.IsNullOrWhiteSpace(spec)) {
                return false;
            }
            spec = spec.Trim();
            if (spec.StartsWith("url(", StringComparison.OrdinalIgnoreCase)) {
                int close = spec.IndexOf(')');
                if (close < 0) {
                    return false;
                }
                string id = spec.Substring(4, close - 4).Trim().Trim('"', '\'');
                if (id.StartsWith('#')) {
                    id = id[1..];
                }
                string fallback = spec[(close + 1)..].Trim();
                if (gradients.TryGetValue(id, out XElement g)) {
                    paint = BuildGradient(g, userPath, in total, alpha);
                    if (paint != null) {
                        return true;
                    }
                }
                if (fallback.Length > 0 && SvgColors.TryParse(fallback, out Color fb, out bool fbNone, st.CurrentColor)) {
                    if (fbNone) {
                        return false;
                    }
                    color = Premultiply(fb, alpha);
                    return true;
                }
                Warn($"paint reference '{spec}' unresolved");
                return false;
            }
            if (!SvgColors.TryParse(spec, out Color c, out bool none, st.CurrentColor)) {
                Warn($"color '{spec}' unrecognized");
                return false;
            }
            if (none) {
                return false;
            }
            color = Premultiply(c, alpha);
            return true;
        }

        private static Color Premultiply(Color straight, float alpha) {
            int a = (int)MathF.Round(straight.A * MathHelper.Clamp(alpha, 0f, 1f));
            return Color.FromNonPremultiplied(straight.R, straight.G, straight.B, a);
        }

        //==================== 渐变 ====================

        private GradientPaint BuildGradient(XElement g, VectorPath userPath, in Matrix total, float alpha) {
            string GAttr(string name) => ChainAttr(g, name, 0);
            bool bboxMode = !string.Equals(GAttr("gradientUnits"), "userSpaceOnUse", StringComparison.OrdinalIgnoreCase);
            Matrix gt = ParseTransform(GAttr("gradientTransform"));
            //渐变空间 → 文档空间：gradientTransform → （bbox 单位空间 → 用户空间）→ 元素变换
            Matrix frame;
            if (bboxMode) {
                Vector2 min = userPath.BoundsMin;
                Vector2 size = userPath.BoundsSize;
                size.X = MathF.Max(size.X, 1e-3f);
                size.Y = MathF.Max(size.Y, 1e-3f);
                frame = Matrix.CreateScale(size.X, size.Y, 1f) * Matrix.CreateTranslation(min.X, min.Y, 0f) * total;
            }
            else {
                frame = total;
            }
            Matrix toDoc = gt * frame;
            Matrix pre = Matrix.Invert(toDoc);
            List<GradientStop> stops = CollectStops(g, alpha);
            if (stops.Count == 0) {
                Warn($"gradient '{(string)g.Attribute("id")}' has no stops");
                return null;
            }
            GradientSpread spread = (GAttr("spreadMethod") ?? "pad").Trim().ToLowerInvariant() switch {
                "repeat" => GradientSpread.Repeat,
                "reflect" => GradientSpread.Reflect,
                _ => GradientSpread.Pad,
            };
            GradientPaint paint;
            if (g.Name.LocalName == "linearGradient") {
                Vector2 start = new(Coord(GAttr("x1"), 0f, bboxMode, viewBoxSize.X), Coord(GAttr("y1"), 0f, bboxMode, viewBoxSize.Y));
                Vector2 end = new(Coord(GAttr("x2"), 1f, bboxMode, viewBoxSize.X), Coord(GAttr("y2"), 0f, bboxMode, viewBoxSize.Y));
                paint = new LinearGradientPaint(start, end, stops);
            }
            else {
                float cx = Coord(GAttr("cx"), 0.5f, bboxMode, viewBoxSize.X);
                float cy = Coord(GAttr("cy"), 0.5f, bboxMode, viewBoxSize.Y);
                float r = Coord(GAttr("r"), 0.5f, bboxMode, MathF.Max(viewBoxSize.X, viewBoxSize.Y));
                RadialGradientPaint radial = new(new Vector2(cx, cy), MathF.Max(r, 1e-6f), stops);
                string fxs = GAttr("fx");
                string fys = GAttr("fy");
                if (fxs != null || fys != null) {
                    radial.Focal = new Vector2(Coord(fxs, cx, bboxMode, viewBoxSize.X), Coord(fys, cy, bboxMode, viewBoxSize.Y));
                }
                paint = radial;
            }
            paint.Spread = spread;
            paint.Space = PaintSpace.Path;
            paint.SetPreTransform(in pre);
            return paint;
        }

        //沿 href 链找属性
        private string ChainAttr(XElement g, string name, int depth) {
            string v = (string)g.Attribute(name);
            if (v != null || depth > 8) {
                return v;
            }
            XElement parent = HrefTarget(g);
            return parent != null ? ChainAttr(parent, name, depth + 1) : null;
        }

        private XElement HrefTarget(XElement g) {
            string href = (string)g.Attribute("href") ?? (string)g.Attribute(xlink + "href");
            if (string.IsNullOrEmpty(href)) {
                return null;
            }
            href = href.Trim();
            if (href.StartsWith('#')) {
                href = href[1..];
            }
            return gradients.TryGetValue(href, out XElement target) && target != g ? target : null;
        }

        private List<GradientStop> CollectStops(XElement g, float alpha) {
            List<GradientStop> stops = [];
            XElement source = g;
            int depth = 0;
            while (source != null && depth++ < 8) {
                bool any = false;
                foreach (XElement s in source.Elements()) {
                    if (s.Name.LocalName != "stop") {
                        continue;
                    }
                    any = true;
                    Dictionary<string, string> inline = ParseInlineStyle((string)s.Attribute("style"));
                    string Get(string n) => inline != null && inline.TryGetValue(n, out string iv) ? iv : (string)s.Attribute(n);
                    float offset = Coord(Get("offset"), 0f, true, 1f);
                    Color c = Color.Black;
                    string sc = Get("stop-color");
                    if (sc != null && SvgColors.TryParse(sc, out Color parsed, out bool none) && !none) {
                        c = parsed;
                    }
                    float so = MathHelper.Clamp(Number(Get("stop-opacity"), 1f), 0f, 1f);
                    stops.Add(new GradientStop(offset, Premultiply(c, so * alpha)));
                }
                if (any) {
                    break;
                }
                source = HrefTarget(source);
            }
            return stops;
        }

        //==================== 属性解析 ====================

        private static string Attr(XElement e, string name, bool inlineOnly = false, bool raw = false) {
            if (raw) {
                return (string)e.Attribute(name);
            }
            Dictionary<string, string> inline = ParseInlineStyle((string)e.Attribute("style"));
            if (inline != null && inline.TryGetValue(name, out string v)) {
                return v;
            }
            return inlineOnly ? null : (string)e.Attribute(name);
        }

        private static Dictionary<string, string> ParseInlineStyle(string style) {
            if (string.IsNullOrWhiteSpace(style)) {
                return null;
            }
            Dictionary<string, string> map = new(StringComparer.OrdinalIgnoreCase);
            foreach (string decl in style.Split(';', StringSplitOptions.RemoveEmptyEntries)) {
                int colon = decl.IndexOf(':');
                if (colon <= 0) {
                    continue;
                }
                map[decl[..colon].Trim()] = decl[(colon + 1)..].Trim();
            }
            return map;
        }

        private void ParseViewBox(XElement root, out Vector2 min, out Vector2 size) {
            min = Vector2.Zero;
            size = Vector2.Zero;
            string vb = Attr(root, "viewBox");
            if (string.IsNullOrWhiteSpace(vb)) {
                return;
            }
            string[] parts = vb.Split(listSeparators, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 4) {
                Warn("viewBox malformed");
                return;
            }
            min = new Vector2(Number(parts[0], 0f), Number(parts[1], 0f));
            size = new Vector2(Number(parts[2], 0f), Number(parts[3], 0f));
        }

        private static float[] ParseDash(string text) {
            text = text.Trim();
            if (text.Length == 0 || text.Equals("none", StringComparison.OrdinalIgnoreCase)) {
                return null;
            }
            string[] parts = text.Split(listSeparators, StringSplitOptions.RemoveEmptyEntries);
            List<float> values = new(parts.Length);
            foreach (string p in parts) {
                float v = Length(p, -1f);
                if (v < 0f) {
                    return null;
                }
                values.Add(v);
            }
            return values.Count == 0 ? null : [.. values];
        }

        //数字 + 可选单位：px 原值，其余常见绝对单位换算到 px（96 dpi），百分比按 fallback 处理不了则返回 fallback
        private static float Length(string text, float fallback) {
            if (string.IsNullOrWhiteSpace(text)) {
                return fallback;
            }
            string s = text.Trim();
            float unit = 1f;
            if (s.EndsWith('%')) {
                return fallback;
            }
            int cut = s.Length;
            while (cut > 0 && (char.IsLetter(s[cut - 1]))) {
                cut--;
            }
            if (cut < s.Length) {
                unit = s[cut..].ToLowerInvariant() switch {
                    "px" => 1f,
                    "pt" => 96f / 72f,
                    "pc" => 16f,
                    "mm" => 96f / 25.4f,
                    "cm" => 96f / 2.54f,
                    "in" => 96f,
                    _ => 1f,
                };
                s = s[..cut];
            }
            return float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v * unit : fallback;
        }

        private static float Number(string text, float fallback) {
            if (string.IsNullOrWhiteSpace(text)) {
                return fallback;
            }
            string s = text.Trim();
            bool percent = s.EndsWith('%');
            if (percent) {
                s = s[..^1];
            }
            if (!float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float v)) {
                return fallback;
            }
            return percent ? v / 100f : v;
        }

        //渐变坐标：bbox 模式下数字与百分比都是 0~1 的比例；用户空间模式下百分比按参考尺寸换算
        private static float Coord(string text, float fallback, bool bboxMode, float reference) {
            if (string.IsNullOrWhiteSpace(text)) {
                return fallback;
            }
            string s = text.Trim();
            if (s.EndsWith('%')) {
                float pct = Number(s, fallback);
                return bboxMode ? pct : pct * reference;
            }
            return Length(s, fallback);
        }

        //==================== transform ====================

        //SVG transform 列表：右边的先作用，因此按出现顺序左乘（行向量约定 p' = p * M）
        private static Matrix ParseTransform(string text) {
            Matrix result = Matrix.Identity;
            if (string.IsNullOrWhiteSpace(text)) {
                return result;
            }
            int i = 0;
            while (i < text.Length) {
                while (i < text.Length && (char.IsWhiteSpace(text[i]) || text[i] == ',')) {
                    i++;
                }
                if (i >= text.Length) {
                    break;
                }
                int open = text.IndexOf('(', i);
                if (open < 0) {
                    break;
                }
                int close = text.IndexOf(')', open);
                if (close < 0) {
                    break;
                }
                string name = text[i..open].Trim().ToLowerInvariant();
                string[] parts = text.Substring(open + 1, close - open - 1).Split(listSeparators, StringSplitOptions.RemoveEmptyEntries);
                float[] a = new float[parts.Length];
                for (int k = 0; k < parts.Length; k++) {
                    a[k] = Number(parts[k], 0f);
                }
                Matrix m = Matrix.Identity;
                switch (name) {
                    case "matrix":
                        if (a.Length >= 6) {
                            m = new Matrix(a[0], a[1], 0f, 0f, a[2], a[3], 0f, 0f, 0f, 0f, 1f, 0f, a[4], a[5], 0f, 1f);
                        }
                        break;
                    case "translate":
                        m = Matrix.CreateTranslation(a.Length > 0 ? a[0] : 0f, a.Length > 1 ? a[1] : 0f, 0f);
                        break;
                    case "scale": {
                        float sx = a.Length > 0 ? a[0] : 1f;
                        float sy = a.Length > 1 ? a[1] : sx;
                        m = Matrix.CreateScale(sx, sy, 1f);
                        break;
                    }
                    case "rotate": {
                        float rad = MathHelper.ToRadians(a.Length > 0 ? a[0] : 0f);
                        if (a.Length >= 3) {
                            m = Matrix.CreateTranslation(-a[1], -a[2], 0f) * Matrix.CreateRotationZ(rad) * Matrix.CreateTranslation(a[1], a[2], 0f);
                        }
                        else {
                            m = Matrix.CreateRotationZ(rad);
                        }
                        break;
                    }
                    case "skewx": {
                        float t = MathF.Tan(MathHelper.ToRadians(a.Length > 0 ? a[0] : 0f));
                        m = Matrix.Identity;
                        m.M21 = t;
                        break;
                    }
                    case "skewy": {
                        float t = MathF.Tan(MathHelper.ToRadians(a.Length > 0 ? a[0] : 0f));
                        m = Matrix.Identity;
                        m.M12 = t;
                        break;
                    }
                }
                result = m * result;
                i = close + 1;
            }
            return result;
        }

        private void Warn(string message) {
            if (warned.Add(message)) {
                warnings.Add(message);
            }
        }
    }
}
