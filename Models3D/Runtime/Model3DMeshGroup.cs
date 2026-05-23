using Microsoft.Xna.Framework.Graphics;

#pragma warning disable CS1591

namespace InnoVault.Models3D.Runtime
{
    /// <summary>
    /// 格式无关的 mesh 分组，保存可直接提交给渲染器的顶点与索引数据
    /// </summary>
    public class Model3DMeshGroup
    {
        public string MaterialName { get; }
        public Model3DMaterial Material { get; internal set; }
        public VertexPositionNormalTexture[] Vertices { get; }
        public int[] Indices { get; }
        public int TriangleCount => Indices == null ? 0 : Indices.Length / 3;

        public Model3DMeshGroup(string materialName, VertexPositionNormalTexture[] vertices, int[] indices) {
            MaterialName = materialName ?? string.Empty;
            Vertices = vertices ?? System.Array.Empty<VertexPositionNormalTexture>();
            Indices = indices ?? System.Array.Empty<int>();
        }
    }
}
#pragma warning restore CS1591
