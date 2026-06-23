using InnoVault.StateMachines;
using System;
using System.Collections.Generic;

namespace InnoVault.BehaviorTrees
{
    /// <summary>
    /// 复合节点的抽象基类：持有一组子节点，按各自策略递归推进<br/>
    /// 内置实现：<see cref="Sequence{TContext}"/>、<see cref="Selector{TContext}"/>、<see cref="Parallel{TContext}"/>、<see cref="RandomSelector{TContext}"/>
    /// </summary>
    public abstract class BTComposite<TContext> : BTNode<TContext>
    {
        /// <summary>子节点列表（按声明顺序）</summary>
        protected readonly List<BTNode<TContext>> _children = [];

        /// <summary>子节点的只读视图，供调试器/上层观察使用</summary>
        public IReadOnlyList<BTNode<TContext>> Children => _children;

        /// <summary>添加一个子节点。返回自身以支持链式</summary>
        public BTComposite<TContext> Add(BTNode<TContext> child) {
            if (child != null) {
                _children.Add(child);
            }
            return this;
        }

        /// <inheritdoc/>
        public override void Reset() {
            base.Reset();
            foreach (BTNode<TContext> child in _children) {
                child.Reset();
            }
        }
    }

    /// <summary>
    /// 顺序节点：依次 tick 每个子节点；任一返回<see cref="BTStatus.Failure"/>立即结束并返回 Failure，<br/>
    /// 全部<see cref="BTStatus.Success"/>则整体返回 Success。<see cref="BTStatus.Running"/>会让外层下一帧从同一子节点继续
    /// </summary>
    public sealed class Sequence<TContext> : BTComposite<TContext>
    {
        private int _index;

        /// <inheritdoc/>
        public override BTStatus Tick(TContext ctx, Blackboard blackboard) {
            while (_index < _children.Count) {
                BTStatus status = _children[_index].Tick(ctx, blackboard);
                if (status == BTStatus.Running) {
                    LastStatus = BTStatus.Running;
                    return BTStatus.Running;
                }
                if (status == BTStatus.Failure) {
                    //终止时 reset 失败的子节点，避免它的内部状态（计时器/索引）被下一次进入时复用
                    _children[_index].Reset();
                    _index = 0;
                    LastStatus = BTStatus.Failure;
                    return BTStatus.Failure;
                }
                _index++;
            }
            //完整跑完一遍：reset 所有子节点，确保下一轮从干净状态开始
            for (int i = 0; i < _children.Count; i++) {
                _children[i].Reset();
            }
            _index = 0;
            LastStatus = BTStatus.Success;
            return BTStatus.Success;
        }

        /// <inheritdoc/>
        public override void Reset() {
            base.Reset();
            _index = 0;
        }
    }

    /// <summary>
    /// 选择节点：依次 tick 每个子节点；任一返回<see cref="BTStatus.Success"/>立即结束并返回 Success，<br/>
    /// 全部<see cref="BTStatus.Failure"/>则整体返回 Failure
    /// </summary>
    public sealed class Selector<TContext> : BTComposite<TContext>
    {
        private int _index;

        /// <inheritdoc/>
        public override BTStatus Tick(TContext ctx, Blackboard blackboard) {
            while (_index < _children.Count) {
                BTStatus status = _children[_index].Tick(ctx, blackboard);
                if (status == BTStatus.Running) {
                    LastStatus = BTStatus.Running;
                    return BTStatus.Running;
                }
                if (status == BTStatus.Success) {
                    //成功结束：reset 成功的子节点，避免其内部状态被下一轮 Selector 重新选择时复用
                    _children[_index].Reset();
                    _index = 0;
                    LastStatus = BTStatus.Success;
                    return BTStatus.Success;
                }
                _index++;
            }
            //全部失败：reset 所有子节点，保证下一轮干净
            for (int i = 0; i < _children.Count; i++) {
                _children[i].Reset();
            }
            _index = 0;
            LastStatus = BTStatus.Failure;
            return BTStatus.Failure;
        }

        /// <inheritdoc/>
        public override void Reset() {
            base.Reset();
            _index = 0;
        }
    }

    /// <summary>
    /// 并行节点的结束策略
    /// </summary>
    public enum ParallelPolicy
    {
        /// <summary>任一子节点返回<see cref="BTStatus.Success"/>立即整体 Success；全部 Failure 才整体 Failure</summary>
        AnySuccess,
        /// <summary>所有子节点都 Success 才整体 Success；任一 Failure 立即 Failure</summary>
        AllSuccess,
        /// <summary>
        /// 至少一个 Success 即可：等到本帧所有子节点都已 settle (非 Running)，<br/>
        /// 只要其中至少有一个 Success 就整体 Success（允许部分子节点 Failure）；<br/>
        /// 当本帧所有子节点都 Failure 时整体 Failure<br/>
        /// 与<see cref="AnySuccess"/>的区别：本策略<b>不会</b>在第一个成功就立即返回，会等其它子节点结束<br/>
        /// 与<see cref="AllSuccess"/>的区别：允许部分子节点失败，只要至少一个成功
        /// </summary>
        RequireOne,
    }

    /// <summary>
    /// 并行节点：每帧 tick <b>所有</b>子节点，根据<see cref="Policy"/>决定整体返回值<br/>
    /// 注意"并行"指逻辑上的并列评估，<b>不是</b>多线程<br/>
    /// 节点返回 Success/Failure 时，仍处于 Running 的兄弟节点会被自动<see cref="BTNode{TContext}.Reset"/>，<br/>
    /// 以避免下一次进入 Parallel 时挂着上一次的脏计时 / 内部进度
    /// </summary>
    public sealed class Parallel<TContext> : BTComposite<TContext>
    {
        /// <summary>并行结束策略</summary>
        public ParallelPolicy Policy { get; }

        /// <summary>构造</summary>
        public Parallel(ParallelPolicy policy = ParallelPolicy.AllSuccess) {
            Policy = policy;
        }

        /// <inheritdoc/>
        public override BTStatus Tick(TContext ctx, Blackboard blackboard) {
            int childCount = _children.Count;
            int successCount = 0;
            int failureCount = 0;
            int runningCount = 0;
            //记录每个子节点本帧的状态，便于结束时只 reset 仍 Running 的子节点
            //典型 BT 并行节点的子数远小于 16，固定栈缓冲避免 GC 压力，超出再退化到堆
            Span<BTStatus> stackBuf = stackalloc BTStatus[16];
            Span<BTStatus> perChild = childCount <= 16
                ? stackBuf[..childCount]
                : new BTStatus[childCount];
            for (int i = 0; i < childCount; i++) {
                BTStatus status = _children[i].Tick(ctx, blackboard);
                perChild[i] = status;
                switch (status) {
                    case BTStatus.Success: successCount++; break;
                    case BTStatus.Failure: failureCount++; break;
                    default: runningCount++; break;
                }
            }

            BTStatus result;
            switch (Policy) {
                case ParallelPolicy.AnySuccess:
                    if (successCount > 0) {
                        result = BTStatus.Success;
                    }
                    else if (failureCount == _children.Count) {
                        result = BTStatus.Failure;
                    }
                    else {
                        result = BTStatus.Running;
                    }
                    break;
                case ParallelPolicy.AllSuccess:
                    if (failureCount > 0) {
                        result = BTStatus.Failure;
                    }
                    else if (successCount == _children.Count) {
                        result = BTStatus.Success;
                    }
                    else {
                        result = BTStatus.Running;
                    }
                    break;
                case ParallelPolicy.RequireOne:
                    //等所有子节点 settle 后再裁决：失败和成功都允许，只要至少一个成功
                    if (runningCount > 0) {
                        result = BTStatus.Running;
                    }
                    else if (successCount > 0) {
                        result = BTStatus.Success;
                    }
                    else {
                        result = BTStatus.Failure;
                    }
                    break;
                default:
                    result = BTStatus.Failure;
                    break;
            }

            //终止时把仍在 Running 的子节点 reset 掉，避免它们的内部进度/计时悬挂到下次进入
            if (result != BTStatus.Running) {
                for (int i = 0; i < _children.Count; i++) {
                    if (perChild[i] == BTStatus.Running) {
                        _children[i].Reset();
                    }
                }
            }

            LastStatus = result;
            return result;
        }
    }

    /// <summary>
    /// 随机选择节点：基于权重随机选取一个子节点 tick，其结果即为本节点结果<br/>
    /// 选中过程使用构造时传入的<see cref="Random"/>实例，多人模式下应当传入"由服务端同步的种子"构造的<see cref="Random"/>，<br/>
    /// 否则各客户端可能选到不同分支并行为不一致——大多数情况建议把权重选择移到<see cref="WeightedRandomPicker{T}"/>外 + FSM 模式做
    /// </summary>
    public sealed class RandomSelector<TContext> : BTComposite<TContext>
    {
        private readonly List<float> _weights = [];
        private readonly Random _rng;
        private int _selectedIndex = -1;

        /// <summary>构造一个权重随机选择器；<paramref name="rng"/>为<see langword="null"/>时使用一个非种子化的本地实例</summary>
        public RandomSelector(Random rng = null) {
            _rng = rng ?? new Random();
        }

        /// <summary>
        /// 不允许直接调用<see cref="BTComposite{TContext}.Add"/>——本节点必须通过<see cref="AddWeighted"/>同时提供权重，<br/>
        /// 否则<c>_children</c>与<c>_weights</c>会失去同步，造成索引越界或权重总和错误<br/>
        /// 该方法被特意保留为静态绑定的"运行时拦截"：无论是<c>new RandomSelector().Add(...)</c>还是反射，都会抛异常
        /// </summary>
        public new BTComposite<TContext> Add(BTNode<TContext> child)
            => throw new InvalidOperationException(
                "RandomSelector.Add(child) is not supported; use AddWeighted(child, weight) to keep weights synchronized.");

        /// <summary>按权重添加子节点</summary>
        public RandomSelector<TContext> AddWeighted(BTNode<TContext> child, float weight) {
            if (child == null || weight <= 0f) {
                return this;
            }
            _children.Add(child);
            _weights.Add(weight);
            return this;
        }

        /// <inheritdoc/>
        public override BTStatus Tick(TContext ctx, Blackboard blackboard) {
            if (_children.Count == 0) {
                LastStatus = BTStatus.Failure;
                return BTStatus.Failure;
            }
            //首次或上一帧已 Settled 时重新选择；Running 中维持原选择
            if (_selectedIndex < 0) {
                float total = 0f;
                for (int i = 0; i < _weights.Count; i++) {
                    total += _weights[i];
                }
                float roll = (float)_rng.NextDouble() * total;
                float acc = 0f;
                _selectedIndex = _children.Count - 1;
                for (int i = 0; i < _weights.Count; i++) {
                    acc += _weights[i];
                    if (roll <= acc) {
                        _selectedIndex = i;
                        break;
                    }
                }
            }
            BTStatus status = _children[_selectedIndex].Tick(ctx, blackboard);
            if (status != BTStatus.Running) {
                _selectedIndex = -1;
            }
            LastStatus = status;
            return status;
        }

        /// <inheritdoc/>
        public override void Reset() {
            base.Reset();
            _selectedIndex = -1;
        }
    }
}
