using InnoVault.Models3D.Skinning;
using Microsoft.Xna.Framework;
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
        /// <br/>对于蒙皮分组：此数组是"无动画时直接绘制的兜底姿态"，与 <see cref="BindVertices"/> 等价
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
        /// 蒙皮源顶点（bind pose）
        /// <br/>非蒙皮分组为 <see langword="null"/>；蒙皮分组与 <see cref="Vertices"/> 同长，
        /// CPU 蒙皮以此数组为源，写入 <see cref="Model3DInstance"/> 的 scratch 缓冲
        /// </summary>
        public VertexPositionNormalTexture[] BindVertices { get; internal set; }
        /// <summary>
        /// 顶点骨骼索引（每顶点 4 个）
        /// <br/>非蒙皮分组为 <see langword="null"/>；索引指向 <see cref="Vault3DModel.Skeletons"/>[<see cref="SkinIndex"/>].Joints
        /// </summary>
        public Joint4[] JointIndices { get; internal set; }
        /// <summary>
        /// 顶点骨骼权重（每顶点 4 个）
        /// <br/>非蒙皮分组为 <see langword="null"/>；加载阶段会做权重归一化
        /// </summary>
        public Vector4[] JointWeights { get; internal set; }
        /// <summary>
        /// 关联的骨架索引
        /// <br/><c>-1</c> 表示非蒙皮分组；非负值时指向 <see cref="Vault3DModel.Skeletons"/>
        /// </summary>
        public int SkinIndex { get; internal set; } = -1;
        /// <summary>
        /// 是否为蒙皮分组
        /// <br/>需要同时具备权重、索引、骨架引用
        /// </summary>
        public bool IsSkinned => SkinIndex >= 0
            && JointIndices != null && JointIndices.Length > 0
            && JointWeights != null && JointWeights.Length > 0
            && BindVertices != null && BindVertices.Length > 0;

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
