using InnoVault.StateMachines;
using System;
using System.Collections.Generic;

namespace InnoVault.BehaviorTrees
{
    /// <summary>
    /// 行为树构建器入口。提供基于"栈"的<see cref="BehaviorTreeBuilder{TContext}"/>，<br/>
    /// 复合节点 / 装饰节点必须以 <c>.End()</c> 闭合，保证树结构在编译期可被栈检查
    /// </summary>
    public static class BehaviorTreeBuilder
    {
        /// <summary>开始构造<typeparamref name="TContext"/>的行为树</summary>
        public static BehaviorTreeBuilder<TContext> For<TContext>() => new BehaviorTreeBuilder<TContext>();
    }

    /// <summary>
    /// 行为树流畅构建器。用法：
    /// <code>
    /// var tree = BehaviorTreeBuilder.For&lt;NpcCtx&gt;()
    ///     .Selector()
    ///         .Sequence()
    ///             .Condition(c =&gt; c.IsLowHp)
    ///             .Action(c =&gt; { /* flee */ return BTStatus.Success; })
    ///         .End()
    ///         .Action(c =&gt; { /* wander */ return BTStatus.Success; })
    ///     .End()
    ///     .Build();
    /// </code>
    /// 装饰节点（<see cref="Inverter"/>/<see cref="Repeat"/>/<see cref="Cooldown"/>/<see cref="Gate"/>/<see cref="TimeLimit"/>/<see cref="AlwaysSucceed"/>/<see cref="AlwaysFail"/>）<br/>
    /// 与复合节点一样，<b>必须</b>由配对的<see cref="End"/>闭合，避免出现"再加一个节点"实际上落到外层错位的隐藏问题
    /// </summary>
    /// <typeparam name="TContext">行为树上下文类型</typeparam>
    public sealed class BehaviorTreeBuilder<TContext>
    {
        //同时承载"复合节点的待挂载子"与"装饰节点等待 SetChild 的一个空槽"
        private readonly Stack<BTNode<TContext>> _open = new();
        private BTNode<TContext> _root;

        internal BehaviorTreeBuilder() { }

        /// <summary>开始一个<see cref="BehaviorTrees.Sequence{TContext}"/></summary>
        public BehaviorTreeBuilder<TContext> Sequence() => PushComposite(new Sequence<TContext>());

        /// <summary>开始一个<see cref="BehaviorTrees.Selector{TContext}"/></summary>
        public BehaviorTreeBuilder<TContext> Selector() => PushComposite(new Selector<TContext>());

        /// <summary>开始一个<see cref="Parallel{TContext}"/></summary>
        public BehaviorTreeBuilder<TContext> Parallel(ParallelPolicy policy = ParallelPolicy.AllSuccess)
            => PushComposite(new Parallel<TContext>(policy));

        /// <summary>开始一个<see cref="RandomSelector{TContext}"/></summary>
        public BehaviorTreeBuilder<TContext> RandomSelector(Random rng = null)
            => PushComposite(new RandomSelector<TContext>(rng));

        /// <summary>在<see cref="RandomSelector{TContext}"/>下声明一个带权重的子节点。<br/>
        /// 用法：<c>.RandomSelector().Weighted(2f).Action(...).Weighted(1f).Action(...)</c><br/>
        /// 若当前栈顶不是<see cref="RandomSelector{TContext}"/>会立即抛出<see cref="InvalidOperationException"/>，<br/>
        /// 否则<c>_pendingWeight</c>会被静默携带到下一次进入 RandomSelector 的子节点，导致权重错位且不报错
        /// </summary>
        public BehaviorTreeBuilder<TContext> Weighted(float weight) {
            if (_open.Count == 0 || _open.Peek() is not RandomSelector<TContext>) {
                throw new InvalidOperationException(
                    "BehaviorTreeBuilder.Weighted is only valid directly inside a RandomSelector scope. " +
                    "Either open a RandomSelector first via .RandomSelector(), or remove the .Weighted(...) call.");
            }
            _pendingWeight = weight;
            return this;
        }

        /// <summary>开始一个<see cref="Inverter{TContext}"/>装饰</summary>
        public BehaviorTreeBuilder<TContext> Inverter() => PushDecorator(new Inverter<TContext>());

        /// <summary>开始一个<see cref="Repeater{TContext}"/>装饰；<paramref name="count"/>为 -1 表示无限</summary>
        public BehaviorTreeBuilder<TContext> Repeat(int count) => PushDecorator(new Repeater<TContext>(count));

        /// <summary>开始一个<see cref="Cooldown{TContext}"/>装饰</summary>
        public BehaviorTreeBuilder<TContext> Cooldown(int ticks) => PushDecorator(new Cooldown<TContext>(ticks));

        /// <summary>开始一个<see cref="ConditionGate{TContext}"/>装饰</summary>
        public BehaviorTreeBuilder<TContext> Gate(Func<TContext, bool> condition)
            => PushDecorator(new ConditionGate<TContext>(condition));

        /// <summary>开始一个<see cref="TimeLimit{TContext}"/>装饰</summary>
        public BehaviorTreeBuilder<TContext> TimeLimit(int ticks) => PushDecorator(new TimeLimit<TContext>(ticks));

        /// <summary>开始一个<see cref="AlwaysSucceed{TContext}"/>装饰</summary>
        public BehaviorTreeBuilder<TContext> AlwaysSucceed() => PushDecorator(new AlwaysSucceed<TContext>());

        /// <summary>开始一个<see cref="AlwaysFail{TContext}"/>装饰</summary>
        public BehaviorTreeBuilder<TContext> AlwaysFail() => PushDecorator(new AlwaysFail<TContext>());

        /// <summary>添加一个<see cref="ActionLeaf{TContext}"/>叶节点</summary>
        public BehaviorTreeBuilder<TContext> Action(Func<TContext, Blackboard, BTStatus> action)
            => AttachLeaf(new ActionLeaf<TContext>(action));

        /// <summary>添加一个忽略 Blackboard 的<see cref="ActionLeaf{TContext}"/>叶节点</summary>
        public BehaviorTreeBuilder<TContext> Action(Func<TContext, BTStatus> action)
            => AttachLeaf(new ActionLeaf<TContext>(action));

        /// <summary>添加一个<see cref="ConditionLeaf{TContext}"/>叶节点</summary>
        public BehaviorTreeBuilder<TContext> Condition(Func<TContext, bool> condition)
            => AttachLeaf(new ConditionLeaf<TContext>(condition));

        /// <summary>添加一个<see cref="WaitLeaf{TContext}"/>叶节点</summary>
        public BehaviorTreeBuilder<TContext> Wait(int ticks)
            => AttachLeaf(new WaitLeaf<TContext>(ticks));

        /// <summary>添加一个外部构造的自定义节点（包括 FSM 桥接节点）</summary>
        public BehaviorTreeBuilder<TContext> Leaf(BTNode<TContext> node) => AttachLeaf(node);

        /// <summary>闭合最近的复合/装饰节点</summary>
        public BehaviorTreeBuilder<TContext> End() {
            if (_open.Count == 0) {
                throw new InvalidOperationException("BehaviorTreeBuilder.End called with no open composite/decorator.");
            }
            _open.Pop();
            return this;
        }

        /// <summary>完成构建。要求所有<see cref="Sequence"/>/<see cref="Selector"/>等都已配对<see cref="End"/></summary>
        public BTNode<TContext> Build() {
            if (_open.Count != 0) {
                throw new InvalidOperationException($"BehaviorTreeBuilder.Build called while {_open.Count} composite(s)/decorator(s) are still open. Did you forget .End()?");
            }
            if (_root == null) {
                throw new InvalidOperationException("BehaviorTreeBuilder produced no root node.");
            }
            return _root;
        }

        private float _pendingWeight = -1f;

        private BehaviorTreeBuilder<TContext> PushComposite(BTComposite<TContext> composite) {
            AttachAsChildOfTop(composite);
            _open.Push(composite);
            return this;
        }

        private BehaviorTreeBuilder<TContext> PushDecorator(BTDecorator<TContext> decorator) {
            AttachAsChildOfTop(decorator);
            _open.Push(decorator);
            return this;
        }

        private BehaviorTreeBuilder<TContext> AttachLeaf(BTNode<TContext> leaf) {
            AttachAsChildOfTop(leaf);
            return this;
        }

        private void AttachAsChildOfTop(BTNode<TContext> node) {
            if (_open.Count == 0) {
                if (_root != null) {
                    throw new InvalidOperationException("BehaviorTreeBuilder cannot have multiple root nodes.");
                }
                _root = node;
                return;
            }
            BTNode<TContext> top = _open.Peek();
            switch (top) {
                case RandomSelector<TContext> rs:
                    float weight = _pendingWeight > 0f ? _pendingWeight : 1f;
                    _pendingWeight = -1f;
                    rs.AddWeighted(node, weight);
                    break;
                case BTComposite<TContext> comp:
                    comp.Add(node);
                    break;
                case BTDecorator<TContext> dec:
                    if (dec.HasChild) {
                        //装饰节点只能持有一个子；走到这里说明用户在第一个子之后又往同一作用域加了第二个节点
                        //这与"忘记 .End()"难以区分，直接报错比静默把第二个节点丢失或挂错位置更安全
                        throw new InvalidOperationException(
                            $"BehaviorTreeBuilder: decorator {dec.GetType().Name} already has a child. " +
                            "Did you forget to call .End() to close the decorator before attaching the next node?");
                    }
                    dec.SetChild(node);
                    //装饰节点不自动弹出——与复合节点保持一致，等待显式 .End() 闭合
                    break;
                default:
                    throw new InvalidOperationException($"Unexpected open node type: {top.GetType().FullName}");
            }
        }
    }
}
