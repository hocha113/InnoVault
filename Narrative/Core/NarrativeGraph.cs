using System;
using System.Collections.Generic;

namespace InnoVault.Narrative.Core
{
    /// <summary>
    /// 一段叙事内容的结构：有序节点 + 标签索引。<br/>
    /// 标签使得"返回主菜单 / 跳转分支"可以在同一张图内声明式表达，<br/>
    /// 取代旧实现中为每个分支单独建立嵌套场景类的繁琐写法
    /// </summary>
    public sealed class NarrativeGraph
    {
        private readonly List<NarrativeNode> _nodes = [];
        private readonly Dictionary<string, int> _labelToIndex = new(StringComparer.Ordinal);

        /// <summary>节点数量</summary>
        public int Count => _nodes.Count;

        /// <summary>只读节点列表</summary>
        public IReadOnlyList<NarrativeNode> Nodes => _nodes;

        /// <summary>按下标获取节点，越界返回 <see langword="null"/></summary>
        public NarrativeNode Get(int index) => index >= 0 && index < _nodes.Count ? _nodes[index] : null;

        /// <summary>
        /// 追加一个节点。若节点带有 <see cref="NarrativeNode.Label"/>，会登记到标签索引（重复标签后者覆盖前者）
        /// </summary>
        public NarrativeGraph Add(NarrativeNode node) {
            if (node == null) {
                return this;
            }
            int index = _nodes.Count;
            _nodes.Add(node);
            if (!string.IsNullOrEmpty(node.Label)) {
                _labelToIndex[node.Label] = index;
            }
            return this;
        }

        /// <summary>尝试解析标签到节点下标</summary>
        public bool TryGetLabelIndex(string label, out int index) {
            if (string.IsNullOrEmpty(label)) {
                index = -1;
                return false;
            }
            return _labelToIndex.TryGetValue(label, out index);
        }

        /// <summary>清空图（场景每次启动会重建）</summary>
        public void Clear() {
            _nodes.Clear();
            _labelToIndex.Clear();
        }
    }
}
