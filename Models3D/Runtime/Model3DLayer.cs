namespace InnoVault.Models3D.Runtime
{
    /// <summary>
    /// 3D 模型的绘制层级，对齐 <see cref="RenderHandles.RenderHandle"/> 暴露的绘制阶段
    /// </summary>
    public enum Model3DLayer
    {
        /// <summary>
        /// 物块绘制之前（墙壁与黑色背景之后），适合贴在背景上的远景模型
        /// </summary>
        BeforeTiles,
        /// <summary>
        /// 物块绘制之后，适合贴地或场景级 3D 物件
        /// </summary>
        AfterTiles,
        /// <summary>
        /// 玩家绘制之前，适合应被玩家遮挡的 3D 内容
        /// </summary>
        BeforePlayers,
        /// <summary>
        /// 玩家绘制之后，适合覆盖在玩家身上的模型（载具、武器等）
        /// </summary>
        AfterPlayers,
        /// <summary>
        /// 默认层，时机略晚于实体绘制，相当于通用世界 FX 层
        /// </summary>
        BeforeInfernoRings,
    }
}
