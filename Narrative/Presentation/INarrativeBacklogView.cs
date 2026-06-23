namespace InnoVault.Narrative.Presentation
{
    /// <summary>
    /// Backlog（历史对话）视图契约。与逐帧广播的 <see cref="INarrativeView"/> 不同，<br/>
    /// backlog 由玩家主动开关，因此通过 <see cref="History.NarrativeHistory"/> 注册一个当前生效的视图，<br/>
    /// 由其统一接收开 / 关 / 切换请求。框架内置一个默认实现，消费者可关闭默认并注册自定义视图
    /// </summary>
    public interface INarrativeBacklogView
    {
        /// <summary>打开 backlog 面板</summary>
        void Open();
        /// <summary>关闭 backlog 面板</summary>
        void Close();
        /// <summary>在开 / 关之间切换</summary>
        void Toggle();
        /// <summary>当前是否处于打开状态（含淡入淡出过渡可由实现自行决定）</summary>
        bool IsOpen { get; }
    }
}
