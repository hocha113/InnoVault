using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using System;
using System.Collections.Generic;
using Terraria;

namespace InnoVault.Trails
{
    //来自珊瑚石，谢谢你瓶中微光 :)

    /// <summary>
    /// 用于生成并渲染闪电轨迹的类，支持动态颜色、透明度和宽度控制
    /// </summary>
    /// <param name="thunderTex">用于渲染闪电的纹理</param>
    /// <param name="widthFunc">计算闪电轨迹宽度的函数，参数为插值因子</param>
    /// <param name="colorFunc">计算闪电轨迹颜色的函数，参数为插值因子</param>
    /// <param name="alphaFunc">计算闪电轨迹透明度的函数，参数为插值因子</param>
    public class ThunderTrail(Asset<Texture2D> thunderTex, Func<float, float> widthFunc, Func<float, Color> colorFunc, Func<float, float> alphaFunc)
    {
        /// <summary>
        /// 基础位置数组，元素数量必须大于 2
        /// </summary>
        public Vector2[] BasePositions { get; set; }
        /// <summary>
        /// 随机偏移后的位置数组
        /// </summary>
        public Vector2[] RandomlyPositions { get; set; }
        /// <summary>
        /// 是否同时使用 Normal（默认）和 Additive（加法）两种绘制模式
        /// </summary>
        public bool UseNonOrAdd { get; set; } = false;
        /// <summary>
        /// 闪电的基础颜色
        /// </summary>
        public Color FlowColor { get; set; } = Color.White;
        /// <summary>
        /// 是否允许绘制闪电，可用于实现闪烁效果
        /// </summary>
        public bool CanDraw { get; set; }
        /// <summary>
        /// 当闪电的转角小于该值时，会进行圆滑处理
        /// </summary>
        public float PartitionLimit { get; set; } = 1.9f;
        /// <summary>
        /// 对于闪电中的锐利部分进行的额外分割次数
        /// </summary>
        public int PartitionPointCount { get; set; } = 1;
        /// <summary>
        /// 控制闪电宽度变化的函数
        /// </summary>
        private Func<float, float> thunderWidthFunc = widthFunc;
        /// <summary>
        /// 控制闪电随机偏移范围 (最小值, 最大值)
        /// </summary>
        private (float Min, float Max) thunderRandomOffsetRange;
        /// <summary>
        /// 当前使用的闪电纹理
        /// </summary>
        public Asset<Texture2D> ThunderTex { get; private set; } = thunderTex;
        /// <summary>
        /// 额外扩展的宽度
        /// </summary>
        private float randomExpandWidth;
        /// <summary>
        /// 设置闪电的宽度变化函数
        /// </summary>
        /// <param name="widthFunc">计算宽度的函数</param>
        public void SetWidth(Func<float, float> widthFunc) {
            thunderWidthFunc = widthFunc;
        }
        /// <summary>
        /// 设置闪电的随机偏移范围
        /// </summary>
        /// <param name="range">偏移范围 (最小值, 最大值)</param>
        /// <exception cref="ArgumentException">如果第一个元素大于第二个元素，则抛出异常</exception>
        public void SetRange((float Min, float Max) range) {
            if (range.Min > range.Max) {
                throw new ArgumentException("最小值不能大于最大值！");
            }
            thunderRandomOffsetRange = range;
        }
        /// <summary>
        /// 设置额外的扩展宽度
        /// </summary>
        /// <param name="width">扩展的宽度值</param>
        public void SetExpandWidth(float width) {
            randomExpandWidth = width;
        }
        /// <summary>
        /// 替换闪电的纹理
        /// </summary>
        /// <param name="asset">新的纹理资源</param>
        public void ExchangeTexture(Asset<Texture2D> asset) {
            ThunderTex = asset;
        }
        /// <summary>
        /// 更新随机位置，使其沿指定速度移动
        /// </summary>
        /// <param name="velocity">移动速度向量</param>
        public void UpdateTrail(Vector2 velocity) {
            if (RandomlyPositions == null) {
                return;
            }

            for (int i = 0; i < RandomlyPositions.Length; i++) {
                RandomlyPositions[i] += velocity;
            }
        }
        /// <summary>
        /// 随机改变闪电形状
        /// </summary>
        public void RandomThunder() {
            RandomlyPositions = new Vector2[BasePositions.Length];
            //首位两端的点不动
            RandomlyPositions[0] = BasePositions[0];
            for (int i = 1; i < BasePositions.Length - 1; i++) {
                Vector2 normal = (BasePositions[i - 1] - BasePositions[i + 1]).SafeNormalize(Vector2.One).RotatedBy(MathHelper.PiOver2);

                float length = Main.rand.NextFromList(-1, 1) * Main.rand.NextFloat(thunderRandomOffsetRange.Item1, thunderRandomOffsetRange.Item2);

                RandomlyPositions[i] = BasePositions[i] + (normal * length) + Main.rand.NextVector2Circular(randomExpandWidth, randomExpandWidth);
            }

            RandomlyPositions[^1] = BasePositions[^1];
        }
        /// <summary>
        /// 绘制闪电
        /// </summary>
        /// <param name="graphicsDevice"></param>
        public void DrawThunder(GraphicsDevice graphicsDevice) {
            if (!CanDraw || RandomlyPositions == null) {
                return;
            }

            Texture2D Texture = ThunderTex.Value;

            int texWidth = Texture.Width;
            float length = 0;

            List<ColoredVertex> barsTop = new();
            List<ColoredVertex> barsBottom = new();
            List<ColoredVertex> bars2Top = new();
            List<ColoredVertex> bars2Bottom = new();

            int trailCachesLength = RandomlyPositions.Length;

            //是否在末端绘制遮盖物
            bool drawInTip = false;
            bool drawInBack = false;

            //先添加0的
            Vector2 Center = RandomlyPositions[0] - Main.screenPosition;

            Vector2 normal = (RandomlyPositions[0] - RandomlyPositions[1]).RotatedBy(-MathHelper.PiOver2).SafeNormalize(Vector2.One);
            float tipRotaion = normal.ToRotation() + 1.57f;
            Color thunderColor = GetColor(0);
            float tipWidth = thunderWidthFunc(0);
            drawInTip = tipWidth > 10;
            Vector2 lengthVec2 = normal * tipWidth;

            AddVertexInfo2(barsTop, barsBottom, Center, lengthVec2, thunderColor, 0);
            AddVertexInfo2(bars2Top, bars2Bottom, Center, lengthVec2, GetFlowColor(0), 0);

            for (int i = 1; i < trailCachesLength - 1; i++) {
                float factor = (float)i / trailCachesLength;
                Center = RandomlyPositions[i] - Main.screenPosition;
                thunderColor = GetColor(factor);
                float width = thunderWidthFunc(factor);

                Vector2 dirToBack = RandomlyPositions[i - 1] - RandomlyPositions[i];
                Vector2 dirToTront = RandomlyPositions[i + 1] - RandomlyPositions[i];

                length += dirToBack.Length();
                float lengthFactor = length / texWidth;//当前闪电长度相对于图片长度的值，总之是用于拉伸闪电贴图的

                float y = ((RandomlyPositions[i].X - RandomlyPositions[i - 1].X)
                    / (RandomlyPositions[i + 1].X - RandomlyPositions[i - 1].X)
                    * (RandomlyPositions[i + 1].Y - RandomlyPositions[i - 1].Y))
                    + RandomlyPositions[i - 1].Y;


                float angle = MathF.Acos(Vector2.Dot(dirToBack, dirToTront) / (dirToTront.Length() * dirToBack.Length()));//Helpers.Helper.AngleRad(dirToBack, dirToTront);

                normal = (RandomlyPositions[i - 1] - RandomlyPositions[i + 1]).RotatedBy(-MathHelper.PiOver2).SafeNormalize(Vector2.One);

                if (angle < PartitionLimit) {
                    bool PartitionBottom = RandomlyPositions[i].Y < y;//分割底部的点
                    if (RandomlyPositions[i - 1].X > RandomlyPositions[i + 1].X)
                        PartitionBottom = !PartitionBottom;

                    int sign = PartitionBottom ? 1 : -1;

                    angle = 3.141f - angle;
                    float perAngle = angle / PartitionPointCount;//分割几次的每次增加的角度
                    Vector2 exNormal = normal.RotatedBy(-sign * angle / 2);

                    for (int j = 0; j < PartitionPointCount + 1; j++) {
                        Vector2 exNormal2;
                        if (j != 0)
                            exNormal2 = exNormal.RotatedBy(sign * perAngle);
                        else
                            exNormal2 = exNormal;

                        Vector2 Top;
                        Vector2 Bottom;
                        if (PartitionBottom) {
                            Top = Center + (normal * width);
                            Bottom = Center - (exNormal2 * width);
                        }
                        else {
                            Top = Center + (exNormal2 * width);
                            Bottom = Center - (normal * width);
                        }

                        AddVertexInfo(barsTop, barsBottom, Top, Bottom, thunderColor, lengthFactor);

                        Vector2 center2 = (Top + Bottom) / 2;
                        Vector2 dir = (Top - center2) / 4;
                        AddVertexInfo2(bars2Top, bars2Bottom, center2, dir, GetFlowColor(factor), lengthFactor);
                        exNormal = exNormal2;
                    }
                }
                else {
                    AddVertexInfo2(barsTop, barsBottom, Center, normal * width, thunderColor, lengthFactor);
                    AddVertexInfo2(bars2Top, bars2Bottom, Center, normal * width / 4, GetFlowColor(factor), lengthFactor);
                }
            }

            Center = RandomlyPositions[^1] - Main.screenPosition;
            Vector2 dirToBack2 = RandomlyPositions[^2] - RandomlyPositions[^1];
            normal = dirToBack2.RotatedBy(-MathHelper.PiOver2).SafeNormalize(Vector2.One);
            float bottomWidth = thunderWidthFunc(1);
            drawInBack = bottomWidth > 10;
            lengthVec2 = normal * bottomWidth;

            length += dirToBack2.Length();
            float lengthFactor2 = length / texWidth;

            AddVertexInfo2(barsTop, barsBottom, Center, lengthVec2, GetColor(1), lengthFactor2);
            AddVertexInfo2(bars2Top, bars2Bottom, Center, lengthVec2, GetFlowColor(1), lengthFactor2);

            graphicsDevice.Textures[0] = Texture;
            BlendState state = graphicsDevice.BlendState;

            if (UseNonOrAdd) {
                graphicsDevice.BlendState = BlendState.NonPremultiplied;
            }
                
            graphicsDevice.DrawUserPrimitives(PrimitiveType.TriangleStrip, barsTop.ToArray(), 0, barsTop.Count - 2);
            graphicsDevice.DrawUserPrimitives(PrimitiveType.TriangleStrip, barsBottom.ToArray(), 0, barsTop.Count - 2);

            if (UseNonOrAdd) {
                graphicsDevice.BlendState = BlendState.Additive;
            }
                
            graphicsDevice.DrawUserPrimitives(PrimitiveType.TriangleStrip, bars2Top.ToArray(), 0, bars2Top.Count - 2);
            graphicsDevice.DrawUserPrimitives(PrimitiveType.TriangleStrip, bars2Bottom.ToArray(), 0, bars2Top.Count - 2);

            graphicsDevice.BlendState = state;

            if (drawInTip) {
                Texture2D mainTex = VaultAsset.Light.Value;
                var pos = RandomlyPositions[0] - Main.screenPosition;
                var origin = mainTex.Size() / 2;
                Color c = colorFunc(0);
                c.A = 0;

                Vector2 scale = new(thunderWidthFunc(0) / 90, thunderWidthFunc(0) / 130);

                Main.spriteBatch.Draw(mainTex, pos, null, c, tipRotaion, origin, scale, 0, 0);
                Main.spriteBatch.Draw(mainTex, pos, null, c, tipRotaion, origin, scale * 0.75f, 0, 0);
            }

            if (drawInBack) {
                Texture2D mainTex = VaultAsset.Light.Value;
                var pos = RandomlyPositions[^1] - Main.screenPosition;
                var origin = mainTex.Size() / 2;
                Color c = colorFunc(1);
                c.A = 0;

                Vector2 scale = new(thunderWidthFunc(1) / 170, thunderWidthFunc(1) / 200);
                float rot = normal.ToRotation() + 1.57f;
                Main.spriteBatch.Draw(mainTex, pos, null, c, rot, origin, scale, 0, 0);
                Main.spriteBatch.Draw(mainTex, pos, null, c, rot, origin, scale * 0.75f, 0, 0);
            }
        }
        /// <summary>
        /// 添加顶点信息，形成从 top 到 bottom 的线段
        /// </summary>
        /// <param name="topList">存储顶部顶点的列表</param>
        /// <param name="bottomList">存储底部顶点的列表</param>
        /// <param name="top">线段的顶部位置</param>
        /// <param name="bottom">线段的底部位置</param>
        /// <param name="color">顶点颜色</param>
        /// <param name="factor">用于纹理或渐变计算的插值因子</param>
        public void AddVertexInfo(List<ColoredVertex> topList, List<ColoredVertex> bottomList, Vector2 top, Vector2 bottom, Color color, float factor) {
            Vector2 center = (top + bottom) / 2;

            topList.Add(new(top, color, new Vector3(factor, 0, 0)));
            topList.Add(new(center, color, new Vector3(factor, 0.5f, 0)));

            bottomList.Add(new(center, color, new Vector3(factor, 0.5f, 0)));
            bottomList.Add(new(bottom, color, new Vector3(factor, 1, 0)));
        }

        /// <summary>
        /// 添加顶点信息，使用中心点与方向向量来确定线段
        /// </summary>
        /// <param name="topList">存储顶部顶点的列表</param>
        /// <param name="bottomList">存储底部顶点的列表</param>
        /// <param name="center">线段中心点</param>
        /// <param name="dir">方向向量，定义线段的偏移</param>
        /// <param name="color">顶点颜色</param>
        /// <param name="factor">用于纹理或渐变计算的插值因子</param>
        public void AddVertexInfo2(List<ColoredVertex> topList, List<ColoredVertex> bottomList, Vector2 center, Vector2 dir, Color color, float factor) {
            topList.Add(new(center + dir, color, new Vector3(factor, 0, 0)));
            topList.Add(new(center, color, new Vector3(factor, 0.5f, 0)));

            bottomList.Add(new(center, color, new Vector3(factor, 0.5f, 0)));
            bottomList.Add(new(center - dir, color, new Vector3(factor, 1, 0)));
        }

        /// <summary>
        /// 获取闪电的颜色，颜色计算方式会根据 <see cref="UseNonOrAdd"/> 发生变化
        /// </summary>
        /// <param name="factor">插值因子，决定颜色渐变</param>
        /// <returns>计算后的颜色</returns>
        public Color GetColor(float factor) {
            Color thunderColor = colorFunc(factor);
            float alpha = alphaFunc(factor);

            if (UseNonOrAdd)
                thunderColor.A = (byte)(alpha * 255);
            else {
                thunderColor.A = 0;
                thunderColor *= alpha;
            }

            return thunderColor;
        }

        /// <summary>
        /// 获取闪电流动的颜色，颜色计算方式会根据 <see cref="UseNonOrAdd"/> 发生变化
        /// </summary>
        /// <param name="factor">插值因子，决定颜色渐变</param>
        /// <returns>计算后的颜色</returns>
        public Color GetFlowColor(float factor) {
            Color thunderColor = FlowColor;
            float alpha = alphaFunc(factor);

            if (UseNonOrAdd)
                thunderColor.A = (byte)(alpha * 255);
            else {
                thunderColor.A = 0;
                thunderColor *= alpha;
            }

            return thunderColor;
        }
    }
}
