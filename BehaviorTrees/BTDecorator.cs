using InnoVault.StateMachines;
using System;

namespace InnoVault.BehaviorTrees
{
    /// <summary>
    /// 装饰节点的抽象基类：恰好持有一个子节点，可改写其返回值或限制其触发条件<br/>
    /// 内置实现：<see cref="Inverter{TContext}"/>、<see cref="Repeater{TContext}"/>、<see cref="Cooldown{TContext}"/>、<see cref="ConditionGate{TContext}"/>、<see cref="TimeLimit{TContext}"/>、<see cref="AlwaysSucceed{TContext}"/>、<see cref="AlwaysFail{TContext}"/>
    /// </summary>
    public abstract class BTDecorator<TContext> : BTNode<TContext>
    {
        /// <summary>所装饰的子节点</summary>
        protected BTNode<TContext> _child;

        /// <summary>设置子节点（builder 使用）；返回自身以支持链式</summary>
        public BTDecorator<TContext> SetChild(BTNode<TContext> child) {
            _child = child;
            return this;
        }

        /// <inheritdoc/>
        public override void Reset() {
            base.Reset();
            _child?.Reset();
        }
    }

    /// <summary>反相器：Success → Failure，Failure → Success，Running 透传</summary>
    public sealed class Inverter<TContext> : BTDecorator<TContext>
    {
        /// <inheritdoc/>
        public override BTStatus Tick(TContext ctx, Blackboard blackboard) {
            if (_child == null) {
                LastStatus = BTStatus.Failure;
                return BTStatus.Failure;
            }
            BTStatus status = _child.Tick(ctx, blackboard);
            BTStatus result = status switch {
                BTStatus.Success => BTStatus.Failure,
                BTStatus.Failure => BTStatus.Success,
                _ => BTStatus.Running,
            };
            LastStatus = result;
            return result;
        }
    }

    /// <summary>
    /// 重复执行器：循环 tick 子节点指定次数；<br/>
    /// <see cref="Count"/>为 -1 表示无限循环（仅在子节点返回 Failure 时才会终止，否则视为 Running 提升给上层）
    /// </summary>
    public sealed class Repeater<TContext> : BTDecorator<TContext>
    {
        /// <summary>重复次数，-1 为无限</summary>
        public int Count { get; }
        private int _completedCount;

        /// <summary>构造一个重复执行器</summary>
        public Repeater(int count) {
            Count = count;
        }

        /// <inheritdoc/>
        public override BTStatus Tick(TContext ctx, Blackboard blackboard) {
            if (_child == null) {
                LastStatus = BTStatus.Failure;
                return BTStatus.Failure;
            }
            while (true) {
                BTStatus status = _child.Tick(ctx, blackboard);
                if (status == BTStatus.Running) {
                    LastStatus = BTStatus.Running;
                    return BTStatus.Running;
                }
                if (status == BTStatus.Failure && Count < 0) {
                    _completedCount = 0;
                    LastStatus = BTStatus.Failure;
                    return BTStatus.Failure;
                }
                _completedCount++;
                if (Count >= 0 && _completedCount >= Count) {
                    _completedCount = 0;
                    LastStatus = BTStatus.Success;
                    return BTStatus.Success;
                }
                _child.Reset();
                //无限模式（Count < 0）继续 while；一帧内完成多次子节点是允许的，<br/>
                //但子节点若返回 Running 我们会立即跳出，避免单帧死循环
                if (Count < 0) {
                    //继续循环
                }
            }
        }

        /// <inheritdoc/>
        public override void Reset() {
            base.Reset();
            _completedCount = 0;
        }
    }

    /// <summary>
    /// 冷却装饰：自上次 Success / Failure 起<see cref="CooldownTicks"/>帧内拒绝再次进入子节点，直接返回 Failure<br/>
    /// 适合"攻击冷却"、"对话间隔"等场景
    /// </summary>
    public sealed class Cooldown<TContext> : BTDecorator<TContext>
    {
        /// <summary>冷却帧数</summary>
        public int CooldownTicks { get; }
        private uint _lastFinishedGameUpdateCount;
        private bool _everFinished;

        /// <summary>构造一个冷却装饰</summary>
        public Cooldown(int cooldownTicks) {
            CooldownTicks = cooldownTicks;
        }

        /// <inheritdoc/>
        public override BTStatus Tick(TContext ctx, Blackboard blackboard) {
            if (_child == null) {
                LastStatus = BTStatus.Failure;
                return BTStatus.Failure;
            }
            uint now = Terraria.Main.GameUpdateCount;
            if (_everFinished && now - _lastFinishedGameUpdateCount < (uint)CooldownTicks) {
                LastStatus = BTStatus.Failure;
                return BTStatus.Failure;
            }
            BTStatus status = _child.Tick(ctx, blackboard);
            if (status != BTStatus.Running) {
                _lastFinishedGameUpdateCount = now;
                _everFinished = true;
                _child.Reset();
            }
            LastStatus = status;
            return status;
        }

        /// <inheritdoc/>
        public override void Reset() {
            base.Reset();
            _everFinished = false;
        }
    }

    /// <summary>
    /// 条件门：先评估<see cref="Condition"/>谓词，<see langword="true"/>时才 tick 子节点；<br/>
    /// 谓词为<see langword="false"/>时直接返回 Failure（不会调用子节点）
    /// </summary>
    public sealed class ConditionGate<TContext> : BTDecorator<TContext>
    {
        /// <summary>门控谓词</summary>
        public Func<TContext, bool> Condition { get; }

        /// <summary>构造一个条件门</summary>
        public ConditionGate(Func<TContext, bool> condition) {
            Condition = condition ?? throw new ArgumentNullException(nameof(condition));
        }

        /// <inheritdoc/>
        public override BTStatus Tick(TContext ctx, Blackboard blackboard) {
            if (_child == null || !Condition(ctx)) {
                LastStatus = BTStatus.Failure;
                return BTStatus.Failure;
            }
            BTStatus status = _child.Tick(ctx, blackboard);
            LastStatus = status;
            return status;
        }
    }

    /// <summary>
    /// 时限装饰：子节点首次返回 Running 后的<see cref="LimitTicks"/>帧内若仍未结束，强制返回 Failure
    /// </summary>
    public sealed class TimeLimit<TContext> : BTDecorator<TContext>
    {
        /// <summary>时限帧数</summary>
        public int LimitTicks { get; }
        private uint _startedGameUpdateCount;
        private bool _running;

        /// <summary>构造</summary>
        public TimeLimit(int limitTicks) {
            LimitTicks = limitTicks;
        }

        /// <inheritdoc/>
        public override BTStatus Tick(TContext ctx, Blackboard blackboard) {
            if (_child == null) {
                LastStatus = BTStatus.Failure;
                return BTStatus.Failure;
            }
            uint now = Terraria.Main.GameUpdateCount;
            if (!_running) {
                _startedGameUpdateCount = now;
                _running = true;
            }
            if (now - _startedGameUpdateCount >= (uint)LimitTicks) {
                _child.Reset();
                _running = false;
                LastStatus = BTStatus.Failure;
                return BTStatus.Failure;
            }
            BTStatus status = _child.Tick(ctx, blackboard);
            if (status != BTStatus.Running) {
                _running = false;
            }
            LastStatus = status;
            return status;
        }

        /// <inheritdoc/>
        public override void Reset() {
            base.Reset();
            _running = false;
        }
    }

    /// <summary>无论子节点返回什么，本节点始终返回<see cref="BTStatus.Success"/>（Running 透传）</summary>
    public sealed class AlwaysSucceed<TContext> : BTDecorator<TContext>
    {
        /// <inheritdoc/>
        public override BTStatus Tick(TContext ctx, Blackboard blackboard) {
            if (_child == null) {
                LastStatus = BTStatus.Success;
                return BTStatus.Success;
            }
            BTStatus status = _child.Tick(ctx, blackboard);
            BTStatus result = status == BTStatus.Running ? BTStatus.Running : BTStatus.Success;
            LastStatus = result;
            return result;
        }
    }

    /// <summary>无论子节点返回什么，本节点始终返回<see cref="BTStatus.Failure"/>（Running 透传）</summary>
    public sealed class AlwaysFail<TContext> : BTDecorator<TContext>
    {
        /// <inheritdoc/>
        public override BTStatus Tick(TContext ctx, Blackboard blackboard) {
            if (_child == null) {
                LastStatus = BTStatus.Failure;
                return BTStatus.Failure;
            }
            BTStatus status = _child.Tick(ctx, blackboard);
            BTStatus result = status == BTStatus.Running ? BTStatus.Running : BTStatus.Failure;
            LastStatus = result;
            return result;
        }
    }
}
