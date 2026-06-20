namespace InnoVault.Narrative.Presentation.Popups
{
    /// <summary>功能弹窗的展示阶段。</summary>
    public enum PopupPresentationPhase
    {
        Appearing,
        Holding,
        Closing,
    }

    /// <summary>功能弹窗单帧展示状态。</summary>
    public sealed class PopupPresentationState
    {
        public PopupPresentationPhase Phase;
        public float Timer;
        public float Alpha;
        public float Scale = 1f;
        public bool Hover;

        public void Reset() {
            Phase = PopupPresentationPhase.Appearing;
            Timer = 0f;
            Alpha = 0f;
            Scale = 1f;
            Hover = false;
        }
    }
}
