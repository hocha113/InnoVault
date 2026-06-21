namespace InnoVault.Narrative.Presentation.Choices
{
    /// <summary>选择框单个选项的展示态快照</summary>
    public readonly struct ChoiceOptionPresentation(string text, bool enabled, string disabledHint)
    {
        /// <summary>选项显示文本</summary>
        public readonly string Text = text;
        /// <summary>该选项是否可点击</summary>
        public readonly bool Enabled = enabled;
        /// <summary>禁用时显示的提示文案</summary>
        public readonly string DisabledHint = disabledHint;
    }
}
