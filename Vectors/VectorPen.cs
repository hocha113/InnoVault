using InnoVault.Vectors.Tessellation;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;

namespace InnoVault.Vectors
{
    /// <summary>
    /// 像素笔后端：用 1×1 白像素贴图逐段拉伸描边，在当前 <see cref="SpriteBatch"/> 批次内直接出画、不切批，
    /// 是 UI 前景细线 / 字形 / 环 / 弧的首选；宽度以当前批次坐标系的像素计
    /// <br/>支持弧长窗口、虚线、<see cref="VectorPaint"/>（按段中点求色）、<see cref="LineCap.Texture"/> 贴图端帽（直接落在当前批次内）；限制：接头与端帽只有方点近似（圆接、圆帽画成方点），没有贴图 UV，不能填充任意多边形；这些需求走 <see cref="VectorMesh"/> + <see cref="VectorRenderer"/>
    /// <br/>白像素永远是硬边：叠几层同心放大不会产生羽化，暗色大面积阴影请用着色器，见 <see cref="GlowStroke"/> 说明
    /// <br/>只允许在渲染线程调用
    /// </summary>
    public static class VectorPen
    {
        //相邻两段各多画一点，密折线靠它自封口
        private const float SegmentOverlap = 0.6f;
        //转角余弦小于此值才补方点
        private const float CornerCos = 0.86f;
        private static readonly Rectangle pixelSrc = new(0, 0, 1, 1);
        private static readonly StrokeStyle scratchStyle = new();
        private static readonly StrokeStyle glowStyle = new();
        //参数式重载专用：paramDefaults 只作全字段复位的样板，从不被赋值
        private static readonly StrokeStyle paramDefaults = new();
        private static readonly StrokeStyle paramStyle = new();
        //辉光层的宽度 / 颜色包装：静态委托读静态字段，不再每层分配闭包
        private static StrokeWidthFunction glowBaseWidth;
        private static StrokeColorFunction glowBaseColor;
        private static float glowExtra;
        private static float glowAlpha;
        private static readonly StrokeWidthFunction glowWidth = t => glowBaseWidth(t) + glowExtra;
        private static readonly StrokeColorFunction glowColor = (t, side) => glowBaseColor(t, side) * glowAlpha;

        private static Texture2D Pixel => VaultAsset.placeholder2?.Value;

        /// <summary>描边路径</summary>
        public static void Stroke(SpriteBatch sb, VectorPath path, StrokeStyle style, in VectorTransform transform) {
            Texture2D px = Pixel;
            if (sb == null || px == null || path == null || style == null || path.IsEmpty) {
                return;
            }
            if (!StrokeTessellator.ResolveWindow(style, out float lo, out float hi)) {
                return;
            }
            bool full = style.IsFullWindow;
            for (int s = 0; s < path.SubPathCount; s++) {
                DrawPrepared(sb, px, style, StrokeTessellator.PrepareSubPath(path, s, style, in transform, full, lo, hi));
            }
        }

        /// <summary>描边路径（恒等变换）</summary>
        public static void Stroke(SpriteBatch sb, VectorPath path, StrokeStyle style) => Stroke(sb, path, style, in VectorTransform.Identity);

        /// <summary>
        /// 按参数直接描路径：宽度 / 颜色 / 弧长窗口作为调用参数，不需要持有 <see cref="StrokeStyle"/>；平头端帽、默认接头，其余字段一律默认
        /// </summary>
        public static void Stroke(SpriteBatch sb, VectorPath path, in VectorTransform transform, float width, Color color, float from = 0f, float to = 1f)
            => Stroke(sb, path, ResetParamStyle(width, color, from, to), in transform);

        /// <summary>描一条已在批次坐标系里的点列</summary>
        public static void Stroke(SpriteBatch sb, ReadOnlySpan<Vector2> points, StrokeStyle style, bool closed = false) {
            Texture2D px = Pixel;
            if (sb == null || px == null || style == null || points.Length == 0) {
                return;
            }
            if (!StrokeTessellator.ResolveWindow(style, out float lo, out float hi)) {
                return;
            }
            DrawPrepared(sb, px, style, StrokeTessellator.PrepareSpan(points, closed, style, style.IsFullWindow, lo, hi));
        }

        private static void DrawPrepared(SpriteBatch sb, Texture2D px, StrokeStyle style, StrokeTessellator.PrepareResult result) {
            switch (result) {
                case StrokeTessellator.PrepareResult.Dot:
                    DrawDotCap(sb, px, style, StrokeTessellator.WorkPoints[0], StrokeTessellator.WorkTs[0], style.StartCap, style.EndCap);
                    break;
                case StrokeTessellator.PrepareResult.Polyline:
                    for (int i = 0; i < StrokeTessellator.PieceCount; i++) {
                        StrokeTessellator.LoadPiece(i, style, out LineCap startCap, out LineCap endCap);
                        if (StrokeTessellator.WorkPoints.Count == 1) {
                            DrawDotCap(sb, px, style, StrokeTessellator.WorkPoints[0], StrokeTessellator.WorkTs[0], startCap, endCap);
                        }
                        else {
                            DrawPolyline(sb, px, style, StrokeTessellator.WorkClosed, startCap, endCap);
                        }
                    }
                    break;
            }
        }

        /// <summary>
        /// 沿路径巡行的一段亮笔：从归一弧长 <paramref name="head"/> 起描 <paramref name="span"/> 长，越过末端自动接回起点；
        /// <paramref name="style"/> 的 From / To 被忽略
        /// </summary>
        public static void StrokeRunner(SpriteBatch sb, VectorPath path, StrokeStyle style, in VectorTransform transform, float head, float span) {
            if (style == null) {
                return;
            }
            head -= MathF.Floor(head);
            float tail = head + MathHelper.Clamp(span, 0f, 1f);
            scratchStyle.CopyFrom(style);
            scratchStyle.From = head;
            scratchStyle.To = MathF.Min(tail, 1f);
            Stroke(sb, path, scratchStyle, in transform);
            if (tail > 1f) {
                scratchStyle.From = 0f;
                scratchStyle.To = tail - 1f;
                Stroke(sb, path, scratchStyle, in transform);
            }
        }

        /// <summary>参数式巡行亮笔，语义同 <see cref="StrokeRunner(SpriteBatch, VectorPath, StrokeStyle, in VectorTransform, float, float)"/></summary>
        public static void StrokeRunner(SpriteBatch sb, VectorPath path, in VectorTransform transform, float width, Color color, float head, float span)
            => StrokeRunner(sb, path, ResetParamStyle(width, color, 0f, 1f), in transform, head, span);

        /// <summary>
        /// 亮色细线的辉光：由外到内叠 <paramref name="layers"/> 层，宽度逐层加宽 <paramref name="spread"/>（最外层）、透明度按 <paramref name="falloff"/> 的幂衰减，最后画本体
        /// <br/>只适用于亮色描边冒充发光；白像素不能羽化，用它给暗色大面板做软阴影会得到阶梯状黑框，那种需求走着色器
        /// </summary>
        public static void GlowStroke(SpriteBatch sb, VectorPath path, StrokeStyle style, in VectorTransform transform, int layers = 3, float spread = 3.5f, float falloff = 0.45f) {
            if (style == null) {
                return;
            }
            layers = Math.Max(layers, 1);
            for (int k = layers - 1; k >= 1; k--) {
                glowExtra = spread * k / (layers - 1);
                glowAlpha = MathF.Pow(falloff, k);
                glowStyle.CopyFrom(style);
                //辉光层不印贴图端帽，避免 N 层叠印
                glowStyle.CapTexture = null;
                if (style.WidthFunction != null) {
                    glowBaseWidth = style.WidthFunction;
                    glowStyle.WidthFunction = glowWidth;
                }
                else {
                    glowStyle.Width = style.Width + glowExtra;
                }
                if (style.ColorFunction != null) {
                    glowBaseColor = style.ColorFunction;
                    glowStyle.ColorFunction = glowColor;
                }
                else if (style.Paint != null) {
                    //Paint 无法直接乘透明度：辉光层退回按常量色，取路径中点处的画笔色
                    glowStyle.Paint = null;
                    glowStyle.Color = StrokeTessellator.Col(style, 0.5f, 0.5f, transform.Apply(path?.PointAt(0.5f) ?? Vector2.Zero)) * glowAlpha;
                    //Col 已经乘过 Opacity，这一层不要再乘一次
                    glowStyle.Opacity = 1f;
                }
                else {
                    glowStyle.Color = style.Color * glowAlpha;
                }
                Stroke(sb, path, glowStyle, in transform);
            }
            Stroke(sb, path, style, in transform);
        }

        #region 基础图元

        /// <summary>线段</summary>
        public static void Line(SpriteBatch sb, Vector2 start, Vector2 end, float width, Color color) {
            Texture2D px = Pixel;
            if (sb == null || px == null) {
                return;
            }
            Vector2 d = end - start;
            float len = d.Length();
            if (len < 0.01f || width <= 0f) {
                return;
            }
            sb.Draw(px, start, pixelSrc, color, MathF.Atan2(d.Y, d.X), new Vector2(0f, 0.5f), new Vector2(len, width), SpriteEffects.None, 0f);
        }

        /// <summary>虚线线段：<paramref name="dash"/> 为实长，<paramref name="gap"/> 为空长，<paramref name="offset"/> 让虚线沿线流动</summary>
        public static void DashedLine(SpriteBatch sb, Vector2 start, Vector2 end, float width, Color color, float dash, float gap, float offset = 0f) {
            Texture2D px = Pixel;
            if (sb == null || px == null || width <= 0f) {
                return;
            }
            Vector2 d = end - start;
            float len = d.Length();
            if (len < 0.01f || dash <= 0f) {
                return;
            }
            Vector2 dir = d / len;
            float period = dash + MathF.Max(gap, 0f);
            float pos = -(offset % period);
            if (pos > 0f) {
                pos -= period;
            }
            for (; pos < len; pos += period) {
                float a = MathF.Max(pos, 0f);
                float b = MathF.Min(pos + dash, len);
                if (b > a) {
                    DrawSegment(sb, px, start + dir * a, start + dir * b, width, color);
                }
            }
        }

        /// <summary>以 <paramref name="center"/> 为中心的方点</summary>
        public static void Dot(SpriteBatch sb, Vector2 center, float size, Color color, float rotation = 0f) {
            Texture2D px = Pixel;
            if (sb == null || px == null || size <= 0f) {
                return;
            }
            sb.Draw(px, center, pixelSrc, color, rotation, new Vector2(0.5f), new Vector2(size), SpriteEffects.None, 0f);
        }

        /// <summary>空心圆环（圆的描边）；<paramref name="segments"/> ≤ 0 时按半径自动</summary>
        public static void Ring(SpriteBatch sb, Vector2 center, float radius, float width, Color color, int segments = 0)
            => Arc(sb, center, radius, 0f, MathHelper.TwoPi, width, color, segments);

        /// <summary>圆弧描边；<paramref name="segments"/> ≤ 0 时按半径与扫角自动</summary>
        public static void Arc(SpriteBatch sb, Vector2 center, float radius, float startAngle, float endAngle, float width, Color color, int segments = 0) {
            Texture2D px = Pixel;
            if (sb == null || px == null || radius <= 0f || width <= 0f) {
                return;
            }
            float sweep = endAngle - startAngle;
            if (MathF.Abs(sweep) < 1e-5f) {
                return;
            }
            int n = segments > 0 ? segments : AutoArcSegments(radius, sweep);
            Vector2 prev = center + new Vector2(MathF.Cos(startAngle), MathF.Sin(startAngle)) * radius;
            for (int i = 1; i <= n; i++) {
                float a = startAngle + sweep * i / n;
                Vector2 cur = center + new Vector2(MathF.Cos(a), MathF.Sin(a)) * radius;
                DrawSegment(sb, px, prev, cur, width, color);
                prev = cur;
            }
        }

        /// <summary>矩形描边，线宽向内占用</summary>
        public static void RectOutline(SpriteBatch sb, Rectangle rect, float width, Color color) {
            Texture2D px = Pixel;
            if (sb == null || px == null || width <= 0f) {
                return;
            }
            Vector2 tl = new(rect.X, rect.Y);
            sb.Draw(px, tl, pixelSrc, color, 0f, Vector2.Zero, new Vector2(rect.Width, width), SpriteEffects.None, 0f);
            sb.Draw(px, new Vector2(rect.X, rect.Bottom - width), pixelSrc, color, 0f, Vector2.Zero, new Vector2(rect.Width, width), SpriteEffects.None, 0f);
            sb.Draw(px, new Vector2(rect.X, rect.Y + width), pixelSrc, color, 0f, Vector2.Zero, new Vector2(width, rect.Height - width * 2f), SpriteEffects.None, 0f);
            sb.Draw(px, new Vector2(rect.Right - width, rect.Y + width), pixelSrc, color, 0f, Vector2.Zero, new Vector2(width, rect.Height - width * 2f), SpriteEffects.None, 0f);
        }

        /// <summary>圆角矩形描边（左上角 + 尺寸），线条居中于轮廓</summary>
        public static void RoundedRectOutline(SpriteBatch sb, Vector2 position, Vector2 size, float radius, float width, Color color) {
            Texture2D px = Pixel;
            if (sb == null || px == null || width <= 0f) {
                return;
            }
            radius = MathHelper.Clamp(radius, 0f, MathF.Min(size.X, size.Y) * 0.5f);
            Vector2 br = position + size;
            if (radius <= 0.5f) {
                DrawSegment(sb, px, position, new Vector2(br.X, position.Y), width, color);
                DrawSegment(sb, px, new Vector2(br.X, position.Y), br, width, color);
                DrawSegment(sb, px, br, new Vector2(position.X, br.Y), width, color);
                DrawSegment(sb, px, new Vector2(position.X, br.Y), position, width, color);
                return;
            }
            //四条直边
            Line(sb, new Vector2(position.X + radius, position.Y), new Vector2(br.X - radius, position.Y), width, color);
            Line(sb, new Vector2(br.X, position.Y + radius), new Vector2(br.X, br.Y - radius), width, color);
            Line(sb, new Vector2(br.X - radius, br.Y), new Vector2(position.X + radius, br.Y), width, color);
            Line(sb, new Vector2(position.X, br.Y - radius), new Vector2(position.X, position.Y + radius), width, color);
            //四个角
            int seg = AutoArcSegments(radius, MathHelper.PiOver2);
            Arc(sb, new Vector2(position.X + radius, position.Y + radius), radius, MathHelper.Pi, MathHelper.Pi * 1.5f, width, color, seg);
            Arc(sb, new Vector2(br.X - radius, position.Y + radius), radius, MathHelper.Pi * 1.5f, MathHelper.TwoPi, width, color, seg);
            Arc(sb, new Vector2(br.X - radius, br.Y - radius), radius, 0f, MathHelper.PiOver2, width, color, seg);
            Arc(sb, new Vector2(position.X + radius, br.Y - radius), radius, MathHelper.PiOver2, MathHelper.Pi, width, color, seg);
        }

        /// <summary>实心矩形</summary>
        public static void FillRect(SpriteBatch sb, Rectangle rect, Color color) {
            Texture2D px = Pixel;
            if (sb == null || px == null) {
                return;
            }
            sb.Draw(px, rect, pixelSrc, color);
        }

        /// <summary>
        /// 实心环形扇区：用密集径向线段铺满，段数按外弧长自适应保证无缝；<paramref name="innerRadius"/> ≤ 0 时为饼形
        /// </summary>
        public static void FillSector(SpriteBatch sb, Vector2 center, float innerRadius, float outerRadius, float startAngle, float endAngle, Color color) {
            Texture2D px = Pixel;
            if (sb == null || px == null || outerRadius <= 0f) {
                return;
            }
            float sweep = endAngle - startAngle;
            if (MathF.Abs(sweep) < 1e-5f) {
                return;
            }
            innerRadius = MathHelper.Clamp(innerRadius, 0f, outerRadius);
            float arcLen = MathF.Abs(sweep) * outerRadius;
            int steps = Math.Max((int)MathF.Ceiling(arcLen / 2.5f), 3);
            float aStep = sweep / steps;
            //径向线的横向厚度按外弧取，内侧略有重叠但外缘不留缝
            float thick = MathF.Max(MathF.Abs(aStep) * outerRadius + 0.8f, 1.5f);
            for (int i = 0; i <= steps; i++) {
                float a = startAngle + aStep * i;
                Vector2 dir = new(MathF.Cos(a), MathF.Sin(a));
                Vector2 p0 = center + dir * innerRadius;
                float len = outerRadius - innerRadius;
                if (len < 0.01f) {
                    continue;
                }
                sb.Draw(px, p0, pixelSrc, color, a, new Vector2(0f, 0.5f), new Vector2(len, thick), SpriteEffects.None, 0f);
            }
        }

        #endregion

        //==================== 内部 ====================

        private static void DrawPolyline(SpriteBatch sb, Texture2D px, StrokeStyle style, bool closed, LineCap startCap, LineCap endCap) {
            List<Vector2> pts = StrokeTessellator.WorkPoints;
            List<float> ts = StrokeTessellator.WorkTs;
            int n = pts.Count;
            int segCount = closed ? n : n - 1;
            for (int s = 0; s < segCount; s++) {
                int i1 = (s + 1) % n;
                float tm = (ts[s] + ts[i1]) * 0.5f;
                float w = style.WidthAt(tm);
                if (w <= 0f) {
                    continue;
                }
                Vector2 mid = (pts[s] + pts[i1]) * 0.5f;
                DrawSegment(sb, px, pts[s], pts[i1], w, StrokeTessellator.Col(style, tm, 0.5f, mid));
            }
            //真拐角补方点，圆弧那种密折线补了也看不见
            int jStart = closed ? 0 : 1;
            int jEnd = closed ? n : n - 1;
            for (int i = jStart; i < jEnd; i++) {
                Vector2 a = pts[(i - 1 + n) % n];
                Vector2 b = pts[i];
                Vector2 c = pts[(i + 1) % n];
                Vector2 din = b - a;
                Vector2 dout = c - b;
                if (din.LengthSquared() < 1e-8f || dout.LengthSquared() < 1e-8f) {
                    continue;
                }
                din.Normalize();
                dout.Normalize();
                if (Vector2.Dot(din, dout) >= CornerCos) {
                    continue;
                }
                float w = style.WidthAt(ts[i]);
                if (w <= 0f) {
                    continue;
                }
                sb.Draw(px, b, pixelSrc, StrokeTessellator.Col(style, ts[i], 0.5f, b), MathF.Atan2(din.Y, din.X), new Vector2(0.5f), new Vector2(w), SpriteEffects.None, 0f);
            }
            if (closed) {
                return;
            }
            //端帽
            Vector2 d0 = pts[1] - pts[0];
            Vector2 d1 = pts[n - 1] - pts[n - 2];
            DrawCap(sb, px, style, startCap, pts[0], d0.LengthSquared() > 1e-8f ? -Vector2.Normalize(d0) : -Vector2.UnitX, ts[0], false);
            DrawCap(sb, px, style, endCap, pts[n - 1], d1.LengthSquared() > 1e-8f ? Vector2.Normalize(d1) : Vector2.UnitX, ts[n - 1], true);
        }

        private static void DrawCap(SpriteBatch sb, Texture2D px, StrokeStyle style, LineCap cap, Vector2 p, Vector2 outward, float t, bool atEnd) {
            if (cap == LineCap.Texture) {
                DrawTextureCap(sb, style, p, outward, t, atEnd);
                return;
            }
            if (cap == LineCap.Butt) {
                return;
            }
            float w = style.WidthAt(t);
            if (w <= 0f) {
                return;
            }
            Color color = StrokeTessellator.Col(style, t, 0.5f, p);
            float rot = MathF.Atan2(outward.Y, outward.X);
            if (cap == LineCap.Arrow) {
                float len = style.CapLength > 0f ? style.CapLength : w;
                Vector2 tip = p + outward * len;
                Vector2 nrm = new Vector2(-outward.Y, outward.X) * (w * 0.5f);
                DrawSegment(sb, px, p + nrm, tip, w * 0.5f, color);
                DrawSegment(sb, px, p - nrm, tip, w * 0.5f, color);
                return;
            }
            //圆帽 / 方帽都以方点近似：外延半宽
            sb.Draw(px, p, pixelSrc, color, rot, new Vector2(0.5f), new Vector2(w), SpriteEffects.None, 0f);
        }

        private static void DrawDotCap(SpriteBatch sb, Texture2D px, StrokeStyle style, Vector2 p, float t, LineCap startCap, LineCap endCap) {
            //两端重合到一点，贴图端帽只印一次、朝向取 +X、尺寸取 CapScale
            if (startCap == LineCap.Texture || endCap == LineCap.Texture) {
                DrawTextureCap(sb, style, p, Vector2.UnitX, t, false);
            }
            //Texture 的几何等同 Butt，两端都不出几何时不画方点
            if ((startCap == LineCap.Butt || startCap == LineCap.Texture) && (endCap == LineCap.Butt || endCap == LineCap.Texture)) {
                return;
            }
            float w = style.WidthAt(t);
            if (w <= 0f) {
                return;
            }
            sb.Draw(px, p, pixelSrc, StrokeTessellator.Col(style, t, 0.5f, p), 0f, new Vector2(0.5f), new Vector2(w), SpriteEffects.None, 0f);
        }

        //贴图端帽：以端点为中心印贴图，尺寸 = 该处全宽 × 该端倍率，+X 指向路径之外；CapRepeat > 1 时同色同位逐次缩小叠印
        //注意 CapBlend 只对网格后端有意义：这里的印章落在调用方的批次内，改不了混合
        private static void DrawTextureCap(SpriteBatch sb, StrokeStyle style, Vector2 p, Vector2 outward, float t, bool atEnd) {
            Texture2D tex = style.CapTexture;
            if (tex == null || tex.Width <= 0 || tex.Height <= 0) {
                return;
            }
            float w = style.WidthAt(t);
            if (w <= 0f) {
                return;
            }
            Vector2 size = new Vector2(w) * style.CapScaleAt(atEnd);
            Color color = style.CapColor ?? StrokeTessellator.Col(style, t, 0.5f, p);
            float rotation = MathF.Atan2(outward.Y, outward.X);
            Vector2 origin = new Vector2(tex.Width, tex.Height) * 0.5f;
            style.ResolveCapRepeat(out int repeat, out float capScale);
            for (int k = 0; k < repeat; k++) {
                sb.Draw(tex, p, null, color, rotation, origin,
                    new Vector2(size.X / tex.Width, size.Y / tex.Height), SpriteEffects.None, 0f);
                size *= capScale;
            }
        }

        //参数式重载的暂存样式：先全字段复位到默认，再按参数赋值
        private static StrokeStyle ResetParamStyle(float width, Color color, float from, float to) {
            paramStyle.CopyFrom(paramDefaults);
            paramStyle.Width = width;
            paramStyle.Color = color;
            paramStyle.From = from;
            paramStyle.To = to;
            return paramStyle;
        }

        private static void DrawSegment(SpriteBatch sb, Texture2D px, Vector2 from, Vector2 to, float width, Color color) {
            Vector2 d = to - from;
            float len = d.Length();
            if (len < 0.01f) {
                return;
            }
            sb.Draw(px, from, pixelSrc, color, MathF.Atan2(d.Y, d.X), new Vector2(0f, 0.5f), new Vector2(len + SegmentOverlap, width), SpriteEffects.None, 0f);
        }

        //像素级弧段数：每段弦长约 3 像素，至少 4 段，整圆不超过 128 段
        private static int AutoArcSegments(float radius, float sweep) {
            float arcLen = MathF.Abs(sweep) * radius;
            int n = (int)MathF.Ceiling(arcLen / 3f);
            int cap = Math.Max(4, (int)MathF.Ceiling(128f * MathF.Abs(sweep) / MathHelper.TwoPi));
            return Math.Clamp(n, Math.Min(4, cap), cap);
        }
    }
}
