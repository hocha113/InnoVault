using Microsoft.Xna.Framework;
using System.Collections.Generic;

namespace InnoVault.Rigs2D.Data
{
    /// <summary>
    /// 一张蒙皮网格件的静态定义：一张贴图铺在一片三角网格上，顶点按权重跟着若干骨骼变形
    /// <br/>整图件是"一节一张图、整张转"，带状件是"沿骨链的一条皮"，网格件是"一张图按控制点揉"：
    /// 裙摆、发束、披风、旗面、整幅立绘的木偶式摆动（AE 操控点 / Spine 网格的同类物）都画成它
    /// <br/>顶点坐标写在宿主骨 <see cref="Bone"/> 的静息局部系里（x 沿骨轴、y 沿骨骼右侧，Scale 为 1 的像素）；
    /// 影响骨 <see cref="Bones"/> 是控制柄，权重缺省由加载时的谐和插值自动求出（控制柄处为 1、沿网格平滑衰减），
    /// 一个顶点没分完的权重归宿主骨，所以不连到任何控制柄的孤岛就跟着宿主骨刚性走
    /// <br/>与整图件、带状件共用层序键，可在同一副骨架里交错
    /// </summary>
    public sealed class Mesh2DDef
    {
        /// <summary>
        /// 件名（可选，缺省用宿主骨名）。运行时按名查 <c>Mesh2DState</c>
        /// </summary>
        public string Name { get; set; }
        /// <summary>
        /// 宿主骨：顶点坐标的参考系，未分到影响骨的权重随它刚性运动
        /// </summary>
        public string Bone { get; set; } = string.Empty;
        /// <summary>
        /// 贴图路径（模组相对路径、不含扩展名；<c>@其他模组/路径</c> 跨模组）
        /// </summary>
        public string Texture { get; set; } = string.Empty;
        /// <summary>
        /// 影响骨（控制柄）列表；空表示整片网格刚性挂在宿主骨上
        /// </summary>
        public List<string> Bones { get; } = [];
        /// <summary>
        /// 顶点坐标 x0, y0, x1, y1 …（宿主骨静息局部系，Scale 为 1 的像素）
        /// </summary>
        public float[] Vertices { get; set; } = [];
        /// <summary>
        /// 三角形顶点下标，每三个一组（正反面不限，绘制时关剔除）
        /// </summary>
        public int[] Triangles { get; set; } = [];
        /// <summary>
        /// 逐顶点纹理坐标 u0, v0, u1, v1 …（0..1）；空时按 <see cref="UvRect"/> 换算
        /// </summary>
        public float[] Uvs { get; set; } = [];
        /// <summary>
        /// 贴图在宿主骨局部系里覆盖的矩形 [x, y, 宽, 高]：<see cref="Uvs"/> 为空时 u = (x − 矩形x) / 宽、v 同理；
        /// 两者都没给时取全部顶点的外接框
        /// </summary>
        public float[] UvRect { get; set; } = [];
        /// <summary>
        /// 显式权重（可选）：顶点数 × 影响骨数，按顶点逐行排列；一行之和不足 1 的部分归宿主骨，超过 1 时按比例归一。
        /// 为空时加载期自动求谐和权重
        /// </summary>
        public float[] Weights { get; set; } = [];
        /// <summary>
        /// 自动权重的控制柄半径（像素，Scale 为 1）：离影响骨静息线段（零长骨即近端点）这么近的顶点钉成该骨 1；
        /// 一根骨一个顶点都没钉住时取离它最近的空闲顶点
        /// </summary>
        public float HandleRadius { get; set; } = 1f;
        /// <summary>
        /// 层序键，小的先画；与整图件、带状件共用同一把尺
        /// </summary>
        public int Layer { get; set; }
        /// <summary>
        /// 设计时着色乘子
        /// </summary>
        public Color Tint { get; set; } = Color.White;
        /// <summary>
        /// 压暗乘子（RGB 同乘）
        /// </summary>
        public float Dark { get; set; } = 1f;
        /// <summary>
        /// 设计时不透明度乘子
        /// </summary>
        public float Alpha { get; set; } = 1f;
        /// <summary>
        /// 是否用加色混合绘制
        /// </summary>
        public bool Additive { get; set; }
        /// <summary>
        /// 默认可见
        /// </summary>
        public bool Visible { get; set; } = true;
        /// <summary>
        /// 不受光照（发光层）：不采样物块光照，以白色代替光照项；固定环境色环境仍照常生效
        /// </summary>
        public bool Unlit { get; set; }

        /// <summary>
        /// 在 <see cref="Rig2DDefinition.Meshes"/> 中的索引，由 <see cref="Rig2DDefinition.Resolve"/> 填充
        /// </summary>
        public int Index { get; internal set; } = -1;
        /// <summary>
        /// 宿主骨索引，由 <see cref="Rig2DDefinition.Resolve"/> 填充
        /// </summary>
        public int BoneIndex { get; internal set; } = -1;
        /// <summary>
        /// 影响骨索引（与 <see cref="Bones"/> 同序），由 <see cref="Rig2DDefinition.Resolve"/> 填充
        /// </summary>
        public int[] BoneIndices { get; internal set; } = [];
        /// <summary>
        /// 顶点数
        /// </summary>
        public int VertexCount { get; internal set; }
        /// <summary>
        /// 绘制用三角形下标（已校验）
        /// </summary>
        public short[] Indices { get; internal set; } = [];
        /// <summary>
        /// 逐顶点纹理坐标（已按 <see cref="Uvs"/> / <see cref="UvRect"/> 解析）
        /// </summary>
        public Vector2[] TexCoords { get; internal set; } = [];
        /// <summary>
        /// 顶点在骨架绑定空间里的位置（定义静息姿态、根在原点不转、Scale 为 1）
        /// </summary>
        public Vector2[] BindPositions { get; internal set; } = [];

        //逐顶点蒙皮表（压缩存储）：顶点 v 的条目是 [SkinStart[v], SkinStart[v + 1])，
        //每条 = 骨骼索引 + 权重 + 顶点在该骨绑定局部系里的坐标（宿主骨补足的余量也是一条）
        internal int[] SkinStart = [];
        internal int[] SkinBone = [];
        internal float[] SkinWeight = [];
        internal Vector2[] SkinLocal = [];

        /// <summary>
        /// 解析后的显示名（缺省回落宿主骨名）
        /// </summary>
        public string DisplayName => !string.IsNullOrEmpty(Name) ? Name : Bone;

        /// <summary>
        /// 某顶点对某骨骼的最终权重（宿主骨含余量）；调试与工具用
        /// </summary>
        public float WeightOf(int vertex, int boneIndex) {
            if (vertex < 0 || vertex + 1 >= SkinStart.Length) {
                return 0f;
            }
            float w = 0f;
            for (int e = SkinStart[vertex]; e < SkinStart[vertex + 1]; e++) {
                if (SkinBone[e] == boneIndex) {
                    w += SkinWeight[e];
                }
            }
            return w;
        }

        /// <summary>
        /// 复制一份定义（不含解析结果）
        /// </summary>
        public Mesh2DDef Clone() {
            Mesh2DDef c = new() {
                Name = Name,
                Bone = Bone,
                Texture = Texture,
                Vertices = (float[])Vertices?.Clone() ?? [],
                Triangles = (int[])Triangles?.Clone() ?? [],
                Uvs = (float[])Uvs?.Clone() ?? [],
                UvRect = (float[])UvRect?.Clone() ?? [],
                Weights = (float[])Weights?.Clone() ?? [],
                HandleRadius = HandleRadius,
                Layer = Layer,
                Tint = Tint,
                Dark = Dark,
                Alpha = Alpha,
                Additive = Additive,
                Visible = Visible,
                Unlit = Unlit,
            };
            c.Bones.AddRange(Bones);
            return c;
        }
    }
}
