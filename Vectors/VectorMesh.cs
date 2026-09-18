using InnoVault.Vectors.Tessellation;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;

namespace InnoVault.Vectors
{
    /// <summary>
    /// CPU 侧的三角网格容器：<see cref="VertexPositionColorTexture"/> 顶点 + 16 位索引，缓冲按需增长、跨帧复用，不持有任何 GPU 资源
    /// <br/>顶点布局与旧 <c>Trail</c> 完全一致（POSITION / COLOR0 / TEXCOORD0），现有的 vs+ps 拖尾着色器无需改动即可消费
    /// <br/>典型用法：每帧 <see cref="Clear"/> → 若干 <c>Append*</c> → <see cref="VectorRenderer.Draw(VectorMesh, in VectorDrawOptions)"/>
    /// </summary>
    public sealed class VectorMesh
    {
        /// <summary>16 位索引允许的最大顶点数</summary>
        public const int MaxVertices = short.MaxValue;

        private VertexPositionColorTexture[] vertices;
        private short[] indices;

        /// <summary>顶点缓冲（有效长度见 <see cref="VertexCount"/>）</summary>
        public VertexPositionColorTexture[] Vertices => vertices;
        /// <summary>索引缓冲（有效长度见 <see cref="IndexCount"/>）</summary>
        public short[] Indices => indices;
        /// <summary>有效顶点数</summary>
        public int VertexCount { get; private set; }
        /// <summary>有效索引数</summary>
        public int IndexCount { get; private set; }
        /// <summary>三角形数</summary>
        public int TriangleCount => IndexCount / 3;
        /// <summary>是否没有任何三角形</summary>
        public bool IsEmpty => IndexCount < 3;

        /// <summary>创建网格，可指定初始容量</summary>
        public VectorMesh(int vertexCapacity = 256, int indexCapacity = 768) {
            vertices = new VertexPositionColorTexture[Math.Max(vertexCapacity, 8)];
            indices = new short[Math.Max(indexCapacity, 24)];
        }

        /// <summary>清空（不释放缓冲）</summary>
        public void Clear() {
            VertexCount = 0;
            IndexCount = 0;
        }

        /// <summary>
        /// 预留空间。顶点总数会超过 <see cref="MaxVertices"/> 时返回 false 且不做任何改动
        /// </summary>
        public bool Reserve(int additionalVertices, int additionalIndices) {
            if (VertexCount + additionalVertices > MaxVertices) {
                VaultMod.LoggerError("VectorMesh.Reserve", $"[Vectors] VectorMesh vertex budget exceeded ({VertexCount} + {additionalVertices} > {MaxVertices}); split the draw into several meshes");
                return false;
            }
            int needV = VertexCount + additionalVertices;
            if (needV > vertices.Length) {
                Array.Resize(ref vertices, Math.Max(needV, Math.Min(vertices.Length * 2, MaxVertices)));
            }
            int needI = IndexCount + additionalIndices;
            if (needI > indices.Length) {
                Array.Resize(ref indices, Math.Max(needI, indices.Length * 2));
            }
            return true;
        }

        /// <summary>追加一个顶点，返回其索引；调用前须已 <see cref="Reserve"/></summary>
        public int AddVertex(Vector2 position, Color color, Vector2 uv) {
            int i = VertexCount++;
            vertices[i] = new VertexPositionColorTexture(new Vector3(position, 0f), color, uv);
            return i;
        }

        /// <summary>追加一个三角形；调用前须已 <see cref="Reserve"/></summary>
        public void AddTriangle(int a, int b, int c) {
            indices[IndexCount++] = (short)a;
            indices[IndexCount++] = (short)b;
            indices[IndexCount++] = (short)c;
        }

        /// <summary>
        /// 追加路径描边。<paramref name="transform"/> 把路径空间映射到输出空间（世界 / 屏幕 / UI 由绘制时的 <see cref="VectorSpace"/> 决定），宽度以输出空间计
        /// </summary>
        public void AppendStroke(VectorPath path, StrokeStyle style, in VectorTransform transform) {
            if (path == null || style == null || path.IsEmpty) {
                return;
            }
            StrokeTessellator.Append(this, path, style, in transform);
        }

        /// <summary>追加路径描边（恒等变换）</summary>
        public void AppendStroke(VectorPath path, StrokeStyle style) => AppendStroke(path, style, in VectorTransform.Identity);

        /// <summary>
        /// 直接描一条输出空间的折线（旧 <c>Trail.TrailPositions</c> 一类逐帧变化的点列走这里，无需先构造 <see cref="VectorPath"/>）
        /// <br/>宽度 / 颜色函数的 t 按点列自身的弧长归一：<c>points[0]</c> 处为 0，<c>points[^1]</c> 处为 1
        /// </summary>
        public void AppendStroke(ReadOnlySpan<Vector2> points, StrokeStyle style, bool closed = false) {
            if (style == null || points.Length == 0) {
                return;
            }
            StrokeTessellator.Append(this, points, closed, style);
        }

        /// <summary>
        /// 追加路径填充：每条至少三点的子路径各自作为简单多边形填充（不支持孔洞与自交），u/v 为该子路径包围盒的归一坐标
        /// </summary>
        public void AppendFill(VectorPath path, FillStyle style, in VectorTransform transform) {
            if (path == null || style == null || path.IsEmpty) {
                return;
            }
            FillTessellator.Append(this, path, style, in transform);
        }

        /// <summary>追加路径填充（恒等变换）</summary>
        public void AppendFill(VectorPath path, FillStyle style) => AppendFill(path, style, in VectorTransform.Identity);

        /// <summary>直接填充一个输出空间的简单多边形</summary>
        public void AppendFill(ReadOnlySpan<Vector2> polygon, FillStyle style) {
            if (style == null || polygon.Length < 3) {
                return;
            }
            FillTessellator.Append(this, polygon, style);
        }

        /// <summary>
        /// 追加一个四边形（两三角形），四点按环绕顺序给出；常用于 A→B 直射光束一类的定长四顶点条带
        /// </summary>
        public void AppendQuad(Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color color, Vector2 uvA, Vector2 uvB, Vector2 uvC, Vector2 uvD) {
            if (!Reserve(4, 6)) {
                return;
            }
            int ia = AddVertex(a, color, uvA);
            int ib = AddVertex(b, color, uvB);
            int ic = AddVertex(c, color, uvC);
            int id = AddVertex(d, color, uvD);
            AddTriangle(ia, ib, ic);
            AddTriangle(ia, ic, id);
        }

        /// <summary>
        /// 追加一段 A→B 的定宽条带：u 沿 A(0)→B(1)，v 横跨，与旧手写 <c>VertexPositionColorTexture[4]</c> 光束写法等价
        /// </summary>
        public void AppendBeam(Vector2 start, Vector2 end, float width, Color color) {
            Vector2 dir = end - start;
            if (dir.LengthSquared() < 1e-8f) {
                return;
            }
            dir.Normalize();
            Vector2 n = new Vector2(-dir.Y, dir.X) * (width * 0.5f);
            AppendQuad(start + n, end + n, end - n, start - n, color,
                new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f));
        }
    }
}
