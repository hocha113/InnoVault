using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace InnoVault.Rigs2D
{
    /// <summary>
    /// 带状件顶点：与 InnoVault 的 <c>ColoredVertex</c> 字段、偏移、格式逐项相同（位置 Vector2 @0、颜色 @8、纹理坐标 Vector3 @12），
    /// 绑定到同一套 SpriteBatch 着色器时结果一致；核心自带一份，离线宿主不必引用 InnoVault 本体
    /// </summary>
    public struct Rig2DVertex : IVertexType
    {
        /// <summary>
        /// 位置
        /// </summary>
        public Vector2 Position;
        /// <summary>
        /// 颜色
        /// </summary>
        public Color Color;
        /// <summary>
        /// 纹理坐标
        /// </summary>
        public Vector3 TexCoord;

        private static readonly VertexDeclaration declaration = new(
            new VertexElement(0, VertexElementFormat.Vector2, VertexElementUsage.Position, 0),
            new VertexElement(8, VertexElementFormat.Color, VertexElementUsage.Color, 0),
            new VertexElement(12, VertexElementFormat.Vector3, VertexElementUsage.TextureCoordinate, 0));

        /// <inheritdoc/>
        public readonly VertexDeclaration VertexDeclaration => declaration;

        /// <summary>
        /// 构造顶点
        /// </summary>
        public Rig2DVertex(Vector2 position, Color color, Vector3 texCoord) {
            Position = position;
            Color = color;
            TexCoord = texCoord;
        }
    }
}
