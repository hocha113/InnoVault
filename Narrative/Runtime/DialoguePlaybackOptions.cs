namespace InnoVault.Narrative
{
    /// <summary>
    /// 对话播放参数：打字速度、自动播放、快进等。这些都是<b>运行时策略</b>，<br/>
    /// 由 <see cref="NarrativeSession"/> 统一推进，不散落到各 UI 控件
    /// </summary>
    public sealed class DialoguePlaybackOptions
    {
        /// <summary>普通模式每显示一个字符所需的 tick 数</summary>
        public float TicksPerChar { get; set; } = 2f;
        /// <summary>快进模式每显示一个字符所需的 tick 数</summary>
        public float FastTicksPerChar { get; set; } = 0.5f;
        /// <summary>自动播放模式</summary>
        public bool AutoMode { get; set; }
        /// <summary>快进模式</summary>
        public bool FastMode { get; set; }
        /// <summary>自动播放的基础等待 tick</summary>
        public float AutoBaseDelay { get; set; } = 75f;
        /// <summary>自动播放按字数追加的等待 tick</summary>
        public float AutoPerCharDelay { get; set; } = 1.4f;
        /// <summary>自动播放最大等待 tick</summary>
        public float AutoMaxDelay { get; set; } = 360f;
        /// <summary>快进模式段后自动推进延迟 tick</summary>
        public float FastAutoAdvanceDelay { get; set; } = 12f;

        /// <summary>计算某段落自动播放的等待 tick</summary>
        public float GetAutoDelay(int totalChars)
        {
            float delay = AutoBaseDelay + totalChars * AutoPerCharDelay;
            return delay < AutoMaxDelay ? delay : AutoMaxDelay;
        }
    }
}
