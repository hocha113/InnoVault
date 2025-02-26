using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;

namespace InnoVault.TileProcessors
{
    /// <summary>
    /// TP系统的全局逻辑挂载处，管理世界加载、卸载、逻辑更新、绘制更新等行为
    /// </summary>
    public sealed class TileProcessorSystem : ModSystem
    {
        //首先我们要明白一点，在多人模式的情况下，只有服务器会加载这两个钩子，其他客户端并不会正常运行
        //所以，如果想数据正常加载，就需要发一个巨大的数据包来让其他的端同步，Save的时候要保证世界数据同步，而Load的时候要保证其他端也被加载
        /// <inheritdoc/>
        public override void SaveWorldData(TagCompound tag) => TileProcessorLoader.SaveWorldData(tag);
        /// <inheritdoc/>
        public override void LoadWorldData(TagCompound tag) => TileProcessorLoader.ActiveWorldTagData = tag;
        /// <inheritdoc/>
        public override void OnWorldUnload() {
            foreach (TileProcessor tpInds in TileProcessorLoader.TP_InWorld) {
                if (!tpInds.Active) {
                    continue;
                }
                tpInds.UnLoadInWorld();
            }
        }

        internal static bool TileProcessorIsDead(TileProcessor tileProcessor) {
            bool isDead = tileProcessor.IsDaed();

            foreach (var tpGlobal in TileProcessorLoader.TPGlobalHooks) {
                bool? reset = tpGlobal.IsDaed(tileProcessor);
                if (reset.HasValue) {
                    isDead = reset.Value;
                }
            }

            if (isDead) {

                tileProcessor.Kill();

                foreach (var tpGlobal in TileProcessorLoader.TPGlobalHooks) {
                    tpGlobal.OnKill(tileProcessor);
                }

                return true;
            }
            return false;
        }

        internal static void TileProcessorUpdate(TileProcessor tileProcessor) {
            bool reset = true;
            foreach (var tpGlobal in TileProcessorLoader.TPGlobalHooks) {
                reset = tpGlobal.PreUpdate(tileProcessor);
            }

            if (reset) {
                tileProcessor.Update();
            }

            foreach (var tpGlobal in TileProcessorLoader.TPGlobalHooks) {
                tpGlobal.PostUpdate(tileProcessor);
            }
        }

        internal static void TileProcessorDraw(TileProcessor tileProcessor) {
            bool reset = true;
            foreach (var tpGlobal in TileProcessorLoader.TPGlobalHooks) {
                reset = tpGlobal.PreDraw(tileProcessor, Main.spriteBatch);
            }

            if (reset) {
                tileProcessor.Draw(Main.spriteBatch);
            }

            foreach (var tpGlobal in TileProcessorLoader.TPGlobalHooks) {
                tpGlobal.PostDraw(tileProcessor, Main.spriteBatch);
            }

            TileProcessorBoxSizeDraw(tileProcessor);
        }

        /// <summary>
        /// 绘制这个TP实体的调试框
        /// </summary>
        /// <param name="tileProcessor"></param>
        public static void TileProcessorBoxSizeDraw(TileProcessor tileProcessor) {
            if (VaultClientConfig.Instance.TileProcessorBoxSizeDraw) {
                Vector2 drawPos = tileProcessor.PosInWorld - Main.screenPosition;

                Main.EntitySpriteDraw(VaultAsset.placeholder2.Value
                    , drawPos, Vector2.Zero.GetRectangle(tileProcessor.Size)
                    , Color.OrangeRed * 0.3f, 0, Vector2.Zero, 1, SpriteEffects.None, 0);

                Main.EntitySpriteDraw(VaultAsset.placeholder2.Value, drawPos
                , new Rectangle(0, 0, 16, 16), Color.Red * 0.6f, 0, Vector2.Zero, 1, SpriteEffects.None, 0);

                Utils.DrawBorderStringFourWay(Main.spriteBatch, FontAssets.ItemStack.Value, tileProcessor.ToString()
                    , drawPos.X + 0, drawPos.Y - 70, Color.AliceBlue, Color.Black, Vector2.Zero, 1f);
            }
        }

        /// <inheritdoc/>
        public override void PostUpdateEverything() {
            if (TileProcessorLoader.TP_InWorld.Count <= 0) {
                return;
            }

            foreach (TileProcessor tileProcessor in TileProcessorLoader.TP_Instances) {
                TileProcessorLoader.TP_ID_To_InWorld_Count[tileProcessor.ID] = 0;
            }

            foreach (TileProcessor tileProcessor in TileProcessorLoader.TP_InWorld) {
                if (!tileProcessor.Active) {
                    continue;
                }

                TileProcessorLoader.TP_ID_To_InWorld_Count[tileProcessor.ID]++;

                if (TileProcessorIsDead(tileProcessor)) {
                    continue;
                }

                TileProcessorUpdate(tileProcessor);
            }

            foreach (TileProcessor tpInds in TileProcessorLoader.TP_Instances) {
                if (tpInds.GetInWorldHasNum() > 0) {
                    tpInds.SingleInstanceUpdate();
                }
            }
        }

        /// <inheritdoc/>
        public override void PostDrawTiles() {
            if (TileProcessorLoader.TP_InWorld.Count <= 0) {
                return;
            }

            Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, Main.DefaultSamplerState
                , DepthStencilState.None, Main.Rasterizer, null, Main.GameViewMatrix.TransformationMatrix);

            foreach (TileProcessor tileProcessor in TileProcessorLoader.TP_InWorld) {
                if (!tileProcessor.Active) {
                    continue;
                }

                TileProcessorDraw(tileProcessor);
            }

            Main.spriteBatch.End();
        }
    }
}
