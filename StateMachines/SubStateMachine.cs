using System;

namespace InnoVault.StateMachines
{
    /// <summary>
    /// 把"一台<see cref="VaultStateMachine{TContext}"/>"打包成"一个<see cref="IVaultState{TContext}"/>"，<br/>
    /// 即层次化状态机（HFSM）的基本构件，结构上与 Unity Animator 子状态机同构<br/>
    /// 用法：在外层声明一个<see cref="SubStateMachine{TContext}"/>类型的状态，<br/>
    /// 在<see cref="OnEnter"/>时由<see cref="_innerFactory"/>构造内层机器并设置初始内层状态；<br/>
    /// 外层每帧<see cref="OnUpdate"/>会自动驱动内层<see cref="VaultStateMachine{TContext}.Update"/>
    /// </summary>
    /// <remarks>
    /// 网络同步说明：内层状态机默认<b>不</b>独立做<c>ai[]</c>同步——多人模式下，<br/>
    /// 通常只需要把"外层处于哪个子机"同步出去，内层的具体子状态由各端各自驱动（受同一<see cref="Blackboard"/>引导）<br/>
    /// 如果确实需要把内层子状态也同步，可在构造时为内层注入第二个<see cref="INetStateSync{TContext}"/>，<br/>
    /// 使用与外层不同的<c>ai[slot]</c>避免槽位冲突
    /// </remarks>
    /// <typeparam name="TContext">状态机上下文类型</typeparam>
    public class SubStateMachine<TContext> : VaultState<TContext>
    {
        private readonly Func<TContext, VaultStateMachine<TContext>> _innerFactory;
        private readonly Func<IVaultState<TContext>> _initialInnerStateFactory;

        /// <summary>
        /// 进入子状态机后的内层运行时；外层未进入时为<see langword="null"/>
        /// </summary>
        public VaultStateMachine<TContext> Inner { get; private set; }

        /// <summary>
        /// 构造一个层次化状态。子状态机会在每次外层<see cref="OnEnter"/>时被重新构造，<br/>
        /// 保证不会跨越外层重入持有脏数据
        /// </summary>
        /// <param name="innerFactory">由当前上下文构造内层状态机的工厂；推荐返回新实例</param>
        /// <param name="initialInnerStateFactory">用于设置内层机器的初始状态的工厂</param>
        public SubStateMachine(Func<TContext, VaultStateMachine<TContext>> innerFactory, Func<IVaultState<TContext>> initialInnerStateFactory) {
            _innerFactory = innerFactory ?? throw new ArgumentNullException(nameof(innerFactory));
            _initialInnerStateFactory = initialInnerStateFactory ?? throw new ArgumentNullException(nameof(initialInnerStateFactory));
        }

        /// <inheritdoc/>
        public override void OnEnter(VaultStateMachine<TContext> machine, TContext ctx) {
            base.OnEnter(machine, ctx);
            Inner = _innerFactory(ctx);
            if (Inner != null) {
                IVaultState<TContext> initial = _initialInnerStateFactory();
                if (initial != null) {
                    Inner.SetInitialState(initial);
                }
            }
        }

        /// <inheritdoc/>
        public override IVaultState<TContext> OnUpdate(VaultStateMachine<TContext> machine, TContext ctx) {
            base.OnUpdate(machine, ctx);
            Inner?.Update();
            //外层的转移仍由外层的 Transitions / PhaseTriggers 决定，这里始终返回 null
            return null;
        }

        /// <inheritdoc/>
        public override void OnExit(VaultStateMachine<TContext> machine, TContext ctx) {
            base.OnExit(machine, ctx);
            //帮助 GC 释放内层状态机；下次进入时由 OnEnter 重新构造
            Inner = null;
        }
    }
}
