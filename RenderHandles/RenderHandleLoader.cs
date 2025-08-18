using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.Graphics.Effects;

namespace InnoVault.RenderHandles
{
    /// <summary>
    /// 所有关于渲染实例的钩子均挂载于此处
    /// </summary>
    public sealed class RenderHandleLoader : IVaultLoader
    {
        internal static RenderTarget2D screen;
        void IVaultLoader.LoadData() {
            On_FilterManager.EndCapture += FilterManager_EndCapture;
            Main.OnResolutionChanged += Main_OnResolutionChanged;
            On_Main.DrawDust += EndDraw;
        }
        void IVaultLoader.UnLoadData() {
            On_FilterManager.EndCapture -= FilterManager_EndCapture;
            Main.OnResolutionChanged -= Main_OnResolutionChanged;
            On_Main.DrawDust -= EndDraw;
        }

        private void Main_OnResolutionChanged(Vector2 screenPos) {
            DisposeScreen();
            screen = new RenderTarget2D(Main.graphics.GraphicsDevice, Main.screenWidth, Main.screenHeight);
            foreach (var render in RenderHandle.Instances) {
                render.OnResolutionChanged(screenPos);
            }
        }

        //确保旧的RenderTarget2D对象被正确释放
        private static void DisposeScreen() {
            screen?.Dispose();
            screen = null;
        }

        private static void FilterManager_EndCapture(On_FilterManager.orig_EndCapture orig
            , FilterManager filterManager
            , RenderTarget2D finalTexture
            , RenderTarget2D screenTarget1
            , RenderTarget2D screenTarget2
            , Color clearColor) {

            screen ??= new RenderTarget2D(Main.instance.GraphicsDevice, Main.screenWidth, Main.screenHeight);

            if (!Main.gameMenu) {
                foreach (var render in RenderHandle.Instances) {
                    render.filterManager = filterManager;
                    render.finalTexture = finalTexture;
                    render.screenTarget1 = screenTarget1;
                    render.screenTarget2 = screenTarget2;
                    render.EndCaptureDraw(screen);
                }
            }

            orig.Invoke(filterManager, finalTexture, screenTarget1, screenTarget2, clearColor);
        }

        private void EndDraw(On_Main.orig_DrawDust orig, Main main) {
            orig(main);
            if (Main.gameMenu) {
                return;
            }
            foreach (var render in RenderHandle.Instances) {
                render.EndEntityDraw(Main.spriteBatch, main);
            }
        }
    }
}
