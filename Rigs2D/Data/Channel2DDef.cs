using Microsoft.Xna.Framework;

namespace InnoVault.Rigs2D.Data
{
    /// <summary>
    /// 通道值的形状
    /// </summary>
    public enum Channel2DType
    {
        /// <summary>
        /// 标量（值存在 X 分量）
        /// </summary>
        Scalar,
        /// <summary>
        /// 二维向量
        /// </summary>
        Vector,
    }

    /// <summary>
    /// 两个通道值之间的插值方式
    /// </summary>
    public enum Channel2DBlend
    {
        /// <summary>
        /// 逐分量线性
        /// </summary>
        Linear,
        /// <summary>
        /// 角度按最短弧（标量）
        /// </summary>
        Angle,
        /// <summary>
        /// 绕空间原点按极坐标插值（向量）：半径线性、方位角走最短弧，效应器因此沿弧线而不是直线过渡
        /// </summary>
        Arc,
    }

    /// <summary>
    /// 通道写到哪里
    /// </summary>
    public enum Channel2DTarget
    {
        /// <summary>
        /// 不绑定，只供消费方读取（枪角、握位比一类自定义量）
        /// </summary>
        None,
        /// <summary>
        /// 骨骼局部量：<c>rotation</c> / <c>offset</c> / <c>length</c>
        /// </summary>
        Bone,
        /// <summary>
        /// 求解器属性：<c>target</c> 等空间量解算前按空间换算成世界点，其余标量原样交给求解器
        /// </summary>
        Solver,
        /// <summary>
        /// 贴图件状态：<c>frame</c> / <c>visible</c> / <c>layer</c> / <c>rotation</c> / <c>alpha</c> / <c>scale</c>
        /// </summary>
        Piece,
        /// <summary>
        /// 带状件状态：<c>width</c> / <c>alpha</c> / <c>visible</c> / <c>uvOffset</c> / <c>layer</c>
        /// </summary>
        Ribbon,
        /// <summary>
        /// 实例根：<c>position</c>（空间量）/ <c>rotation</c>
        /// </summary>
        Root,
    }

    /// <summary>
    /// 骨骼局部量的写法
    /// </summary>
    public enum Channel2DMode
    {
        /// <summary>
        /// 定义值 + 通道值 × 倍率
        /// </summary>
        Add,
        /// <summary>
        /// 直接用通道值 × 倍率
        /// </summary>
        Replace,
    }

    /// <summary>
    /// 空间量（求解器目标、根位置）的原点
    /// </summary>
    public enum Channel2DSpace
    {
        /// <summary>
        /// 实例锚点 <c>Rig2DInstance.Anchor</c>（未设置时回落根位置）；角色脚下地面点这类「世界里钉住、骨架跟着走」的原点
        /// </summary>
        Anchor,
        /// <summary>
        /// 实例根位置
        /// </summary>
        Root,
        /// <summary>
        /// 某根骨骼按父骨骼现算的静息近端（解算时刻取，已含本帧上游传播）
        /// </summary>
        Bone,
        /// <summary>
        /// 世界原点（值即世界坐标）
        /// </summary>
        World,
    }

    /// <summary>
    /// 一个通道的绑定：把通道值写进骨骼、求解器、贴图件、带状件或根
    /// <br/>空间量的换算：<c>世界点 = 原点 + 旋转(值 × 倍率 + 偏移) × Scale + 世界偏移</c>；
    /// 不旋转时（缺省）骨架镜像只翻 x（侧视约定），旋转时按原点朝向的局部系、镜像翻侧向分量；
    /// 世界偏移由消费方逐帧写（<c>Rig2DChannels.SetWorldOffset</c>，地形高差一类），<see cref="Channel2DSpace.World"/> 空间不乘 Scale
    /// </summary>
    public sealed class Channel2DBind
    {
        /// <summary>
        /// 写入对象的类别
        /// </summary>
        public Channel2DTarget Target { get; set; }
        /// <summary>
        /// 对象名（骨骼 / 求解器 / 件 / 带；根不需要）
        /// </summary>
        public string Name { get; set; } = string.Empty;
        /// <summary>
        /// 属性名（见 <see cref="Channel2DTarget"/> 各项）
        /// </summary>
        public string Prop { get; set; } = string.Empty;
        /// <summary>
        /// 骨骼局部量的写法
        /// </summary>
        public Channel2DMode Mode { get; set; } = Channel2DMode.Add;
        /// <summary>
        /// 空间量的原点
        /// </summary>
        public Channel2DSpace Space { get; set; } = Channel2DSpace.Anchor;
        /// <summary>
        /// <see cref="Channel2DSpace.Bone"/> 的骨骼名
        /// </summary>
        public string SpaceBone { get; set; }
        /// <summary>
        /// 空间偏移是否随原点朝向旋转（<see cref="Channel2DSpace.Root"/> 取根朝向，<see cref="Channel2DSpace.Bone"/> 取该骨静息轴向）
        /// </summary>
        public bool Rotate { get; set; }
        /// <summary>
        /// 空间偏移常量（Scale 为 1 的像素，加在值上）
        /// </summary>
        public Vector2 Offset { get; set; }
        /// <summary>
        /// 值的乘子
        /// </summary>
        public float Scale { get; set; } = 1f;
        /// <summary>
        /// 定义里显式给了空间（否则只有求解器声明为空间量的属性才走空间换算）
        /// </summary>
        public bool HasSpace { get; set; }

        /// <summary>
        /// 对象索引（骨骼 / 求解器 / 件 / 带），由 <see cref="Rig2DDefinition.Resolve"/> 填充
        /// </summary>
        public int TargetIndex { get; internal set; } = -1;
        /// <summary>
        /// 空间骨骼索引，由 <see cref="Rig2DDefinition.Resolve"/> 填充
        /// </summary>
        public int SpaceBoneIndex { get; internal set; } = -1;
        /// <summary>
        /// 属性码（骨骼 / 件 / 带 / 根的属性名在解析时换成的序号），由 <see cref="Rig2DDefinition.Resolve"/> 填充
        /// </summary>
        public int PropCode { get; internal set; } = -1;

        /// <summary>
        /// 复制一份（不含已解析索引）
        /// </summary>
        public Channel2DBind Clone() => new() {
            Target = Target,
            Name = Name,
            Prop = Prop,
            Mode = Mode,
            Space = Space,
            SpaceBone = SpaceBone,
            Rotate = Rotate,
            Offset = Offset,
            Scale = Scale,
            HasSpace = HasSpace,
        };
    }

    /// <summary>
    /// 一个命名通道：动画层（姿态库、招式、分层动画机）与骨骼 / 求解器之间的中间量
    /// <br/>姿态是「每个通道一个值」的表；消费方和动画层只写通道，绑定负责把值落到骨骼局部量、IK 目标、件状态上，
    /// 没有绑定的通道留给消费方自己读（武器角、握位比一类骨架外的量）
    /// </summary>
    public sealed class Channel2DDef
    {
        /// <summary>
        /// 通道名，同一骨架内唯一
        /// </summary>
        public string Name { get; set; } = string.Empty;
        /// <summary>
        /// 值的形状
        /// </summary>
        public Channel2DType Type { get; set; } = Channel2DType.Scalar;
        /// <summary>
        /// 插值方式
        /// </summary>
        public Channel2DBlend Blend { get; set; } = Channel2DBlend.Linear;
        /// <summary>
        /// 缺省值（标量取 X）
        /// </summary>
        public Vector2 Default { get; set; }
        /// <summary>
        /// 绑定；<see langword="null"/> 表示只供消费方读
        /// </summary>
        public Channel2DBind Bind { get; set; }

        /// <summary>
        /// 在 <see cref="Rig2DDefinition.Channels"/> 中的索引，由 <see cref="Rig2DDefinition.Resolve"/> 填充
        /// </summary>
        public int Index { get; internal set; } = -1;

        /// <summary>
        /// 是否标量
        /// </summary>
        public bool IsScalar => Type == Channel2DType.Scalar;

        /// <summary>
        /// 复制一份定义（不含已解析索引）
        /// </summary>
        public Channel2DDef Clone() => new() {
            Name = Name,
            Type = Type,
            Blend = Blend,
            Default = Default,
            Bind = Bind?.Clone(),
        };
    }
}
