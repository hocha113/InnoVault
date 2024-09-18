using Microsoft.Xna.Framework.Graphics;
using Terraria.GameContent;
using Terraria;
using Terraria.ModLoader;
using Microsoft.Xna.Framework;

namespace InnoVault.TileProcessors
{
    /// <summary>
    /// TP系统的全局逻辑挂载处，管理世界加载、卸载、逻辑更新、绘制更新等行为
    /// </summary>
    public class TileProcessorSystem : ModSystem
    {
        /// <inheritdoc/>
        public override void OnWorldUnload() {
            foreach (TileProcessor module in TileProcessorLoader.TP_InWorld) {
                if (!module.Active) {
                    continue;
                }
                module.UnLoadInWorld();
            }
        }
        /// <inheritdoc/>
        public override void PostUpdateEverything() {
            if (TileProcessorLoader.TP_InWorld.Count <= 0) {
                return;
            }

            foreach (TileProcessor module in TileProcessorLoader.TP_Instances) {
                TileProcessorLoader.TP_ID_To_InWorld_Count[module.ID] = 0;
            }

            foreach (TileProcessor module in TileProcessorLoader.TP_InWorld) {
                if (!module.Active) {
                    continue;
                }

                TileProcessorLoader.TP_ID_To_InWorld_Count[module.ID]++;

                module.Tile = VaultUtils.GetTile(module.Position.X, module.Position.Y);
                if (module.IsDaed()) {
                    module.Kill();
                    continue;
                }
                module.Update();
            }

            foreach (TileProcessor module in TileProcessorLoader.TP_Instances) {
                if (module.GetInWorldHasNum() > 0) {
                    module.SingleInstanceUpdate();
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

            foreach (TileProcessor module in TileProcessorLoader.TP_InWorld) {
                if (!module.Active) {
                    continue;
                }

                module.Draw(Main.spriteBatch);

                if (VaultClientConfig.Instance.TileOperatorBoxSizeDraw) {
                    Vector2 drawPos = module.PosInWorld - Main.screenPosition;
                    Main.EntitySpriteDraw(VaultAsset.placeholder2.Value, drawPos
                    , new Rectangle(0, 0, 16, 16), Color.Red * 0.6f, 0, Vector2.Zero, 1, SpriteEffects.None, 0);
                    Utils.DrawBorderStringFourWay(Main.spriteBatch, FontAssets.ItemStack.Value, module.ToString()
                        , drawPos.X + 0, drawPos.Y - 70, Color.AliceBlue, Color.Black, Vector2.Zero, 1f);
                }
            }

            Main.spriteBatch.End();
        }
    }
}
