using InnoVault.Narrative.Core;
using InnoVault.Narrative.Services;
using Terraria;

namespace InnoVault.Narrative.Demo
{
    /// <summary>
    /// 仅供 Narrative 演示使用的奖励发放服务。真实消费者应实现自己的 <see cref="IRewardGrantService"/>，<br/>
    /// 这里只是为了让内置演示在没有任何消费者注入时也能真正给出物品
    /// </summary>
    internal sealed class DemoRewardGrantService : IRewardGrantService
    {
        public void Grant(RewardPayload reward, Player player) {
            if (reward == null || player == null || !player.active || reward.ItemType <= 0) {
                return;
            }
            player.QuickSpawnItem(player.GetSource_Misc("InnoVault:NarrativeDemo"), reward.ItemType, reward.Stack);
        }
    }
}
