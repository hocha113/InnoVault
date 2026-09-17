using Microsoft.Xna.Framework;
using System.Collections.Generic;

namespace InnoVault.Rigs2D.Data
{
    /// <summary>
    /// 带状件的纹理映射方式
    /// </summary>
    public enum Ribbon2DUv
    {
        /// <summary>
        /// 整张贴图沿链拉满：u 从链根 0 到链尖 1
        /// </summary>
        Stretch,
        /// <summary>
        /// 按世界长度平铺：每 <see cref="Ribbon2DDef.TileLength"/> 像素重复一次贴图（采样器换成 Wrap）
        /// </summary>
        Tile,
    }

    /// <summary>
    /// 一条带状件的静态定义：把一条骨链的关节点连成三角形条带并贴上纹理
    /// <br/>整图旋转件画"一节一张图"的身体；带状件画"连续一条皮"的身体（尾巴、触手、绳索、拖尾），
    /// 二者共用层序键，可在同一副骨架里交错
    /// <br/>贴图约定：u 沿链方向（根 → 尖），v 横跨条带（左 0 → 右 1）
    /// </summary>
    public sealed class Ribbon2DDef
    {
        /// <summary>
        /// 件名（可选，缺省用首骨名）。运行时按名查 <c>Ribbon2DState</c>
        /// </summary>
        public string Name { get; set; }
        /// <summary>
        /// 骨链（有序，根 → 尖）；条带经过每根骨的近端，<see cref="IncludeTip"/> 为真时再延到末骨尖端
        /// </summary>
        public List<string> Bones { get; } = [];
        /// <summary>
        /// 贴图路径（模组相对路径、不含扩展名；<c>@其他模组/路径</c> 跨模组）
        /// </summary>
        public string Texture { get; set; } = string.Empty;
        /// <summary>
        /// 根端宽度（像素，Scale 为 1 时的量，整宽而非半宽）
        /// </summary>
        public float Width { get; set; } = 16f;
        /// <summary>
        /// 尖端宽度（像素）；<see cref="float.NaN"/> 表示与 <see cref="Width"/> 相同（等宽），小于它即线性收窄
        /// </summary>
        public float WidthEnd { get; set; } = float.NaN;
        /// <summary>
        /// 宽度剖面（像素）：非空时按沿链进度 0..1 线性采样，覆盖 <see cref="Width"/> / <see cref="WidthEnd"/>
        /// </summary>
        public float[] WidthProfile { get; set; } = [];
        /// <summary>
        /// 纹理映射方式
        /// </summary>
        public Ribbon2DUv Uv { get; set; } = Ribbon2DUv.Stretch;
        /// <summary>
        /// 平铺模式下每次重复占的世界长度（像素，Scale 为 1 时的量）
        /// </summary>
        public float TileLength { get; set; } = 64f;
        /// <summary>
        /// 条带是否延伸到末骨尖端（否则止于末骨近端）
        /// </summary>
        public bool IncludeTip { get; set; } = true;
        /// <summary>
        /// 每两关节之间的 Catmull-Rom 细分数；0 关闭（直接连关节点）
        /// </summary>
        public int Smooth { get; set; }
        /// <summary>
        /// 层序键，小的先画；与整图件共用同一把尺
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
        /// 是否用加色混合绘制（拖尾 / 能量带）
        /// </summary>
        public bool Additive { get; set; }
        /// <summary>
        /// 默认可见
        /// </summary>
        public bool Visible { get; set; } = true;
        /// <summary>
        /// 不受光照（能量带 / 发光条）：顶点着色不采样物块光照，以白色代替光照项；固定环境色环境仍照常生效
        /// </summary>
        public bool Unlit { get; set; }

        /// <summary>
        /// 骨链的骨骼索引，由 <see cref="Rig2DDefinition.Resolve"/> 填充
        /// </summary>
        public int[] BoneIndices { get; internal set; } = [];
        /// <summary>
        /// 在 <see cref="Rig2DDefinition.Ribbons"/> 中的索引，由 <see cref="Rig2DDefinition.Resolve"/> 填充
        /// </summary>
        public int Index { get; internal set; } = -1;

        /// <summary>
        /// 解析后的显示名（缺省回落首骨名）
        /// </summary>
        public string DisplayName => !string.IsNullOrEmpty(Name) ? Name : (Bones.Count > 0 ? Bones[0] : string.Empty);

        /// <summary>
        /// 按沿链进度取设计宽度（像素，Scale 为 1）
        /// </summary>
        public float WidthAt(float t) {
            if (WidthProfile != null && WidthProfile.Length > 0) {
                if (WidthProfile.Length == 1) {
                    return WidthProfile[0];
                }
                float f = MathHelper.Clamp(t, 0f, 1f) * (WidthProfile.Length - 1);
                int a = (int)f;
                int b = System.Math.Min(a + 1, WidthProfile.Length - 1);
                return MathHelper.Lerp(WidthProfile[a], WidthProfile[b], f - a);
            }
            float end = float.IsNaN(WidthEnd) ? Width : WidthEnd;
            return MathHelper.Lerp(Width, end, MathHelper.Clamp(t, 0f, 1f));
        }

        /// <summary>
        /// 复制一份定义（不含已解析的索引）
        /// </summary>
        public Ribbon2DDef Clone() {
            Ribbon2DDef c = new() {
                Name = Name,
                Texture = Texture,
                Width = Width,
                WidthEnd = WidthEnd,
                WidthProfile = WidthProfile != null ? (float[])WidthProfile.Clone() : [],
                Uv = Uv,
                TileLength = TileLength,
                IncludeTip = IncludeTip,
                Smooth = Smooth,
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
