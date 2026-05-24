using System;
using System.Collections.Generic;
using System.Text;

namespace InnoVault.StateMachines
{
    /// <summary>
    /// 一次状态切换的日志条目（环形缓冲的元素）
    /// </summary>
    public readonly struct StateChangeEntry
    {
        /// <summary>记录时刻的<see cref="Terraria.Main.GameUpdateCount"/></summary>
        public uint GameUpdateCount { get; }
        /// <summary>转移触发原因</summary>
        public StateChangeReason Reason { get; }
        /// <summary>切换前状态名（可能为<see langword="null"/>，初始切换时）</summary>
        public string FromStateName { get; }
        /// <summary>切换后状态名</summary>
        public string ToStateName { get; }

        /// <summary>构造一条日志</summary>
        public StateChangeEntry(uint gameUpdateCount, StateChangeReason reason, string fromName, string toName) {
            GameUpdateCount = gameUpdateCount;
            Reason = reason;
            FromStateName = fromName;
            ToStateName = toName;
        }
    }

    /// <summary>
    /// 调试探针的非泛型抽象，用于以<see cref="StateMachineDebugger"/>静态注册表统一管理不同上下文类型的探针<br/>
    /// 具体的强类型探针见<see cref="StateMachineProbe{TContext}"/>
    /// </summary>
    public abstract class StateMachineProbe
    {
        /// <summary>探针的可读名称，供调试 UI 渲染</summary>
        public string DisplayName { get; }
        /// <summary>已记录的转移日志，由派生类填充</summary>
        protected readonly LinkedList<StateChangeEntry> _entries = new();
        /// <summary>环形缓冲容量</summary>
        public int Capacity { get; }

        /// <summary>构造一个探针</summary>
        protected StateMachineProbe(string displayName, int capacity) {
            DisplayName = displayName;
            Capacity = capacity > 0 ? capacity : 32;
        }

        /// <summary>当前已记录的转移条数</summary>
        public int EntryCount => _entries.Count;
        /// <summary>按"最新到最旧"顺序枚举转移条目（不分配列表）</summary>
        public IEnumerable<StateChangeEntry> EnumerateNewestFirst() {
            for (LinkedListNode<StateChangeEntry> node = _entries.Last; node != null; node = node.Previous) {
                yield return node.Value;
            }
        }

        /// <summary>派生类把当前状态机的"概览"塞进给定字符串构造器，供 UI 显示</summary>
        public abstract void AppendOverview(StringBuilder sb);

        /// <summary>解除事件订阅，断开与状态机的连接；供<see cref="StateMachineDebugger.Detach"/>调用</summary>
        public abstract void DetachInternal();
    }

    /// <summary>
    /// 状态机调试探针：订阅<see cref="VaultStateMachine{TContext}.OnStateChanged"/>，把每次切换写入环形缓冲，并暴露 Blackboard 快照<br/>
    /// 业务侧用法：在构造完状态机之后，<c>new StateMachineProbe&lt;Ctx&gt;("MyBoss", machine)</c>，调试 UI 自动可见
    /// </summary>
    /// <typeparam name="TContext">状态机上下文类型</typeparam>
    public sealed class StateMachineProbe<TContext> : StateMachineProbe
    {
        private readonly VaultStateMachine<TContext> _machine;
        private readonly Action<IVaultState<TContext>, IVaultState<TContext>, StateChangeReason> _handler;

        /// <summary>
        /// 构造一个新的探针并立即注册到<see cref="StateMachineDebugger"/>全局表中
        /// </summary>
        /// <param name="displayName">UI 中显示的标题，例如"Destroyer FSM"</param>
        /// <param name="machine">要探针的状态机</param>
        /// <param name="capacity">环形缓冲容量，默认 32</param>
        public StateMachineProbe(string displayName, VaultStateMachine<TContext> machine, int capacity = 32)
            : base(displayName, capacity) {
            _machine = machine ?? throw new ArgumentNullException(nameof(machine));
            _handler = OnStateChanged;
            _machine.OnStateChanged += _handler;
            StateMachineDebugger.Attach(this);
        }

        private void OnStateChanged(IVaultState<TContext> from, IVaultState<TContext> to, StateChangeReason reason) {
            StateChangeEntry entry = new(Terraria.Main.GameUpdateCount, reason, from?.StateName, to?.StateName);
            _entries.AddLast(entry);
            while (_entries.Count > Capacity) {
                _entries.RemoveFirst();
            }
        }

        /// <inheritdoc/>
        public override void AppendOverview(StringBuilder sb) {
            sb.Append('[').Append(DisplayName).Append("] current=").Append(_machine.CurrentState?.StateName ?? "<null>");
            if (_machine.PreviousState != null) {
                sb.Append(" prev=").Append(_machine.PreviousState.StateName);
            }
            sb.Append(" terminated=").Append(_machine.IsTerminated);
            sb.AppendLine();
            sb.Append("  blackboard(").Append(_machine.Blackboard.Count).Append("): ");
            int i = 0;
            foreach ((Type type, string name, object value) in _machine.Blackboard.Snapshot()) {
                if (i++ > 0) {
                    sb.Append(", ");
                }
                sb.Append(name).Append(':').Append(type.Name).Append('=').Append(value);
            }
            sb.AppendLine();
        }

        /// <inheritdoc/>
        public override void DetachInternal() {
            _machine.OnStateChanged -= _handler;
        }
    }

    /// <summary>
    /// 全局状态机调试登记处：保存所有活跃的<see cref="StateMachineProbe{TContext}"/>，便于一个统一的调试面板批量渲染<br/>
    /// 业务侧无需手动调用——构造<see cref="StateMachineProbe{TContext}"/>时已自动注册
    /// </summary>
    public static class StateMachineDebugger
    {
        private static readonly List<StateMachineProbe> _active = [];

        /// <summary>当前活跃的探针只读列表，供调试 UI 遍历渲染</summary>
        public static IReadOnlyList<StateMachineProbe> Active => _active;

        /// <summary>注册探针（由<see cref="StateMachineProbe{TContext}"/>构造函数调用）</summary>
        public static void Attach(StateMachineProbe probe) {
            if (probe == null || _active.Contains(probe)) {
                return;
            }
            _active.Add(probe);
        }

        /// <summary>解除探针注册并断开其事件订阅</summary>
        public static void Detach(StateMachineProbe probe) {
            if (probe == null) {
                return;
            }
            if (_active.Remove(probe)) {
                probe.DetachInternal();
            }
        }

        /// <summary>清空所有探针。仅由框架卸载阶段调用</summary>
        internal static void ClearAll() {
            foreach (StateMachineProbe probe in _active) {
                probe.DetachInternal();
            }
            _active.Clear();
        }

        /// <summary>把所有活跃探针的"当前状态 + Blackboard 快照"格式化为多行文本，便于一次性贴到屏幕</summary>
        public static string FormatActiveOverview() {
            if (_active.Count == 0) {
                return string.Empty;
            }
            StringBuilder sb = new();
            foreach (StateMachineProbe probe in _active) {
                probe.AppendOverview(sb);
            }
            return sb.ToString();
        }
    }
}
