using InnoVault.Narrative.Core;
using Terraria;

namespace InnoVault.Narrative.Demo
{
    /// <summary>
    /// 演示专用的自包含奖励弹窗：领取时直接发放物品，不经过 <see cref="NarrativeServices"/>，<br/>
    /// 因此运行内置演示不会污染全局奖励服务。<br/>
    /// 真实消费者应改用 <c>n.Reward(...)</c> + 注入自己的 <see cref="IRewardGrantService"/>
    /// </summary>
    internal sealed class DemoRewardPayload : PopupPayload
    {
        /// <summary>物品类型</summary>
        public int ItemType { get; set; }
        /// <summary>数量</summary>
        public int Stack { get; set; } = 1;

        /// <inheritdoc/>
        public override int IconItemType => ItemType;

        /// <inheritdoc/>
        public override void OnClaimed(Player player)
        {
            if (player == null || !player.active || ItemType <= 0)
            {
                return;
            }
            player.QuickSpawnItem(player.GetSource_Misc("InnoVault:NarrativeDemo"), ItemType, Stack);
        }
    }
}
