using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader;

namespace InnoVault.GameSystem
{
    /// <summary>
    /// 物品使用动画框架的全局派发与卸载清理挂载点<br/>
    /// 作为 <see cref="GlobalItem"/> 在所有物品的使用动画回调中查表并转发到对应的 <see cref="ItemUseAnimation"/>，
    /// 未注册动画的物品完全不受影响；作为 <see cref="IVaultLoader"/> 在卸载时清空静态表
    /// </summary>
    public class ItemUseAnimationLoader : GlobalItem, IVaultLoader
    {
        void IVaultLoader.UnLoadData() {
            ItemUseAnimation.Instances?.Clear();
            ItemUseAnimation.TypeToInstance?.Clear();
            ItemUseAnimation.ByID?.Clear();
            ItemUseAnimation.ExplicitByID?.Clear();
        }

        /// <inheritdoc/>
        public override void UseStyle(Item item, Player player, Rectangle heldItemFrame) {
            if (ItemUseAnimation.TryGetByID(item.type, out ItemUseAnimation animation) && animation.CanRun(item, player)) {
                animation.ApplyUseStyle(item, player, heldItemFrame);
            }
        }

        /// <inheritdoc/>
        public override void UseItemFrame(Item item, Player player) {
            if (ItemUseAnimation.TryGetByID(item.type, out ItemUseAnimation animation) && animation.CanRun(item, player)) {
                animation.ApplyUseItemFrame(item, player);
            }
        }
    }
}
