using Microsoft.Xna.Framework;
using System;

namespace InnoVault.Vectors
{
    /// <summary>
    /// 路径空间 → 输出空间的仿射变换：先缩放、再旋转、最后平移（与 <c>center + (p * scale).RotatedBy(rotation)</c> 同义）
    /// <br/>描边宽度不受它影响，宽度永远以输出空间（像素 / 世界单位）计
    /// <br/>三角函数在构造时算好，逐点变换不再调 sin / cos
    /// </summary>
    public readonly struct VectorTransform
    {
        /// <summary>平移量，即路径原点落在输出空间的位置</summary>
        public readonly Vector2 Position;
        /// <summary>各轴缩放</summary>
        public readonly Vector2 Scale;
        /// <summary>旋转弧度，缩放之后施加</summary>
        public readonly float Rotation;
        private readonly float cos;
        private readonly float sin;

        /// <summary>恒等变换</summary>
        public static readonly VectorTransform Identity = new(Vector2.Zero, Vector2.One, 0f);

        /// <summary>构造一个变换</summary>
        public VectorTransform(Vector2 position, Vector2 scale, float rotation) {
            Position = position;
            Scale = scale;
            Rotation = rotation;
            if (rotation == 0f) {
                cos = 1f;
                sin = 0f;
            }
            else {
                cos = MathF.Cos(rotation);
                sin = MathF.Sin(rotation);
            }
        }

        /// <summary>构造一个等比缩放的变换</summary>
        public VectorTransform(Vector2 position, float scale, float rotation = 0f) : this(position, new Vector2(scale), rotation) { }

        /// <summary>只平移</summary>
        public static VectorTransform At(Vector2 position) => new(position, Vector2.One, 0f);

        /// <summary>平移 + 等比缩放 + 旋转</summary>
        public static VectorTransform At(Vector2 position, float scale, float rotation = 0f) => new(position, new Vector2(scale), rotation);

        /// <summary>是否为恒等变换</summary>
        public bool IsIdentity => Position == Vector2.Zero && Scale == Vector2.One && Rotation == 0f;

        /// <summary>两轴缩放的几何平均（含镜像时取绝对值），SVG 描边宽度随变换缩放时用它</summary>
        public float MeanScale => MathF.Sqrt(MathF.Abs(Scale.X * Scale.Y));

        /// <summary>变换一个点</summary>
        public Vector2 Apply(Vector2 point) {
            float x = point.X * Scale.X;
            float y = point.Y * Scale.Y;
            return new Vector2(x * cos - y * sin + Position.X, x * sin + y * cos + Position.Y);
        }

        /// <summary>变换一个方向（缩放 + 旋转，不平移），返回值不保证单位长度</summary>
        public Vector2 ApplyDirection(Vector2 direction) {
            float x = direction.X * Scale.X;
            float y = direction.Y * Scale.Y;
            return new Vector2(x * cos - y * sin, x * sin + y * cos);
        }

        /// <summary>把输出空间的点变回路径空间（<see cref="Apply"/> 的逆运算）；某轴缩放为 0 时该轴返回 0</summary>
        public Vector2 ApplyInverse(Vector2 point) {
            float dx = point.X - Position.X;
            float dy = point.Y - Position.Y;
            //逆旋转
            float x = dx * cos + dy * sin;
            float y = -dx * sin + dy * cos;
            x = MathF.Abs(Scale.X) > 1e-9f ? x / Scale.X : 0f;
            y = MathF.Abs(Scale.Y) > 1e-9f ? y / Scale.Y : 0f;
            return new Vector2(x, y);
        }

        /// <summary>转成等价的 4x4 矩阵（Z 不变）</summary>
        public Matrix ToMatrix()
            => Matrix.CreateScale(Scale.X, Scale.Y, 1f) * Matrix.CreateRotationZ(Rotation) * Matrix.CreateTranslation(Position.X, Position.Y, 0f);
    }

    /// <summary>开放路径两端的端帽形状</summary>
    public enum LineCap
    {
        /// <summary>平头，正好停在端点</summary>
        Butt,
        /// <summary>方头，沿切向外延半个宽度</summary>
        Square,
        /// <summary>圆头，半圆扇面</summary>
        Round,
        /// <summary>箭头，等腰三角形，尖长由 <see cref="StrokeStyle.CapLength"/> 决定（旧 <c>ArrowheadTrailGenerator</c> 的语义）</summary>
        Arrow,
    }

    /// <summary>折线内部转角的接头形状</summary>
    public enum LineJoin
    {
        /// <summary>
        /// 平均切向：转角处取前后两段切向的平均法线，单条连续条带、UV 连续、无重叠；
        /// 半宽按 <c>min(1 / cos(转角 / 2), <see cref="StrokeStyle.AveragedLimit"/>)</c> 放大以保持厚度，回折处不会掐成零宽。拖尾类效果的默认选择
        /// </summary>
        Averaged,
        /// <summary>尖接：同为单条带，半宽沿平均法线放大到外角尖点，放大倍数夹紧到 <see cref="StrokeStyle.MiterLimit"/>（SVG <c>stroke-miterlimit</c> 语义的夹紧版）</summary>
        Miter,
        /// <summary>圆接：每段各自出四边形，外角以扇面补圆；内侧有重叠，适合不透明或加法描边</summary>
        Round,
        /// <summary>斜接：每段各自出四边形，外角以单个三角形补平；内侧有重叠，适合不透明或加法描边</summary>
        Bevel,
    }

    /// <summary>描边沿路径方向（u 轴）的贴图坐标分布</summary>
    public enum StrokeUvMode
    {
        /// <summary>u 从 0 拉伸到 1 覆盖整条路径</summary>
        Stretch,
        /// <summary>u = 弧长 / <see cref="StrokeStyle.TileLength"/>，配合 Wrap 采样器沿路径平铺贴图</summary>
        Tile,
    }

    /// <summary>描边参数 t 沿路径的分布方式</summary>
    public enum StrokeParameterization
    {
        /// <summary>按弧长归一：t 与走过的距离成正比。SVG / Canvas 的标准语义</summary>
        ArcLength,
        /// <summary>按点序均分：第 i 个点的 t = i / (n - 1)，与点距无关。旧 <c>Trail</c> 的语义，拖尾按"点的年龄"衰减时用它</summary>
        PointIndex,
    }

    /// <summary>多子路径填充时判定内外的规则，与 SVG <c>fill-rule</c> 同义</summary>
    public enum FillRule
    {
        /// <summary>非零环绕：环绕数不为 0 的区域填充；同向嵌套的环被并入外环，反向嵌套的环成为孔</summary>
        NonZero,
        /// <summary>奇偶：被奇数个环包住的区域填充；任何嵌套一层的环都是孔</summary>
        EvenOdd,
    }

    /// <summary>
    /// 描边宽度函数。<paramref name="t"/> 为 0~1 的归一弧长（整条路径，多子路径连续计），返回该处的全宽
    /// <br/>注意旧 <c>TrailThicknessCalculator</c> 返回的是半宽，迁移时乘 2
    /// </summary>
    public delegate float StrokeWidthFunction(float t);

    /// <summary>
    /// 描边颜色函数。<paramref name="t"/> 为归一弧长；<paramref name="side"/> 为横向坐标：0 在法线正侧（<c>normal = (-tangent.Y, tangent.X)</c>），1 在负侧，与旧 <c>Trail</c> 顶点的 v 完全一致
    /// <br/>旧 <c>TrailColorEvaluator(Vector2 uv)</c> 迁移写法：<c>(t, side) => old(new Vector2(t, side))</c>
    /// </summary>
    public delegate Color StrokeColorFunction(float t, float side);

    /// <summary>填充颜色函数，参数为顶点在路径空间（变换前）的位置，便于在形状自身坐标里定义渐变</summary>
    public delegate Color FillColorFunction(Vector2 localPosition);

    /// <summary>
    /// 描边样式。实例可缓存复用，样式本身不持有任何 GPU 资源
    /// <br/>宽度、端帽长度、虚线长度、细分长度都以输出空间单位计，不随 <see cref="VectorTransform.Scale"/> 缩放
    /// <br/>颜色优先级：<see cref="ColorFunction"/> &gt; <see cref="Paint"/> &gt; <see cref="Color"/>
    /// </summary>
    public sealed class StrokeStyle
    {
        /// <summary>默认宽度</summary>
        public const float DefaultWidth = 2f;

        /// <summary>常量全宽，<see cref="WidthFunction"/> 为空时使用</summary>
        public float Width { get; set; } = DefaultWidth;
        /// <summary>按弧长变化的全宽，非空时覆盖 <see cref="Width"/></summary>
        public StrokeWidthFunction WidthFunction { get; set; }
        /// <summary>常量颜色，<see cref="ColorFunction"/> 与 <see cref="Paint"/> 都为空时使用</summary>
        public Color Color { get; set; } = Color.White;
        /// <summary>按弧长与横向位置变化的颜色，非空时优先级最高</summary>
        public StrokeColorFunction ColorFunction { get; set; }
        /// <summary>按位置求色的画笔（纯色 / 线性渐变 / 径向渐变），<see cref="ColorFunction"/> 为空时使用；位置空间见 <see cref="VectorPaint.Space"/></summary>
        public VectorPaint Paint { get; set; }
        /// <summary>起点端帽（仅开放路径）</summary>
        public LineCap StartCap { get; set; } = LineCap.Butt;
        /// <summary>终点端帽（仅开放路径）。旧 <c>Trail</c> 的箭头尖端对应 <see cref="LineCap.Arrow"/></summary>
        public LineCap EndCap { get; set; } = LineCap.Butt;
        /// <summary>箭头端帽的尖长；小于等于 0 时取该处全宽</summary>
        public float CapLength { get; set; }
        /// <summary>转角接头</summary>
        public LineJoin Join { get; set; } = LineJoin.Averaged;
        /// <summary><see cref="LineJoin.Miter"/> 下半宽放大倍数的上限，与 SVG <c>stroke-miterlimit</c> 同义（默认 4）</summary>
        public float MiterLimit { get; set; } = 4f;
        /// <summary><see cref="LineJoin.Averaged"/> 下半宽放大倍数的上限（默认 2：90° 转角内厚度恒定，更锐的角逐渐变薄但不掐死）</summary>
        public float AveragedLimit { get; set; } = 2f;
        /// <summary>t 的分布方式：默认按弧长；旧 <c>Trail</c> 迁移取 <see cref="StrokeParameterization.PointIndex"/></summary>
        public StrokeParameterization Parameterization { get; set; } = StrokeParameterization.ArcLength;
        /// <summary>弧长窗口起点（0~1），只描出 <c>[From, To]</c> 这一段，用于逐笔揭示</summary>
        public float From { get; set; } = 0f;
        /// <summary>弧长窗口终点（0~1）</summary>
        public float To { get; set; } = 1f;
        /// <summary>u 轴贴图模式</summary>
        public StrokeUvMode UvMode { get; set; } = StrokeUvMode.Stretch;
        /// <summary><see cref="StrokeUvMode.Tile"/> 下一个贴图周期对应的弧长，通常填贴图宽度</summary>
        public float TileLength { get; set; } = 64f;
        /// <summary>加在 u 上的偏移，递增即可让贴图沿路径流动</summary>
        public float UvOffset { get; set; }
        /// <summary>交换两侧的 v（0↔1），与旧 <c>Trail.SetFlipState</c> 同义</summary>
        public bool FlipV { get; set; }
        /// <summary>
        /// 虚线模式：交替的「实 / 空」弧长（输出空间单位），与 SVG <c>stroke-dasharray</c> 同义，奇数个元素自动首尾拼接成偶数周期；
        /// 为空或总和 ≤ 0 时为实线。虚线路径一律按开放路径处理（闭合路径的回合段也参与切分）
        /// </summary>
        public float[] Dash { get; set; }
        /// <summary>虚线模式的起始偏移（输出空间单位），递增即可让虚线沿路径流动，与 SVG <c>stroke-dashoffset</c> 同义</summary>
        public float DashOffset { get; set; }
        /// <summary>虚线的每一段是否都套 <see cref="StartCap"/> / <see cref="EndCap"/>；为 false 时只有路径本身的两端有端帽</summary>
        public bool DashCaps { get; set; } = true;
        /// <summary>
        /// 最大段长（输出空间单位，0 = 不细分）：预处理时把超长的直线段等分插点，
        /// 让径向渐变 <see cref="Paint"/>、逐顶点颜色函数与 SDF 着色器在长直段上也有足够的采样
        /// </summary>
        public float MaxSegmentLength { get; set; }

        /// <summary>创建一个常量宽度、常量颜色的样式</summary>
        public StrokeStyle() { }

        /// <summary>创建一个常量宽度、常量颜色的样式</summary>
        public StrokeStyle(float width, Color color) {
            Width = width;
            Color = color;
        }

        /// <summary>该处的全宽</summary>
        public float WidthAt(float t) => WidthFunction != null ? MathF.Max(WidthFunction(t), 0f) : Width;

        /// <summary>该处的颜色（忽略 <see cref="Paint"/>；需要按位置求色时用 <see cref="ColorAt(float, float, Vector2)"/>）</summary>
        public Color ColorAt(float t, float side) => ColorFunction != null ? ColorFunction(t, side) : Color;

        /// <summary>
        /// 该处的颜色：<see cref="ColorFunction"/> 优先，其次 <see cref="Paint"/>（在 <paramref name="paintPosition"/> 处求色），最后 <see cref="Color"/>
        /// </summary>
        /// <param name="t">归一弧长</param>
        /// <param name="side">横向位置 0~1</param>
        /// <param name="paintPosition">已换算到 <see cref="Paint"/> 所在空间的位置</param>
        public Color ColorAt(float t, float side, Vector2 paintPosition) {
            if (ColorFunction != null) {
                return ColorFunction(t, side);
            }
            if (Paint != null) {
                return Paint.Evaluate(paintPosition);
            }
            return Color;
        }

        /// <summary>是否需要按位置求色（有 <see cref="Paint"/> 且没有更高优先级的 <see cref="ColorFunction"/>）</summary>
        public bool UsesPaint => ColorFunction == null && Paint != null;

        /// <summary>是否启用虚线</summary>
        public bool IsDashed => Dash != null && Dash.Length > 0 && DashTotal > 0f;

        /// <summary>一个虚线周期的总长（奇数个元素按 SVG 规则翻倍）</summary>
        public float DashTotal {
            get {
                if (Dash == null || Dash.Length == 0) {
                    return 0f;
                }
                float sum = 0f;
                for (int i = 0; i < Dash.Length; i++) {
                    sum += MathF.Max(Dash[i], 0f);
                }
                return (Dash.Length & 1) == 1 ? sum * 2f : sum;
            }
        }

        /// <summary>窗口是否覆盖整条路径</summary>
        public bool IsFullWindow => From <= 0f && To >= 1f;

        /// <summary>复制一份，便于在共享样式上做局部改动</summary>
        public StrokeStyle Clone() => (StrokeStyle)MemberwiseClone();

        /// <summary>把 <paramref name="other"/> 的全部字段复制到本实例（复用暂存样式、避免每帧分配）；<see cref="Dash"/> 数组按引用共享</summary>
        public void CopyFrom(StrokeStyle other) {
            if (other == null || ReferenceEquals(other, this)) {
                return;
            }
            Width = other.Width;
            WidthFunction = other.WidthFunction;
            Color = other.Color;
            ColorFunction = other.ColorFunction;
            Paint = other.Paint;
            StartCap = other.StartCap;
            EndCap = other.EndCap;
            CapLength = other.CapLength;
            Join = other.Join;
            MiterLimit = other.MiterLimit;
            AveragedLimit = other.AveragedLimit;
            Parameterization = other.Parameterization;
            From = other.From;
            To = other.To;
            UvMode = other.UvMode;
            TileLength = other.TileLength;
            UvOffset = other.UvOffset;
            FlipV = other.FlipV;
            Dash = other.Dash;
            DashOffset = other.DashOffset;
            DashCaps = other.DashCaps;
            MaxSegmentLength = other.MaxSegmentLength;
        }
    }

    /// <summary>
    /// 填充样式。实例可缓存复用
    /// <br/>颜色优先级：<see cref="ColorFunction"/> &gt; <see cref="Paint"/> &gt; <see cref="Color"/>
    /// </summary>
    public sealed class FillStyle
    {
        /// <summary>常量颜色，<see cref="ColorFunction"/> 与 <see cref="Paint"/> 都为空时使用</summary>
        public Color Color { get; set; } = Color.White;
        /// <summary>按路径空间位置变化的颜色，非空时优先级最高</summary>
        public FillColorFunction ColorFunction { get; set; }
        /// <summary>按位置求色的画笔（纯色 / 线性渐变 / 径向渐变）；位置空间见 <see cref="VectorPaint.Space"/></summary>
        public VectorPaint Paint { get; set; }
        /// <summary>多子路径的内外判定规则</summary>
        public FillRule Rule { get; set; } = FillRule.NonZero;
        /// <summary>
        /// 最大三角形边长（输出空间单位，0 = 不细分）：有 <see cref="Paint"/> / <see cref="ColorFunction"/> 时把填充三角形均匀细分到边长不超过它，
        /// 让渐变在形状内部也有采样点（颜色逐顶点插值，轮廓上的顶点撑不出内部的径向渐变）；最多细分 3 级（64 倍三角形）
        /// </summary>
        public float MaxTriangleEdge { get; set; }

        /// <summary>是否按位置求色（细分才有意义）</summary>
        public bool UsesPositionColor => ColorFunction != null || Paint != null;

        /// <summary>创建白色填充</summary>
        public FillStyle() { }

        /// <summary>创建常量颜色填充</summary>
        public FillStyle(Color color) {
            Color = color;
        }

        /// <summary>该点的颜色（忽略 <see cref="Paint"/> 的输出空间模式；完整求色用 <see cref="ColorAt(Vector2, Vector2)"/>）</summary>
        public Color ColorAt(Vector2 localPosition) => ColorAt(localPosition, localPosition);

        /// <summary>该点的颜色：<see cref="ColorFunction"/>（路径空间）优先，其次 <see cref="Paint"/>（按其空间选位置），最后 <see cref="Color"/></summary>
        public Color ColorAt(Vector2 localPosition, Vector2 outputPosition) {
            if (ColorFunction != null) {
                return ColorFunction(localPosition);
            }
            if (Paint != null) {
                return Paint.Evaluate(Paint.Space == PaintSpace.Path ? localPosition : outputPosition);
            }
            return Color;
        }

        /// <summary>复制一份</summary>
        public FillStyle Clone() => (FillStyle)MemberwiseClone();

        /// <summary>把 <paramref name="other"/> 的全部字段复制到本实例</summary>
        public void CopyFrom(FillStyle other) {
            if (other == null || ReferenceEquals(other, this)) {
                return;
            }
            Color = other.Color;
            ColorFunction = other.ColorFunction;
            Paint = other.Paint;
            Rule = other.Rule;
            MaxTriangleEdge = other.MaxTriangleEdge;
        }
    }
}
