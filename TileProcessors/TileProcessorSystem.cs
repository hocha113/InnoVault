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
        /// <inheritdoc/>
        public override void SaveWorldData(TagCompound tag) => TileProcessorLoader.SaveWorldData(tag);
        /// <inheritdoc/>
        public override void LoadWorldData(TagCompound tag) => TileProcessorLoader.ActiveWorldTagData = TileProcessorLoader.LoadTileProcessorIO();

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

        private static void TileProcessorBoxSizeDraw(TileProcessor tileProcessor) {
            if (VaultClientConfig.Instance.TileProcessorBoxSizeDraw) {
                Vector2 drawPos = tileProcessor.PosInWorld - Main.screenPosition;
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

                tileProcessor.Tile = VaultUtils.GetTile(tileProcessor.Position.X, tileProcessor.Position.Y);

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
