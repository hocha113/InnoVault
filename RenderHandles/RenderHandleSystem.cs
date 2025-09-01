using Terraria.Graphics.Effects;
using Terraria.ModLoader;

namespace InnoVault.RenderHandles
{
    internal class RenderHandleSystem : ModSystem
    {
        public override void PostUpdateEverything() {
            if (VaultUtils.isServer) {
                return;
            }

            for (int i = 0; i < RenderHandle.Instances.Count; i++) {
                RenderHandle.Instances[i].UpdateBySystem(i);
                if (RenderHandle.Instances[i].ignoreBug > 0) {
                    RenderHandle.Instances[i].ignoreBug--;
                }
            }
        }
    }
}
