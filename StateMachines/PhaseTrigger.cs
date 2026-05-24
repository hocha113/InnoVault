using System;

namespace InnoVault.StateMachines
{
    /// <summary>
    /// 阶段触发器：当谓词<see cref="When"/>命中时，状态机会自动切换到<see cref="Transition"/>返回的新状态<br/>
    /// 与<see cref="VaultStateTransition{TContext}"/>的"Any-State + Once"组合等价，单独建模是为了语义清晰<br/>
    /// （HP阈值、伴生死亡、暴怒态、登场结束等"宏观状态层"事件应当走这条路）<br/>
    /// 一旦<see cref="OnFire"/>被调用，会同时尝试切换状态并执行外部副作用（屏幕震动、音效、切歌等）
    /// </summary>
    /// <typeparam name="TContext">承载状态机的上下文类型</typeparam>
    public sealed class PhaseTrigger<TContext>
    {
        /// <summary>
        /// 触发条件谓词；建议无副作用、可重复调用<br/>
        /// 在<see cref="VaultStateMachine{TContext}.Update"/>每一帧都会被询问一次（直到触发并标记<see cref="Fired"/>）
        /// </summary>
        public Func<TContext, bool> When { get; init; }
        /// <summary>
        /// 目标状态工厂；<see langword="null"/>表示不切换状态，只执行<see cref="OnFire"/>副作用<br/>
        /// （例如"50%血时只播放怒吼但不切状态"的场景）
        /// </summary>
        public Func<IVaultState<TContext>> Transition { get; init; }
        /// <summary>
        /// 触发时执行的副作用回调，<b>仅</b>在服务端/单机端运行，避免多客户端各自触发不同的视觉副作用<br/>
        /// 客户端需要看到的同步性视觉，应通过<see cref="VaultStateMachine{TContext}.Blackboard"/>同步参数后再各自驱动
        /// </summary>
        public Action<TContext> OnFire { get; init; }
        /// <summary>
        /// 是否可重复触发<br/>
        /// 默认<see langword="false"/>（一次性，匹配大多数阶段切换需求）<br/>
        /// 设为<see langword="true"/>时，每次<see cref="When"/>命中都会再次触发，需要谓词内部具备"沿"语义（例如检测<i>状态变化</i>而非"是否"）
        /// </summary>
        public bool Repeatable { get; init; }
        /// <summary>
        /// 内部状态标记：是否已经触发过。<see cref="Repeatable"/>为<see langword="false"/>的触发器在触发后会置为<see langword="true"/>
        /// </summary>
        internal bool Fired;
        /// <summary>
        /// 可选标签，用于<see cref="StateMachineDebugger"/>显示，无业务作用
        /// </summary>
        public string Label { get; init; }
    }
}
