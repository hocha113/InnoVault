using Microsoft.Xna.Framework;

namespace InnoVault.Rigs2D.Data
{
    /// <summary>
    /// 贴图件沿骨骼的拉伸方式
    /// </summary>
    public enum Piece2DStretch
    {
        /// <summary>
        /// 不拉伸：按骨架 Scale 等比绘制，骨长与贴图无关（关节像素与骨长事先对齐的整图件）
        /// </summary>
        None,
        /// <summary>
        /// 沿贴图轴向拉伸：轴向缩放 = 骨长 / <see cref="Piece2DDef.AxisLength"/>，垂直方向保持 Scale。
        /// 要求贴图轴向为 ±X 或 ±Y（SpriteBatch 只能做轴对齐的非等比缩放）
        /// </summary>
        Axis,
        /// <summary>
        /// 等比拉伸：整图按 骨长 / <see cref="Piece2DDef.AxisLength"/> 缩放，可配 <see cref="Piece2DDef.StretchMin"/> / <see cref="Piece2DDef.StretchMax"/> 钳制
        /// </summary>
        Uniform,
    }

    /// <summary>
    /// 按角换帧的取值来源
    /// </summary>
    public enum Piece2DFrameSource
    {
        /// <summary>
        /// 骨的世界角（朝向系：镜像时按面向折算，0 = 朝前，正 = 顺时针 / 朝下）
        /// </summary>
        Angle,
        /// <summary>
        /// 骨相对参照骨（缺省父骨）的夹角（朝向系）
        /// </summary>
        Relative,
        /// <summary>
        /// 一条通道的标量值
        /// </summary>
        Channel,
    }

    /// <summary>
    /// 按角换帧：按骨世界角、相对角或通道值分桶自动选帧，带迟滞防抖（手握 / 张、脚正侧 / 透视缩短、头三分之四侧）
    /// <br/>分桶：取值小于第 k 个阈值即第 k 桶（阈值升序；全不小于即最后一桶）；<see cref="Map"/> 把桶号映射到帧号（缺省桶号即帧号）
    /// </summary>
    public sealed class Piece2DFrameBy
    {
        /// <summary>
        /// 取值来源
        /// </summary>
        public Piece2DFrameSource Source { get; set; } = Piece2DFrameSource.Angle;
        /// <summary>
        /// 取角的骨（缺省件自己的骨）
        /// </summary>
        public string Bone { get; set; }
        /// <summary>
        /// 相对角的参照骨（缺省取角骨的父骨）
        /// </summary>
        public string Ref { get; set; }
        /// <summary>
        /// 通道名（<see cref="Piece2DFrameSource.Channel"/>）
        /// </summary>
        public string Channel { get; set; }
        /// <summary>
        /// 升序阈值（角度来源为弧度）
        /// </summary>
        public float[] Thresholds { get; set; } = [];
        /// <summary>
        /// 桶号 → 帧号（可空）
        /// </summary>
        public int[] Map { get; set; }
        /// <summary>
        /// 迟滞：越过阈值这么多才换桶（与阈值同单位）
        /// </summary>
        public float Hysteresis { get; set; }

        internal int BoneIndex = -1;
        internal int RefIndex = -1;
        internal int ChannelIndex = -1;

        /// <summary>
        /// 深拷贝（不含解析结果）
        /// </summary>
        public Piece2DFrameBy Clone() => new() {
            Source = Source,
            Bone = Bone,
            Ref = Ref,
            Channel = Channel,
            Thresholds = (float[])Thresholds?.Clone() ?? [],
            Map = (int[])Map?.Clone(),
            Hysteresis = Hysteresis,
        };
    }

    /// <summary>
    /// 一件贴图的静态定义：挂在哪根骨、贴图内哪一像素钉在骨骼近端、贴图内在的骨轴角，以及层序与着色
    /// <br/>绘制公式：<c>rotation = 骨骼世界轴向 − Axis + ExtraRotation</c>；镜像时轴角取 π − Axis、锚点 x 取 宽 − x
    /// <br/>关节盖件：给了 <see cref="Bone2"/> 时件钉在 <see cref="Bone2"/> 的近端（两骨相接的关节），轴向取两骨角平分线
    /// （<see cref="JointWeight"/> 调偏向），可按弯曲角放大（<see cref="BendScale"/>）：肩头、肘、膝、胯的补缝统一用它
    /// </summary>
    public sealed class Piece2DDef
    {
        /// <summary>
        /// 件名（可选，缺省用骨骼名）。用于运行时按名查 <c>Piece2DState</c>
        /// </summary>
        public string Name { get; set; }
        /// <summary>
        /// 所挂骨骼名
        /// </summary>
        public string Bone { get; set; } = string.Empty;
        /// <summary>
        /// 贴图路径（模组相对路径、不含扩展名；<c>@其他模组/路径</c> 跨模组）
        /// </summary>
        public string Texture { get; set; } = string.Empty;
        /// <summary>
        /// 贴图内钉在骨骼近端关节的像素（单帧坐标）
        /// </summary>
        public Vector2 Proximal { get; set; }
        /// <summary>
        /// 贴图内在骨轴角（弧度）：近端 → 远端在贴图坐标系里的方向。贴图"尖端朝上"即 −π/2，"朝右"即 0
        /// </summary>
        public float Axis { get; set; }
        /// <summary>
        /// 贴图坐标系里近端到远端的像素距离；拉伸模式据此换算缩放。<c>0</c> 表示按贴图沿轴向的整边长
        /// </summary>
        public float AxisLength { get; set; }
        /// <summary>
        /// 拉伸方式
        /// </summary>
        public Piece2DStretch Stretch { get; set; } = Piece2DStretch.None;
        /// <summary>
        /// 拉伸倍率下限（仅 <see cref="Piece2DStretch.Uniform"/> / <see cref="Piece2DStretch.Axis"/> 生效，<c>0</c> 不限）
        /// </summary>
        public float StretchMin { get; set; }
        /// <summary>
        /// 拉伸倍率上限（<c>0</c> 不限）
        /// </summary>
        public float StretchMax { get; set; }
        /// <summary>
        /// 层序键，小的先画（压在下面）。运行时可由 <c>Piece2DState.SortKey</c> 覆盖
        /// </summary>
        public int Layer { get; set; }
        /// <summary>
        /// 默认是否水平镜像（运行时可由 <c>Piece2DState.Mirror</c> 覆盖）
        /// </summary>
        public bool Mirror { get; set; }
        /// <summary>
        /// 设计时额外缩放（贴图坐标系）
        /// </summary>
        public Vector2 Scale { get; set; } = Vector2.One;
        /// <summary>
        /// 压暗乘子（RGB 同乘，远层部件常用 0.55 ~ 0.85）
        /// </summary>
        public float Dark { get; set; } = 1f;
        /// <summary>
        /// 设计时着色乘子
        /// </summary>
        public Color Tint { get; set; } = Color.White;
        /// <summary>
        /// 设计时不透明度乘子
        /// </summary>
        public float Alpha { get; set; } = 1f;
        /// <summary>
        /// 默认可见
        /// </summary>
        public bool Visible { get; set; } = true;
        /// <summary>
        /// 不受光照（发光贴图 / glowmask）：绘制时不采样物块光照，以白色代替光照项；剪影一类的固定环境色环境仍照常生效。
        /// 着色、压暗、不透明度与环境 <c>Tint</c> / <c>Alpha</c> 照旧相乘
        /// </summary>
        public bool Unlit { get; set; }
        /// <summary>
        /// 竖排帧数（贴图为多帧竖排图集时 &gt; 1；运行时以 <c>Piece2DState.Frame</c> 选帧）
        /// </summary>
        public int Frames { get; set; } = 1;
        /// <summary>
        /// 每帧底部隔帧留白像素（多帧竖排时从帧高里扣除）
        /// </summary>
        public int FramePad { get; set; }
        /// <summary>
        /// <see cref="Proximal"/> 是否按帧尺寸的比例给（0..1）：贴图尺寸在定义期未知（原版贴图、运行时覆写）时用它，
        /// <c>(0.5, 0.5)</c> = 帧中心、<c>(0.5, 1)</c> = 底边中点。镜像规则同像素锚（x 取 1 − x）
        /// </summary>
        public bool ProximalNormalized { get; set; }
        /// <summary>
        /// 关节盖件的第二根骨（可空）：件钉在它的近端，轴向取 <see cref="Bone"/> 与它的角平分线
        /// </summary>
        public string Bone2 { get; set; }
        /// <summary>
        /// 关节盖件的轴向偏向：0 = 随 <see cref="Bone"/>，1 = 随 <see cref="Bone2"/>，缺省 0.5 取平分线
        /// </summary>
        public float JointWeight { get; set; } = 0.5f;
        /// <summary>
        /// 关节盖件按弯曲角放大：缩放 × (1 + BendScale × 弯角 / π)
        /// </summary>
        public float BendScale { get; set; }
        /// <summary>
        /// 按角换帧（可空）
        /// </summary>
        public Piece2DFrameBy FrameBy { get; set; }

        /// <summary>
        /// 所挂骨骼索引，由 <see cref="Rig2DDefinition.Resolve"/> 填充
        /// </summary>
        public int BoneIndex { get; internal set; } = -1;
        /// <summary>
        /// 关节盖件第二根骨的索引（没有为 <c>-1</c>），由 <see cref="Rig2DDefinition.Resolve"/> 填充
        /// </summary>
        public int Bone2Index { get; internal set; } = -1;
        /// <summary>
        /// 在 <see cref="Rig2DDefinition.Pieces"/> 中的索引，由 <see cref="Rig2DDefinition.Resolve"/> 填充
        /// </summary>
        public int Index { get; internal set; } = -1;

        /// <summary>
        /// 解析后的显示名（缺省回落骨骼名）
        /// </summary>
        public string DisplayName => string.IsNullOrEmpty(Name) ? Bone : Name;

        /// <summary>
        /// 用"远端像素"反推轴角与轴长：<c>Axis = (distal − Proximal).ToRotation()</c>
        /// </summary>
        public void SetDistal(Vector2 distal) {
            Vector2 d = distal - Proximal;
            AxisLength = d.Length();
            Axis = AxisLength > 0.0001f ? (float)System.Math.Atan2(d.Y, d.X) : 0f;
        }

        /// <summary>
        /// 复制一份定义（不含已解析的索引）
        /// </summary>
        public Piece2DDef Clone() => new() {
            Name = Name,
            Bone = Bone,
            Texture = Texture,
            Proximal = Proximal,
            Axis = Axis,
            AxisLength = AxisLength,
            Stretch = Stretch,
            StretchMin = StretchMin,
            StretchMax = StretchMax,
            Layer = Layer,
            Mirror = Mirror,
            Scale = Scale,
            Dark = Dark,
            Tint = Tint,
            Alpha = Alpha,
            Visible = Visible,
            Unlit = Unlit,
            Frames = Frames,
            FramePad = FramePad,
            ProximalNormalized = ProximalNormalized,
            Bone2 = Bone2,
            JointWeight = JointWeight,
            BendScale = BendScale,
            FrameBy = FrameBy?.Clone(),
        };
    }
}
