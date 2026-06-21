namespace InnoVault.Narrative.Presentation.Popups
{
    /// <summary>功能弹窗的展示阶段</summary>
    public enum PopupPresentationPhase
    {
        /// <summary>入场动画阶段</summary>
        Appearing,
        /// <summary>稳定展示阶段</summary>
        Holding,
        /// <summary>退场动画阶段</summary>
        Closing,
    }

    /// <summary>功能弹窗单帧展示状态</summary>
    public sealed class PopupPresentationState
    {
        /// <summary>当前展示阶段</summary>
        public PopupPresentationPhase Phase;
        /// <summary>当前阶段已持续的帧数</summary>
        public float Timer;
        /// <summary>面板整体透明度 0~1</summary>
        public float Alpha;
        /// <summary>面板缩放倍率</summary>
        public float Scale = 1f;
        /// <summary>图标与标题的出现进度 0~1</summary>
        public float Appear;
        /// <summary>鼠标是否悬停在面板上</summary>
        public bool Hover;

        /// <summary>重置为入场初始状态</summary>
        public void Reset() {
            Phase = PopupPresentationPhase.Appearing;
            Timer = 0f;
            Alpha = 0f;
            Scale = 1f;
            Appear = 0f;
            Hover = false;
        }
    }
}
