using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

/// <summary>
/// 表示一个带颜色和纹理坐标的顶点结构
/// </summary>
public struct ColoredVertex : IVertexType
{
    /// <summary>
    /// 顶点的位置，使用二维向量表示
    /// </summary>
    public Vector2 Position;

    /// <summary>
    /// 顶点的颜色
    /// </summary>
    public Color Color;

    /// <summary>
    /// 顶点的纹理坐标，使用三维向量表示
    /// </summary>
    public Vector3 TexCoord;

    // 顶点声明，定义了顶点的内存布局，用于图形渲染管线的识别
    private static readonly VertexDeclaration _vertexDeclaration = new VertexDeclaration(new VertexElement[] {
        // 位置：偏移量为 0，格式为 Vector2，用途为 Position
        new VertexElement(0, VertexElementFormat.Vector2, VertexElementUsage.Position, 0),
        // 颜色：偏移量为 8，格式为 Color，用途为 Color
        new VertexElement(8, VertexElementFormat.Color, VertexElementUsage.Color, 0),
        // 纹理坐标：偏移量为 12，格式为 Vector3，用途为 TextureCoordinate
        new VertexElement(12, VertexElementFormat.Vector3, VertexElementUsage.TextureCoordinate, 0)
    });

    /// <summary>
    /// 构造一个带有位置、颜色和纹理坐标的顶点
    /// </summary>
    /// <param name="position">顶点的位置</param>
    /// <param name="color">顶点的颜色</param>
    /// <param name="texCoord">顶点的纹理坐标</param>
    public ColoredVertex(Vector2 position, Color color, Vector3 texCoord) {
        Position = position;
        Color = color;
        TexCoord = texCoord;
    }

    /// <summary>
    /// 获取该顶点的声明，用于描述顶点的内存布局
    /// </summary>
    public VertexDeclaration VertexDeclaration => _vertexDeclaration;
}
