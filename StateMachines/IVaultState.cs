namespace InnoVault.StateMachines
{
    /// <summary>
    /// 表示<see cref="VaultStateMachine{TContext}"/>所驱动的一个具体状态<br/>
    /// 任何状态都必须显式公开<see cref="StateId"/>（建议由<see cref="VaultStateAttribute"/>统一分配）<br/>
    /// 框架内部以<see cref="StateId"/>为唯一身份用于网络同步与日志，请勿在运行期动态改变
    /// </summary>
    /// <typeparam name="TContext">承载状态机的上下文类型，通常是与某个实体（NPC、Projectile、Actor）关联的数据容器</typeparam>
    public interface IVaultState<TContext>
    {
        /// <summary>
        /// 当前状态在所属<typeparamref name="TContext"/>注册表中的唯一整型标识，用于网络同步与重建
        /// </summary>
        int StateId { get; }
        /// <summary>
        /// 当前状态的人类可读名称，默认实现使用类型短名，用于调试与日志
        /// </summary>
        string StateName { get; }
        /// <summary>
        /// 在状态成为<see cref="VaultStateMachine{TContext}.CurrentState"/>时调用一次<br/>
        /// 该方法是状态实例化路径的唯一公共入口，适合放置初始化、计数器归零、动作起手等逻辑
        /// </summary>
        /// <param name="machine">驱动该状态的状态机实例，可用于直接调用<see cref="VaultStateMachine{TContext}.ChangeState(IVaultState{TContext}, string)"/>切换</param>
        /// <param name="ctx">状态所处的上下文实例</param>
        void OnEnter(VaultStateMachine<TContext> machine, TContext ctx);
        /// <summary>
        /// 在状态保持期间，每帧调用一次<br/>
        /// 在多人模式下，无论客户端还是服务端，<see cref="OnUpdate"/>都会被驱动；<br/>
        /// 但仅<b>服务端</b>返回非<see langword="null"/>且不同于当前状态的实例时才会真正发生状态切换（<see cref="VaultStateMachine{TContext}.ServerAuthoritative"/>默认为<see langword="true"/>）<br/><br/>
        /// <b>客户端语义</b>：当<see cref="VaultStateMachine{TContext}.ServerAuthoritative"/>为<see langword="true"/>时，<br/>
        /// 客户端调用<see cref="OnUpdate"/>但<b>会丢弃</b>其返回的下一状态——客户端的状态切换只能通过<see cref="INetStateSync{TContext}"/>从服务端被动同步进来<br/>
        /// 因此：希望"两端一致"的视觉/特效/计时副作用应当写在<see cref="OnUpdate"/>体内并自驱动；<br/>
        /// 任何"满足条件就切到下一状态"的判定都<b>不要</b>依赖返回值在客户端生效，而应当声明为<see cref="VaultStateTransition{TContext}"/>或<see cref="PhaseTrigger{TContext}"/>，<br/>
        /// 由服务端裁决后再通过<see cref="INetStateSync{TContext}"/>下发
        /// </summary>
        /// <param name="machine">驱动该状态的状态机实例</param>
        /// <param name="ctx">状态所处的上下文实例</param>
        /// <returns>返回非<see langword="null"/>表示请求切换到该新状态；返回<see langword="null"/>表示保持当前状态<br/>
        /// 注意客户端会无视该返回值——详见上文"客户端语义"</returns>
        IVaultState<TContext> OnUpdate(VaultStateMachine<TContext> machine, TContext ctx);
        /// <summary>
        /// 在该状态被替换前调用一次，配对于<see cref="OnEnter"/><br/>
        /// 适合放置资源释放、特效收尾、清理临时计数器等逻辑
        /// </summary>
        /// <param name="machine">驱动该状态的状态机实例</param>
        /// <param name="ctx">状态所处的上下文实例</param>
        void OnExit(VaultStateMachine<TContext> machine, TContext ctx);
    }
}
