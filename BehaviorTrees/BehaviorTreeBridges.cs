using InnoVault.StateMachines;
using System;

namespace InnoVault.BehaviorTrees
{
    /// <summary>
    /// "把一棵行为树包装成<see cref="IVaultState{TContext}"/>" 的桥接节点<br/>
    /// 进入该状态时<see cref="BTNode{TContext}.Reset"/>子树，每帧<see cref="OnUpdate"/>用宿主<see cref="VaultStateMachine{TContext}.Blackboard"/>去 tick<br/>
    /// 当子树返回非<see cref="BTStatus.Running"/>时，框架根据"成功 → <see cref="OnSuccessTransition"/>" / "失败 → <see cref="OnFailureTransition"/>"决定外层切换
    /// </summary>
    public sealed class BehaviorTreeAsState<TContext> : VaultState<TContext>
    {
        private readonly BTNode<TContext> _root;
        private BTStatus _lastTick = BTStatus.Running;

        /// <summary>BT 返回<see cref="BTStatus.Success"/>时的"下一状态"工厂，可为<see langword="null"/>表示保持当前状态</summary>
        public Func<IVaultState<TContext>> OnSuccessTransition { get; init; }
        /// <summary>BT 返回<see cref="BTStatus.Failure"/>时的"下一状态"工厂，可为<see langword="null"/>表示保持当前状态</summary>
        public Func<IVaultState<TContext>> OnFailureTransition { get; init; }

        /// <summary>构造一个 BT 包装状态</summary>
        public BehaviorTreeAsState(BTNode<TContext> root) {
            _root = root ?? throw new ArgumentNullException(nameof(root));
        }

        /// <inheritdoc/>
        public override void OnEnter(VaultStateMachine<TContext> machine, TContext ctx) {
            base.OnEnter(machine, ctx);
            _root.Reset();
            _lastTick = BTStatus.Running;
        }

        /// <inheritdoc/>
        public override IVaultState<TContext> OnUpdate(VaultStateMachine<TContext> machine, TContext ctx) {
            base.OnUpdate(machine, ctx);
            _lastTick = _root.Tick(ctx, machine.Blackboard);
            return _lastTick switch {
                BTStatus.Success => OnSuccessTransition?.Invoke(),
                BTStatus.Failure => OnFailureTransition?.Invoke(),
                _ => null,
            };
        }

        /// <inheritdoc/>
        public override void OnExit(VaultStateMachine<TContext> machine, TContext ctx) {
            base.OnExit(machine, ctx);
            _root.Reset();
        }
    }

    /// <summary>
    /// "把一台<see cref="VaultStateMachine{TContext}"/>包装成<see cref="BTNode{TContext}"/>" 的桥接叶<br/>
    /// 每帧<see cref="Tick"/>调用<see cref="VaultStateMachine{TContext}.Update"/>，<br/>
    /// 当状态机<see cref="VaultStateMachine{TContext}.IsTerminated"/>为<see langword="true"/>时，<br/>
    /// 根据<see cref="SuccessOnTerminate"/>决定整体返回 <see cref="BTStatus.Success"/> 或 <see cref="BTStatus.Failure"/>，否则返回 <see cref="BTStatus.Running"/><br/><br/>
    /// <b>黑板共享约定</b>：BT 在<see cref="Tick"/>时会把上层<see cref="Blackboard"/>传进来；要让内层状态机也读到同一份黑板，<br/>
    /// 请在构造内层<see cref="VaultStateMachine{TContext}"/>时通过<see cref="VaultStateMachineBuilder{TContext}.WithBlackboard"/>注入<br/>
    /// （或直接<c>new VaultStateMachine&lt;TContext&gt;(ctx, sharedBlackboard)</c>）；<br/>
    /// 否则内层状态机会持有一份自己私有的黑板，无法与外层 BT 通过黑板互通
    /// </summary>
    public sealed class VaultStateMachineAsBtLeaf<TContext> : BTNode<TContext>
    {
        private readonly VaultStateMachine<TContext> _machine;
        private bool _warnedBlackboardMismatch;

        /// <summary>状态机终止时返回的 BT 状态</summary>
        public bool SuccessOnTerminate { get; }
        /// <summary>所包装的<see cref="VaultStateMachine{TContext}"/>实例</summary>
        public VaultStateMachine<TContext> Machine => _machine;

        /// <summary>构造一个 FSM 桥接叶</summary>
        public VaultStateMachineAsBtLeaf(VaultStateMachine<TContext> machine, bool successOnTerminate = true) {
            _machine = machine ?? throw new ArgumentNullException(nameof(machine));
            SuccessOnTerminate = successOnTerminate;
        }

        /// <inheritdoc/>
        public override BTStatus Tick(TContext ctx, Blackboard blackboard) {
            //在外层 BT 提供了黑板的情况下，校验内外是否同一引用；不一致时打一次警告便于排查
            if (blackboard != null && !ReferenceEquals(blackboard, _machine.Blackboard) && !_warnedBlackboardMismatch) {
                _warnedBlackboardMismatch = true;
                VaultMod.LoggerError(
                    $"VaultStateMachineAsBtLeaf<{typeof(TContext).Name}>:bb",
                    $"Inner FSM has its own Blackboard while the wrapping BT supplied a different one. " +
                    "Inject the outer Blackboard via VaultStateMachineBuilder.WithBlackboard(...) to enable shared blackboard semantics.");
            }
            _machine.Update();
            BTStatus result;
            if (_machine.IsTerminated) {
                result = SuccessOnTerminate ? BTStatus.Success : BTStatus.Failure;
            }
            else {
                result = BTStatus.Running;
            }
            LastStatus = result;
            return result;
        }

        /// <inheritdoc/>
        public override void Reset() {
            base.Reset();
            //不重置子状态机的内部进度——子机自己管理 CurrentState 的转移
        }
    }
}
