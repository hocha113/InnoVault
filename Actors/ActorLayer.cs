namespace InnoVault.Actors
{
    /// <summary>
    /// 定义Actor的绘制层级
    /// </summary>
    public enum ActorDrawLayer
    {
        /// <summary>
        /// 默认绘制层级 (通常在所有内容之后，DrawInfernoRings阶段)
        /// </summary>
        Default,
        /// <summary>
        /// 在物块绘制之前绘制
        /// </summary>
        BeforeTiles,
        /// <summary>
        /// 在物块绘制之后绘制
        /// </summary>
        AfterTiles,
        /// <summary>
        /// 在玩家绘制之前绘制
        /// </summary>
        BeforePlayers,
        /// <summary>
        /// 在玩家绘制之后绘制
        /// </summary>
        AfterPlayers,       
    }
}
