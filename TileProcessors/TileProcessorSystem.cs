using InnoVault.GameSystem;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Mono.Cecil.Cil;
using System;
using System.IO;
using System.Linq;
using System.Text;
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
        public override void SaveWorldData(TagCompound tag) {
            tag["root:worldData"] = "";
        }
        /// <inheritdoc/>
        public override void LoadWorldData(TagCompound tag) {
            tag.TryGet("root:worldData", out string _);
            //如果不存在对应的NBT存档数据，说明是第一次进行有效加载
            //那么就按找老版本去读取.twd的内容将老存档的数据加载进游戏的TP实体，以便保存时可以成功将老存档的数据保存进NBT
            if (!File.Exists(SaveWorld.SaveTPDataPath)) {
                TileProcessorLoader.ActiveWorldTagData = tag;
            }
        }

        /// <summary>
        /// 统一的物块处理器(TP)错误处理函数
        /// </summary>
        /// <param name="tp">发生错误的TP实例</param>
        /// <param name="ex">捕获到的异常</param>
        /// <param name="errorContext">错误发生的阶段(如"Update", "Draw")</param>
        private static void TPErrorHandle(TileProcessor tp, Exception ex, string errorContext) {
            tp.ignoreBug = 60; //设置60帧的冷却，防止刷屏报错
            tp.errorCount++;

            var errorBuilder = new StringBuilder();
            errorBuilder.AppendLine("A Tile Processor encountered a fatal error");
            errorBuilder.AppendLine($"Stage: {errorContext}");
            errorBuilder.AppendLine($"Type: {tp.GetType().FullName}");
            errorBuilder.AppendLine($"Position: {tp.Position}");
            errorBuilder.AppendLine($"Total Error Count: {tp.errorCount}");
            errorBuilder.AppendLine("Exception Details:");
            errorBuilder.Append(ex.ToString()); //记录完整的异常信息，包括堆栈跟踪

            VaultUtils.Text(errorBuilder.ToString(), Color.Red);
            VaultMod.Instance.Logger.Error(errorBuilder.ToString());
        }

        /// <summary>
        /// 带有错误处理的TP行为执行器
        /// </summary>
        /// <param name="tp">要执行操作的TP实例</param>
        /// <param name="func">要执行的操作</param>
        /// <param name="context">操作的上下文名称，用于报错</param>
        private static void DoRun(TileProcessor tp, Action<TileProcessor> func, string context) {
            if (tp.ignoreBug > 0) {
                return;
            }
            try {
                func.Invoke(tp);
            } catch (Exception ex) {
                TPErrorHandle(tp, ex, context);
            }
        }

        /// <inheritdoc/>
        public override void OnWorldUnload() {
            foreach (TileProcessor tpInds in TileProcessorLoader.TP_InWorld) {
                if (!tpInds.Active) {
                    continue;
                }
                tpInds.UnLoadInWorld();
            }
        }

        /// <summary>
        /// 检查一个TP实例是否应被销毁
        /// </summary>
        internal static bool TileProcessorIsDead(TileProcessor tileProcessor) {
            try {
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
            } catch (Exception ex) {
                TPErrorHandle(tileProcessor, ex, "TileProcessorIsDead");
            }

            return false;
        }

        /// <summary>
        /// 更新一个TP实例的逻辑
        /// </summary>
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
                        break; //有玩家在范围内就可以停止检查
                    }
                }

                if (!playerInRange) {
                    return; //没有任何玩家在范围内，跳过更新
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
            if (!VaultUtils.isSinglePlayer) {
                TileProcessorNetWork.UpdateNetworkStatusWatchdog();
            }

            if (!TileProcessorLoader.CanRunByWorld()) {
                return;
            }

            foreach (var tpGlobal in TileProcessorLoader.TPGlobalHooks) {
                if (tpGlobal.ignoreBug > 0) {
                    tpGlobal.ignoreBug--;
                }
            }

            foreach (TileProcessor tileProcessor in TileProcessorLoader.TP_Instances) {
                TileProcessorLoader.TP_ID_To_InWorld_Count[tileProcessor.ID] = 0;
            }

            Rectangle mouseRec = Main.MouseWorld.GetRectangle(1);//在这里缓存鼠标的矩形，避免在下面的遍历中多次构造矩形
            //这里故意使用for来避免更新中途的删加情况
            //另一个选择是使用快照遍历，但那个开销在理论上很大，因为这个集合的元素数量往往会很多
            for (int i = 0; i < TileProcessorLoader.TP_InWorld.Count; i++) {
                TileProcessor tileProcessor = TileProcessorLoader.TP_InWorld[i];
                if (!tileProcessor.Active) {
                    continue;
                }

                if (tileProcessor.ignoreBug > 0) {
                    tileProcessor.ignoreBug--;//这里不跳过
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

                if (tileProcessor.ignoreBug > 0) {
                    tileProcessor.ignoreBug--;
                    continue;
                }

                DoRun(tileProcessor, TileProcessorUpdate, "Update");
            }

            foreach (TileProcessor tpInds in TileProcessorLoader.TP_Instances.ToList()) {
                bool reset = true;

                foreach (var tpGlobal in TileProcessorLoader.TPGlobalHooks) {
                    if (tpGlobal.ignoreBug > 0) {
                        continue;
                    }

                    try {
                        reset = tpGlobal.PreSingleInstanceUpdate(tpInds);
                    } catch (Exception ex) {
                        tpGlobal.ignoreBug = 60;
                        tpGlobal.errorCount++;

                        string errorContent = $"TPGlobalHooks.PreSingleInstanceUpdate:{ex}";
                        VaultUtils.Text(errorContent, Color.Red);
                        VaultMod.Instance.Logger.Error(errorContent);
                    }
                }

                if (!reset) {
                    continue;
                }

                if (tpInds.GetInWorldHasNum() > 0) {
                    //对单例更新也进行封装
                    DoRun(tpInds, tp => tp.SingleInstanceUpdate(), "SingleInstanceUpdate");
                    foreach (var tpGlobal in TileProcessorLoader.TPGlobalHooks) {
                        if (tpGlobal.ignoreBug > 0) {
                            continue;
                        }

                        try {
                            tpGlobal.SingleInstanceUpdate(tpInds);
                        } catch (Exception ex) {
                            tpGlobal.ignoreBug = 60;
                            tpGlobal.errorCount++;

                            string errorContent = $"TPGlobalHooks.SingleInstanceUpdate:{ex}";
                            VaultUtils.Text(errorContent, Color.Red);
                            VaultMod.Instance.Logger.Error(errorContent);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 预绘制一个TP实例
        /// </summary>
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

        /// <summary>
        /// 在原版物块绘制之前执行的TP绘制
        /// </summary>
        public static void PreDrawTiles() {
            if (!TileProcessorLoader.CanRunByWorld()) {
                return;
            }

            bool reset = true;
            foreach (var tpGlobal in TileProcessorLoader.TPGlobalHooks) {
                if (tpGlobal.ignoreBug > 0) {
                    continue;
                }

                try {
                    reset = tpGlobal.PreTileDrawEverything(Main.spriteBatch);
                } catch (Exception ex) {
                    tpGlobal.ignoreBug = 60;
                    tpGlobal.errorCount++;

                    string errorContent = $"TPGlobalHooks.PreTileDrawEverthing:{ex}";
                    VaultUtils.Text(errorContent, Color.Red);
                    VaultMod.Instance.Logger.Error(errorContent);
                }
            }

            if (!reset) {
                return;
            }

            for (int i = 0; i < TileProcessorLoader.TP_InWorld.Count; i++) {
                TileProcessor tileProcessor = TileProcessorLoader.TP_InWorld[i];
                if (!tileProcessor.Active) {
                    continue;
                }

                DoRun(tileProcessor, TileProcessorPreTileDraw, "PreTileDraw");
            }
        }

        /// <inheritdoc/>
        public override void PostDrawTiles() {
            if (!TileProcessorLoader.CanRunByWorld()) {
                return;
            }

            bool globalReset = true;
            foreach (var tpGlobal in TileProcessorLoader.TPGlobalHooks) {
                if (tpGlobal.ignoreBug > 0) {
                    continue;
                }

                try {
                    globalReset = tpGlobal.PreDrawEverything(Main.spriteBatch);
                } catch (Exception ex) {
                    tpGlobal.ignoreBug = 60;
                    tpGlobal.errorCount++;

                    string errorContent = $"TPGlobalHooks.PreDrawEverthing:{ex}";
                    VaultUtils.Text(errorContent, Color.Red);
                    VaultMod.Instance.Logger.Error(errorContent);
                }
            }

            if (!globalReset) {
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

                DoRun(tileProcessor, tp => tp.BackDraw(Main.spriteBatch), "BackDraw");
            }
            //第二次层次
            foreach (TileProcessor tileProcessor in TileProcessorLoader.TP_InWorld) {
                if (!tileProcessor.Active || !tileProcessor.InScreen) {
                    continue;
                }

                bool reset = true;

                foreach (var tpGlobal in TileProcessorLoader.TPGlobalHooks) {
                    try {
                        reset = tpGlobal.PreDraw(tileProcessor, Main.spriteBatch);
                    } catch (Exception ex) {
                        TPErrorHandle(tileProcessor, ex, $"Global PreDraw By {tpGlobal}");
                    }
                }

                if (reset) {
                    DoRun(tileProcessor, tp => tp.Draw(Main.spriteBatch), "Draw");
                }

                foreach (var tpGlobal in TileProcessorLoader.TPGlobalHooks) {
                    try {
                        tpGlobal.PostDraw(tileProcessor, Main.spriteBatch);
                    } catch (Exception ex) {
                        TPErrorHandle(tileProcessor, ex, $"Global PostDraw By {tpGlobal}");
                    }
                }
            }
            //第三次层，也是最上方的
            foreach (TileProcessor tileProcessor in TileProcessorLoader.TP_InWorld) {
                if (!tileProcessor.Active || !tileProcessor.InScreen) {
                    continue;
                }

                DoRun(tileProcessor, tp => tp.FrontDraw(Main.spriteBatch), "FrontDraw");

                TileProcessorBoxSizeDraw(tileProcessor);
            }

            Main.spriteBatch.End();

            foreach (var tpGlobal in TileProcessorLoader.TPGlobalHooks) {
                if (tpGlobal.ignoreBug > 0) {
                    continue;
                }

                try {
                    tpGlobal.PostDrawEverything(Main.spriteBatch);
                } catch (Exception ex) {
                    tpGlobal.ignoreBug = 60;
                    tpGlobal.errorCount++;

                    string errorContent = $"TPGlobalHooks.PostDrawEverthing:{ex}";
                    VaultUtils.Text(errorContent, Color.Red);
                    VaultMod.Instance.Logger.Error(errorContent);
                }
            }
        }
    }
}