using InnoVault.TileProcessors;
using Terraria.ModLoader;

namespace InnoVault
{
    internal class VaultPlayer : ModPlayer
    {
        public override void OnEnterWorld() {
            TileProcessorNetWork.ClientRequest_TPData_Send(true);
        }
    }
}
