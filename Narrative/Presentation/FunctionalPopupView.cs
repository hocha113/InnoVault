using InnoVault.Narrative.Presentation.Popups;

namespace InnoVault.Narrative.Presentation
{
    /// <summary>
    /// 框架内置默认功能弹窗视图。复杂 consumer 可以关闭默认视图并注册自己的
    /// <see cref="NarrativePopupViewBase{TSelf}"/> 派生实现。
    /// </summary>
    public sealed class FunctionalPopupView : NarrativePopupViewBase<FunctionalPopupView>
    {
        /// <inheritdoc/>
        protected override bool IsDefaultPopupView => true;
    }
}
