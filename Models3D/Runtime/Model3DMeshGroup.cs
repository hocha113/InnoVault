using Microsoft.Xna.Framework.Graphics;

namespace InnoVault.Models3D.Runtime
{
    /// <summary>
    /// 格式无关的 mesh 分组，保存可直接提交给渲染器的顶点与索引数据
    /// <br/>通常按材质拆分，一个模型会包含一个或多个分组
    /// </summary>
    public class Model3DMeshGroup
    {
        /// <summary>
        /// 分组材质名
        /// <br/>用于关联 <see cref="Vault3DModel.Materials"/> 中的材质
        /// </summary>
        public string MaterialName { get; }
        /// <summary>
        /// 分组材质
        /// <br/>可能为空，渲染器会回落到默认材质参数
        /// </summary>
        public Model3DMaterial Material { get; internal set; }
        /// <summary>
        /// 顶点数据
        /// <br/>当前固定使用 <see cref="VertexPositionNormalTexture"/> 以兼容 BasicEffect
        /// </summary>
        public VertexPositionNormalTexture[] Vertices { get; }
        /// <summary>
        /// 三角形索引
        /// <br/>长度应为 3 的倍数，索引值必须落在 <see cref="Vertices"/> 范围内
        /// </summary>
        public int[] Indices { get; }
        /// <summary>
        /// 三角形数量
        /// <br/>由索引数量推导，不额外存储
        /// </summary>
        public int TriangleCount => Indices == null ? 0 : Indices.Length / 3;

        /// <summary>
        /// 构造一个网格分组
        /// <br/>传入 null 数组时会自动替换为空数组
        /// </summary>
        /// <param name="materialName">材质名</param>
        /// <param name="vertices">顶点数据</param>
        /// <param name="indices">三角形索引</param>
        public Model3DMeshGroup(string materialName, VertexPositionNormalTexture[] vertices, int[] indices) {
            MaterialName = materialName ?? string.Empty;
            Vertices = vertices ?? System.Array.Empty<VertexPositionNormalTexture>();
            Indices = indices ?? System.Array.Empty<int>();
        }
    }
}
