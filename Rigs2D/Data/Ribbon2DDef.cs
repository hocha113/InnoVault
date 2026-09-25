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
        /// <summary>
        /// 逐骨锚定：每个关节点的 u 固定（<see cref="Ribbon2DDef.UvStops"/>，缺省均分），骨长变化、弯折时纹理不沿条带游走（肢体条带用它）
        /// </summary>
        Bone,
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
        /// <see cref="Ribbon2DUv.Bone"/> 模式下每个关节点（去重后的关节 + 可选尖端）的 u；缺省或数量不够时均分
        /// </summary>
        public float[] UvStops { get; set; } = [];
        /// <summary>
        /// 左右偏置剖面（−1..1，按沿链进度线性采样）：正 = 条带右侧（法线正向）更厚、左侧更薄，总宽不变；
        /// 前后厚度不同的肢体（小腿肚在后、胸在前）用它，骨架镜像时自动取反
        /// </summary>
        public float[] BiasProfile { get; set; } = [];
        /// <summary>
        /// 关节斜接保体积：转角处半宽 ÷ cos(折角 / 2)，弯肘弯膝时截面不被压瘦
        /// </summary>
        public bool Miter { get; set; }
        /// <summary>
        /// 斜接放大上限（折得很死时不外扩成尖刺）
        /// </summary>
        public float MiterLimit { get; set; } = 2.5f;
        /// <summary>
        /// 关节按折角加宽：宽 × (1 + JointBulge × 折角 / π)（肌肉在弯折处鼓起）
        /// </summary>
        public float JointBulge { get; set; }
        /// <summary>
        /// 首端帽长度（像素，Scale 为 1）：这段里 u 按 <see cref="CapStartU"/> 定长映射，不随条带拉伸（手掌、脚掌、尾尖）
        /// </summary>
        public float CapStart { get; set; }
        /// <summary>
        /// 首端帽占纹理 u 的比例（0..1）
        /// </summary>
        public float CapStartU { get; set; }
        /// <summary>
        /// 尾端帽长度（像素，Scale 为 1）
        /// </summary>
        public float CapEnd { get; set; }
        /// <summary>
        /// 尾端帽占纹理 u 的比例（0..1）
        /// </summary>
        public float CapEndU { get; set; }

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
        /// 按沿链进度取左右偏置（−1..1）
        /// </summary>
        public float BiasAt(float t) {
            if (BiasProfile == null || BiasProfile.Length == 0) {
                return 0f;
            }
            if (BiasProfile.Length == 1) {
                return MathHelper.Clamp(BiasProfile[0], -1f, 1f);
            }
            float f = MathHelper.Clamp(t, 0f, 1f) * (BiasProfile.Length - 1);
            int a = (int)f;
            int b = System.Math.Min(a + 1, BiasProfile.Length - 1);
            return MathHelper.Clamp(MathHelper.Lerp(BiasProfile[a], BiasProfile[b], f - a), -1f, 1f);
        }

        /// <summary>
        /// 条带离中心线的最大外扩（像素，Scale 为 1）：外接框估算用，含偏置、斜接与关节加宽的上限
        /// </summary>
        public float MaxHalfWidth() {
            float w = System.Math.Max(Width, float.IsNaN(WidthEnd) ? Width : WidthEnd);
            if (WidthProfile != null) {
                foreach (float v in WidthProfile) {
                    w = System.Math.Max(w, v);
                }
            }
            float bias = 0f;
            if (BiasProfile != null) {
                foreach (float v in BiasProfile) {
                    bias = System.Math.Max(bias, System.Math.Abs(v));
                }
            }
            float k = (1f + System.Math.Min(bias, 1f)) * (Miter ? System.Math.Max(MiterLimit, 1f) : 1f) * (1f + System.Math.Max(JointBulge, 0f));
            return w * 0.5f * k;
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
                UvStops = UvStops != null ? (float[])UvStops.Clone() : [],
                BiasProfile = BiasProfile != null ? (float[])BiasProfile.Clone() : [],
                Miter = Miter,
                MiterLimit = MiterLimit,
                JointBulge = JointBulge,
                CapStart = CapStart,
                CapStartU = CapStartU,
                CapEnd = CapEnd,
                CapEndU = CapEndU,
            };
            c.Bones.AddRange(Bones);
            return c;
        }
    }
}
