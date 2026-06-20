using InnoVault.Narrative.Progress;

namespace InnoVault.Narrative.Services
{
    /// <summary>
    /// 宿主服务聚合，便于消费模组一次性提供进度存储与奖励发放实现。<br/>
    /// 任一成员返回 <see langword="null"/> 时框架保留对应的默认实现
    /// </summary>
    public interface INarrativeHostService
    {
        /// <summary>叙事进度存储，<see langword="null"/> 时使用框架内置 <see cref="MemoryNarrativeProgressStore"/></summary>
        INarrativeProgressStore ProgressStore { get; }
        /// <summary>奖励发放服务，<see langword="null"/> 时奖励弹窗只展示不发放</summary>
        IRewardGrantService RewardGrant { get; }
    }
}
