using InnoVault.TileProcessors;
using Terraria.ModLoader;

namespace InnoVault
{
    internal class VaultPlayer : ModPlayer
    {
        public override void OnEnterWorld() {
            TileProcessorLoader.ClientRequest_TPData_Send();
        }
    }
}
