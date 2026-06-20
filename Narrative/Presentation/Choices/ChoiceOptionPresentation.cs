namespace InnoVault.Narrative.Presentation.Choices
{
    /// <summary>选择框单个选项的展示态快照。</summary>
    public readonly struct ChoiceOptionPresentation(string text, bool enabled, string disabledHint)
    {
        public readonly string Text = text;
        public readonly bool Enabled = enabled;
        public readonly string DisabledHint = disabledHint;
    }
}
