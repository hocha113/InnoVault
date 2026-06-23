using InnoVault.Narrative.History;
using InnoVault.Narrative.Progress;

namespace InnoVault.Narrative.Services
{
    /// <summary>
    /// 叙事框架的服务定位入口。框架内部只依赖这些接口，<br/>
    /// 消费模组通过设置这些属性（或 <see cref="UseHost"/>）注入自己的实现<br/>
    /// 默认情况下进度走内存实现、奖励不发放（只展示），保证框架单独可运行
    /// </summary>
    public static class NarrativeServices
    {
        /// <summary>叙事进度存储，默认内存实现</summary>
        public static INarrativeProgressStore Progress { get; set; } = new MemoryNarrativeProgressStore();

        /// <summary>对话历史存储，默认内存实现（落盘由框架 <c>NarrativeHistorySave</c> 承担）</summary>
        public static INarrativeHistoryStore History { get; set; } = new MemoryNarrativeHistoryStore();

        /// <summary>奖励发放服务，默认 <see langword="null"/>（奖励弹窗只展示不发放）</summary>
        public static IRewardGrantService RewardGrant { get; set; }

        /// <summary>多人同步服务，默认 <see langword="null"/>（仅本地）</summary>
        public static INarrativeSyncService Sync { get; set; }

        /// <summary>用一个聚合宿主服务一次性注入进度存储与奖励发放</summary>
        public static void UseHost(INarrativeHostService host) {
            if (host == null) {
                return;
            }
            if (host.ProgressStore != null) {
                Progress = host.ProgressStore;
            }
            if (host.RewardGrant != null) {
                RewardGrant = host.RewardGrant;
            }
        }

        /// <summary>恢复为默认实现（卸载 / 测试时使用）</summary>
        internal static void ResetToDefaults() {
            Progress = new MemoryNarrativeProgressStore();
            History = new MemoryNarrativeHistoryStore();
            RewardGrant = null;
            Sync = null;
        }
    }
}
