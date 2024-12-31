using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace InnoVault.Trails
{
    /// <summary>
    /// 路径网格生成器接口，用于定义生成路径网格数据的行为。任何实现此接口的类都需要提供生成路径网格顶点和索引的逻辑
    /// </summary>
    public interface IMeshTrailGenerator
    {
        /// <summary>
        /// 获取额外的索引数量实现此接口的类可以根据需要计算并返回额外的索引数量，用于生成路径网格时的索引数据
        /// </summary>
        int AdditionalIndexCount { get; }
        /// <summary>
        /// 获取额外的顶点数量实现此接口的类可以根据需要计算并返回额外的顶点数量，用于生成路径网格时的顶点数据
        /// </summary>
        int AdditionalVertexCount { get; }
        /// <summary>
        /// 创建路径网格数据实现此方法的类应根据路径的尖端位置、法线以及其他计算函数来生成路径的网格顶点和索引
        /// </summary>
        /// <param name="trailTipPosition">路径尖端的世界空间位置，表示路径的起始点</param>
        /// <param name="trailTipNormal">路径尖端的法线，表示路径的方向和旋转</param>
        /// <param name="startFromIndex">从哪个索引开始插入新顶点，用于组织网格的顶点顺序</param>
        /// <param name="vertices">输出的顶点数组，包含生成的路径网格的所有顶点</param>
        /// <param name="indices">输出的索引数组，表示如何连接顶点来形成路径网格的三角形</param>
        /// <param name="trailWidthFunction">路径宽度计算函数，根据路径的时间、位置等因素计算路径的宽度</param>
        /// <param name="trailColorFunction">路径颜色计算函数，根据路径的时间、位置等因素计算路径的颜色</param>
        void CreateMesh(Vector2 trailTipPosition, Vector2 trailTipNormal, int startFromIndex
            , out VertexPositionColorTexture[] vertices, out short[] indices
            , TrailThicknessCalculator trailWidthFunction, TrailColorEvaluator trailColorFunction);
    }
}
