namespace InnoVault.Rigs2D.Data
{
    /// <summary>
    /// 受击 / 碰撞胶囊：沿一根骨的 [<see cref="From"/>, <see cref="To"/>] 段（占骨长比例）加半径。
    /// 在骨架定义里按命名组存放（<c>"hitboxes": { "body": [ ... ], "weapon": [ ... ] }</c>），
    /// <see cref="Runtime.Rig2DHit"/> 按组检测，verlet 链的 <c>colliders</c> 也可以直接引用组名
    /// </summary>
    public sealed class Hitbox2DDef
    {
        /// <summary>
        /// 所沿骨名
        /// </summary>
        public string Bone { get; set; } = string.Empty;
        /// <summary>
        /// 半径（像素，Scale 为 1）
        /// </summary>
        public float Radius { get; set; } = 8f;
        /// <summary>
        /// 胶囊起点（占骨长比例，0 = 近端）
        /// </summary>
        public float From { get; set; }
        /// <summary>
        /// 胶囊终点（占骨长比例，1 = 尖端）
        /// </summary>
        public float To { get; set; } = 1f;

        /// <summary>
        /// 骨索引，由 <see cref="Rig2DDefinition.Resolve"/> 填充
        /// </summary>
        public int BoneIndex { get; internal set; } = -1;

        /// <summary>
        /// 复制一份（不含解析结果）
        /// </summary>
        public Hitbox2DDef Clone() => new() { Bone = Bone, Radius = Radius, From = From, To = To };
    }
}
