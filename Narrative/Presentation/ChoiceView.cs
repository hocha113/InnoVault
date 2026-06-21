using InnoVault.Narrative.Presentation.Choices;

namespace InnoVault.Narrative.Presentation
{
    /// <summary>
    /// 框架内置默认选择框视图。复杂 consumer 可以关闭默认视图并注册自己的
    /// <see cref="NarrativeChoiceViewBase{TSelf}"/> 派生实现
    /// </summary>
    public sealed class ChoiceView : NarrativeChoiceViewBase<ChoiceView>
    {
        /// <inheritdoc/>
        protected override bool IsDefaultChoiceView => true;
    }
}
