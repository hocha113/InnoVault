using InnoVault.GameSystem;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.IO;
using System.Linq;
using System.Text;
using Terraria;
using Terraria.GameContent;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using static InnoVault.TileProcessors.TileProcessorLoader;

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
            //那么就按照老版本去读取.twd的内容将老存档的数据加载进游戏的TP实体，以便保存时可以成功将老存档的数据保存进NBT
            if (!File.Exists(SaveWorld.SaveTPDataPath)) {
                ActiveWorldTagData = tag;
            }
        }

        /// <inheritdoc/>
        public override void OnWorldUnload() {
            foreach (TileProcessor tp in TP_InWorld) {
                if (tp.Active) {
                    tp.UnLoadInWorld();
                }
            }
        }

        /// <inheritdoc/>
        public override void PreSaveAndQuit() {
            //客户端不会调用SaveWorldData钩子，所以在这里确保TP在离开服务器后被清理，避免静态数据污染其他世界
            if (VaultUtils.isClient) {
                InitializeWorldTP();
            }
        }

        /// <inheritdoc/>
        public override void PostUpdateEverything() {
            if (!CanRunByWorld()) {
                return;
            }

            if (!VaultUtils.isSinglePlayer) {
                TileProcessorNetWork.UpdateNetworkStatusWatchdog();
            }

            //全局钩子的冷却逻辑
            foreach (var tpGlobal in TPGlobalHooks) {
                if (tpGlobal.ignoreBug > 0) {
                    tpGlobal.ignoreBug--;
                }
            }

            //更新世界内的TP实例
            UpdateInWorldProcessors();
            //更新所有单例TP实例
            UpdateSingleInstanceProcessors();
        }

        /// <inheritdoc/>
        public override void PostDrawTiles() {
            if (!CanRunByWorld()) {
                return;
            }

            //全局绘制前的钩子
            bool canContinue = ExecuteGlobalHook(g => g.PreDrawEverything(Main.spriteBatch), "PreDrawEverything", HookPreDrawEverything);
            if (!canContinue) {
                return;
            }

            Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, Main.DefaultSamplerState,
                DepthStencilState.None, Main.Rasterizer, null, Main.GameViewMatrix.TransformationMatrix);

            //分层绘制TP
            DrawBackLayer();//最下层
            DrawMainLayer();//中间层
            DrawFrontLayer();//最上层

            Main.spriteBatch.End();

            //全局绘制后的钩子
            ExecuteGlobalHook(g => g.PostDrawEverything(Main.spriteBatch), "PostDrawEverything", HookPostDrawEverything);
        }

        /// <summary>
        /// 在原版物块绘制之前执行的TP绘制
        /// </summary>
        public static void PreDrawTiles() {
            if (!CanRunByWorld()) {
                return;
            }

            bool canContinue = ExecuteGlobalHook(g => g.PreTileDrawEverything(Main.spriteBatch), "PreTileDrawEverything", HookPreTileDrawEverything);
            if (!canContinue) {
                return;
            }

            for (int i = 0; i < TP_InWorld.Count; i++) {
                TileProcessor tileProcessor = TP_InWorld[i];
                if (tileProcessor.Active) {
                    DoRun(tileProcessor, TileProcessorPreTileDraw, "PreTileDraw");
                }
            }
        }

        /// <summary>
        /// 预绘制一个TP实例
        /// </summary>
        private static void TileProcessorPreTileDraw(TileProcessor tileProcessor) {
            if (!VaultUtils.IsPointOnScreen(tileProcessor.PosInWorld - Main.screenPosition, tileProcessor.DrawExtendMode)) {
                return;
            }

            bool canDraw = true;
            foreach (var gTP in HookPreTileDraw.Enumerate()) {
                if (!gTP.PreTileDraw(tileProcessor, Main.spriteBatch)) {
                    canDraw = false;
                }
            }
            if (canDraw) {
                tileProcessor.PreTileDraw(Main.spriteBatch);
            }
        }

        /// <summary>
        /// 绘制TP的背景层
        /// </summary>
        private static void DrawBackLayer() {
            foreach (TileProcessor tileProcessor in TP_InWorld) {
                tileProcessor.InScreen = false;
                if (!tileProcessor.Active) {
                    continue;
                }

                //计算并缓存InScreen状态
                tileProcessor.InScreen = VaultUtils.IsPointOnScreen(tileProcessor.PosInWorld - Main.screenPosition, tileProcessor.DrawExtendMode);
                if (!tileProcessor.InScreen) {
                    continue;
                }

                DoRun(tileProcessor, tp => tp.BackDraw(Main.spriteBatch), "BackDraw");
            }
        }

        /// <summary>
        /// 绘制TP的主体层
        /// </summary>
        private static void DrawMainLayer() {
            foreach (TileProcessor tileProcessor in TP_InWorld) {
                if (!tileProcessor.Active || !tileProcessor.InScreen) {
                    continue;
                }

                bool canDraw = true;
                foreach (var tpGlobal in HookPreDraw.Enumerate()) {
                    try {
                        if (!tpGlobal.PreDraw(tileProcessor, Main.spriteBatch)) {
                            canDraw = false;
                        }
                    } catch (Exception ex) {
                        TPErrorHandle(tileProcessor, ex, $"Global PreDraw By {tpGlobal}");
                    }
                }

                if (canDraw) {
                    DoRun(tileProcessor, tp => tp.Draw(Main.spriteBatch), "Draw");
                }

                foreach (var tpGlobal in HookPostDraw.Enumerate()) {
                    try {
                        tpGlobal.PostDraw(tileProcessor, Main.spriteBatch);
                    } catch (Exception ex) {
                        TPErrorHandle(tileProcessor, ex, $"Global PostDraw By {tpGlobal}");
                    }
                }
            }
        }

        /// <summary>
        /// 绘制TP的前景层和调试信息
        /// </summary>
        private static void DrawFrontLayer() {
            foreach (TileProcessor tileProcessor in TP_InWorld) {
                if (!tileProcessor.Active || !tileProcessor.InScreen) {
                    continue;
                }

                DoRun(tileProcessor, tp => tp.FrontDraw(Main.spriteBatch), "FrontDraw");
                DrawTileProcessorDebugBox(tileProcessor);
            }
        }

        /// <summary>
        /// 绘制这个TP实体的调试框
        /// </summary>
        private static void DrawTileProcessorDebugBox(TileProcessor tileProcessor) {
            if (!VaultClientConfig.Instance.TileProcessorBoxSizeDraw) {
                return;
            }

            Vector2 drawPos = tileProcessor.PosInWorld - Main.screenPosition;

            Main.EntitySpriteDraw(VaultAsset.placeholder2.Value, drawPos,
                new Rectangle(0, 0, (int)tileProcessor.Size.X, (int)tileProcessor.Size.Y),
                Color.OrangeRed * 0.3f, 0, Vector2.Zero, 1, SpriteEffects.None, 0);

            Main.EntitySpriteDraw(VaultAsset.placeholder2.Value, drawPos,
                new Rectangle(0, 0, 16, 16), Color.Red * 0.6f, 0, Vector2.Zero, 1, SpriteEffects.None, 0);

            Utils.DrawBorderStringFourWay(Main.spriteBatch, FontAssets.ItemStack.Value, tileProcessor.ToString(),
                drawPos.X, drawPos.Y - 70, Color.AliceBlue, Color.Black, Vector2.Zero, 1f);
        }

        /// <summary>
        /// 更新所有在世界中的TP实例
        /// </summary>
        private static void UpdateInWorldProcessors() {
            //重置计数器
            foreach (TileProcessor tileProcessor in TP_Instances) {
                TP_ID_To_InWorld_Count[tileProcessor.ID] = 0;
            }

            Rectangle mouseRec = Main.MouseWorld.GetRectangle(1);
            //使用for循环以安全地处理更新过程中集合的增删
            for (int i = 0; i < TP_InWorld.Count; i++) {
                TileProcessor tileProcessor = TP_InWorld[i];
                if (!tileProcessor.Active) continue;

                //更新鼠标悬浮状态
                tileProcessor.HoverTP = tileProcessor.InScreen && tileProcessor.HitBox.Intersects(mouseRec);

                TP_ID_To_InWorld_Count[tileProcessor.ID]++;

                if (TileProcessorIsDead(tileProcessor)) continue;

                if (tileProcessor.ignoreBug > 0) {
                    tileProcessor.ignoreBug--;
                    continue; //如果刚出过错或正在冷却，则跳过本次更新
                }

                DoRun(tileProcessor, TileProcessorUpdate, "Update");
            }
        }

        /// <summary>
        /// 更新所有单例TP实例
        /// </summary>
        private static void UpdateSingleInstanceProcessors() {
            foreach (var tpKeyValue in TP_ID_To_InWorld_Count) {
                if (tpKeyValue.Value <= 0) {
                    continue;
                }

                var tpInstance = TP_ID_To_Instance[tpKeyValue.Key];

                //执行全局钩子的PreSingleInstanceUpdate
                bool canUpdate = ExecuteGlobalHook(g => g.PreSingleInstanceUpdate(tpInstance), "PreSingleInstanceUpdate", HookPreSingleInstanceUpdate);
                if (!canUpdate) {
                    continue;
                }

                //执行自身的单例更新
                DoRun(tpInstance, tp => tp.SingleInstanceUpdate(), "SingleInstanceUpdate");

                //执行全局钩子的SingleInstanceUpdate
                ExecuteGlobalHook(g => g.SingleInstanceUpdate(tpInstance), "SingleInstanceUpdate", HookSingleInstanceUpdate);
            }
        }

        /// <summary>
        /// 检查一个TP实例是否应被销毁
        /// </summary>
        private static bool TileProcessorIsDead(TileProcessor tileProcessor) {
            try {
                bool isDead = tileProcessor.IsDaed();

                foreach (var tpGlobal in HookIsDaed.Enumerate()) {
                    bool? reset = tpGlobal.IsDaed(tileProcessor);
                    if (reset.HasValue) {
                        isDead = reset.Value;
                    }
                }

                //在多人游戏中，只有服务器能销毁TP实体
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
        private static void TileProcessorUpdate(TileProcessor tileProcessor) {
            //初始化
            if (!tileProcessor.Spwan) {
                tileProcessor.Initialize();
                foreach (var tpGlobal in HookInitialize.Enumerate()) {
                    tpGlobal.Initialize(tileProcessor);
                }
                if (VaultUtils.isClient && tileProcessor.PlaceNet && tileProcessor.TrackItem != null) {
                    tileProcessor.SendData();
                }
                tileProcessor.Spwan = true;
            }

            //网络同步相关冷却
            if (!VaultUtils.isSinglePlayer) {
                if (Main.GameUpdateCount % 60 == 0) {
                    tileProcessor.SendpacketCount = 0;
                }
                if (tileProcessor.SendCooldownTicks > 0) {
                    tileProcessor.SendCooldownTicks--;
                }
            }

            //玩家距离检测，如果没有任何玩家在范围内则跳过更新
            if (tileProcessor.IdleDistance > 0) {
                long idleDistanceSQ = tileProcessor.IdleDistance * tileProcessor.IdleDistance;
                Vector2 posInWorld = tileProcessor.PosInWorld;
                bool isPlayerInRange = Main.player.Any(p => p.active && p.position.DistanceSQ(posInWorld) < idleDistanceSQ);
                if (!isPlayerInRange) {
                    return;
                }
            }

            //执行更新前钩子
            bool canUpdate = true;
            foreach (var tpGlobal in HookPreUpdate.Enumerate()) {
                if (!tpGlobal.PreUpdate(tileProcessor)) {
                    canUpdate = false;
                }
            }

            //执行更新
            if (canUpdate) {
                tileProcessor.Update();
            }

            //执行更新后钩子
            foreach (var tpGlobal in HookPostUpdate.Enumerate()) {
                tpGlobal.PostUpdate(tileProcessor);
            }

            //网络包发送频率限制
            if (!VaultUtils.isSinglePlayer && tileProcessor.SendCooldownTicks <= 0) {
                if (tileProcessor.SendpacketCount > tileProcessor.SendpacketPeak) {
                    tileProcessor.SendCooldownTicks = 60;
                    tileProcessor.SendpacketCount = 0;
                }
            }
        }

        /// <summary>
        /// 带有错误处理的TP行为执行器
        /// </summary>
        /// <param name="tp">要执行操作的TP实例</param>
        /// <param name="action">要执行的操作</param>
        /// <param name="context">操作的上下文名称，用于报错</param>
        private static void DoRun(TileProcessor tp, Action<TileProcessor> action, string context) {
            if (tp.ignoreBug > 0) {
                return;
            }

            try {
                action.Invoke(tp);
            } catch (Exception ex) {
                TPErrorHandle(tp, ex, context);
            }
        }

        /// <summary>
        /// 通用的全局钩子执行器，封装了错误处理和流程控制
        /// </summary>
        /// <param name="hookAction">要对每个全局钩子执行的操作，返回false可中断后续操作</param>
        /// <param name="context">当前操作的上下文，用于错误报告</param>
        /// <param name="hooks">使用的钩子列表</param>
        /// <returns>如果所有钩子都允许继续，则返回true，否则返回false</returns>
        private static bool ExecuteGlobalHook(Func<GlobalTileProcessor, bool> hookAction, string context, VaultHookMethodCache<GlobalTileProcessor> hooks = null) {
            bool canContinue = true;

            if (hooks == null) {
                foreach (var tpGlobal in TPGlobalHooks) {
                    if (tpGlobal.ignoreBug > 0) {
                        continue;
                    }

                    try {
                        //如果需要聚合结果，并且有一个钩子返回false，则最终结果为false
                        if (!hookAction.Invoke(tpGlobal)) {
                            canContinue = false;
                        }
                    } catch (Exception ex) {
                        TPErrorHandle(tpGlobal, ex, context);
                    }
                }
            }
            else {
                foreach (var tpGlobal in hooks.Enumerate()) {
                    if (tpGlobal.ignoreBug > 0) {
                        continue;
                    }

                    try {
                        //如果需要聚合结果，并且有一个钩子返回false，则最终结果为false
                        if (!hookAction.Invoke(tpGlobal)) {
                            canContinue = false;
                        }
                    } catch (Exception ex) {
                        TPErrorHandle(tpGlobal, ex, context);
                    }
                }
            }

            return canContinue;
        }

        /// <summary>
        /// 通用的全局钩子执行器，封装了错误处理和流程控制
        /// </summary>
        /// <param name="hookAction">要对每个全局钩子执行的操作，返回false可中断后续操作</param>
        /// <param name="context">当前操作的上下文，用于错误报告</param>
        /// <param name="hooks">使用的钩子列表</param>
        /// <returns>如果所有钩子都允许继续，则返回true，否则返回false</returns>
        private static void ExecuteGlobalHook(Action<GlobalTileProcessor> hookAction, string context, VaultHookMethodCache<GlobalTileProcessor> hooks = null) {
            if (hooks == null) {
                foreach (var tpGlobal in TPGlobalHooks) {
                    if (tpGlobal.ignoreBug > 0) {
                        continue;
                    }

                    try {
                        hookAction.Invoke(tpGlobal);
                    } catch (Exception ex) {
                        TPErrorHandle(tpGlobal, ex, context);
                    }
                }
            }
            else {
                foreach (var tpGlobal in hooks.Enumerate()) {
                    if (tpGlobal.ignoreBug > 0) {
                        continue;
                    }

                    try {
                        hookAction.Invoke(tpGlobal);
                    } catch (Exception ex) {
                        TPErrorHandle(tpGlobal, ex, context);
                    }
                }
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

            string errorMessage = errorBuilder.ToString();
            VaultUtils.Text(errorMessage, Color.Red);
            VaultMod.Instance.Logger.Error(errorMessage);
        }

        /// <summary>
        /// 统一的物块处理器(TP)错误处理函数
        /// </summary>
        /// <param name="tpGlobal">发生错误的TP实例</param>
        /// <param name="ex">捕获到的异常</param>
        /// <param name="errorContext">错误发生的阶段(如"Update", "Draw")</param>
        private static void TPErrorHandle(GlobalTileProcessor tpGlobal, Exception ex, string errorContext) {
            tpGlobal.ignoreBug = 60;
            tpGlobal.errorCount++;

            var errorBuilder = new StringBuilder();
            errorBuilder.AppendLine("A Global Tile Processor Hook encountered a fatal error");
            errorBuilder.AppendLine($"Stage: {errorContext}");
            errorBuilder.AppendLine($"Hook Type: {tpGlobal.GetType().FullName}");
            errorBuilder.AppendLine($"Total Error Count: {tpGlobal.errorCount}");
            errorBuilder.AppendLine("Exception Details:");
            errorBuilder.Append(ex.ToString());

            string errorMessage = errorBuilder.ToString();
            VaultUtils.Text(errorMessage, Color.Red);
            VaultMod.Instance.Logger.Error(errorMessage);
        }
    }
}