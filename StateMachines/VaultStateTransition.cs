using System;

namespace InnoVault.StateMachines
{
    /// <summary>
    /// 触发当前状态切换的原因，用于<see cref="StateMachineDebugger"/>的环形缓冲日志，<br/>
    /// 也用于上层逻辑在<see cref="VaultStateMachine{TContext}"/>的事件回调中分流处理
    /// </summary>
    public enum StateChangeReason
    {
        /// <summary>显式调用<see cref="VaultStateMachine{TContext}.SetInitialState"/>时</summary>
        Initial,
        /// <summary>显式调用<see cref="VaultStateMachine{TContext}.ChangeState(IVaultState{TContext}, string)"/>时</summary>
        Manual,
        /// <summary>状态自身在<see cref="IVaultState{TContext}.OnUpdate"/>中返回了一个非<see langword="null"/>的新状态</summary>
        UpdateReturn,
        /// <summary>由一个匹配的<see cref="VaultStateTransition{TContext}"/>（含AnyState）触发</summary>
        Transition,
        /// <summary>由一个匹配的<see cref="PhaseTrigger{TContext}"/>（HP阶段等）触发</summary>
        Phase,
        /// <summary>客户端从服务端的<see cref="INetStateSync{TContext}"/>反推得到（被动同步）</summary>
        NetSync,
    }

    /// <summary>
    /// 声明式状态转移规则<br/>
    /// 既可作为"普通转移"（指定<see cref="FromState"/>，仅当当前处于该状态时评估），<br/>
    /// 也可作为"Any-State 转移"（<see cref="FromState"/>为<see langword="null"/>，在任意状态下评估）<br/>
    /// 通常由<see cref="VaultStateMachineBuilder{TContext}"/>构造，运行时也可手动 new 后塞入<see cref="VaultStateMachine{TContext}.Transitions"/>
    /// </summary>
    /// <typeparam name="TContext">承载状态机的上下文类型</typeparam>
    public sealed class VaultStateTransition<TContext>
    {
        /// <summary>
        /// 源状态类型；<see langword="null"/>表示 Any-State（在所有状态下都参与评估）
        /// </summary>
        public Type FromState { get; init; }
        /// <summary>
        /// 触发条件谓词；返回<see langword="true"/>则切换。建议无副作用、可重复调用
        /// </summary>
        public Func<TContext, bool> Condition { get; init; }
        /// <summary>
        /// 目标状态工厂；通常返回一个新实例，避免共享状态在多帧间持有脏数据<br/>
        /// 若上层框架希望复用单例状态，可在此返回缓存实例
        /// </summary>
        public Func<IVaultState<TContext>> TargetFactory { get; init; }
        /// <summary>
        /// 转移优先级，<b>数值越大越先评估</b>，仅在 Any-State 转移之间生效（普通转移不冲突）<br/>
        /// 同优先级按注册顺序评估
        /// </summary>
        public int Priority { get; init; }
        /// <summary>
        /// 是否一次性触发<br/>
        /// 一旦匹配并发生切换，<see cref="Fired"/>会被置为<see langword="true"/>，再不参与评估<br/>
        /// 适合阶段切换（"血量低于60%"只想触发一次）
        /// </summary>
        public bool Once { get; init; }
        /// <summary>
        /// 内部状态标记：是否已经触发过。<see cref="Once"/>为<see langword="true"/>的转移在触发后会置为<see langword="true"/>
        /// </summary>
        internal bool Fired;
        /// <summary>
        /// 可选的便于阅读的标签，用于<see cref="StateMachineDebugger"/>日志显示，无业务作用
        /// </summary>
        public string Label { get; init; }
    }
}
