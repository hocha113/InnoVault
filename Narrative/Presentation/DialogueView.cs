using InnoVault.Narrative.Presentation.Dialogue;

namespace InnoVault.Narrative.Presentation
{
    /// <summary>
    /// 框架内置默认对话框视图。复杂 consumer 可以关闭默认视图并注册自己的
    /// <see cref="NarrativeDialogueViewBase{TSelf}"/> 派生实现。
    /// </summary>
    public sealed class DialogueView : NarrativeDialogueViewBase<DialogueView>
    {
        /// <inheritdoc/>
        protected override bool IsDefaultDialogueView => true;
    }
}
