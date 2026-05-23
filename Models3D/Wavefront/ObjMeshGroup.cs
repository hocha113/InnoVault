using Microsoft.Xna.Framework.Graphics;

namespace InnoVault.Models3D.Wavefront
{
    /// <summary>
    /// 一个按材质聚合的网格分组
    /// <br/>保存可以直接送入 GPU 的顶点与索引数据
    /// </summary>
    public sealed class ObjMeshGroup
    {
        /// <summary>
        /// 该分组使用的材质名（对应 OBJ 的 <c>usemtl</c>），未指定时为空字符串
        /// </summary>
        public string MaterialName { get; }

        /// <summary>
        /// 该分组绑定的材质实例，可能为 <see langword="null"/>（材质未声明或解析失败）
        /// </summary>
        public ObjMaterial Material { get; internal set; }

        /// <summary>
        /// 顶点数据，使用 <see cref="VertexPositionNormalTexture"/> 与 <see cref="BasicEffect"/> 兼容
        /// </summary>
        public VertexPositionNormalTexture[] Vertices { get; }

        /// <summary>
        /// 三角形索引数据，长度必为 3 的倍数
        /// </summary>
        public short[] Indices { get; }

        /// <summary>
        /// 三角形数量，等于 <see cref="Indices"/> 长度除以 3
        /// </summary>
        public int TriangleCount => Indices == null ? 0 : Indices.Length / 3;

        /// <summary>
        /// 构造一个新的网格分组
        /// </summary>
        /// <param name="materialName">材质名</param>
        /// <param name="vertices">顶点数组</param>
        /// <param name="indices">三角形索引数组</param>
        public ObjMeshGroup(string materialName, VertexPositionNormalTexture[] vertices, short[] indices) {
            MaterialName = materialName ?? string.Empty;
            Vertices = vertices ?? System.Array.Empty<VertexPositionNormalTexture>();
            Indices = indices ?? System.Array.Empty<short>();
        }
    }
}
