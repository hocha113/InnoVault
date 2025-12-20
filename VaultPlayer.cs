using InnoVault.GameSystem;
using InnoVault.TileProcessors;
using InnoVault.UIHandles;
using Terraria.ModLoader;

namespace InnoVault
{
    internal sealed class VaultPlayer : ModPlayer
    {
        public override void OnEnterWorld() {
            UIHandleLoader.OnEnterWorld();
            NPCOverride.OnEnterWorldNetwork();
            TileProcessorNetWork.ClientRequest_TPData_Send();
        }
    }
}
