using InnoVault.Narrative.Presentation.Backlog;

namespace InnoVault.Narrative.Presentation
{
    /// <summary>
    /// 框架内置默认 backlog 视图。复杂 consumer 可关闭默认（<see cref="NarrativeViews.UseDefaultBacklogView"/>）
    /// 并注册自己的 <see cref="NarrativeBacklogViewBase{TSelf}"/> 派生实现
    /// </summary>
    public sealed class BacklogView : NarrativeBacklogViewBase<BacklogView>
    {
        /// <inheritdoc/>
        protected override bool IsDefaultBacklogView => true;
    }
}
