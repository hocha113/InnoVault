namespace InnoVault.PRT
{
    /// <summary>
    /// PRT粒子的渲染层级枚举每一项对应 <see cref="RenderHandles.RenderHandle"/> 系统中的一个绘制阶段
    /// 决定粒子在屏幕"什么时候"被画，与决定混合方式的 <see cref="PRTDrawModeEnum"/> 正交
    /// 默认值为 <see cref="BeforeInfernoRings"/>
    /// </summary>
    public enum PRTRenderLayer
    {
        /// <summary>
        /// 在物块绘制之前（墙壁与黑色背景之后）绘制
        /// 适合需要画在物块下方的背景型粒子（如远景雾、地下尘土）
        /// </summary>
        BeforeTiles,
        /// <summary>
        /// 在物块绘制完成之后绘制
        /// 适合悬浮在物块上方但需要被实体遮挡的粒子
        /// </summary>
        AfterTiles,
        /// <summary>
        /// 在玩家绘制之前绘制
        /// 适合需要被玩家遮挡的特效粒子
        /// </summary>
        BeforePlayers,
        /// <summary>
        /// 在玩家绘制之后绘制
        /// 适合覆盖在玩家身上的火焰、轨迹等
        /// </summary>
        AfterPlayers,
        /// <summary>
        /// 默认层在 <c>DrawInfernoRings</c> 之前绘制
        /// 没有显式指定 RenderLayer 的粒子都落在此层
        /// </summary>
        BeforeInfernoRings,
    }
}
