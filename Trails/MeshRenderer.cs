using Microsoft.Xna.Framework.Graphics;
using System;
using Terraria;

namespace InnoVault.Trails
{
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
