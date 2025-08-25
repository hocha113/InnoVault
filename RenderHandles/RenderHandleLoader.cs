using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using Terraria;
using Terraria.Graphics.Effects;

namespace InnoVault.RenderHandles
{
    /// <summary>
    /// 所有关于渲染实例的钩子均挂载于此处
    /// </summary>
    public sealed class RenderHandleLoader : IVaultLoader
    {
        /// <summary>
        /// 一个主动给予和自动维护的中间屏幕对象，作用类似于 <see cref="Main.screenTargetSwap"/> ，
        /// 如果需要实际修改画面，请使用 <see cref="Main.screenTarget"/>
        /// </summary>
        public static RenderTarget2D ScreenSwap { get; set; }
        void IVaultLoader.LoadData() {
            On_FilterManager.EndCapture += FilterManager_EndCapture;
            Main.OnResolutionChanged += Main_OnResolutionChanged;
            On_Main.DrawDust += EndDraw;
        }
        void IVaultLoader.UnLoadData() {
            On_FilterManager.EndCapture -= FilterManager_EndCapture;
            Main.OnResolutionChanged -= Main_OnResolutionChanged;
            On_Main.DrawDust -= EndDraw;

            if (VaultUtils.isServer) {
                return;
            }

            Main.QueueMainThreadAction(() => {
                DisposeScreen();
                foreach (var render in RenderHandle.Instances) {
                    render.DisposeRenderTargets();
                    render.InitializeScreenTargets(create: false);
                }

                RenderHandle.Instances?.Clear();
            });
        }

        private void Main_OnResolutionChanged(Vector2 screenSize) {
            DisposeScreen();
            ScreenSwap = new RenderTarget2D(Main.instance.GraphicsDevice, Main.screenWidth, Main.screenHeight);
            foreach (var render in RenderHandle.Instances) {
                render.InitializeScreenTargets(create: true);
                render.OnResolutionChanged(screenSize);
            }
        }

        //确保旧的RenderTarget2D对象被正确释放
        private static void DisposeScreen() {
            ScreenSwap?.Dispose();
            ScreenSwap = null;
        }

        private static void FilterManager_EndCapture(On_FilterManager.orig_EndCapture orig
            , FilterManager filterManager
            , RenderTarget2D finalTexture
            , RenderTarget2D screenTarget1
            , RenderTarget2D screenTarget2
            , Color clearColor) {

            if (RenderHandle.Instances?.Count == 0) {//列表为空的话直接返回
                orig.Invoke(filterManager, finalTexture, screenTarget1, screenTarget2, clearColor);
                return;
            }

            if (ScreenSwap == null) {
                ScreenSwap = new RenderTarget2D(Main.instance.GraphicsDevice, Main.screenWidth, Main.screenHeight);
                foreach (var render in RenderHandle.Instances) {
                    render.InitializeScreenTargets(create: true);
                }
            }

            foreach (var render in RenderHandle.Instances) {
                render.filterManager = filterManager;
                render.finalTexture = finalTexture;
                render.screenTarget1 = screenTarget1;
                render.screenTarget2 = screenTarget2;
                if (render.ignoreBug > 0) {
                    render.ignoreBug--;
                }
            }

            if (!Main.gameMenu) {
                foreach (var render in RenderHandle.Instances) {
                    HandleRenderAction(render, "EndCaptureDraw", () =>
                        render.EndCaptureDraw(Main.spriteBatch, Main.instance.GraphicsDevice, ScreenSwap)
                    );
                }
            }

            foreach (var render in RenderHandle.Instances) {
                HandleRenderAction(render, "PostEndCaptureDraw", () =>
                    render.PostEndCaptureDraw(Main.spriteBatch, Main.instance.GraphicsDevice, ScreenSwap)
                );
            }

            orig.Invoke(filterManager, finalTexture, screenTarget1, screenTarget2, clearColor);
        }

        private void EndDraw(On_Main.orig_DrawDust orig, Main main) {
            orig(main);
            if (Main.gameMenu) {
                return;
            }
            foreach (var render in RenderHandle.Instances) {
                HandleRenderAction(render, "EndEntityDraw", () =>
                    render.EndEntityDraw(Main.spriteBatch, main)
                );
            }
        }

        private static void HandleRenderAction(RenderHandle render, string stage, Action action) {
            if (render.ignoreBug > 0) {
                return;
            }

            try {
                action();
            } catch (Exception ex) {
                render.ignoreBug = 60;//暂时屏蔽一段时间，避免帧帧报错
                render.errorCount++;
                string message = $"Stage [{stage}] failed: {ex.Message}. errorCount={render.errorCount}";
                VaultMod.LoggerError($"[RenderHandleLoader:{render}{stage}]", message);
                VaultUtils.Text(message, Color.Red);
            }
        }
    }
}
