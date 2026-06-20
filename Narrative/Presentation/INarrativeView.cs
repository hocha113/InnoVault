namespace InnoVault.Narrative
{
    /// <summary>
    /// 叙事视图契约。<see cref="NarrativeRunner"/> 每帧把当前会话同步给所有已注册视图，<br/>
    /// 视图据此决定自身的开 / 关与渲染。框架内置三个默认视图，消费者也可注册自定义视图
    /// </summary>
    public interface INarrativeView
    {
        /// <summary>同步当前活动会话（<see langword="null"/> 表示当前无会话）</summary>
        void Sync(NarrativeSession active);
    }
}
