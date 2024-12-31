using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace InnoVault.Trails
{
    /// <summary>
    /// 一个空的路径网格生成器实现，作为默认实现，不生成任何顶点和索引。该类可以用于作为占位符，或在不需要生成网格时使用
    /// </summary>
    public class EmptyMeshGenerator : IMeshTrailGenerator
    {
        /// <summary>
        /// 获取额外的顶点数量。对于此实现，该值始终为0，因为不生成任何顶点
        /// </summary>
        public int AdditionalVertexCount => 0;
        /// <summary>
        /// 获取额外的索引数量。对于此实现，该值始终为0，因为不生成任何索引
        /// </summary>
        public int AdditionalIndexCount => 0;
        /// <summary>
        /// 创建网格数据。由于该类为占位符实现，因此该方法不会生成任何顶点或索引
        /// </summary>
        /// <param name="trailTipPosition">路径尖端的世界空间位置，用于确定路径的起始位置</param>
        /// <param name="trailTipNormal">路径尖端的法线，用于确定路径的方向</param>
        /// <param name="startFromIndex">从该索引开始插入新顶点，用于确定网格的索引顺序</param>
        /// <param name="vertices">生成的顶点数组，在此实现中始终为空数组</param>
        /// <param name="indices">生成的索引数组，在此实现中始终为空数组</param>
        /// <param name="trailWidthFunction">路径宽度计算函数，根据路径的位置、时间等因素计算路径的宽度</param>
        /// <param name="trailColorFunction">路径颜色计算函数，根据路径的位置、时间等因素计算路径的颜色</param>
        public void CreateMesh(Vector2 trailTipPosition, Vector2 trailTipNormal
            , int startFromIndex, out VertexPositionColorTexture[] vertices
            , out short[] indices, TrailThicknessCalculator trailWidthFunction, TrailColorEvaluator trailColorFunction) {
            // 该实现不生成任何顶点和索引，因此返回空数组
            vertices = [];
            indices = [];
        }
    }
}
