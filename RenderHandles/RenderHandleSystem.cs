using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.ModLoader;

namespace InnoVault.RenderHandles
{
    internal sealed class RenderHandleSystem : ModSystem
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

        public override void PostDrawTiles() {
            RenderHandleLoader.EnsureScreenSwap();
            var gd = Main.instance.GraphicsDevice;

            Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, Main.DefaultSamplerState
                , DepthStencilState.None, Main.Rasterizer, null, Main.GameViewMatrix.TransformationMatrix);

            foreach (var render in RenderHandle.Instances) {
                RenderHandleLoader.HandleRenderAction(render, "DrawAfterTiles", () =>
                    render.DrawAfterTiles(Main.spriteBatch, gd, RenderHandleLoader.ScreenSwap)
                );
            }

            Main.spriteBatch.End();
        }
    }
}
