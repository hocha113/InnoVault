using InnoVault.StateMachines;
using System.Collections.Generic;
using System.Text;

namespace InnoVault.BehaviorTrees
{
    /// <summary>
    /// 行为树调试探针：把一棵 BT 根节点登记到全局列表中，每帧调用<see cref="MarkTicked"/>记录上次根 tick 的状态<br/>
    /// 业务侧只需在<c>Tick</c>之后调用<see cref="MarkTicked"/>把状态告诉 probe，UI 可以遍历所有 probe 显示其当前 tick 路径
    /// </summary>
    public sealed class BehaviorTreeProbe<TContext>
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
            sb.Append('[').Append(DisplayName).Append("] last=").Append(LastTickStatus).AppendLine();
            DumpRecursive(Root, 1, sb);
        }

        private static void DumpRecursive(BTNode<TContext> node, int indent, StringBuilder sb) {
            if (node == null) {
                return;
            }
            for (int i = 0; i < indent; i++) {
                sb.Append("  ");
            }
            sb.Append(node.GetType().Name).Append('(').Append(node.LastStatus).Append(')').AppendLine();
            if (node is BTComposite<TContext> comp) {
                //不向下递归装饰/复合节点的私有字段，避免引入反射；只输出本节点的类型和最后状态
                _ = comp;
            }
        }
    }

    /// <summary>
    /// 行为树调试登记处。与<see cref="StateMachineDebugger"/>类似，但维护的是 BT 探针
    /// </summary>
    public static class BehaviorTreeDebugger
    {
        //存为非泛型 object 列表；UI 渲染时再通过反射或<c>dynamic</c>取出 DisplayName 即可
        private static readonly List<object> _active = [];

        /// <summary>当前活跃探针的只读列表</summary>
        public static IReadOnlyList<object> Active => _active;

        /// <summary>登记探针（由<see cref="BehaviorTreeProbe{TContext}"/>构造器调用）</summary>
        public static void Attach<TContext>(BehaviorTreeProbe<TContext> probe) {
            if (probe == null || _active.Contains(probe)) {
                return;
            }
            _active.Add(probe);
        }

        /// <summary>解除探针登记</summary>
        public static void Detach(object probe) {
            if (probe != null) {
                _active.Remove(probe);
            }
        }

        /// <summary>清空所有探针；仅由框架卸载阶段调用</summary>
        internal static void ClearAll() {
            _active.Clear();
        }
    }
}
