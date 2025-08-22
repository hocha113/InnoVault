using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Linq;
using Terraria;

namespace InnoVault.Trails
{
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
}
