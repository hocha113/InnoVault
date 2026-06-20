using InnoVault.Narrative.Progress;

namespace InnoVault.Narrative.Runtime
{
    /// <summary>
    /// 会话的运行期阶段。与存档层的 <see cref="ScenarioProgress"/> 分工：<br/>
    /// 这里描述"此刻在播什么"，而 <see cref="ScenarioProgress"/> 描述"这个场景到底有没有看完"
    /// </summary>
    public enum NarrativeSessionPhase
    {
        /// <summary>尚未开始 / 已结束的空闲态</summary>
        Inactive,
        /// <summary>正在播放对话（打字、等待推进、限时、等待节点等）</summary>
        Playing,
        /// <summary>等待玩家做出选择</summary>
        AwaitingChoice,
        /// <summary>被阻塞弹窗挂起，等待其领取 / 关闭</summary>
        AwaitingPopup,
        /// <summary>已完整播放完毕</summary>
        Completed,
        /// <summary>被中止</summary>
        Aborted,
    }
}
