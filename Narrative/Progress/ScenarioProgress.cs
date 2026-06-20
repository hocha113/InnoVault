namespace InnoVault.Narrative
{
    /// <summary>
    /// 场景进度状态。关键点是把"触发"与"完成"分成不同阶段，<br/>
    /// 避免旧实现中"场景刚启动（甚至只是入队）就写完成标记"导致的剧情 / 奖励被永久跳过
    /// </summary>
    public enum ScenarioProgress
    {
        /// <summary>从未触发</summary>
        None = 0,
        /// <summary>已触发（开始播放或已入队），但尚未真正完成</summary>
        Triggered = 1,
        /// <summary>正在播放</summary>
        Started = 2,
        /// <summary>已完整播放完毕（含必要的阻塞弹窗领取）</summary>
        Completed = 3,
        /// <summary>被中途中止（例如世界切换 / 强制关闭）</summary>
        Aborted = 4,
    }
}
