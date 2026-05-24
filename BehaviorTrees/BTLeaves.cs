using InnoVault.StateMachines;
using System;

namespace InnoVault.BehaviorTrees
{
    /// <summary>
    /// 通用动作叶节点：把一个委托包装成<see cref="BTNode{TContext}"/><br/>
    /// 业务侧可直接<c>new ActionLeaf&lt;Ctx&gt;((ctx, bb) =&gt; { ...; return BTStatus.Success; })</c>
    /// </summary>
    public sealed class ActionLeaf<TContext> : BTNode<TContext>
    {
        private readonly Func<TContext, Blackboard, BTStatus> _action;

        /// <summary>构造一个动作叶</summary>
        public ActionLeaf(Func<TContext, Blackboard, BTStatus> action) {
            _action = action ?? throw new ArgumentNullException(nameof(action));
        }

        /// <summary>构造一个忽略 Blackboard 的动作叶</summary>
        public ActionLeaf(Func<TContext, BTStatus> action) {
            if (action == null) {
                throw new ArgumentNullException(nameof(action));
            }
            _action = (c, _) => action(c);
        }

        /// <inheritdoc/>
        public override BTStatus Tick(TContext ctx, Blackboard blackboard) {
            BTStatus result = _action(ctx, blackboard);
            LastStatus = result;
            return result;
        }
    }

    /// <summary>
    /// 条件叶：把一个谓词包装成"成功 = 返回 Success / 失败 = 返回 Failure"的叶节点<br/>
    /// 适合做"组合树"中的"分支判定"，搭配<see cref="Sequence{TContext}"/>使用
    /// </summary>
    public sealed class ConditionLeaf<TContext> : BTNode<TContext>
    {
        private readonly Func<TContext, bool> _condition;

        /// <summary>构造一个条件叶</summary>
        public ConditionLeaf(Func<TContext, bool> condition) {
            _condition = condition ?? throw new ArgumentNullException(nameof(condition));
        }

        /// <inheritdoc/>
        public override BTStatus Tick(TContext ctx, Blackboard blackboard) {
            BTStatus result = _condition(ctx) ? BTStatus.Success : BTStatus.Failure;
            LastStatus = result;
            return result;
        }
    }

    /// <summary>
    /// 等待叶：在指定帧数内持续返回 <see cref="BTStatus.Running"/>，到点后返回<see cref="BTStatus.Success"/>
    /// </summary>
    public sealed class WaitLeaf<TContext> : BTNode<TContext>
    {
        /// <summary>等待帧数</summary>
        public int WaitTicks { get; }
        private int _elapsed;

        /// <summary>构造一个等待叶</summary>
        public WaitLeaf(int waitTicks) {
            WaitTicks = waitTicks;
        }

        /// <inheritdoc/>
        public override BTStatus Tick(TContext ctx, Blackboard blackboard) {
            _elapsed++;
            if (_elapsed >= WaitTicks) {
                _elapsed = 0;
                LastStatus = BTStatus.Success;
                return BTStatus.Success;
            }
            LastStatus = BTStatus.Running;
            return BTStatus.Running;
        }

        /// <inheritdoc/>
        public override void Reset() {
            base.Reset();
            _elapsed = 0;
        }
    }
}
