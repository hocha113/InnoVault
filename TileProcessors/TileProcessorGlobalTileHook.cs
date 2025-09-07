using Terraria;
using Terraria.DataStructures;
using Terraria.ModLoader;

namespace InnoVault.TileProcessors
{
    internal class TileProcessorGlobalTileHook : GlobalTile
    {
        public override void PlaceInWorld(int i, int j, int type, Item item) {
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
