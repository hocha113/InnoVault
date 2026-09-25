using System;

namespace InnoVault.Rigs2D.Runtime
{
    /// <summary>
    /// 声明式句柄绑定的基类：标在消费方对象的实例字段 / 属性上，
    /// <see cref="Rig2DInstance.Bind"/> 按名把骨骼 / 贴图件下标或求解器引用填进来，热重载重绑后自动重填
    /// <br/>不写名字时取成员名；<see cref="Count"/> &gt; 0 时每个名字视作 <c>string.Format</c> 模板，
    /// 用 <see cref="Start"/> 起的序号展开（<c>"leg{0}"</c> → leg0, leg1, …）
    /// <br/>成员形状与名字的对应：
    /// <list type="bullet">
    ///   <item>标量：恰好一个名字，<see cref="Count"/> 必须为 0</item>
    ///   <item>一维数组：<see cref="Count"/> 为 0 时按名字列表逐个填；大于 0 时只允许一个模板，展开成 Count 个</item>
    ///   <item>二维数组 <c>[Count, 名字数]</c>：每个名字一个模板，行 = 序号、列 = 名字（<c>parts[leg, part]</c> 这类布局）</item>
    /// </list>
    /// 已存在且尺寸相符的数组原地回填（可用于 <see langword="readonly"/> 字段），否则新建后写回
    /// <br/>缺名 / 类型不符不抛：下标写 <c>-1</c>、引用写 <see langword="null"/>，问题汇总在 <see cref="Rig2DInstance.BindErrors"/> 并记一条日志
    /// </summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
    public abstract class Rig2DBindAttribute : Attribute
    {
        /// <summary>
        /// 名字或模板列表；为空时取成员名
        /// </summary>
        public string[] Names { get; }
        /// <summary>
        /// 模板展开个数；0 = 不展开，名字按字面使用
        /// </summary>
        public int Count { get; set; }
        /// <summary>
        /// 模板展开的起始序号
        /// </summary>
        public int Start { get; set; }

        /// <inheritdoc cref="Rig2DBindAttribute"/>
        protected Rig2DBindAttribute(string[] names) {
            Names = names ?? [];
        }
    }

    /// <summary>
    /// 骨骼下标绑定：成员为 <see cref="int"/>、<c>int[]</c> 或 <c>int[,]</c>
    /// <br/><c>[Rig2DBone("head")] int head;</c>　<c>[Rig2DBone("station{0}", Count = 4)] int[] stations;</c>
    /// </summary>
    public sealed class Rig2DBoneAttribute(params string[] names) : Rig2DBindAttribute(names)
    {
    }

    /// <summary>
    /// 贴图件下标绑定（件名或其所属骨骼名）：成员为 <see cref="int"/>、<c>int[]</c> 或 <c>int[,]</c>
    /// </summary>
    public sealed class Rig2DPieceAttribute(params string[] names) : Rig2DBindAttribute(names)
    {
    }

    /// <summary>
    /// 带状件下标绑定（件名或其首骨名）：成员为 <see cref="int"/>、<c>int[]</c> 或 <c>int[,]</c>
    /// <br/><c>[Rig2DRibbon("tail")] int tailRibbon;</c>
    /// </summary>
    public sealed class Rig2DRibbonAttribute(params string[] names) : Rig2DBindAttribute(names)
    {
    }

    /// <summary>
    /// 求解器引用绑定：成员为 <see cref="Solvers.Rig2DSolver"/> 的具体子类或其一维 / 二维数组；类型不符视为缺失
    /// <br/><c>[Rig2DSolver("armR", "armL")] TwoBoneIKSolver[] arms;</c>　<c>[Rig2DSolver] FootPlantGaitSolver gait;</c>
    /// </summary>
    public sealed class Rig2DSolverAttribute(params string[] names) : Rig2DBindAttribute(names)
    {
    }

    /// <summary>
    /// 通道下标绑定：成员为 <see cref="int"/>、<c>int[]</c> 或 <c>int[,]</c>（下标用于 <see cref="Rig2DChannels"/> 与 <see cref="Animation.Rig2DPose"/>）
    /// <br/><c>[Rig2DChannel("spine1", "spine2", "neck", "head")] readonly int[] spine = new int[4];</c>
    /// </summary>
    public sealed class Rig2DChannelAttribute(params string[] names) : Rig2DBindAttribute(names)
    {
    }

    /// <summary>
    /// 姿态绑定：成员为 <see cref="int"/>（姿态库下标）或 <see cref="Animation.Rig2DPose"/>（姿态库条目，只读共享），及其一维 / 二维数组
    /// <br/><c>[Rig2DPose("guard", "crouch")] Rig2DPose[] stances;</c>　<c>[Rig2DPose] Rig2DPose guard;</c>
    /// </summary>
    public sealed class Rig2DPoseAttribute(params string[] names) : Rig2DBindAttribute(names)
    {
    }

    /// <summary>
    /// 招式绑定：成员为 <see cref="int"/>（招式表下标）或 <see cref="Animation.Rig2DMove"/>（解析后的招式，只读共享），及其一维 / 二维数组
    /// <br/><c>[Rig2DMove("thrust", "sweep", "chop")] readonly Rig2DMove[] combo = new Rig2DMove[3];</c>
    /// </summary>
    public sealed class Rig2DMoveAttribute(params string[] names) : Rig2DBindAttribute(names)
    {
    }
}
