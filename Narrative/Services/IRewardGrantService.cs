using Terraria;

namespace InnoVault.Narrative
{
    /// <summary>
    /// 奖励发放服务，由消费模组实现并注入。框架在弹窗被领取时调用它，<br/>
    /// 自身从不直接发放物品，从而把"展示奖励"与"业务发放（含可能的多人逻辑）"彻底解耦
    /// </summary>
    public interface IRewardGrantService
    {
        /// <summary>实际发放一个奖励给玩家</summary>
        void Grant(RewardPayload reward, Player player);
    }
}
