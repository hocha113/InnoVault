using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Linq;
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

            //在多人游戏中，不允许客户端自行杀死Tp实体，这些要通过服务器的统一广播来管理
            if (isDead && !VaultUtils.isClient) {
                if (VaultUtils.isServer) {
                    TileProcessorNetWork.SendTPDeathByServer(tileProcessor);
                }

                tileProcessor.Kill();

                return true;
            }

            return false;
        }

        internal static void TileProcessorUpdate(TileProcessor tileProcessor) {
            if (!tileProcessor.Spwan) {
                tileProcessor.Initialize();
                foreach (var tpGlobal in TileProcessorLoader.TPGlobalHooks) {
                    tpGlobal.Initialize(tileProcessor);
                }
                if (VaultUtils.isClient && tileProcessor.PlaceNet && tileProcessor.TrackItem != null) {
                    tileProcessor.SendData();
                }
                tileProcessor.Spwan = true;
            }

            if (!VaultUtils.isSinglePlayer) {
                if (Main.GameUpdateCount % 60 == 0) {
                    tileProcessor.SendpacketCount = 0;
                }
                if (tileProcessor.SendCooldownTicks > 0) {
                    tileProcessor.SendCooldownTicks--;
                }
            }

            //如果待机距离大于0则启动距离计算
            if (tileProcessor.IdleDistance > 0) {
                long idleDistanceSQ = tileProcessor.IdleDistance * tileProcessor.IdleDistance;
                Vector2 posInWorld = tileProcessor.PosInWorld;

                bool playerInRange = false;

                foreach (var p in Main.player) {
                    if (!p.active) {
                        continue;
                    }
                    if (p.position.DistanceSQ(posInWorld) < idleDistanceSQ) {
                        playerInRange = true;
                        break; // 有玩家在范围内就可以停止检查
                    }
                }

                if (!playerInRange) {
                    return; // 没有任何玩家在范围内，跳过更新
                }
            }

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

            if (!VaultUtils.isSinglePlayer && tileProcessor.SendCooldownTicks <= 0) {
                if (tileProcessor.SendpacketCount > tileProcessor.SendpacketPeak) {
                    tileProcessor.SendCooldownTicks = 60;
                    tileProcessor.SendpacketCount = 0;
                }
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

            Rectangle mouseRec = Main.MouseWorld.GetRectangle(1);//在这里缓存鼠标的矩形，避免在下面的遍历中多次构造矩形
            //这里故意使用for来避免更新中途的删加情况，虽然中途进行移除元素的操作仍旧有可能出问题，但尽量不要当混蛋这么写
            //另一个选择是使用快照遍历，但那个开销在理论上很大，因为这个集合的元素数量往往会很多
            for (int i = 0; i < TileProcessorLoader.TP_InWorld.Count; i++) {
                TileProcessor tileProcessor = TileProcessorLoader.TP_InWorld[i];
                if (!tileProcessor.Active) {
                    continue;
                }

                if (tileProcessor.InScreen) {
                    tileProcessor.HoverTP = tileProcessor.HitBox.Intersects(mouseRec);
                }
                else {//不在屏幕里面鼠标肯定是点不到的
                    tileProcessor.HoverTP = false;
                }

                TileProcessorLoader.TP_ID_To_InWorld_Count[tileProcessor.ID]++;

                if (TileProcessorIsDead(tileProcessor)) {
                    continue;
                }

                TileProcessorUpdate(tileProcessor);
            }

            foreach (TileProcessor tpInds in TileProcessorLoader.TP_Instances.ToList()) {
                if (tpInds.GetInWorldHasNum() > 0) {
                    tpInds.SingleInstanceUpdate();
                }
            }
        }

        internal static void TileProcessorPreTileDraw(TileProcessor tileProcessor) {
            if (!VaultUtils.IsPointOnScreen(tileProcessor.PosInWorld - Main.screenPosition, tileProcessor.DrawExtendMode)) {
                return;
            }

            bool reset = true;
            foreach (var gTP in TileProcessorLoader.TPGlobalHooks) {
                reset = gTP.PreTileDraw(tileProcessor, Main.spriteBatch);
            }
            if (reset) {
                tileProcessor.PreTileDraw(Main.spriteBatch);
            }
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
        public static void PreDrawTiles() {
            if (TileProcessorLoader.TP_InWorld.Count <= 0) {
                return;
            }

            foreach (TileProcessor tileProcessor in TileProcessorLoader.TP_InWorld) {
                if (!tileProcessor.Active) {
                    continue;
                }

                TileProcessorPreTileDraw(tileProcessor);
            }
        }

        /// <inheritdoc/>
        public override void PostDrawTiles() {
            if (TileProcessorLoader.TP_InWorld.Count <= 0) {
                return;
            }

            Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, Main.DefaultSamplerState
                , DepthStencilState.None, Main.Rasterizer, null, Main.GameViewMatrix.TransformationMatrix);

            //第一次层，也是最下方的
            foreach (TileProcessor tileProcessor in TileProcessorLoader.TP_InWorld) {
                tileProcessor.InScreen = false;
                if (!tileProcessor.Active) {
                    continue;
                }

                tileProcessor.InScreen = VaultUtils.IsPointOnScreen(tileProcessor.PosInWorld - Main.screenPosition, tileProcessor.DrawExtendMode);

                if (!tileProcessor.InScreen) {
                    continue;
                }

                tileProcessor.BackDraw(Main.spriteBatch);
            }
            //第二次层次
            foreach (TileProcessor tileProcessor in TileProcessorLoader.TP_InWorld) {
                if (!tileProcessor.Active || !tileProcessor.InScreen) {
                    continue;
                }

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
            }
            //第三次层，也是最上方的
            foreach (TileProcessor tileProcessor in TileProcessorLoader.TP_InWorld) {
                if (!tileProcessor.Active || !tileProcessor.InScreen) {
                    continue;
                }

                tileProcessor.FrontDraw(Main.spriteBatch);

                TileProcessorBoxSizeDraw(tileProcessor);
            }

            Main.spriteBatch.End();
        }
    }
}
