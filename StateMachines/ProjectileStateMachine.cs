using Terraria;

namespace InnoVault.StateMachines
{
    /// <summary>
    /// 上下文契约：要从中取出关联的<see cref="Projectile"/><br/>
    /// 自定义上下文类型实现此接口即可"零配置"接入<see cref="ProjectileStateMachine{TContext}"/>
    /// </summary>
    public interface IProjectileStateContext
    {
        /// <summary>
        /// 返回当前 Projectile 实例
        /// </summary>
        Projectile Projectile { get; }
    }

    /// <summary>
    /// 面向 Projectile 的<see cref="VaultStateMachine{TContext}"/>轻量适配器<br/>
    /// 调用方应在<c>ModProjectile.AI()</c>中每帧调用一次<see cref="VaultStateMachine{TContext}.Update"/>，<br/>
    /// 并确保<see cref="Projectile.aiStyle"/>不与所选槽位冲突
    /// </summary>
    /// <typeparam name="TContext">实现了<see cref="IProjectileStateContext"/>的上下文类型</typeparam>
    public class ProjectileStateMachine<TContext> : VaultStateMachine<TContext> where TContext : IProjectileStateContext
    {
        /// <summary>
        /// 状态机使用的<c>proj.ai[]</c>同步槽位
        /// </summary>
        public int AiSlot { get; }

        /// <summary>
        /// 构造一个面向弹幕的状态机；默认槽位为<see cref="AiSlotNetSync{TContext}.RecommendedSlot"/>
        /// </summary>
        public ProjectileStateMachine(TContext context, int aiSlot = AiSlotNetSync<TContext>.RecommendedSlot) : base(context) {
            AiSlot = aiSlot;
            NetSync = AiSlotNetSync<TContext>.ForProjectile(c => c.Projectile, aiSlot);
        }
    }
}
