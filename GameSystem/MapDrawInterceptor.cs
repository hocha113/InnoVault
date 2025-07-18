using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Diagnostics;
using System.Reflection;
using Terraria;
using Terraria.Map;

namespace InnoVault.GameSystem
{
    internal class MapDrawInterceptor : IVaultLoader
    {
        private delegate void On_DrawToMap_Section_Delegate(Main main, int secX, int secY);
        void IVaultLoader.LoadData() {
            MethodInfo methodInfo = typeof(Main).GetMethod("DrawToMap_Section", BindingFlags.Instance | BindingFlags.NonPublic);
            VaultHook.Add(methodInfo, On_DrawToMap_Section_Hook);
        }

        private static bool CheckMap(int i, int j) {
            if (Main.instance.mapTarget[i, j] == null || Main.instance.mapTarget[i, j].IsDisposed) {
                Main.initMap[i, j] = false;
            }
            if (!Main.initMap[i, j]) {
                try {
                    int width = Main.textureMaxWidth;
                    int height = Main.textureMaxHeight;
                    if (i == Main.mapTargetX - 1) {
                        width = 400;
                    }
                    if (j == Main.mapTargetY - 1) {
                        height = 600;
                    }
                    Main.instance.mapTarget[i, j] = new RenderTarget2D(Main.instance.GraphicsDevice, width, height, mipMap: false
                        , Main.instance.GraphicsDevice.PresentationParameters.BackBufferFormat, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
                } catch {
                    Main.mapEnabled = false;
                    for (int k = 0; k < Main.mapTargetX; k++) {
                        for (int l = 0; l < Main.mapTargetY; l++) {
                            try {
                                Main.initMap[k, l] = false;
                                Main.instance.mapTarget[k, l].Dispose();
                            } catch {
                            }
                        }
                    }
                    return false;
                }
                Main.initMap[i, j] = true;
            }
            return true;
        }

        private static void On_DrawToMap_Section_Hook(On_DrawToMap_Section_Delegate orig, Main main, int secX, int secY) {
            Stopwatch stopwatch = Stopwatch.StartNew();
            Color[] mapColorCacheArray = new Color[30000];
            int num = secX * 200;
            int num2 = num + 200;
            int num3 = secY * 150;
            int num4 = num3 + 150;
            int num5 = num / Main.textureMaxWidth;
            int num6 = num3 / Main.textureMaxHeight;
            int num7 = num % Main.textureMaxWidth;
            int num8 = num3 % Main.textureMaxHeight;

            if (!CheckMap(num5, num6)) {
                return;
            }

            int num9 = 0;
            _ = Color.Transparent;

            for (int i = num3; i < num4; i++) {
                for (int j = num; j < num2; j++) {
                    if (j < 0 || j >= Main.maxTilesX || i < 0 || i >= Main.maxTilesY) {
                        continue;
                    }
                    MapTile mapTile = Main.Map[j, i];
                    mapColorCacheArray[num9] = MapHelper.GetMapTileXnaColor(ref mapTile);
                    num9++;
                }
            }

            try {
                Main.instance.GraphicsDevice.SetRenderTarget(Main.instance.mapTarget[num5, num6]);
            } catch (ObjectDisposedException) {
                Main.initMap[num5, num6] = false;
                return;
            }

            Main.spriteBatch.Begin(SpriteSortMode.Immediate, BlendState.AlphaBlend);
            _ = stopwatch.Elapsed.TotalMilliseconds;
            Main.instance.mapSectionTexture.SetData(mapColorCacheArray, 0, mapColorCacheArray.Length);
            _ = stopwatch.Elapsed.TotalMilliseconds;
            _ = stopwatch.Elapsed.TotalMilliseconds;
            Main.spriteBatch.Draw(Main.instance.mapSectionTexture, new Vector2(num7, num8), Color.White);
            Main.spriteBatch.End();
            Main.instance.GraphicsDevice.SetRenderTarget(null);
            _ = stopwatch.Elapsed.TotalMilliseconds;
            stopwatch.Stop();
        }
    }
}
