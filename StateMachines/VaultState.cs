namespace InnoVault.StateMachines
{
    /// <summary>
    /// <see cref="IVaultState{TContext}"/>的默认抽象基类，提供<see cref="Timer"/>/<see cref="Counter"/>两个内置计数器以及自动归零的<see cref="OnEnter"/>实现<br/>
    /// 子类按需重写<see cref="OnEnter"/>、<see cref="OnUpdate"/>、<see cref="OnExit"/>；如果重写<see cref="OnEnter"/>或<see cref="OnUpdate"/>，<br/>
    /// 请先调用<c>base.OnEnter(...)</c> / <c>base.OnUpdate(...)</c>以保证<see cref="Timer"/>逻辑成立
    /// </summary>
    /// <typeparam name="TContext">承载状态机的上下文类型</typeparam>
    public abstract class VaultState<TContext> : IVaultState<TContext>
    {
        /// <summary>
        /// 进入状态后已经过的逻辑帧数（每次<see cref="OnUpdate"/>自增1，<see cref="OnEnter"/>会清零）<br/>
        /// 仅是本地驱动量，<b>不</b>随网络同步，多人模式下客户端与服务端可能短暂不一致——<br/>
        /// 不要依赖它做关键攻击判定，攻击触发条件请放到服务端逻辑或写入<see cref="VaultStateMachine{TContext}.Blackboard"/>
        /// </summary>
        public int Timer { get; protected set; }
        /// <summary>
        /// 通用整型计数器，用途由子类自行约定（例如已完成的子动作数）<br/>
        /// 与<see cref="Timer"/>同样仅本地维护，不参与网络同步，<see cref="OnEnter"/>会清零
        /// </summary>
        public int Counter { get; protected set; }
        /// <summary>
        /// 状态在<see cref="VaultStateRegistry{TContext}"/>中的注册ID<br/>
        /// 默认实现走注册表查询，要求子类带有<see cref="VaultStateAttribute"/>且<see cref="VaultStateAttribute.ContextType"/>等于<typeparamref name="TContext"/><br/>
        /// 也可直接覆盖该属性返回一个常量
        /// </summary>
        public virtual int StateId => VaultStateRegistry<TContext>.GetIdFor(GetType());
        /// <summary>
        /// 默认返回当前类型短名，方便调试日志区分；子类可覆盖以提供更友好的中文名
        /// </summary>
        public virtual string StateName => GetType().Name;

        /// <inheritdoc/>
        public virtual void OnEnter(VaultStateMachine<TContext> machine, TContext ctx) {
            Timer = 0;
            Counter = 0;
        }

        /// <inheritdoc/>
        public virtual IVaultState<TContext> OnUpdate(VaultStateMachine<TContext> machine, TContext ctx) {
            Timer++;
            return null;
        }

        /// <inheritdoc/>
        public virtual void OnExit(VaultStateMachine<TContext> machine, TContext ctx) { }
    }
}
