using InnoVault.Narrative.Core;

namespace InnoVault.Narrative.History
{
    /// <summary>
    /// 历史条目的种类。用显式标记区分"一句被展示的台词"与"玩家做出的一次选择"，<br/>
    /// 便于 backlog 视图按不同样式呈现（例如选择结果右对齐 / 加前缀）
    /// </summary>
    public enum NarrativeLogKind
    {
        /// <summary>一句被展示过的台词（Say 节点或选择提示句）</summary>
        Line,
        /// <summary>玩家选中的一个选项</summary>
        Choice,
    }

    /// <summary>
    /// 一条对话历史记录。它是<b>已发生</b>的展示快照，由 <see cref="Runtime.NarrativeSession"/> 在台词开始 / 选择解析时追加。<br/>
    /// 只保存稳定的语义 id（角色 / 表情 / 样式）与最终文本，<b>不</b>快照显示名与立绘——<br/>
    /// 这些由视图在展示时经 <see cref="Portraits.PortraitRegistry"/> 实时解析，保持运行时层与表现层解耦
    /// </summary>
    public readonly record struct NarrativeLogEntry(
        NarrativeLogKind Kind,
        string ScenarioKey,
        CharacterId Speaker,
        ExpressionId Expression,
        string Text,
        StyleId Style,
        bool StartsConversation)
    {
        /// <summary>构造一条台词记录</summary>
        public static NarrativeLogEntry Line(string scenarioKey, CharacterId speaker, ExpressionId expression, string text, StyleId style, bool startsConversation)
            => new(NarrativeLogKind.Line, scenarioKey, speaker, expression, text, style, startsConversation);

        /// <summary>构造一条选择结果记录（无说话者）</summary>
        public static NarrativeLogEntry Choice(string scenarioKey, string text, StyleId style, bool startsConversation)
            => new(NarrativeLogKind.Choice, scenarioKey, CharacterId.None, ExpressionId.Default, text, style, startsConversation);
    }
}
