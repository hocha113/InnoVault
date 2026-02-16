using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using Terraria;

namespace InnoVault.Trails
{
    /// <summary>
    /// 一个传递平滑点集方式的委托
    /// </summary>
    /// <param name="controlPoints"></param>
    /// <param name="offset"></param>
    /// <param name="totalPoints"></param>
    /// <param name="rotations"></param>
    /// <returns></returns>
    public delegate List<Vector2> PathPointRetrievalDelegation(IEnumerable<Vector2> controlPoints
            , Vector2 offset, int totalPoints, IEnumerable<float> rotations = null);
    /// <summary>
    /// 一个用于传递纹理映射采样方式的委托
    /// </summary>
    /// <param name="t"></param>
    /// <param name="leftTexCoord"></param>
    /// <param name="rightTexCoord"></param>
    public delegate void HandlerTexturePossDelegation(float t, out Vector2 leftTexCoord, out Vector2 rightTexCoord);

    /// <summary>
    /// 路径网格生成器接口，用于定义生成路径网格数据的行为。任何实现此接口的类都需要提供生成路径网格顶点和索引的逻辑
    /// </summary>
    public interface IMeshTrailGenerator
    {
        /// <summary>
        /// 获取额外的索引数量实现此接口的类可以根据需要计算并返回额外的索引数量，用于生成路径网格时的索引数据
        /// </summary>
        int AdditionalIndexCount { get; }
        /// <summary>
        /// 获取额外的顶点数量实现此接口的类可以根据需要计算并返回额外的顶点数量，用于生成路径网格时的顶点数据
        /// </summary>
        int AdditionalVertexCount { get; }
        /// <summary>
        /// 创建路径网格数据实现此方法的类应根据路径的尖端位置、法线以及其他计算函数来生成路径的网格顶点和索引
        /// </summary>
        /// <param name="trailTipPosition">路径尖端的世界空间位置，表示路径的起始点</param>
        /// <param name="trailTipNormal">路径尖端的法线，表示路径的方向和旋转</param>
        /// <param name="startFromIndex">从哪个索引开始插入新顶点，用于组织网格的顶点顺序</param>
        /// <param name="vertices">输出的顶点数组，包含生成的路径网格的所有顶点</param>
        /// <param name="indices">输出的索引数组，表示如何连接顶点来形成路径网格的三角形</param>
        /// <param name="trailWidthFunction">路径宽度计算函数，根据路径的时间、位置等因素计算路径的宽度</param>
        /// <param name="trailColorFunction">路径颜色计算函数，根据路径的时间、位置等因素计算路径的颜色</param>
        void CreateMesh(Vector2 trailTipPosition, Vector2 trailTipNormal, int startFromIndex
            , out VertexPositionColorTexture[] vertices, out short[] indices
            , TrailThicknessCalculator trailWidthFunction, TrailColorEvaluator trailColorFunction);
    }

    /// <summary>
    /// 计算路径上的指定进度点的厚度
    /// 用于动态生成路径的厚度，常见于轨迹、光效等效果的宽度计算
    /// </summary>
    /// <param name="progressAlongPath">
    /// 路径上的进度，范围通常为 0 到 1，表示从起点到终点的相对位置
    /// </param>
    /// <returns>
    /// 返回路径在指定进度位置的厚度
    /// </returns>
    public delegate float TrailThicknessCalculator(float progressAlongPath);

    /// <summary>
    /// 根据纹理坐标计算路径上指定点的颜色
    /// 用于路径的动态着色，例如渐变效果或根据纹理采样动态生成颜色
    /// </summary>
    /// <param name="textureCoordinates">
    /// 纹理坐标，通常范围为 (0, 0) 到 (1, 1)，表示路径中某点的相对位置
    /// </param>
    /// <returns>
    /// 返回路径在指定纹理坐标上的颜色值
    /// </returns>
    public delegate Color TrailColorEvaluator(Vector2 textureCoordinates);

    /// <summary>
    /// 表示一个动态轨迹的渲染器，可以生成带有宽度和颜色变化的轨迹网格
    /// </summary>
    /// <remarks>
    /// Trail 类主要用于渲染动态的轨迹效果（例如武器挥舞的光效、投射物的轨迹等）
    /// 它通过一组顶点和索引构建网格，并使用指定的宽度和颜色计算委托来动态调整轨迹外观
    /// 同时也支持基于贝塞尔曲线的路径效果绘制，以及自定义纹理坐标映射等高级功能
    /// </remarks>
    public class Trail : IDisposable
    {
        /// <summary>
        /// 轨迹的默认宽度
        /// </summary>
        public const float DefaultTrailWidth = 16;
        private readonly MeshRenderer renderer;
        private readonly int maxPoints;
        private readonly IMeshTrailGenerator tipHandler;
        private readonly TrailThicknessCalculator calculateWidth;
        private readonly TrailColorEvaluator calculateColor;
        private bool isFlippedVertically;
        private Vector2[] _trailPositions;
        /// <summary>
        /// 定义轨迹的顶点位置数组
        /// </summary>
        /// <value>
        /// 注意：`TrailPositions[Positions.Length - 1]` 假定为轨迹的起点（例如 `Projectile.Center`），
        /// 而 `TrailPositions[0]` 假定为轨迹的末尾
        /// </value>
        /// <exception cref="ArgumentException">
        /// 如果设置的数组长度与最大顶点数不匹配，则抛出异常
        /// </exception>
        public Vector2[] TrailPositions {
            get => _trailPositions;
            set {
                if (value.Length != maxPoints) {
                    throw new ArgumentException("The length of the position array is different from the expected result.");
                }
                _trailPositions = value;
            }
        }

        /// <summary>
        /// <para>用于从最前面的位置计算法线，因为在原始列表中它后面没有点</para>
        /// </summary>
        public Vector2 ExtrapolatedStart { get; set; }

        /// <summary>
        /// 竖直方向上进行翻转，仅需调用一次
        /// </summary>
        public void ToggleVerticalFlip() => isFlippedVertically = !isFlippedVertically;
        /// <summary>
        /// 竖直方向上进行翻转
        /// </summary>
        public void SetFlipState(bool flip) => isFlippedVertically = flip;

        /// <summary>
        /// 初始化一个新的 <see cref="Trail"/> 实例
        /// </summary>
        /// <param name="device">渲染所需的 GraphicsDevice（例如 `Main.graphics.GraphicsDevice`）</param>
        /// <param name="maxPointCount">轨迹的最大顶点数</param>
        /// <param name="tip">用于处理轨迹尖端的生成器</param>
        /// <param name="trailWidthFunction">用于计算轨迹宽度的委托</param>
        /// <param name="trailColorFunction">用于计算轨迹颜色的委托</param>
        /// <param name="flipVertical">是否启用竖直翻转</param>
        public Trail(GraphicsDevice device, int maxPointCount, IMeshTrailGenerator tip, TrailThicknessCalculator trailWidthFunction, TrailColorEvaluator trailColorFunction, bool flipVertical = false) {
            tipHandler = tip ?? new EmptyMeshGenerator();
            maxPoints = maxPointCount;
            calculateWidth = trailWidthFunction;
            calculateColor = trailColorFunction;
            isFlippedVertically = flipVertical;
            renderer = new MeshRenderer(device, maxPointCount * 2 + tipHandler.AdditionalVertexCount, 6 * (maxPointCount - 1) + tipHandler.AdditionalIndexCount);
        }

        /// <summary>
        /// 初始化一个新的 <see cref="Trail"/> 实例
        /// </summary>
        /// <param name="device">渲染所需的 GraphicsDevice（例如 `Main.graphics.GraphicsDevice`）</param>
        /// <param name="points">渲染需要的点集</param>
        /// <param name="trailWidthFunction">用于计算轨迹宽度的委托</param>
        /// <param name="trailColorFunction">用于计算轨迹颜色的委托</param>
        /// <param name="tip">用于处理轨迹尖端的生成器</param>
        /// <param name="flipVertical">是否启用竖直翻转</param>
        public Trail(Vector2[] points, TrailThicknessCalculator trailWidthFunction, TrailColorEvaluator trailColorFunction, GraphicsDevice device = null, IMeshTrailGenerator tip = null, bool flipVertical = false) {
            tipHandler = tip ?? new EmptyMeshGenerator();
            maxPoints = points.Length;
            TrailPositions = points;
            calculateWidth = trailWidthFunction;
            calculateColor = trailColorFunction;
            isFlippedVertically = flipVertical;
            renderer = new MeshRenderer(device ?? Main.graphics.GraphicsDevice, maxPoints * 2 + tipHandler.AdditionalVertexCount, 6 * (maxPoints - 1) + tipHandler.AdditionalIndexCount);
        }

        /// <summary>
        /// 构建轨迹的顶点和索引数据
        /// </summary>
        /// <param name="vertices">生成的顶点数组</param>
        /// <param name="indices">生成的索引数组</param>
        /// <param name="vertexOffset">生成顶点数组的偏移量</param>
        private void BuildTrailMesh(out VertexPositionColorTexture[] vertices, out short[] indices, out int vertexOffset) {
            var localVertices = new VertexPositionColorTexture[maxPoints * 2];
            var localIndices = new short[(maxPoints - 1) * 6];

            int topTexCoord = isFlippedVertically ? 1 : 0;
            int bottomTexCoord = isFlippedVertically ? 0 : 1;

            for (int i = 0; i < TrailPositions.Length; i++) {
                float factor = (float)i / (TrailPositions.Length - 1);
                float width = calculateWidth?.Invoke(factor) ?? DefaultTrailWidth;

                Vector2 current = TrailPositions[i];
                Vector2 next = i == TrailPositions.Length - 1
                    ? current + (current - TrailPositions[i - 1])
                    : TrailPositions[i + 1];

                Vector2 direction = (next - current).SafeNormalize(Vector2.Zero);
                Vector2 perpendicular = direction.RotatedBy(MathHelper.PiOver2);

                Vector2 topVertex = current + perpendicular * width;
                Vector2 bottomVertex = current - perpendicular * width;

                Vector2 topUV = new(factor, topTexCoord);
                Vector2 bottomUV = new(factor, bottomTexCoord);

                Color topColor = calculateColor?.Invoke(topUV) ?? Color.White;
                Color bottomColor = calculateColor?.Invoke(bottomUV) ?? Color.White;

                localVertices[i] = new VertexPositionColorTexture(topVertex.ToVector3(), topColor, topUV);
                localVertices[i + maxPoints] = new VertexPositionColorTexture(bottomVertex.ToVector3(), bottomColor, bottomUV);
            }

            for (short i = 0; i < maxPoints - 1; i++) {
                localIndices[i * 6 + 0] = (short)(i + maxPoints);
                localIndices[i * 6 + 1] = (short)(i + maxPoints + 1);
                localIndices[i * 6 + 2] = (short)(i + 1);

                localIndices[i * 6 + 3] = (short)(i + 1);
                localIndices[i * 6 + 4] = i;
                localIndices[i * 6 + 5] = (short)(i + maxPoints);
            }

            vertices = localVertices;
            indices = localIndices;
            vertexOffset = localVertices.Length;
        }

        /// <summary>
        /// 准备轨迹的网格数据，包括尖端处理
        /// </summary>
        private void PrepareMesh() {
            BuildTrailMesh(out var trailVertices, out var trailIndices, out var offset);

            Vector2 extrapolatedDirection = (TrailPositions[^1] - TrailPositions[^2]).SafeNormalize(Vector2.Zero);
            tipHandler.CreateMesh(TrailPositions[^1], extrapolatedDirection, offset,
                out var tipVertices, out var tipIndices, calculateWidth, calculateColor);

            renderer.UpdateVertexBuffer(trailVertices.Concat(tipVertices).ToArray());
            renderer.UpdateIndexBuffer(trailIndices.Concat(tipIndices).ToArray());
        }

        /// <summary>
        /// 渲染轨迹
        /// </summary>
        /// <param name="effect">用于渲染的 Effect（例如 Shader）</param>
        public void DrawTrail(Effect effect) {
            if (TrailPositions == null && !(renderer?.CanDisposed ?? true)) {
                return;
            }
            PrepareMesh();
            renderer.Draw(effect);
        }

        /// <summary>
        /// 释放轨迹的资源
        /// </summary>
        public void Dispose() => renderer?.Dispose();

        #region PathEffect 功能

        /// <summary>
        /// 贝塞尔曲线求值函数，通过 De Casteljau 算法计算曲线上的点
        /// </summary>
        /// <param name="points">控制点数组</param>
        /// <param name="T">参数 t，范围 [0, 1]</param>
        /// <returns>曲线上对应参数 t 的点</returns>
        public static Vector2 BezierEvaluate(Vector2[] points, float T) {
            while (points.Length > 2) {
                Vector2[] nextPoints = new Vector2[points.Length - 1];
                for (int i = 0; i < points.Length - 1; i++) {
                    nextPoints[i] = Vector2.Lerp(points[i], points[i + 1], T);
                }
                points = nextPoints;
            }
            return Vector2.Lerp(points[0], points[1], T);
        }

        /// <summary>
        /// 使用贝塞尔曲线对控制点进行平滑采样，生成指定数量的路径点
        /// </summary>
        /// <param name="controlPoints">控制点数组</param>
        /// <param name="totalPoints">需要采样的总点数</param>
        /// <returns>平滑路径点列表</returns>
        public static List<Vector2> SampleBezierPoints(Vector2[] controlPoints, int totalPoints) {
            float perStep = 1f / totalPoints;
            List<Vector2> points = new();
            for (float step = 0f; step <= 1f; step += perStep) {
                Vector2 bezierPoint = BezierEvaluate(controlPoints, MathHelper.Clamp(step, 0f, 1f));
                points.Add(bezierPoint);
            }
            return points;
        }

        /// <summary>
        /// 使用平滑贝塞尔曲线生成路径点，过滤零向量并应用偏移
        /// </summary>
        /// <param name="controlPoints">原始控制点</param>
        /// <param name="offset">偏移量</param>
        /// <param name="totalPoints">采样总点数</param>
        /// <param name="rotations">预留的旋转参数（暂未使用）</param>
        /// <returns>平滑路径点列表</returns>
        public static List<Vector2> GenerateSmoothPath(IEnumerable<Vector2> controlPoints, Vector2 offset, int totalPoints, IEnumerable<float> rotations = null) {
            List<Vector2> adjustedPoints = controlPoints.Where(p => p != Vector2.Zero).Select(p => p + offset).ToList();
            if (adjustedPoints.Count < 2) {
                return adjustedPoints;
            }
            return SampleBezierPoints(adjustedPoints.ToArray(), totalPoints);
        }

        /// <summary>
        /// 默认的纹理映射采样方式
        /// </summary>
        /// <param name="t">路径进度 [0, 1]</param>
        /// <param name="leftTexCoord">左侧纹理坐标</param>
        /// <param name="rightTexCoord">右侧纹理坐标</param>
        public static void DefaultHandlerTexturePoss(float t, out Vector2 leftTexCoord, out Vector2 rightTexCoord) {
            leftTexCoord = new Vector2(t, 0f);
            rightTexCoord = new Vector2(t, 1f);
        }

        /// <summary>
        /// 默认的参数化坐标函数（简单映射到直线坐标）
        /// </summary>
        /// <param name="t">路径进度 [0, 1]</param>
        /// <returns>参数化后的二维坐标</returns>
        public static Vector2 DefaultParametricPosition(float t) => new Vector2(t, t);

        /// <summary>
        /// 计算透视投影和视图矩阵，用于2D渲染的场景转换
        /// </summary>
        /// <param name="viewMatrix">输出的视图矩阵</param>
        /// <param name="projectionMatrix">输出的投影矩阵</param>
        public static void CalculateRenderingMatrices(out Matrix viewMatrix, out Matrix projectionMatrix) {
            Vector2 zoom = Main.GameViewMatrix.Zoom;
            Matrix zoomScaleMatrix = Matrix.CreateScale(zoom.X, zoom.Y, 1f);

            int width = Main.instance.GraphicsDevice.Viewport.Width;
            int height = Main.instance.GraphicsDevice.Viewport.Height;

            viewMatrix = Matrix.CreateLookAt(Vector3.Zero, Vector3.UnitZ, Vector3.Up);
            viewMatrix *= Matrix.CreateTranslation(0f, -height, 0f);
            viewMatrix *= Matrix.CreateRotationZ(MathHelper.Pi);

            if (Main.LocalPlayer.gravDir < 0f) {
                viewMatrix *= Matrix.CreateScale(1f, -1f, 1f) * Matrix.CreateTranslation(0f, height, 0f);
            }

            viewMatrix *= zoomScaleMatrix;
            projectionMatrix = Matrix.CreateOrthographicOffCenter(0f, width * zoom.X, 0f, height * zoom.Y, 0f, 1f) * zoomScaleMatrix;
        }

        /// <summary>
        /// 根据路径点生成交错布局的顶点数组和索引数组，使用 <see cref="ColoredVertex"/> 顶点格式，
        /// 适用于 <see cref="GraphicsDevice.DrawUserIndexedPrimitives{ColoredVertex}(PrimitiveType, ColoredVertex[], int, int, short[], int, int)"/> 轻量渲染
        /// </summary>
        /// <param name="pathPoints">路径点列表</param>
        /// <param name="thicknessEvaluator">宽度计算委托</param>
        /// <param name="colorEvaluator">颜色计算委托</param>
        /// <param name="vertices">输出的顶点数组</param>
        /// <param name="indices">输出的索引数组</param>
        /// <param name="parametricPositionFunction">参数化位置函数，为 null 时使用 <see cref="DefaultParametricPosition"/></param>
        /// <param name="handlerTexturePoss">纹理坐标映射委托，为 null 时使用 <see cref="DefaultHandlerTexturePoss"/></param>
        /// <param name="stickPoint">起点粘合点，为 <see cref="Vector2.Zero"/> 时不启用</param>
        public static void GenerateInterleavedMesh(List<Vector2> pathPoints
            , TrailThicknessCalculator thicknessEvaluator, TrailColorEvaluator colorEvaluator
            , out ColoredVertex[] vertices, out short[] indices
            , Func<float, Vector2> parametricPositionFunction = null
            , HandlerTexturePossDelegation handlerTexturePoss = null
            , Vector2 stickPoint = default) {
            parametricPositionFunction ??= DefaultParametricPosition;
            handlerTexturePoss ??= DefaultHandlerTexturePoss;

            vertices = new ColoredVertex[pathPoints.Count * 2 - 2];
            for (int i = 0; i < pathPoints.Count - 1; i++) {
                float t = (float)i / (pathPoints.Count - 1);
                float width = thicknessEvaluator(t);
                Color color = colorEvaluator(parametricPositionFunction(t));

                Vector2 current = pathPoints[i];
                Vector2 direction = Utils.SafeNormalize(pathPoints[i + 1] - current, Vector2.Zero);

                handlerTexturePoss.Invoke(t, out Vector2 leftTexCoord, out Vector2 rightTexCoord);

                Vector2 sideOffset = new Vector2(-direction.Y, direction.X) * width;

                Vector2 left = current - sideOffset;
                Vector2 right = current + sideOffset;

                if (i == 0 && stickPoint != Vector2.Zero) {
                    left = stickPoint;
                    right = stickPoint;
                }

                vertices[i * 2] = new ColoredVertex(left, color, leftTexCoord.ToVector3());
                vertices[i * 2 + 1] = new ColoredVertex(right, color, rightTexCoord.ToVector3());
            }

            indices = new short[(pathPoints.Count - 1) * 6];
            for (int i = 0; i < pathPoints.Count - 2; i++) {
                int startIndex = i * 6;
                int vertexIndex = i * 2;

                indices[startIndex] = (short)vertexIndex;
                indices[startIndex + 1] = (short)(vertexIndex + 1);
                indices[startIndex + 2] = (short)(vertexIndex + 2);

                indices[startIndex + 3] = (short)(vertexIndex + 2);
                indices[startIndex + 4] = (short)(vertexIndex + 1);
                indices[startIndex + 5] = (short)(vertexIndex + 3);
            }
        }

        /// <summary>
        /// 使用 <see cref="GraphicsDevice.DrawUserIndexedPrimitives{T}(PrimitiveType, T[], int, int, short[], int, int)"/> 轻量方式渲染 <see cref="ColoredVertex"/> 网格数据，
        /// 无需 GPU 缓冲区管理
        /// </summary>
        /// <param name="vertices">顶点数组</param>
        /// <param name="indices">索引数组</param>
        /// <param name="device">图形设备，为 null 时使用 <see cref="Main.instance"/>.GraphicsDevice</param>
        public static void DrawUserPrimitives(ColoredVertex[] vertices, short[] indices, GraphicsDevice device = null) {
            if (vertices == null || indices == null || vertices.Length < 3 || indices.Length < 3) {
                return;
            }

            device ??= Main.instance.GraphicsDevice;
            device.RasterizerState = RasterizerState.CullNone;
            device.DrawUserIndexedPrimitives(PrimitiveType.TriangleList, vertices, 0
                , vertices.Length, indices, 0, indices.Length / 3);
        }

        #endregion
    }

    /// <summary>
    /// 生成一个箭头形状的尾迹尖端尾迹尖端的形状为一个朝向尾迹方向的箭头形三角形
    /// </summary>
    public class ArrowheadTrailGenerator : IMeshTrailGenerator
    {
        /// <summary>
        /// 该尾迹尖端的附加顶点数量（一个三角形的三个顶点）
        /// </summary>
        public int AdditionalVertexCount => 3;
        /// <summary>
        /// 该尾迹尖端的附加索引数量（一个三角形的三个索引）
        /// </summary>
        public int AdditionalIndexCount => 3;
        //仅仅在实例化时赋值
        private readonly float length;
        /// <summary>
        /// 初始化箭头形尾迹尖端生成器
        /// </summary>
        /// <param name="length">箭头尖端的长度，表示三角形的底边长度</param>
        public ArrowheadTrailGenerator(float length) {
            this.length = length;
        }

        /// <summary>
        /// 生成箭头形尾迹网格这个尾迹网格通过一个尖端朝向尾迹方向的三角形表示
        /// </summary>
        /// <param name="trailTipPosition">尾迹尖端的世界位置</param>
        /// <param name="trailTipNormal">尾迹尖端的法线方向</param>
        /// <param name="startFromIndex">索引起始位置</param>
        /// <param name="vertices">返回的顶点数组</param>
        /// <param name="indices">返回的索引数组</param>
        /// <param name="trailWidthFunction">尾迹宽度函数，用于计算宽度</param>
        /// <param name="trailColorFunction">尾迹颜色评估函数，用于计算每个顶点的颜色</param>
        public void CreateMesh(Vector2 trailTipPosition, Vector2 trailTipNormal, int startFromIndex, out VertexPositionColorTexture[] vertices
            , out short[] indices, TrailThicknessCalculator trailWidthFunction, TrailColorEvaluator trailColorFunction) {
            // 计算与尾迹法线方向垂直的单位向量，用于三角形的左右两边
            Vector2 normalPerp = trailTipNormal.RotatedBy(MathHelper.PiOver2);

            // 获取尾迹宽度，若未提供宽度函数则默认为1
            float width = trailWidthFunction?.Invoke(1) ?? 1;

            // 计算三角形的三个顶点位置
            Vector2 a = trailTipPosition + (normalPerp * width);  // 左边的顶点
            Vector2 b = trailTipPosition - (normalPerp * width);  // 右边的顶点
            Vector2 c = trailTipPosition + (trailTipNormal * length);  // 尖端顶点

            // 纹理坐标，A、B、C 分别对应三角形的三个顶点
            Vector2 texCoordA = Vector2.UnitX;
            Vector2 texCoordB = Vector2.One;
            Vector2 texCoordC = new(1, 0.5f);  // 通过调整C点的纹理坐标来修复纹理问题

            // 获取每个顶点的颜色，若未提供颜色评估函数则默认使用白色
            Color colorA = trailColorFunction?.Invoke(texCoordA) ?? Color.White;
            Color colorB = trailColorFunction?.Invoke(texCoordB) ?? Color.White;
            Color colorC = trailColorFunction?.Invoke(texCoordC) ?? Color.White;

            // 生成顶点数组，每个顶点包含位置、颜色和纹理坐标
            vertices = new VertexPositionColorTexture[]
            {
            new(a.ToVector3(), colorA, texCoordA),  // 顶点A
            new(b.ToVector3(), colorB, texCoordB),  // 顶点B
            new(c.ToVector3(), colorC, texCoordC)   // 顶点C
            };

            // 生成索引数组，定义一个三角形的顶点顺序
            indices = [
            (short)startFromIndex,     // 顶点A的索引
            (short)(startFromIndex + 1), // 顶点B的索引
            (short)(startFromIndex + 2)  // 顶点C的索引
            ];
        }
    }

    /// <summary>
    /// 一个空的路径网格生成器实现，作为默认实现，不生成任何顶点和索引。该类可以用于作为占位符，或在不需要生成网格时使用
    /// </summary>
    public class EmptyMeshGenerator : IMeshTrailGenerator
    {
        /// <summary>
        /// 获取额外的顶点数量。对于此实现，该值始终为0，因为不生成任何顶点
        /// </summary>
        public int AdditionalVertexCount => 0;
        /// <summary>
        /// 获取额外的索引数量。对于此实现，该值始终为0，因为不生成任何索引
        /// </summary>
        public int AdditionalIndexCount => 0;
        /// <summary>
        /// 创建网格数据。由于该类为占位符实现，因此该方法不会生成任何顶点或索引
        /// </summary>
        /// <param name="trailTipPosition">路径尖端的世界空间位置，用于确定路径的起始位置</param>
        /// <param name="trailTipNormal">路径尖端的法线，用于确定路径的方向</param>
        /// <param name="startFromIndex">从该索引开始插入新顶点，用于确定网格的索引顺序</param>
        /// <param name="vertices">生成的顶点数组，在此实现中始终为空数组</param>
        /// <param name="indices">生成的索引数组，在此实现中始终为空数组</param>
        /// <param name="trailWidthFunction">路径宽度计算函数，根据路径的位置、时间等因素计算路径的宽度</param>
        /// <param name="trailColorFunction">路径颜色计算函数，根据路径的位置、时间等因素计算路径的颜色</param>
        public void CreateMesh(Vector2 trailTipPosition, Vector2 trailTipNormal
            , int startFromIndex, out VertexPositionColorTexture[] vertices
            , out short[] indices, TrailThicknessCalculator trailWidthFunction, TrailColorEvaluator trailColorFunction) {
            // 该实现不生成任何顶点和索引，因此返回空数组
            vertices = [];
            indices = [];
        }
    }

    /// <summary>
    /// 用于渲染网格的类，支持动态更新顶点和索引缓冲区，并在渲染时将数据提交给显卡进行绘制
    /// 该类负责管理和处理网格渲染所需的顶点和索引数据缓冲区，并能够高效地更新这些数据
    /// </summary>
    public class MeshRenderer : IDisposable
    {
        /// <summary>
        /// 指示该对象是否可以被销毁只有在调用 <see cref="ReleaseResources"/> 后，才能标记为可销毁
        /// </summary>
        public bool CanDisposed { get; private set; }
        /// <summary>
        /// 存储顶点数据的动态缓冲区，用于渲染网格的顶点信息
        /// </summary>
        private DynamicVertexBuffer vertexDataBuffer;
        /// <summary>
        /// 存储索引数据的动态缓冲区，用于渲染网格的索引信息
        /// </summary>
        private DynamicIndexBuffer indexDataBuffer;
        /// <summary>
        /// 渲染网格所需的图形设备实例
        /// </summary>
        private readonly GraphicsDevice device;
        /// <summary>
        /// 构造一个 <see cref="MeshRenderer"/> 实例，初始化顶点和索引缓冲区
        /// </summary>
        /// <param name="device">图形设备实例，渲染过程中用于处理 GPU 操作</param>
        /// <param name="maxVertices">顶点数据缓冲区的最大顶点数</param>
        /// <param name="maxIndices">索引数据缓冲区的最大索引数</param>
        public MeshRenderer(GraphicsDevice device, int maxVertices, int maxIndices) {
            this.device = device;

            if (device != null && !Main.dedServ) {
                // 在主线程上初始化缓冲区
                Main.QueueMainThreadAction(() => {
                    vertexDataBuffer = new DynamicVertexBuffer(device, typeof(VertexPositionColorTexture), maxVertices, BufferUsage.None);
                    indexDataBuffer = new DynamicIndexBuffer(device, IndexElementSize.SixteenBits, maxIndices, BufferUsage.None);
                });
            }
        }

        /// <summary>
        /// 使用指定的效果渲染网格数据
        /// </summary>
        /// <param name="effect">用于渲染的效果（通常是一个Shader），包含渲染网格所需的着色器程序</param>
        public void Draw(Effect effect) {
            if (vertexDataBuffer is null || indexDataBuffer is null) {
                return;
            }

            // 设置顶点和索引缓冲区
            device.SetVertexBuffer(vertexDataBuffer);
            device.Indices = indexDataBuffer;

            // 渲染网格
            foreach (EffectPass pass in effect.CurrentTechnique.Passes) {
                pass.Apply();
                device.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, vertexDataBuffer.VertexCount, 0, indexDataBuffer.IndexCount / 3);
            }
        }

        /// <summary>
        /// 更新顶点缓冲区的数据
        /// </summary>
        /// <param name="vertices">新的顶点数据，包含了网格的顶点位置、颜色和纹理坐标等信息</param>
        public void UpdateVertexBuffer(VertexPositionColorTexture[] vertices) {
            if (vertexDataBuffer == null) {
                return;
            }
            // 计算顶点数据的偏移量和大小
            int vertexStride = VertexPositionColorTexture.VertexDeclaration.VertexStride;
            int vertexOffset = 0;

            // 更新顶点缓冲区的数据
            vertexDataBuffer.SetData(vertexOffset, vertices, 0, vertices.Length, vertexStride, SetDataOptions.NoOverwrite);
        }

        /// <summary>
        /// 更新索引缓冲区的数据
        /// </summary>
        /// <param name="indices">新的索引数据，表示如何连接顶点形成三角形</param>
        public void UpdateIndexBuffer(short[] indices) {
            if (indexDataBuffer == null) {
                return;
            }
            int indexOffset = 0;

            // 更新索引缓冲区的数据
            indexDataBuffer.SetData(indexOffset, indices, 0, indices.Length, SetDataOptions.Discard);
        }

        /// <summary>
        /// 释放资源，释放顶点和索引缓冲区
        /// </summary>
        public void ReleaseResources() {
            CanDisposed = true;
            vertexDataBuffer?.Dispose();
            indexDataBuffer?.Dispose();
        }

        /// <summary>
        /// 实现 <see cref="IDisposable"/> 接口，释放资源
        /// </summary>
        public void Dispose() {
            ReleaseResources();
            // 防止垃圾回收器调用析构函数。因为资源已被释放，不再需要调用析构函数
            // 这是一个优化操作，告诉垃圾回收器在对象被销毁时不需要调用析构函数。
            // 这通常是在资源已经手动释放的情况下使用，以减少垃圾回收器的不必要开销
            //GC.SuppressFinalize(this);
        }
    }
}
