using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;

namespace InnoVault.Vectors
{
    /// <summary>画笔求色时使用的坐标空间</summary>
    public enum PaintSpace
    {
        /// <summary>路径空间（变换前的形状自身坐标），随形状一起移动缩放。SVG 渐变的语义</summary>
        Path,
        /// <summary>输出空间（世界 / 屏幕 / UI 坐标），渐变钉在画面上不随形状走</summary>
        Output,
    }

    /// <summary>渐变超出 [0, 1] 范围后的延展方式，与 SVG <c>spreadMethod</c> 同义</summary>
    public enum GradientSpread
    {
        /// <summary>两端各取端点色</summary>
        Pad,
        /// <summary>周期重复</summary>
        Repeat,
        /// <summary>来回镜像</summary>
        Reflect,
    }

    /// <summary>渐变上的一个色标</summary>
    public readonly struct GradientStop
    {
        /// <summary>位置 0~1</summary>
        public readonly float Offset;
        /// <summary>该位置的颜色</summary>
        public readonly Color Color;

        /// <summary>构造色标</summary>
        public GradientStop(float offset, Color color) {
            Offset = offset;
            Color = color;
        }
    }

    /// <summary>
    /// 按位置求色的画笔基类，挂在 <see cref="StrokeStyle.Paint"/> / <see cref="FillStyle.Paint"/> 上
    /// <br/>网格后端逐顶点求色，所以渐变在三角形内部是线性插值；长直段上的径向渐变请配合 <see cref="StrokeStyle.MaxSegmentLength"/> 细分
    /// </summary>
    public abstract class VectorPaint
    {
        /// <summary>求色位置所在的空间</summary>
        public PaintSpace Space { get; set; } = PaintSpace.Path;

        /// <summary>求 <paramref name="position"/> 处的颜色</summary>
        public abstract Color Evaluate(Vector2 position);
    }

    /// <summary>纯色画笔（用于把「颜色」当作画笔传递的场合，例如 SVG 文档的统一接口）</summary>
    public sealed class SolidPaint : VectorPaint
    {
        /// <summary>颜色</summary>
        public Color Color { get; set; }

        /// <summary>构造纯色画笔</summary>
        public SolidPaint(Color color) {
            Color = color;
        }

        /// <inheritdoc/>
        public override Color Evaluate(Vector2 position) => Color;
    }

    /// <summary>
    /// 渐变画笔基类：色标表 + 延展方式 + 可选的位置前置矩阵（SVG <c>gradientTransform</c> / 用户空间换算用）
    /// </summary>
    public abstract class GradientPaint : VectorPaint
    {
        private GradientStop[] stops = [];
        private Matrix preTransform = Matrix.Identity;
        private bool hasPreTransform;

        /// <summary>色标（按 <see cref="GradientStop.Offset"/> 升序保存）</summary>
        public IReadOnlyList<GradientStop> Stops => stops;
        /// <summary>超出 [0, 1] 后的延展方式</summary>
        public GradientSpread Spread { get; set; } = GradientSpread.Pad;

        /// <summary>设置色标，会按位置排序并夹到 [0, 1]</summary>
        public void SetStops(IEnumerable<GradientStop> newStops) {
            List<GradientStop> list = [];
            if (newStops != null) {
                foreach (GradientStop s in newStops) {
                    list.Add(new GradientStop(MathHelper.Clamp(s.Offset, 0f, 1f), s.Color));
                }
            }
            list.Sort((a, b) => a.Offset.CompareTo(b.Offset));
            stops = [.. list];
        }

        /// <summary>两色渐变的快捷设置</summary>
        public void SetStops(Color start, Color end) => SetStops([new GradientStop(0f, start), new GradientStop(1f, end)]);

        /// <summary>
        /// 设置求色前施加在位置上的矩阵（例如把输出 / 文档空间的点换回渐变自身的定义空间）；传 <see cref="Matrix.Identity"/> 清除
        /// </summary>
        public void SetPreTransform(in Matrix matrix) {
            preTransform = matrix;
            hasPreTransform = matrix != Matrix.Identity;
        }

        /// <summary>子类算出的归一参数 → 颜色</summary>
        protected Color Sample(float t) {
            if (stops.Length == 0) {
                return Color.White;
            }
            if (stops.Length == 1) {
                return stops[0].Color;
            }
            if (float.IsNaN(t)) {
                t = 0f;
            }
            switch (Spread) {
                case GradientSpread.Repeat:
                    t -= MathF.Floor(t);
                    break;
                case GradientSpread.Reflect: {
                    float m = t - 2f * MathF.Floor(t * 0.5f);
                    t = m > 1f ? 2f - m : m;
                    break;
                }
                default:
                    t = MathHelper.Clamp(t, 0f, 1f);
                    break;
            }
            if (t <= stops[0].Offset) {
                return stops[0].Color;
            }
            if (t >= stops[^1].Offset) {
                return stops[^1].Color;
            }
            for (int i = 1; i < stops.Length; i++) {
                if (t <= stops[i].Offset) {
                    float span = stops[i].Offset - stops[i - 1].Offset;
                    float f = span > 1e-6f ? (t - stops[i - 1].Offset) / span : 1f;
                    return Color.Lerp(stops[i - 1].Color, stops[i].Color, f);
                }
            }
            return stops[^1].Color;
        }

        /// <summary>施加前置矩阵后的位置</summary>
        protected Vector2 Pre(Vector2 position) => hasPreTransform ? Vector2.Transform(position, preTransform) : position;
    }

    /// <summary>线性渐变：沿 <see cref="Start"/> → <see cref="End"/> 投影求参数</summary>
    public sealed class LinearGradientPaint : GradientPaint
    {
        /// <summary>t = 0 的点</summary>
        public Vector2 Start { get; set; }
        /// <summary>t = 1 的点</summary>
        public Vector2 End { get; set; }

        /// <summary>构造线性渐变</summary>
        public LinearGradientPaint(Vector2 start, Vector2 end, IEnumerable<GradientStop> stops = null) {
            Start = start;
            End = end;
            if (stops != null) {
                SetStops(stops);
            }
        }

        /// <summary>两色线性渐变</summary>
        public LinearGradientPaint(Vector2 start, Vector2 end, Color startColor, Color endColor) : this(start, end) {
            SetStops(startColor, endColor);
        }

        /// <inheritdoc/>
        public override Color Evaluate(Vector2 position) {
            Vector2 p = Pre(position);
            Vector2 axis = End - Start;
            float len2 = axis.LengthSquared();
            float t = len2 > 1e-12f ? Vector2.Dot(p - Start, axis) / len2 : 0f;
            return Sample(t);
        }
    }

    /// <summary>径向渐变：以 <see cref="Center"/> 为圆心、<see cref="Radius"/> 为 t = 1 的半径；可选焦点 <see cref="Focal"/>（SVG <c>fx / fy</c>）</summary>
    public sealed class RadialGradientPaint : GradientPaint
    {
        /// <summary>圆心</summary>
        public Vector2 Center { get; set; }
        /// <summary>t = 1 的半径</summary>
        public float Radius { get; set; }
        /// <summary>焦点（t = 0 的位置）；为空时与圆心重合</summary>
        public Vector2? Focal { get; set; }

        /// <summary>构造径向渐变</summary>
        public RadialGradientPaint(Vector2 center, float radius, IEnumerable<GradientStop> stops = null) {
            Center = center;
            Radius = radius;
            if (stops != null) {
                SetStops(stops);
            }
        }

        /// <summary>两色径向渐变</summary>
        public RadialGradientPaint(Vector2 center, float radius, Color centerColor, Color edgeColor) : this(center, radius) {
            SetStops(centerColor, edgeColor);
        }

        /// <inheritdoc/>
        public override Color Evaluate(Vector2 position) {
            Vector2 p = Pre(position);
            float r = MathF.Max(Radius, 1e-6f);
            if (!Focal.HasValue) {
                return Sample(Vector2.Distance(p, Center) / r);
            }
            //焦点模式：从焦点出发经过 p 的射线打到圆周的距离为 1，p 处的 t = |p - f| / |hit - f|
            Vector2 f = Focal.Value - Center;
            //焦点若落在圆外，按 SVG 规范拉回圆内
            float fl = f.Length();
            if (fl > r * 0.999f) {
                f *= r * 0.999f / fl;
            }
            Vector2 d = p - Center - f;
            float a = d.LengthSquared();
            if (a < 1e-12f) {
                return Sample(0f);
            }
            float b = 2f * Vector2.Dot(f, d);
            float c = f.LengthSquared() - r * r;
            float disc = MathF.Max(b * b - 4f * a * c, 0f);
            float s = (-b + MathF.Sqrt(disc)) / (2f * a);
            return Sample(s > 1e-6f ? 1f / s : 1f);
        }
    }
}
