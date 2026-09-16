using Microsoft.Xna.Framework;

namespace InnoVault.Rigs2D.Data
{
    /// <summary>
    /// 一根骨骼的静态定义
    /// <br/>骨骼用"近端关节位置 + 轴向角 + 长度"描述，轴向从近端指向远端（尖端）
    /// <br/>子骨骼的 <see cref="Offset"/> 与 <see cref="Rotation"/> 都在父骨骼的局部系里表达：
    /// x 沿父骨骼轴向，y 为轴向顺时针转 90° 的方向（屏幕系 y 向下）
    /// </summary>
    public sealed class Bone2DDef
    {
        /// <summary>
        /// 骨骼名，在同一骨架内唯一
        /// </summary>
        public string Name { get; set; } = string.Empty;
        /// <summary>
        /// 父骨骼名；空表示根骨骼（跟随实例的 <c>RootPosition</c> / <c>RootRotation</c>）
        /// </summary>
        public string Parent { get; set; }
        /// <summary>
        /// 相对父骨骼锚点的局部偏移（像素，Scale 为 1 时的量）
        /// </summary>
        public Vector2 Offset { get; set; }
        /// <summary>
        /// 偏移从父骨骼的尖端起量（链式连接）；<see langword="false"/> 时从父骨骼近端起量
        /// </summary>
        public bool AtParentTip { get; set; }
        /// <summary>
        /// 静息旋转（弧度）。<see cref="InheritRotation"/> 为真时相对父骨骼轴向，否则为世界绝对角
        /// </summary>
        public float Rotation { get; set; }
        /// <summary>
        /// 是否继承父骨骼旋转；关掉后骨骼只跟随父位置、朝向保持世界绝对角
        /// </summary>
        public bool InheritRotation { get; set; } = true;
        /// <summary>
        /// 静息骨长（像素，Scale 为 1 时的量）
        /// </summary>
        public float Length { get; set; }

        /// <summary>
        /// 在 <see cref="Rig2DDefinition.Bones"/> 中的索引，由 <see cref="Rig2DDefinition.Resolve"/> 填充
        /// </summary>
        public int Index { get; internal set; } = -1;
        /// <summary>
        /// 父骨骼索引，<c>-1</c> 表示根，由 <see cref="Rig2DDefinition.Resolve"/> 填充
        /// </summary>
        public int ParentIndex { get; internal set; } = -1;

        /// <summary>
        /// 是否为根骨骼
        /// </summary>
        public bool IsRoot => string.IsNullOrEmpty(Parent);

        /// <summary>
        /// 复制一份定义（不含已解析的索引）
        /// </summary>
        public Bone2DDef Clone() => new() {
            Name = Name,
            Parent = Parent,
            Offset = Offset,
            AtParentTip = AtParentTip,
            Rotation = Rotation,
            InheritRotation = InheritRotation,
            Length = Length,
        };
    }
}
