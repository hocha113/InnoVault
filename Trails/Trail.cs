using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Linq;
using Terraria;

namespace InnoVault.Trails
{
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
