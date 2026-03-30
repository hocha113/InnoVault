using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using Terraria;
using Terraria.Graphics;
using Terraria.Graphics.Effects;
using Terraria.Graphics.Renderers;

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
            On_Main.DrawDust += DrawDustHook;
            On_Main.DoDraw_WallsAndBlacks += DrawBeforeTilesHook;
            On_Main.DoDraw_DrawNPCsOverTiles += DrawNPCsOverTilesHook;
            On_LegacyPlayerRenderer.DrawPlayers += DrawPlayersHook;
        }

        void IVaultLoader.UnLoadData() {
            On_FilterManager.EndCapture -= FilterManager_EndCapture;
            Main.OnResolutionChanged -= Main_OnResolutionChanged;
            On_Main.DrawDust -= DrawDustHook;
            On_Main.DoDraw_WallsAndBlacks -= DrawBeforeTilesHook;
            On_Main.DoDraw_DrawNPCsOverTiles -= DrawNPCsOverTilesHook;
            On_LegacyPlayerRenderer.DrawPlayers -= DrawPlayersHook;

            if (VaultUtils.isServer) {
                return;
            }

            Main.QueueMainThreadAction(() => {
                DisposeScreen();
                foreach (var render in RenderHandle.Instances) {
                    render.DisposeScreenTargets();
                }

                RenderHandle.Instances?.Clear();
            });
        }

        private void Main_OnResolutionChanged(Vector2 screenSize) {
            DisposeScreen();
            ScreenSwap = new RenderTarget2D(Main.instance.GraphicsDevice, Main.screenWidth, Main.screenHeight);
            foreach (var render in RenderHandle.Instances) {
                render.CreateScreenTargets();
                render.OnResolutionChanged(screenSize);
            }
        }

        //确保旧的RenderTarget2D对象被正确释放
        private static void DisposeScreen() {
            ScreenSwap?.Dispose();
            ScreenSwap = null;
        }

        /// <summary>
        /// 确保 <see cref="ScreenSwap"/> 及各实例的 <see cref="RenderHandle.ScreenTargets"/> 已初始化，
        /// 任何绘制阶段均可安全调用
        /// </summary>
        internal static void EnsureScreenSwap() {
            if (ScreenSwap != null) {
                return;
            }

            ScreenSwap = new RenderTarget2D(Main.instance.GraphicsDevice, Main.screenWidth, Main.screenHeight);
            foreach (var render in RenderHandle.Instances) {
                render.CreateScreenTargets();
            }
        }

        #region EndCapture 阶段
        private static void FilterManager_EndCapture(On_FilterManager.orig_EndCapture orig
            , FilterManager filterManager
            , RenderTarget2D finalTexture
            , RenderTarget2D screenTarget1
            , RenderTarget2D screenTarget2
            , Color clearColor) {

            if (RenderHandle.Instances?.Count == 0) {
                orig.Invoke(filterManager, finalTexture, screenTarget1, screenTarget2, clearColor);
                return;
            }

            EnsureScreenSwap();

            foreach (var render in RenderHandle.Instances) {
                render.filterManager = filterManager;
                render.finalTexture = finalTexture;
                render.screenTarget1 = screenTarget1;
                render.screenTarget2 = screenTarget2;
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
        #endregion

        #region 分层绘制阶段
        private static void DrawBeforeTilesHook(On_Main.orig_DoDraw_WallsAndBlacks orig, Main self) {
            orig(self);

            if (Main.gameMenu) {
                return;
            }

            EnsureScreenSwap();
            var gd = Main.instance.GraphicsDevice;
            DrawBatch("DrawBeforeTiles", render => render.DrawBeforeTiles(Main.spriteBatch, gd, ScreenSwap));
        }

        private static void DrawNPCsOverTilesHook(On_Main.orig_DoDraw_DrawNPCsOverTiles orig, Main self) {
            if (Main.gameMenu) {
                orig(self);
                return;
            }

            EnsureScreenSwap();
            var gd = Main.instance.GraphicsDevice;
            DrawBatch("DrawNPCsOverTiles", render => render.DrawNPCsOverTiles(Main.spriteBatch, gd, ScreenSwap));

            orig(self);
        }

        private static void DrawPlayersHook(On_LegacyPlayerRenderer.orig_DrawPlayers orig, LegacyPlayerRenderer self, Camera camera, IEnumerable<Player> players) {
            EnsureScreenSwap();
            var gd = Main.instance.GraphicsDevice;
            DrawBatch("DrawBeforePlayers", render => render.DrawBeforePlayers(Main.spriteBatch, gd, ScreenSwap));

            orig(self, camera, players);

            DrawBatch("DrawAfterPlayers", render => render.DrawAfterPlayers(Main.spriteBatch, gd, ScreenSwap));
        }

        private void DrawDustHook(On_Main.orig_DrawDust orig, Main main) {
            orig(main);
            if (Main.gameMenu) {
                return;
            }

            EnsureScreenSwap();

            var gd = Main.instance.GraphicsDevice;
            DrawBatch("OldEndEntityDraw", render => render.EndEntityDraw(Main.spriteBatch, main));
            DrawBatch("EndEntityDraw", render => render.EndEntityDraw(Main.spriteBatch, main, gd, ScreenSwap));
            DrawBatch("DrawAfterEntities", render => render.DrawAfterEntities(Main.spriteBatch, gd, ScreenSwap));
        }
        #endregion

        #region SpriteBatch 辅助
        /// <summary>
        /// 在已有活跃的 SpriteBatch 环境中插入绘制（End → Begin → Draw → End → Begin）
        /// </summary>
        internal static void DrawBatch(string stage, Action<RenderHandle> drawAction) {
            bool any = false;
            foreach (var render in RenderHandle.Instances) {
                any = true;
                break;
            }
            if (!any) {
                return;
            }
            foreach (var render in RenderHandle.Instances) {
                HandleRenderAction(render, stage, () => drawAction(render));
            }
        }
        #endregion

        internal static void HandleRenderAction(RenderHandle render, string stage, Action action) {
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
