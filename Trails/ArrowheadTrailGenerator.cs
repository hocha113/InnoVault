using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;

namespace InnoVault.Trails
{
    /// <summary>
    /// 生成一个箭头形状的尾迹尖端尾迹尖端的形状为一个朝向尾迹方向的箭头形三角形
    /// </summary>
    public class ArrowheadTrailGenerator : IMeshTrailGenerator
    {
        /// <summary>
        /// 该尾迹尖端的附加顶点数量（一个三角形的三个顶点）
        /// </summary>
        public int AdditionalVertexCount => 3;
        /// <summary>
        /// 该尾迹尖端的附加索引数量（一个三角形的三个索引）
        /// </summary>
        public int AdditionalIndexCount => 3;
        //仅仅在实例化时赋值
        private readonly float length;
        /// <summary>
        /// 初始化箭头形尾迹尖端生成器
        /// </summary>
        /// <param name="length">箭头尖端的长度，表示三角形的底边长度</param>
        public ArrowheadTrailGenerator(float length) {
            this.length = length;
        }

        /// <summary>
        /// 生成箭头形尾迹网格这个尾迹网格通过一个尖端朝向尾迹方向的三角形表示
        /// </summary>
        /// <param name="trailTipPosition">尾迹尖端的世界位置</param>
        /// <param name="trailTipNormal">尾迹尖端的法线方向</param>
        /// <param name="startFromIndex">索引起始位置</param>
        /// <param name="vertices">返回的顶点数组</param>
        /// <param name="indices">返回的索引数组</param>
        /// <param name="trailWidthFunction">尾迹宽度函数，用于计算宽度</param>
        /// <param name="trailColorFunction">尾迹颜色评估函数，用于计算每个顶点的颜色</param>
        public void CreateMesh(Vector2 trailTipPosition, Vector2 trailTipNormal, int startFromIndex, out VertexPositionColorTexture[] vertices
            , out short[] indices, TrailThicknessCalculator trailWidthFunction, TrailColorEvaluator trailColorFunction) {
            // 计算与尾迹法线方向垂直的单位向量，用于三角形的左右两边
            Vector2 normalPerp = trailTipNormal.RotatedBy(MathHelper.PiOver2);

            // 获取尾迹宽度，若未提供宽度函数则默认为1
            float width = trailWidthFunction?.Invoke(1) ?? 1;

            // 计算三角形的三个顶点位置
            Vector2 a = trailTipPosition + (normalPerp * width);  // 左边的顶点
            Vector2 b = trailTipPosition - (normalPerp * width);  // 右边的顶点
            Vector2 c = trailTipPosition + (trailTipNormal * length);  // 尖端顶点

            // 纹理坐标，A、B、C 分别对应三角形的三个顶点
            Vector2 texCoordA = Vector2.UnitX;
            Vector2 texCoordB = Vector2.One;
            Vector2 texCoordC = new(1, 0.5f);  // 通过调整C点的纹理坐标来修复纹理问题

            // 获取每个顶点的颜色，若未提供颜色评估函数则默认使用白色
            Color colorA = trailColorFunction?.Invoke(texCoordA) ?? Color.White;
            Color colorB = trailColorFunction?.Invoke(texCoordB) ?? Color.White;
            Color colorC = trailColorFunction?.Invoke(texCoordC) ?? Color.White;

            // 生成顶点数组，每个顶点包含位置、颜色和纹理坐标
            vertices = new VertexPositionColorTexture[]
            {
            new(a.ToVector3(), colorA, texCoordA),  // 顶点A
            new(b.ToVector3(), colorB, texCoordB),  // 顶点B
            new(c.ToVector3(), colorC, texCoordC)   // 顶点C
            };

            // 生成索引数组，定义一个三角形的顶点顺序
            indices = [
            (short)startFromIndex,     // 顶点A的索引
            (short)(startFromIndex + 1), // 顶点B的索引
            (short)(startFromIndex + 2)  // 顶点C的索引
            ];
        }
    }
}
