using InnoVault.StateMachines;
using System.Collections.Generic;
using System.Text;

namespace InnoVault.BehaviorTrees
{
    /// <summary>
    /// 行为树调试探针：把一棵 BT 根节点登记到全局列表中，每帧调用<see cref="MarkTicked"/>记录上次根 tick 的状态<br/>
    /// 业务侧只需在<c>Tick</c>之后调用<see cref="MarkTicked"/>把状态告诉 probe，UI 可以遍历所有 probe 显示其当前 tick 路径
    /// </summary>
    public sealed class BehaviorTreeProbe<TContext> : IBehaviorTreeProbe
    {
        /// <summary>探针在 UI 中显示的标题</summary>
        public string DisplayName { get; }
        /// <summary>被探针的根节点</summary>
        public BTNode<TContext> Root { get; }
        /// <summary>最近一次根 tick 的返回值</summary>
        public BTStatus LastTickStatus { get; private set; }
        /// <summary>距上次 tick 已过的<see cref="Terraria.Main.GameUpdateCount"/></summary>
        public uint LastTickedGameUpdateCount { get; private set; }

        /// <summary>构造一个 BT 探针并自动登记到<see cref="BehaviorTreeDebugger"/></summary>
        public BehaviorTreeProbe(string displayName, BTNode<TContext> root) {
            DisplayName = displayName;
            Root = root;
            BehaviorTreeDebugger.Attach(this);
        }

        /// <summary>把本帧根节点 tick 后的返回状态告知探针；建议在每帧 tick 之后立即调用</summary>
        public void MarkTicked(BTStatus status) {
            LastTickStatus = status;
            LastTickedGameUpdateCount = Terraria.Main.GameUpdateCount;
        }

        /// <summary>把当前根节点的"递归 LastStatus"格式化成多行文本，方便 UI 直接显示</summary>
        public void AppendOverview(StringBuilder sb) {
            sb.Append('[').Append(DisplayName).Append("] last=").Append(LastTickStatus)
                .Append(" tickedAt=").Append(LastTickedGameUpdateCount).AppendLine();
            DumpRecursive(Root, 1, sb);
        }

        private static void DumpRecursive(BTNode<TContext> node, int indent, StringBuilder sb) {
            if (node == null) {
                return;
            }
            for (int i = 0; i < indent; i++) {
                sb.Append("  ");
            }
            string typeName = node.GetType().Name;
            //去除泛型反引号尾巴（"Sequence`1" → "Sequence"），让输出更友好
            int backtick = typeName.IndexOf('`');
            if (backtick >= 0) {
                typeName = typeName[..backtick];
            }
            sb.Append(typeName).Append('(').Append(node.LastStatus).Append(')');

            switch (node) {
                case BTComposite<TContext> comp:
                    sb.Append(" children=").Append(comp.Children.Count).AppendLine();
                    for (int i = 0; i < comp.Children.Count; i++) {
                        DumpRecursive(comp.Children[i], indent + 1, sb);
                    }
                    break;
                case BTDecorator<TContext> dec:
                    sb.AppendLine();
                    if (dec.HasChild) {
                        DumpRecursive(dec.Child, indent + 1, sb);
                    }
                    break;
                default:
                    sb.AppendLine();
                    break;
            }
        }
    }

    /// <summary>
    /// 调试探针的非泛型抽象，仅提供"显示名 + 把概览塞进 <see cref="StringBuilder"/>"两个能力<br/>
    /// 让<see cref="BehaviorTreeDebugger.Active"/>无需泛型即可统一遍历
    /// </summary>
    public interface IBehaviorTreeProbe
    {
        /// <summary>UI 标题</summary>
        string DisplayName { get; }
        /// <summary>把当前 BT 概览（递归 LastStatus 路径）追加到<paramref name="sb"/></summary>
        void AppendOverview(StringBuilder sb);
    }

    /// <summary>
    /// 行为树调试登记处。与<see cref="StateMachineDebugger"/>类似，但维护的是 BT 探针
    /// </summary>
    public static class BehaviorTreeDebugger
    {
        private static readonly List<IBehaviorTreeProbe> _active = [];

        /// <summary>当前活跃探针的只读列表</summary>
        public static IReadOnlyList<IBehaviorTreeProbe> Active => _active;

        /// <summary>登记探针（由<see cref="BehaviorTreeProbe{TContext}"/>构造器调用）</summary>
        public static void Attach<TContext>(BehaviorTreeProbe<TContext> probe) {
            if (probe == null || _active.Contains(probe)) {
                return;
            }
            _active.Add(probe);
        }

        /// <summary>解除探针登记</summary>
        public static void Detach(IBehaviorTreeProbe probe) {
            if (probe != null) {
                _active.Remove(probe);
            }
        }

        /// <summary>清空所有探针；仅由框架卸载阶段调用</summary>
        internal static void ClearAll() {
            _active.Clear();
        }

        /// <summary>把所有活跃 BT 探针的概览拼成多行文本，便于一次性贴到屏幕</summary>
        public static string FormatActiveOverview() {
            if (_active.Count == 0) {
                return string.Empty;
            }
            StringBuilder sb = new();
            foreach (IBehaviorTreeProbe probe in _active) {
                probe.AppendOverview(sb);
            }
            return sb.ToString();
        }
    }
}
