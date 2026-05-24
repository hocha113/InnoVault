using Terraria;

namespace InnoVault.StateMachines
{
    /// <summary>
    /// 上下文契约：要从中取出关联的<see cref="NPC"/><br/>
    /// 自定义上下文类型实现此接口即可"零配置"接入<see cref="NpcStateMachine{TContext}"/>的<see cref="AiSlotNetSync{TContext}"/>
    /// </summary>
    public interface INpcStateContext
    {
        /// <summary>
        /// 返回当前 NPC 实例。常见实现：直接返回构造时存的 NPC 字段
        /// </summary>
        NPC Npc { get; }
    }

    /// <summary>
    /// 面向 NPC 的<see cref="VaultStateMachine{TContext}"/>轻量适配器<br/>
    /// 在构造时自动接入<see cref="AiSlotNetSync{TContext}"/>，调用方仅需在 NPC 的<c>AI()</c>钩子里每帧调用一次<see cref="VaultStateMachine{TContext}.Update"/><br/>
    /// 对于不实现<see cref="INpcStateContext"/>的上下文，请使用<see cref="VaultStateMachineBuilder{TContext}.WithNetSync"/>手动注入<see cref="AiSlotNetSync{TContext}.ForNpc"/>
    /// </summary>
    /// <typeparam name="TContext">实现了<see cref="INpcStateContext"/>的上下文类型</typeparam>
    public class NpcStateMachine<TContext> : VaultStateMachine<TContext> where TContext : INpcStateContext
    {
        /// <summary>
        /// 状态机使用的<c>npc.ai[]</c>同步槽位
        /// </summary>
        public int AiSlot { get; }

        /// <summary>
        /// 构造一个面向 NPC 的状态机；默认槽位为<see cref="AiSlotNetSync{TContext}.RecommendedSlot"/>
        /// </summary>
        public NpcStateMachine(TContext context, int aiSlot = AiSlotNetSync<TContext>.RecommendedSlot) : base(context) {
            AiSlot = aiSlot;
            NetSync = AiSlotNetSync<TContext>.ForNpc(c => c.Npc, aiSlot);
        }
    }
}
