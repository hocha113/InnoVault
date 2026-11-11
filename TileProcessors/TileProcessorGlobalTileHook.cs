using Terraria;
using Terraria.DataStructures;
using Terraria.ModLoader;

namespace InnoVault.TileProcessors
{
    internal class TileProcessorGlobalTileHook : GlobalTile
    {
        public override void PlaceInWorld(int i, int j, int type, Item item) {
            //这个钩子对玩家放下的每一块物块都会触发，没有TP以它为目标的物块既建不出实体，也不该占用放置同步包
            if (!TileProcessorLoader.TargetTileTypes.Contains(type)) {
                return;
            }
            if (!TileProcessorLoader.TileProcessorSafeGetTopLeft(i, j, out Point16 point)) {
                return;
            }
            TileProcessorLoader.AddInWorld(type, point, item);
            if (!VaultUtils.isClient) {
                return;
            }
            TileProcessorNetWork.PlaceInWorldNetSend(Mod, type, point);
        }
    }
}
