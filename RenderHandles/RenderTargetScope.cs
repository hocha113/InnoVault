using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using Terraria;

namespace InnoVault.RenderHandles
{
    /// <summary>
    /// 帧中途切换到自有 RT 的作用域守卫：构造时绑定目标，<see cref="Dispose"/> 时还原原绑定并把主画面铺回<br/>
    /// 原版 <see cref="Main.screenTarget"/> 以 <see cref="RenderTargetUsage.DiscardContents"/> 创建，
    /// 解绑再重绑时 D3D11 后端会丢弃整帧已绘内容，表现为屏幕只剩本层与其后阶段。
    /// 因此切走前把当前被绑画面备份到 <see cref="RenderHandleLoader.ScreenSwap"/>，切回后再铺回<br/>
    /// 用法：<c>using (RenderHandleLoader.PushRenderTarget(gd, myTarget, Color.Transparent)) { 画进 myTarget }</c><br/>
    /// 构造与 <see cref="Dispose"/> 时 <see cref="Main.spriteBatch"/> 都必须处于非活跃状态；
    /// 不要把 <see cref="RenderHandleLoader.ScreenSwap"/> 本身当作目标传入，它是备份用的缓冲<br/>
    /// 备份或铺回失败只记日志，不会中断作用域内的绘制；绑定目标失败会先还原原绑定再抛出
    /// </summary>
    public readonly struct RenderTargetScope : IDisposable
    {
        private readonly GraphicsDevice device;
        private readonly RenderTargetBinding[] previousBindings;
        private readonly RenderTarget2D backup;

        /// <summary>
        /// 进入作用域前的绑定是否已成功备份，为 <see langword="false"/> 时 <see cref="Dispose"/> 后原画面可能丢失
        /// </summary>
        public bool HasBackup => backup != null;

        internal RenderTargetScope(GraphicsDevice device, RenderTarget2D target, Color? clearColor) {
            this.device = device;
            previousBindings = device.GetRenderTargets();
            backup = null;

            //只有被绑的是会丢内容的 RT 才值得备份，后备缓冲与 PreserveContents 的 RT 切回后内容不丢
            if (previousBindings != null && previousBindings.Length > 0
                && previousBindings[0].RenderTarget is RenderTarget2D bound
                && bound.RenderTargetUsage == RenderTargetUsage.DiscardContents
                && bound != target) {
                RenderHandleLoader.EnsureScreenSwap();
                RenderTarget2D swap = RenderHandleLoader.ScreenSwap;
                if (swap != null && !swap.IsDisposed && swap != target && swap != bound) {
                    try {
                        device.SetRenderTarget(swap);
                        Blit(bound);
                        backup = swap;
                    } catch (Exception ex) {
                        VaultMod.LoggerError("[RenderTargetScope]", $"Failed to back up the bound target: {ex.Message}");
                        backup = null;
                    }
                }
            }

            try {
                device.SetRenderTarget(target);
                if (clearColor.HasValue) {
                    device.Clear(clearColor.Value);
                }
            } catch {
                //目标绑不上就把绑定放回原处再抛，不让设备停在备份缓冲上
                Restore(device, previousBindings);
                throw;
            }
        }

        /// <summary>
        /// 还原进入作用域前的 RT 绑定，并把备份的主画面铺回
        /// </summary>
        public void Dispose() {
            if (device == null) {
                return;
            }

            Restore(device, previousBindings);

            if (backup != null && !backup.IsDisposed) {
                try {
                    Blit(backup);
                } catch (Exception ex) {
                    VaultMod.LoggerError("[RenderTargetScope]", $"Failed to restore the backed-up frame: {ex.Message}");
                }
            }
        }

        private static void Restore(GraphicsDevice device, RenderTargetBinding[] bindings) {
            if (bindings == null || bindings.Length == 0) {
                device.SetRenderTarget(null);
            }
            else {
                device.SetRenderTargets(bindings);
            }
        }

        private static void Blit(Texture2D source) {
            SpriteBatch spriteBatch = Main.spriteBatch;
            spriteBatch.Begin(SpriteSortMode.Immediate, BlendState.Opaque, SamplerState.PointClamp
                , DepthStencilState.None, RasterizerState.CullNone);
            spriteBatch.Draw(source, Vector2.Zero, Color.White);
            spriteBatch.End();
        }
    }
}
