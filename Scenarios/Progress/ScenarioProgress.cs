namespace InnoVault.Scenarios.Progress
{
    /// <summary>
    /// 场景的进度：跨越多次激活、可以持久化的"这个场景走到哪一步了"。<see cref="ScenarioStatus"/> 描述的是单个实例此刻的生命周期，两者分工不同<br/>
    /// 取值与数值和 <see cref="Narrative.Progress.ScenarioProgress"/> 一一对应，叙事场景的进度是它的特例，两者可以直接按数值转换。
    /// 两个类型同名，在同时引用 <c>InnoVault.Scenarios.Progress</c> 与 <c>InnoVault.Narrative.Progress</c> 的文件里需要写全名或加别名
    /// </summary>
    public enum ScenarioProgress
    {
        /// <summary>从未触发</summary>
        None = 0,
        /// <summary>已被请求（例如正在等待世界切换完成），尚未真正开始</summary>
        Triggered = 1,
        /// <summary>已经开始（至少激活过一次）</summary>
        Started = 2,
        /// <summary>已完成；是否完成由场景或消费者判定，框架不会自行写入</summary>
        Completed = 3,
        /// <summary>被中途中止（例如切换失败、会话被强制结束）</summary>
        Aborted = 4,
    }
}
