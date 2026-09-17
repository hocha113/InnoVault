using System;

namespace InnoVault.RenderHandles
{
    /// <summary>
    /// 原版每帧调用两轮 <c>LegacyPlayerRenderer.DrawPlayers</c>，
    /// <see cref="RenderHandle.DrawBeforePlayers"/> 与 <see cref="RenderHandle.DrawAfterPlayers"/> 随之各触发两次；
    /// 用本枚举描述当前轮次，或在 <see cref="RenderHandle.PlayerDrawPasses"/> 中声明实例响应哪几轮
    /// </summary>
    [Flags]
    public enum PlayerDrawPass : byte
    {
        /// <summary>
        /// 不在任何玩家绘制轮次中
        /// </summary>
        None = 0,
        /// <summary>
        /// 第一轮：实心物块之后、NPC 之前，只画 <c>isLockedToATile</c> 的玩家
        /// </summary>
        BehindNPCs = 1,
        /// <summary>
        /// 第二轮：弹幕之后的主玩家层，绝大多数玩家在这一轮绘制
        /// </summary>
        AfterProjectiles = 2,
        /// <summary>
        /// 两轮都响应，与旧行为一致
        /// </summary>
        Both = BehindNPCs | AfterProjectiles,
    }
}
