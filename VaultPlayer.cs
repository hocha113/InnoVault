using InnoVault.Dimensions;
using InnoVault.GameSystem;
using InnoVault.TileProcessors;
using InnoVault.UIHandles;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;

namespace InnoVault
{
    internal sealed class VaultPlayer : ModPlayer
    {
        public override void OnEnterWorld() {
            //确保玩家进入世界时维度状态是干净的
            //如果玩家在维度中退出游戏，重新进入时应该在主世界
            if (DimensionLoader.Current != null) {
                VaultMod.Instance.Logger.Warn("Dimension state was not properly cleared. Resetting to main world.");
                //这里不调用维度的钩子，因为这是异常状态的修正
                DimensionLoader.currentDimension = null;
                DimensionLoader.cachedDimension = null;
            }

            UIHandleLoader.OnEnterWorld();
            NPCOverride.OnEnterWorldNetwork();
            TileProcessorNetWork.ClientRequest_TPData_Send();
        }

        public override void SaveData(TagCompound tag) {
            UIHandleLoader.SaveUIData(tag);
        }

        public override void LoadData(TagCompound tag) {
            UIHandleLoader.LoadUIData(tag);
        }
    }
}
