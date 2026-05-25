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

        /// <summary>当前是否已经持有子节点（Builder 用于侦测"两个子"或"忘记 .End()"的错误）</summary>
        public bool HasChild => _child != null;

        /// <summary>所装饰的子节点的只读视图，供调试器/上层观察使用；构造期或断开时可能为<see langword="null"/></summary>
        public BTNode<TContext> Child => _child;

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
    /// 重复执行器：每帧最多 tick 一次子节点<br/>
    /// <list type="bullet">
    /// <item><see cref="Count"/> &gt;= 0：要求子节点累计成功<see cref="Count"/>次才整体返回<see cref="BTStatus.Success"/>；<br/>
    /// 任意一次<see cref="BTStatus.Failure"/>都会立即终止并整体返回 Failure</item>
    /// <item><see cref="Count"/> &lt; 0：无限循环；子节点成功后<see cref="BTNode{TContext}.Reset"/>并下一帧再 tick（本帧返回 Running），<br/>
    /// 子节点失败则整体返回 Failure</item>
    /// </list>
    /// 注意：此节点<b>不会</b>在单帧内多次 tick 子节点，避免子节点瞬时 Success 时形成死循环
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

            BTStatus status = _child.Tick(ctx, blackboard);
            if (status == BTStatus.Running) {
                LastStatus = BTStatus.Running;
                return BTStatus.Running;
            }
            if (status == BTStatus.Failure) {
                //无论有限/无限模式，子节点失败都立即向上传播 Failure 并复位计数
                _completedCount = 0;
                _child.Reset();
                LastStatus = BTStatus.Failure;
                return BTStatus.Failure;
            }

            //此分支：子节点本帧返回 Success
            _completedCount++;
            if (Count >= 0 && _completedCount >= Count) {
                _completedCount = 0;
                _child.Reset();
                LastStatus = BTStatus.Success;
                return BTStatus.Success;
            }

            //尚未达到目标次数（或处于无限模式），重置子节点准备下一帧再 tick，本帧返回 Running
            _child.Reset();
            LastStatus = BTStatus.Running;
            return BTStatus.Running;
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
    /// 谓词为<see langword="false"/>时直接返回 Failure（不会调用子节点）。<br/>
    /// 当谓词从<see langword="true"/>翻转为<see langword="false"/>时，子节点（可能正在 Running）会被<see cref="BTNode{TContext}.Reset"/>，<br/>
    /// 避免下一次门打开时子节点继续上一次的脏进度
    /// </summary>
    public sealed class ConditionGate<TContext> : BTDecorator<TContext>
    {
        /// <summary>门控谓词</summary>
        public Func<TContext, bool> Condition { get; }
        //追踪子节点是否处于"已进入但未结束"的状态——只有这种情况下关门才需要 reset
        private bool _childInFlight;

        /// <summary>构造一个条件门</summary>
        public ConditionGate(Func<TContext, bool> condition) {
            Condition = condition ?? throw new ArgumentNullException(nameof(condition));
        }

        /// <inheritdoc/>
        public override BTStatus Tick(TContext ctx, Blackboard blackboard) {
            if (_child == null) {
                _childInFlight = false;
                LastStatus = BTStatus.Failure;
                return BTStatus.Failure;
            }
            if (!Condition(ctx)) {
                if (_childInFlight) {
                    _child.Reset();
                    _childInFlight = false;
                }
                LastStatus = BTStatus.Failure;
                return BTStatus.Failure;
            }
            BTStatus status = _child.Tick(ctx, blackboard);
            _childInFlight = status == BTStatus.Running;
            LastStatus = status;
            return status;
        }

        /// <inheritdoc/>
        public override void Reset() {
            base.Reset();
            _childInFlight = false;
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
