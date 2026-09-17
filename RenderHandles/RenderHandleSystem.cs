using System;
using Terraria;
using Terraria.ModLoader;

namespace InnoVault.RenderHandles
{
    internal sealed class RenderHandleSystem : ModSystem
    {
        //是否身处一个尚未发出 OnWorldUnload 的世界：在世界内每帧置真，分发过卸载后置假
        //tML 的 OnWorldUnload 只在 SaveAndQuit / 断线时触发，子世界切换只走 ClearWorld，两条路都要覆盖且只发一次
        private static bool worldUnloadPending;

        public override void PostUpdateEverything() {
            if (VaultUtils.isServer) {
                return;
            }

            worldUnloadPending = true;
            for (int i = 0; i < RenderHandle.Instances.Count; i++) {
                RenderHandle.Instances[i].UpdateBySystem(i);
                if (RenderHandle.Instances[i].ignoreBug > 0) {
                    RenderHandle.Instances[i].ignoreBug--;
                }
            }
        }

        public override void PostDrawTiles() {
            if (RenderHandle.Instances.Count == 0) {
                return;
            }

            RenderHandleLoader.EnsureScreenSwap();
            RenderHandleLoader.DrawBatch("DrawAfterTiles", false
                , static render => render.DrawAfterTiles(Main.spriteBatch, Main.instance.GraphicsDevice, RenderHandleLoader.ScreenSwap));
        }

        public override void OnWorldLoad() {
            if (VaultUtils.isServer) {
                return;
            }
            worldUnloadPending = true;
            DispatchWorldHook(static render => render.OnWorldLoad(), "OnWorldLoad");
        }

        public override void OnWorldUnload() => DispatchWorldUnload();

        //子世界切换与进入新世界前的 WorldGen.clearWorld 只走这里，没发过卸载的世界在此补发
        public override void ClearWorld() {
            if (worldUnloadPending) {
                DispatchWorldUnload();
            }
        }

        private static void DispatchWorldUnload() {
            if (VaultUtils.isServer) {
                return;
            }
            worldUnloadPending = false;
            DispatchWorldHook(static render => render.OnWorldUnload(), "OnWorldUnload");
        }

        //世界钩子逐实例隔离异常：一个实例复位失败不该拖垮其余实例，也不该打断进出世界流程
        private static void DispatchWorldHook(Action<RenderHandle> action, string stage) {
            foreach (var render in RenderHandle.Instances) {
                try {
                    action(render);
                } catch (Exception ex) {
                    render.errorCount++;
                    VaultMod.LoggerError($"[RenderHandleSystem:{render}{stage}]", $"Stage [{stage}] failed: {ex.Message}. errorCount={render.errorCount}");
                }
            }
        }
    }
}
