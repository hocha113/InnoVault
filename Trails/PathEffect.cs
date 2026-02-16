using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using Terraria;

namespace InnoVault.Trails
{
    /// <summary>
    /// 关于绘制路径效果的另一种实现，更加简洁
    /// <para>该类的核心功能已迁移至 <see cref="Trail"/>，此类作为向下兼容的包装保留，
    /// 已有的使用代码无需修改即可继续工作</para>
    /// </summary>
    [Obsolete("已经过时，请直接使用 Trail 类来实现路径效果，PathEffect 作为向下兼容的包装保留，已有代码无需修改即可继续工作")]
    public class PathEffect
    {
        #region Data
        private readonly BasicEffect BaseEffect;
        private Vector2 StickPoint = Vector2.Zero;
        private Vector2[] ControlPoints;
        /// <summary>
        /// 计算轨迹（Trail）在不同位置的宽度（厚度）
        /// 该委托通常用于实现渐变或动态变化的轨迹宽度
        /// </summary>
        public TrailThicknessCalculator ThicknessEvaluator;
        /// <summary>
        /// 计算轨迹在不同位置的颜色
        /// 该委托用于提供颜色渐变或动态颜色变化的实现方式
        /// </summary>
        public TrailColorEvaluator ColorEvaluator;
        /// <summary>
        /// 用于计算轨迹上各个点的位置的参数化函数
        /// 输入参数通常是一个范围在 [0,1] 之间的浮点数 t，表示路径的进度，返回值为轨迹点的二维坐标
        /// 适用于基于时间或路径进度的曲线轨迹生成
        /// </summary>
        public Func<float, Vector2> ParametricPositionFunction;
        /// <summary>
        /// 用于获取轨迹上的关键点（Path Points），以便生成路径
        /// 该委托允许自定义路径采样方式，影响轨迹的形态和流畅度
        /// </summary>
        public PathPointRetrievalDelegation PathPointRetrieval;
        /// <summary>
        /// 处理轨迹纹理的映射方式，决定纹理如何在轨迹上分布和变换
        /// 例如，它可以定义纹理平铺、拉伸或其他自定义效果
        /// </summary>
        public HandlerTexturePossDelegation HandlerTexturePoss;
        /// <summary>
        /// 这个路径实例所拥有的点集数据，可以直接用于顶点绘制，在使用前必须调用<see cref="GetPathData"/>
        /// </summary>
        public PathDataStruct PathData;
        /// <summary>
        /// 路径信息结构体
        /// </summary>
        public struct PathDataStruct
        {
            /// <summary>
            /// 顶点
            /// </summary>
            public ColoredVertex[] vertices;
            /// <summary>
            /// 链接点
            /// </summary>
            public short[] indices;
        }
        #endregion
        /// <summary>
        /// 生成一个路径效果实例
        /// </summary>
        /// <param name="thicknessEvaluator"></param>
        /// <param name="colorEvaluator"></param>
        /// <param name="pointRetrievalFunction"></param>
        /// <param name="parametricPositionFunction"></param>
        /// <param name="handlerTexturePoss"></param>
        public PathEffect(TrailThicknessCalculator thicknessEvaluator, TrailColorEvaluator colorEvaluator, PathPointRetrievalDelegation pointRetrievalFunction = null
            , Func<float, Vector2> parametricPositionFunction = null, HandlerTexturePossDelegation handlerTexturePoss = null) {
            ThicknessEvaluator = thicknessEvaluator;
            ColorEvaluator = colorEvaluator;

            PathPointRetrieval = pointRetrievalFunction ?? GenerateSmoothPath;

            ParametricPositionFunction = parametricPositionFunction ?? Trail.DefaultParametricPosition;

            HandlerTexturePoss = handlerTexturePoss ?? Trail.DefaultHandlerTexturePoss;

            BaseEffect = new BasicEffect(Main.instance.GraphicsDevice) {
                VertexColorEnabled = true,
                TextureEnabled = false
            };
            UpdateRenderingMatrices(out _, out _);
        }

        /// <summary>
        /// 缓动函数
        /// </summary>
        /// <param name="points"></param>
        /// <param name="T"></param>
        /// <returns></returns>
        public static Vector2 Evaluate(Vector2[] points, float T) => Trail.BezierEvaluate(points, T);

        /// <summary>
        /// 补全链接点的方式
        /// </summary>
        /// <param name="totalPoints"></param>
        /// <returns></returns>
        public List<Vector2> GetPoints(int totalPoints) => Trail.SampleBezierPoints(ControlPoints, totalPoints);

        /// <summary>
        /// 默认的纹理映射采样方式
        /// </summary>
        /// <param name="t"></param>
        /// <param name="leftTexCoord"></param>
        /// <param name="rightTexCoord"></param>
        public static void DefaultHandlerTexturePoss(float t, out Vector2 leftTexCoord, out Vector2 rightTexCoord)
            => Trail.DefaultHandlerTexturePoss(t, out leftTexCoord, out rightTexCoord);

        /// <summary>
        /// 默认的参数化坐标函数（简单映射到直线坐标）
        /// </summary>
        public static Vector2 DefaultParametricPosition(float t) => Trail.DefaultParametricPosition(t);

        /// <summary>
        /// 计算透视投影和视图矩阵，用于2D渲染的场景转换
        /// </summary>
        public static void CalculateRenderingMatrices(out Matrix viewMatrix, out Matrix projectionMatrix)
            => Trail.CalculateRenderingMatrices(out viewMatrix, out projectionMatrix);

        /// <summary>
        /// 更新基本效果的投影矩阵和视图矩阵
        /// </summary>
        public void UpdateRenderingMatrices(out Matrix projection, out Matrix view) {
            Trail.CalculateRenderingMatrices(out view, out projection);
            BaseEffect.View = view;
            BaseEffect.Projection = projection;
        }

        /// <summary>
        /// 根据路径点生成三角形索引数组
        /// </summary>
        public short[] GenerateIndices(int pointCount) {
            short[] indices = new short[(pointCount - 1) * 6];
            for (int i = 0; i < pointCount - 2; i++) {
                int startIndex = i * 6;
                int vertexIndex = i * 2;

                indices[startIndex] = (short)vertexIndex;
                indices[startIndex + 1] = (short)(vertexIndex + 1);
                indices[startIndex + 2] = (short)(vertexIndex + 2);

                indices[startIndex + 3] = (short)(vertexIndex + 2);
                indices[startIndex + 4] = (short)(vertexIndex + 1);
                indices[startIndex + 5] = (short)(vertexIndex + 3);
            }

            return indices;
        }

        /// <summary>
        /// 获取路径数据
        /// </summary>
        /// <param name="controlPoints"></param>
        /// <param name="offset"></param>
        /// <param name="totalPoints"></param>
        /// <param name="rotations"></param>
        /// <returns></returns>
        public PathDataStruct GetPathData(IEnumerable<Vector2> controlPoints, Vector2 offset, int totalPoints, IEnumerable<float> rotations = null) {
            List<Vector2> pathPoints = PathPointRetrieval(controlPoints, offset, totalPoints, rotations);

            if (pathPoints.Count < 2 || pathPoints.All(p => p == pathPoints[0])) {
                return default;
            }

            UpdateRenderingMatrices(out Matrix projection, out Matrix view);

            Trail.GenerateInterleavedMesh(pathPoints, ThicknessEvaluator, ColorEvaluator
                , out ColoredVertex[] vertices, out short[] indices
                , ParametricPositionFunction, HandlerTexturePoss, StickPoint);

            if (indices.Length % 6 != 0 || vertices.Length % 2 != 0) {
                return default;
            }

            PathData = new PathDataStruct {
                vertices = vertices,
                indices = indices
            };

            return PathData;
        }

        /// <summary>
        /// 渲染路径效果，使用前必须调用<see cref="GetPathData(IEnumerable{Vector2}, Vector2, int, IEnumerable{float})"/>
        /// </summary>
        public void Draw() {
            if (PathData.vertices == null || PathData.indices == null) {
                return;
            }

            Trail.DrawUserPrimitives(PathData.vertices, PathData.indices);
        }

        /// <summary>
        /// 渲染路径效果，使用前必须调用<see cref="GetPathData(IEnumerable{Vector2}, Vector2, int, IEnumerable{float})"/>
        /// </summary>
        public void Draw(Texture2D texture2D) {
            if (PathData.vertices == null || PathData.indices == null) {
                return;
            }

            Main.graphics.GraphicsDevice.Textures[0] = texture2D;
            Trail.DrawUserPrimitives(PathData.vertices, PathData.indices);
        }

        /// <summary>
        /// 渲染路径效果
        /// </summary>
        public void Draw(IEnumerable<Vector2> controlPoints, Vector2 offset, int totalPoints, IEnumerable<float> rotations = null) {
            PathDataStruct pathData = GetPathData(controlPoints, offset, totalPoints, rotations);
            if (pathData.vertices == null || pathData.indices == null) {
                return;
            }

            BaseEffect.CurrentTechnique.Passes[0].Apply();
            Trail.DrawUserPrimitives(pathData.vertices, pathData.indices);
        }

        /// <summary>
        /// 使用平滑贝塞尔曲线生成路径点
        /// </summary>
        public List<Vector2> GenerateSmoothPath(IEnumerable<Vector2> controlPoints, Vector2 offset, int totalPoints, IEnumerable<float> rotations = null) {
            List<Vector2> adjustedPoints = controlPoints.Where(p => p != Vector2.Zero).Select(p => p + offset).ToList();
            if (adjustedPoints.Count < 2) {
                return adjustedPoints;
            }
            ControlPoints = adjustedPoints.ToArray();
            return GetPoints(totalPoints);
        }

        /// <summary>
        /// 根据路径点生成顶点数组，包含纹理坐标
        /// </summary>
        public ColoredVertex[] GenerateVertices(List<Vector2> pathPoints) {
            Trail.GenerateInterleavedMesh(pathPoints, ThicknessEvaluator, ColorEvaluator
                , out ColoredVertex[] vertices, out _
                , ParametricPositionFunction, HandlerTexturePoss, StickPoint);
            return vertices;
        }
    }
}