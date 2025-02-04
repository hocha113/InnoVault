using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using Terraria;

namespace InnoVault.Trails
{
    /// <summary>
    /// 一个传递平滑点集方式的委托
    /// </summary>
    /// <param name="controlPoints"></param>
    /// <param name="offset"></param>
    /// <param name="totalPoints"></param>
    /// <param name="rotations"></param>
    /// <returns></returns>
    public delegate List<Vector2> PathPointRetrievalDelegation(IEnumerable<Vector2> controlPoints
            , Vector2 offset, int totalPoints, IEnumerable<float> rotations = null);
    /// <summary>
    /// 一个用于传递纹理映射采样方式的委托
    /// </summary>
    /// <param name="t"></param>
    /// <param name="leftTexCoord"></param>
    /// <param name="rightTexCoord"></param>
    public delegate void HandlerTexturePossDelegation(float t, out Vector2 leftTexCoord, out Vector2 rightTexCoord);
    /// <summary>
    /// 关于绘制路径效果的另一种实现，更加简洁
    /// </summary>
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

            // 默认路径点生成算法：平滑贝塞尔曲线
            PathPointRetrieval = pointRetrievalFunction ?? GenerateSmoothPath;

            // 默认参数化坐标映射函数
            ParametricPositionFunction = parametricPositionFunction ?? DefaultParametricPosition;

            HandlerTexturePoss = handlerTexturePoss ?? DefaultHandlerTexturePoss;

            // 初始化基本渲染效果
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
        public static Vector2 Evaluate(Vector2[] points, float T) {
            while (points.Length > 2) {
                Vector2[] nextPoints = new Vector2[points.Length - 1];
                for (int i = 0; i < points.Length - 1; i++) {
                    nextPoints[i] = Vector2.Lerp(points[i], points[i + 1], T);
                }
                points = nextPoints;
            }
            return Vector2.Lerp(points[0], points[1], T);
        }

        /// <summary>
        /// 补全链接点的方式
        /// </summary>
        /// <param name="totalPoints"></param>
        /// <returns></returns>
        public List<Vector2> GetPoints(int totalPoints) {
            float straightnessFactor = 0f;
            // straightnessFactor: 控制直线和曲线的平衡，0完全曲线，1完全直线
            straightnessFactor = MathHelper.Clamp(straightnessFactor, 0f, 1f);
            float perStep = 1f / totalPoints;
            List<Vector2> points = new();
            for (float step = 0f; step <= 1f; step += perStep) {
                // 使用直线和贝塞尔曲线的加权插值
                Vector2 bezierPoint = Evaluate(ControlPoints, MathHelper.Clamp(step, 0f, 1f));
                Vector2 linearPoint = Vector2.Lerp(ControlPoints[0], ControlPoints[^1], step); // 起点到终点的直线
                points.Add(Vector2.Lerp(bezierPoint, linearPoint, straightnessFactor));
            }
            return points;
        }

        /// <summary>
        /// 默认的纹理映射采样方式
        /// </summary>
        /// <param name="t"></param>
        /// <param name="leftTexCoord"></param>
        /// <param name="rightTexCoord"></param>
        public static void DefaultHandlerTexturePoss(float t, out Vector2 leftTexCoord, out Vector2 rightTexCoord) {
            leftTexCoord = new Vector2(t, 0f);
            rightTexCoord = new Vector2(t, 1f);
        }

        /// <summary>
        /// 默认的参数化坐标函数（简单映射到直线坐标）
        /// </summary>
        public static Vector2 DefaultParametricPosition(float t) => new Vector2(t, t);

        /// <summary>
        /// 计算透视投影和视图矩阵，用于2D渲染的场景转换
        /// </summary>
        public static void CalculateRenderingMatrices(out Matrix viewMatrix, out Matrix projectionMatrix) {
            Vector2 zoom = Main.GameViewMatrix.Zoom;
            Matrix zoomScaleMatrix = Matrix.CreateScale(zoom.X, zoom.Y, 1f);

            int width = Main.instance.GraphicsDevice.Viewport.Width;
            int height = Main.instance.GraphicsDevice.Viewport.Height;

            viewMatrix = Matrix.CreateLookAt(Vector3.Zero, Vector3.UnitZ, Vector3.Up);
            viewMatrix *= Matrix.CreateTranslation(0f, -height, 0f);
            viewMatrix *= Matrix.CreateRotationZ(MathHelper.Pi);

            if (Main.LocalPlayer.gravDir < 0f) {
                viewMatrix *= Matrix.CreateScale(1f, -1f, 1f) * Matrix.CreateTranslation(0f, height, 0f);
            }

            viewMatrix *= zoomScaleMatrix;
            projectionMatrix = Matrix.CreateOrthographicOffCenter(0f, width * zoom.X, 0f, height * zoom.Y, 0f, 1f) * zoomScaleMatrix;
        }

        /// <summary>
        /// 更新基本效果的投影矩阵和视图矩阵
        /// </summary>
        public void UpdateRenderingMatrices(out Matrix projection, out Matrix view) {
            CalculateRenderingMatrices(out view, out projection);
            BaseEffect.View = view;
            BaseEffect.Projection = projection;
        }

        /// <summary>
        /// 根据路径点生成三角形索引数组
        /// </summary>
        public short[] GenerateIndices(int pointCount) {
            short[] indices = new short[(pointCount - 1) * 6];
            //他妈的这里必须是pointCount - 2，别问我为什么
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

            ColoredVertex[] vertices = GenerateVertices(pathPoints);
            short[] indices = GenerateIndices(pathPoints.Count);
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
            Main.instance.GraphicsDevice.RasterizerState = RasterizerState.CullNone;

            if (PathData.vertices == null || PathData.indices == null) {
                return;
            }

            Main.instance.GraphicsDevice.DrawUserIndexedPrimitives(PrimitiveType.TriangleList, PathData.vertices, 0
                , PathData.vertices.Length, PathData.indices, 0, PathData.indices.Length / 3);
        }

        /// <summary>
        /// 渲染路径效果，使用前必须调用<see cref="GetPathData(IEnumerable{Vector2}, Vector2, int, IEnumerable{float})"/>
        /// </summary>
        public void Draw(Texture2D texture2D) {
            Main.instance.GraphicsDevice.RasterizerState = RasterizerState.CullNone;

            if (PathData.vertices == null || PathData.indices == null) {
                return;
            }

            Main.graphics.GraphicsDevice.Textures[0] = texture2D;

            Main.instance.GraphicsDevice.DrawUserIndexedPrimitives(PrimitiveType.TriangleList, PathData.vertices, 0
                , PathData.vertices.Length, PathData.indices, 0, PathData.indices.Length / 3);
        }

        /// <summary>
        /// 渲染路径效果
        /// </summary>
        public void Draw(IEnumerable<Vector2> controlPoints, Vector2 offset, int totalPoints, IEnumerable<float> rotations = null) {
            Main.instance.GraphicsDevice.RasterizerState = RasterizerState.CullNone;

            PathDataStruct pathData = GetPathData(controlPoints, offset, totalPoints, rotations);
            if (pathData.vertices == null || pathData.indices == null) {
                return;
            }

            BaseEffect.CurrentTechnique.Passes[0].Apply();

            Main.instance.GraphicsDevice.DrawUserIndexedPrimitives(PrimitiveType.TriangleList, pathData.vertices, 0
                , pathData.vertices.Length, pathData.indices, 0, pathData.indices.Length / 3);
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
            ColoredVertex[] vertices = new ColoredVertex[pathPoints.Count * 2 - 2];
            for (int i = 0; i < pathPoints.Count - 1; i++) {
                float t = (float)i / (pathPoints.Count - 1);
                float width = ThicknessEvaluator(t);
                Color color = ColorEvaluator(ParametricPositionFunction(t));

                Vector2 current = pathPoints[i];
                Vector2 direction = Utils.SafeNormalize(pathPoints[i + 1] - current, Vector2.Zero);

                HandlerTexturePoss.Invoke(t, out Vector2 leftTexCoord, out Vector2 rightTexCoord);

                Vector2 sideOffset = new Vector2(-direction.Y, direction.X) * width;

                Vector2 left = current - sideOffset;
                Vector2 right = current + sideOffset;

                if (i == 0 && StickPoint != Vector2.Zero) {
                    left = StickPoint;
                    right = StickPoint;
                }

                vertices[i * 2] = new ColoredVertex(left, color, leftTexCoord.ToVector3());
                vertices[i * 2 + 1] = new ColoredVertex(right, color, rightTexCoord.ToVector3());
            }

            return vertices;
        }
    }
}