using System;

namespace InnoVault.Narrative
{
    /// <summary>
    /// 选项构建器，用于在 <see cref="NarrativeComposer.Choice(CharacterId, string, Action{ChoiceBuilder})"/> 中声明选项与限时行为
    /// </summary>
    public sealed class ChoiceBuilder
    {
        internal ChoiceNode Node { get; }

        internal ChoiceBuilder(ChoiceNode node)
        {
            Node = node;
        }

        /// <summary>添加一个选项</summary>
        /// <param name="id">稳定选项 id</param>
        /// <param name="text">显示文本</param>
        /// <param name="target">选择后的流程跳转，<see langword="null"/> 表示继续下一节点</param>
        /// <param name="onSelect">选择时的副作用回调</param>
        /// <param name="enabled">启用判定，<see langword="null"/> 表示始终启用</param>
        /// <param name="disabledHint">禁用提示</param>
        public ChoiceBuilder Option(ChoiceId id, string text, NarrativeTarget target = null,
            Action onSelect = null, Func<bool> enabled = null, string disabledHint = null)
        {
            Node.Options.Add(new ChoiceOption
            {
                Id = id,
                Text = text,
                Target = target ?? NarrativeTarget.Continue,
                OnSelect = onSelect,
                Enabled = enabled,
                DisabledHint = disabledHint,
            });
            return this;
        }

        /// <summary>设置该选择为限时选择</summary>
        /// <param name="seconds">限时秒数</param>
        /// <param name="defaultChoice">超时默认选择的选项 id，<see langword="null"/> 时超时随机选择一个可用项</param>
        public ChoiceBuilder Timed(float seconds, ChoiceId? defaultChoice = null)
        {
            Node.Timed = TimedSettings.Of(seconds);
            Node.DefaultChoice = defaultChoice;
            return this;
        }
    }
}
