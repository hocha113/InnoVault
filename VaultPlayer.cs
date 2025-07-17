using InnoVault.TileProcessors;
using InnoVault.UIHandles;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;

namespace InnoVault
{
    internal sealed class VaultPlayer : ModPlayer
    {
        public override void OnEnterWorld() {
            UIHandleLoader.OnEnterWorld();
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
