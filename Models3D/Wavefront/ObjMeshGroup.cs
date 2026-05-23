using InnoVault.Models3D.Runtime;
using Microsoft.Xna.Framework.Graphics;

namespace InnoVault.Models3D.Wavefront
{
    /// <summary>
    /// 一个按材质聚合的网格分组
    /// <br/>保存可以直接送入 GPU 的顶点与索引数据
    /// </summary>
    public sealed class ObjMeshGroup : Model3DMeshGroup
    {
        /// <summary>
        /// 该分组绑定的材质实例，可能为 <see langword="null"/>（材质未声明或解析失败）
        /// </summary>
        public new ObjMaterial Material {
            get => base.Material as ObjMaterial;
            internal set => base.Material = value;
        }

        /// <summary>
        /// 三角形索引数据，长度必为 3 的倍数
        /// </summary>
        public new short[] Indices { get; }

        /// <summary>
        /// 构造一个新的网格分组
        /// </summary>
        /// <param name="materialName">材质名</param>
        /// <param name="vertices">顶点数组</param>
        /// <param name="indices">三角形索引数组</param>
        public ObjMeshGroup(string materialName, VertexPositionNormalTexture[] vertices, short[] indices)
            : base(materialName, vertices, ToIntArray(indices)) {
            Indices = indices ?? System.Array.Empty<short>();
        }

        private static int[] ToIntArray(short[] indices) {
            if (indices == null || indices.Length == 0) {
                return System.Array.Empty<int>();
            }
            int[] result = new int[indices.Length];
            for (int i = 0; i < indices.Length; i++) {
                result[i] = (ushort)indices[i];
            }
            return result;
        }
    }
}
