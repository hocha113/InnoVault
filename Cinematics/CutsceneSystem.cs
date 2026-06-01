using Terraria.ModLoader;

namespace InnoVault.Cinematics
{
    internal sealed class CutsceneSystem : ModSystem
    {
        public override void PostUpdateEverything() {
            if (VaultUtils.isServer) {
                return;
            }

            CutsceneDirector.Update();
        }

        public override void OnWorldUnload() {
            CutsceneDirector.Reset();
        }
    }
}
