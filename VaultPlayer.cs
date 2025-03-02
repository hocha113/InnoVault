using InnoVault.TileProcessors;
using Terraria.ModLoader;

namespace InnoVault
{
    internal class VaultPlayer : ModPlayer
    {
        public static int ClientTPWaitsTime = -1;
        public override void OnEnterWorld() {
            ClientTPWaitsTime = 60;
            TileProcessorNetWork.ClientRequest_TPData_Send(true);
        }

        public override void PostUpdate() {
            if (ClientTPWaitsTime > 0) {
                ClientTPWaitsTime--;
            }
        }
    }
}
