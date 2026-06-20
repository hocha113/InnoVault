namespace InnoVault.Narrative
{
    /// <summary>
    /// 可选的多人同步服务。框架默认只在本地播放叙事，进度也只在本地记录；<br/>
    /// 若消费模组需要在多人中共享剧情标记，可实现本接口并注入，<br/>
    /// 框架会在场景完成等关键节点回调它，由消费者决定如何走网络
    /// </summary>
    public interface INarrativeSyncService
    {
        /// <summary>当某场景进度发生变化且需要同步时调用</summary>
        void SyncProgress(string scenarioKey, ScenarioProgress progress);
    }
}
