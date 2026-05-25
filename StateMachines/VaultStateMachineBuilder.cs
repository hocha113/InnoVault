using System;
using System.Collections.Generic;

namespace InnoVault.StateMachines
{
    /// <summary>
    /// 一组流畅 API 入口，用于声明式组装<see cref="VaultStateMachine{TContext}"/><br/>
    /// 典型用法：
    /// <code>
    /// var fsm = VaultStateMachineBuilder.For&lt;DestroyerCtx&gt;(ctx)
    ///     .WithNetSync(AiSlotNetSync&lt;DestroyerCtx&gt;.ForNpc(c =&gt; c.Npc, slot: 3))
    ///     .Initial&lt;Intro&gt;()
    ///     .From&lt;Intro&gt;().To&lt;Patrol&gt;().When(c =&gt; c.Timer &gt; 120).End()
    ///     .AnyState().To&lt;Enraged&gt;().When(c =&gt; c.Npc.life * 2 &lt; c.Npc.lifeMax).Once().End()
    ///     .Build();
    /// </code>
    /// </summary>
    public static class VaultStateMachineBuilder
    {
        /// <summary>
        /// 入口方法：以指定上下文实例开始构建。返回的<see cref="VaultStateMachineBuilder{TContext}"/>可被链式调用
        /// </summary>
        public static VaultStateMachineBuilder<TContext> For<TContext>(TContext context)
            => new VaultStateMachineBuilder<TContext>(context);
    }

    /// <summary>
    /// <see cref="VaultStateMachine{TContext}"/>的流畅构建器；通过<see cref="VaultStateMachineBuilder.For{TContext}(TContext)"/>获取入口
    /// </summary>
    /// <typeparam name="TContext">状态机上下文类型</typeparam>
    public sealed class VaultStateMachineBuilder<TContext>
    {
        private readonly TContext _context;
        private Blackboard _blackboard;
        private VaultStateMachine<TContext> _machine;
        private IVaultState<TContext> _initialState;
        private INetStateSync<TContext> _pendingNetSync;
        private bool? _pendingServerAuthoritative;
        //缓冲到 Build 时再写入，以便<see cref="WithBlackboard(Blackboard)"/>在<see cref="From{TFrom}"/>之前/之后调用都不会出问题
        private readonly List<VaultStateTransition<TContext>> _pendingTransitions = [];
        private readonly List<PhaseTrigger<TContext>> _pendingPhaseTriggers = [];

        internal VaultStateMachineBuilder(TContext context) {
            _context = context;
        }

        /// <summary>
        /// 指定一份与外部共享的<see cref="StateMachines.Blackboard"/>实例；常用于把本机作为<see cref="InnoVault.BehaviorTrees.VaultStateMachineAsBtLeaf{TContext}"/>嵌入到行为树时，<br/>
        /// 与外层 BT 复用同一份黑板<br/>
        /// 不调用本方法时，<see cref="Build"/>会为状态机内部新建一份独立黑板
        /// </summary>
        public VaultStateMachineBuilder<TContext> WithBlackboard(Blackboard blackboard) {
            _blackboard = blackboard;
            return this;
        }

        /// <summary>
        /// 设置<see cref="VaultStateMachine{TContext}.NetSync"/>
        /// </summary>
        public VaultStateMachineBuilder<TContext> WithNetSync(INetStateSync<TContext> sync) {
            _pendingNetSync = sync;
            return this;
        }

        /// <summary>
        /// 显式打开/关闭<see cref="VaultStateMachine{TContext}.ServerAuthoritative"/>。默认<see langword="true"/>
        /// </summary>
        public VaultStateMachineBuilder<TContext> WithServerAuthoritative(bool value) {
            _pendingServerAuthoritative = value;
            return this;
        }

        /// <summary>
        /// 设置初始状态（按类型）；要求<typeparamref name="TState"/>注册了<see cref="VaultStateAttribute"/>或具备无参构造
        /// </summary>
        public VaultStateMachineBuilder<TContext> Initial<TState>() where TState : IVaultState<TContext>, new() {
            _initialState = new TState();
            return this;
        }

        /// <summary>
        /// 设置初始状态（按实例）；适合需要带闭包参数的初始化场景
        /// </summary>
        public VaultStateMachineBuilder<TContext> Initial(IVaultState<TContext> state) {
            _initialState = state;
            return this;
        }

        /// <summary>
        /// 开始声明一条"普通转移"：当机器当前处于<typeparamref name="TFrom"/>时评估
        /// </summary>
        public TransitionBuilder<TContext> From<TFrom>() where TFrom : IVaultState<TContext>
            => new TransitionBuilder<TContext>(this, _pendingTransitions, typeof(TFrom));

        /// <summary>
        /// 开始声明一条"Any-State 转移"：任意状态下都参与评估，支持<see cref="TransitionBuilder{TContext}.Priority(int)"/>排序
        /// </summary>
        public TransitionBuilder<TContext> AnyState()
            => new TransitionBuilder<TContext>(this, _pendingTransitions, null);

        /// <summary>
        /// 添加一个一次性<see cref="PhaseTrigger{TContext}"/>：当谓词命中时切换到指定目标状态<br/>
        /// 等价于 AnyState 转移 + <c>.Once()</c>，但语义上更清晰
        /// </summary>
        /// <remarks>
        /// 仅支持<b>无参</b>构造的目标状态。如果目标状态需要带闭包参数（例如把当前血量、随机种子等捕获进状态），<br/>
        /// 请改用<see cref="PhaseController{TContext}.OnHpBelow"/>或<see cref="PhaseController{TContext}.OnCondition"/>，两者都接受<c>Func&lt;IVaultState&lt;TContext&gt;&gt;</c>形式的目标工厂
        /// </remarks>
        /// <seealso cref="PhaseController{TContext}"/>
        public VaultStateMachineBuilder<TContext> Phase<TTarget>(Func<TContext, bool> when, Action<TContext> onFire = null, string label = null)
            where TTarget : IVaultState<TContext>, new() {
            _pendingPhaseTriggers.Add(new PhaseTrigger<TContext> {
                When = when,
                Transition = () => new TTarget(),
                OnFire = onFire,
                Label = label
            });
            return this;
        }

        /// <summary>
        /// 完成构建并返回<see cref="VaultStateMachine{TContext}"/>；若已设置初始状态会立刻调用<see cref="VaultStateMachine{TContext}.SetInitialState"/><br/>
        /// 转移列表会被重排为"AnyState(按 Priority 降序，同优先级保留注册顺序) → 普通转移(保留注册顺序)"<br/>
        /// 运行时<see cref="VaultStateMachine{TContext}"/>依赖该顺序保证 Any-State 不会被普通转移覆盖
        /// </summary>
        public VaultStateMachine<TContext> Build() {
            _machine = new VaultStateMachine<TContext>(_context, _blackboard);
            if (_pendingNetSync != null) {
                _machine.NetSync = _pendingNetSync;
            }
            if (_pendingServerAuthoritative.HasValue) {
                _machine.ServerAuthoritative = _pendingServerAuthoritative.Value;
            }

            //List<T>.Sort 不是稳定排序——这里手工拆成两段，AnyState 段做稳定的优先级排序，普通段保留原序后拼回
            //保证：1) AnyState 一定排在普通转移前；2) 同优先级 AnyState 保持注册顺序；3) 普通转移完全保持注册顺序
            List<VaultStateTransition<TContext>> all = _machine.Transitions;
            List<KeyValuePair<int, VaultStateTransition<TContext>>> anyStateIndexed = [];
            List<VaultStateTransition<TContext>> normals = [];
            for (int i = 0; i < _pendingTransitions.Count; i++) {
                VaultStateTransition<TContext> t = _pendingTransitions[i];
                if (t.FromState == null) {
                    anyStateIndexed.Add(new KeyValuePair<int, VaultStateTransition<TContext>>(i, t));
                }
                else {
                    normals.Add(t);
                }
            }
            anyStateIndexed.Sort((a, b) => {
                int byPriority = b.Value.Priority.CompareTo(a.Value.Priority);
                return byPriority != 0 ? byPriority : a.Key.CompareTo(b.Key);
            });
            for (int i = 0; i < anyStateIndexed.Count; i++) {
                all.Add(anyStateIndexed[i].Value);
            }
            for (int i = 0; i < normals.Count; i++) {
                all.Add(normals[i]);
            }

            for (int i = 0; i < _pendingPhaseTriggers.Count; i++) {
                _machine.PhaseTriggers.Add(_pendingPhaseTriggers[i]);
            }

            if (_initialState != null) {
                _machine.SetInitialState(_initialState);
            }

            //清空 pending 列表，避免重复调用 Build() 时把同一批转移/触发器塞进多个机器
            _pendingTransitions.Clear();
            _pendingPhaseTriggers.Clear();
            _initialState = null;
            return _machine;
        }
    }

    /// <summary>
    /// 单条转移的链式构建辅助器：从<see cref="VaultStateMachineBuilder{TContext}.From{TFrom}"/>或<see cref="VaultStateMachineBuilder{TContext}.AnyState"/>得到<br/>
    /// 收尾必须调用<see cref="End"/>以将转移真正提交到状态机
    /// </summary>
    public sealed class TransitionBuilder<TContext>
    {
        private readonly VaultStateMachineBuilder<TContext> _owner;
        private readonly List<VaultStateTransition<TContext>> _sink;
        private readonly Type _fromType;
        private Func<IVaultState<TContext>> _targetFactory;
        private Func<TContext, bool> _condition;
        private int _priority;
        private bool _once;
        private string _label;

        internal TransitionBuilder(VaultStateMachineBuilder<TContext> owner, List<VaultStateTransition<TContext>> sink, Type fromType) {
            _owner = owner;
            _sink = sink;
            _fromType = fromType;
        }

        /// <summary>
        /// 目标状态（按类型）。要求<typeparamref name="TTo"/>具备无参构造
        /// </summary>
        public TransitionBuilder<TContext> To<TTo>() where TTo : IVaultState<TContext>, new() {
            _targetFactory = () => new TTo();
            return this;
        }

        /// <summary>
        /// 目标状态（自定义工厂）。适合需要根据上下文动态选择参数化状态的场景
        /// </summary>
        public TransitionBuilder<TContext> To(Func<IVaultState<TContext>> factory) {
            _targetFactory = factory;
            return this;
        }

        /// <summary>
        /// 设置触发条件
        /// </summary>
        public TransitionBuilder<TContext> When(Func<TContext, bool> condition) {
            _condition = condition;
            return this;
        }

        /// <summary>
        /// 设置 Any-State 间的评估优先级（普通转移不受此影响）
        /// </summary>
        public TransitionBuilder<TContext> Priority(int priority) {
            _priority = priority;
            return this;
        }

        /// <summary>
        /// 标记为"一次性"：成功匹配后不再参与评估
        /// </summary>
        public TransitionBuilder<TContext> Once() {
            _once = true;
            return this;
        }

        /// <summary>
        /// 可选标签，会显示在<see cref="StateMachineDebugger"/>的转移日志中
        /// </summary>
        public TransitionBuilder<TContext> Label(string label) {
            _label = label;
            return this;
        }

        /// <summary>
        /// 收尾：把当前转移提交到机器并返回上层构建器以继续链式调用
        /// </summary>
        public VaultStateMachineBuilder<TContext> End() {
            if (_targetFactory == null || _condition == null) {
                VaultMod.LoggerError("VaultStateMachineBuilder.TransitionBuilder.End",
                    $"Transition for {_fromType?.FullName ?? "AnyState"} is missing target or condition; skipped.");
                return _owner;
            }
            _sink.Add(new VaultStateTransition<TContext> {
                FromState = _fromType,
                TargetFactory = _targetFactory,
                Condition = _condition,
                Priority = _priority,
                Once = _once,
                Label = _label
            });
            return _owner;
        }
    }
}
