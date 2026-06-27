using InnoVault.Concurrent;
using InnoVault.Debugs;
using InnoVault.GameSystem;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
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
            foreach (TileProcessor tp in TP_InWorld.ToList()) {
                if (!tp.Active) {
                    continue;
                }
                tp.UnLoadInWorld();
            }
            //卸载世界时清空缓存的数据，防止污染下一个世界
            ActiveWorldTagData = null;
            //清理并行调度的所有缓存状态
            TileProcessorParallel.Clear();
        }

        /// <inheritdoc/>
        public override void PreSaveAndQuit() {
            if (!VaultUtils.isClient) {
                return;
            }
            foreach (TileProcessor tp in TP_InWorld.ToList()) {
                if (!tp.Active) {
                    continue;
                }
                tp.ClientSaveAndQuit();
            }
        }

        /// <inheritdoc/>
        public override void PostUpdateEverything() {
            //加载系统的看门狗必须在 CanRunByWorld 门控之前驱动
            //否则客户端在网络加载期间(NetworkTPLoaded 为 false)会被门控挡下，导致超时检测与卡块重传永不触发
            VaultLoadingProgress.Tick();

            //TP网络出站/入站的分帧泵同样必须在门控之前驱动：
            //服务端需要持续推送数据块，客户端需要在网络加载期间分帧应用收到的TP数据
            TileProcessorNetWork.NetworkPump();

            if (!CanRunByWorld()) {
                return;
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
            //如果没有启用任何调试选项，直接返回
            if (!DebugSettings.TileProcessorBoxSizeDraw &&
                !DebugSettings.TileProcessorShowName &&
                !DebugSettings.TileProcessorShowPosition &&
                !DebugSettings.TileProcessorShowID) {
                return;
            }

            Vector2 drawPos = tileProcessor.PosInWorld - Main.screenPosition;

            //绘制碰撞箱
            if (DebugSettings.TileProcessorBoxSizeDraw) {
                Main.EntitySpriteDraw(VaultAsset.placeholder2.Value, drawPos,
                    new Rectangle(0, 0, (int)tileProcessor.Size.X, (int)tileProcessor.Size.Y),
                    Color.OrangeRed * 0.3f, 0, Vector2.Zero, 1, SpriteEffects.None, 0);

                Main.EntitySpriteDraw(VaultAsset.placeholder2.Value, drawPos,
                    new Rectangle(0, 0, 16, 16), Color.Red * 0.6f, 0, Vector2.Zero, 1, SpriteEffects.None, 0);
            }

            //构建调试信息文本
            float yOffset = -20;
            if (DebugSettings.TileProcessorShowName) {
                string nameText = tileProcessor.GetType().Name;
                Utils.DrawBorderStringFourWay(Main.spriteBatch, FontAssets.ItemStack.Value, nameText,
                    drawPos.X, drawPos.Y + yOffset, Color.AliceBlue, Color.Black, Vector2.Zero, 0.9f);
                yOffset -= 18;
            }

            if (DebugSettings.TileProcessorShowPosition) {
                string posText = $"Pos: {tileProcessor.Position.X}, {tileProcessor.Position.Y}";
                Utils.DrawBorderStringFourWay(Main.spriteBatch, FontAssets.ItemStack.Value, posText,
                    drawPos.X, drawPos.Y + yOffset, Color.LightGreen, Color.Black, Vector2.Zero, 0.8f);
                yOffset -= 16;
            }

            if (DebugSettings.TileProcessorShowID) {
                string idText = $"ID: {tileProcessor.ID}";
                Utils.DrawBorderStringFourWay(Main.spriteBatch, FontAssets.ItemStack.Value, idText,
                    drawPos.X, drawPos.Y + yOffset, Color.Yellow, Color.Black, Vector2.Zero, 0.8f);
            }
        }

        //并行/串行下统一读取的鼠标上下文（整帧不变，提前算好，避免逐TP重复计算与闭包分配）
        private static Rectangle parallelMouseRec;
        private static Rectangle parallelSmartRec;
        private static bool parallelSmartCursorActive;
        //每帧重建的分桶缓存
        private static readonly List<TileProcessor> serialBucket = new(256);
        private static readonly List<TileProcessor> independentBucket = new(256);

        /// <summary>
        /// 更新所有在世界中的TP实例，采用"分桶 + 三阶段"并行管线：<br/>
        /// Phase 0 主线程预处理（重置计数 / 鼠标上下文 / 单实例预处理 / 分桶 / 按需重建岛屿）；<br/>
        /// Phase 1 并行（独立桶全量并行 + 分组桶按岛屿并行、岛内串行）；<br/>
        /// Phase 1b 主线程串行桶；Phase 2 主线程有序排空所有延迟副作用<br/>
        /// 当并行被禁用或实体数量过少时，完全回退到历史的单线程路径
        /// </summary>
        private static void UpdateInWorldProcessors() {
            //Phase 0：仅重置上一帧有计数的ID，避免遍历所有已注册的TP类型
            foreach (var key in TP_ID_To_InWorld_Count.Keys) {
                TP_ID_To_InWorld_Count[key] = 0;
            }

            //鼠标/智能光标上下文整帧不变，提前算好
            parallelMouseRec = Main.MouseWorld.GetRectangle(1);
            //智能光标(Smart Cursor)不会移动鼠标真实坐标，它把"被瞄准的图格"重定向到附近的可交互物块
            //原版用 Player.tileTargetX/Y 来表达这个目标。这里额外构造一个 16x16 矩形以覆盖这种情况
            parallelSmartRec = new Rectangle(Player.tileTargetX * 16, Player.tileTargetY * 16, 16, 16);
            parallelSmartCursorActive = Main.SmartCursorIsUsed;

            //单实例预处理在并行/串行两条路径下都需要执行（如重建跨实体路由），故放在分支之前
            RunPreParallel();

            //并行不划算或被显式禁用：完全回退到历史的单线程路径
            if (!TileProcessorParallel.ShouldRunParallel(TP_InWorld.Count)) {
                TileProcessorParallel.SetUsedParallel(false);
                //使用for循环以安全地处理更新过程中集合的增删
                for (int i = 0; i < TP_InWorld.Count; i++) {
                    UpdateOneProcessor(TP_InWorld[i]);
                }
                return;
            }

            try {
                TileProcessorParallel.SetUsedParallel(true);

                //Phase 0b：分桶 + 按需重建并行岛屿
                ClassifyBuckets();
                TileProcessorParallel.EnsureIslands();

                //Phase 1：并行更新
                TileProcessorParallel.BeginParallelPhase();
                try {
                    TileProcessorParallel.RunIndependent(independentBucket, UpdateOneProcessor);
                    TileProcessorParallel.RunIslands(UpdateOneProcessor);
                    TileProcessorParallel.EndParallelPhase();

                    //Phase 1b：串行桶在主线程更新，副作用立即生效（与历史一致）
                    for (int i = 0; i < serialBucket.Count; i++) {
                        UpdateOneProcessor(serialBucket[i]);
                    }
                }
                finally {
                    //Phase 2：无论并行体/串行桶是否抛异常，都必须退出并行阶段并排空：合并计数 + 执行已入队的
                    //延迟副作用（生成物 / 发包 / 死亡 / 击杀 / 报错文本）+ 回收命令缓冲到对象池
                    //否则调度级异常会永久丢弃本帧的延迟副作用并使缓冲脱离对象池。EndParallelPhase 幂等
                    TileProcessorParallel.EndParallelPhase();
                    try {
                        TileProcessorParallel.DrainDeferred();
                    } catch (Exception drainEx) {
                        //排空本身可能抛异常，独立捕获以免掩盖原始异常或二次泄漏
                        VaultMod.Instance.Logger.Error($"[TileProcessorParallel] Drain after parallel update failed: {drainEx}");
                    }
                }
            } catch (Exception ex) {
                //调度级别的异常：仅自动禁用TP并行并回退到串行（不波及Actor子系统），优先保证可用性
                TileProcessorParallel.AutoDisableParallel();
                VaultMod.Instance.Logger.Error($"[TileProcessorParallel] Parallel update failed, disabled and fell back to serial: {ex}");
            }
        }

        /// <summary>
        /// 按<see cref="TileProcessor.ParallelKind"/>把世界中的活跃TP分入串行桶 / 独立桶（分组桶由岛屿调度处理）
        /// </summary>
        private static void ClassifyBuckets() {
            serialBucket.Clear();
            independentBucket.Clear();
            var inWorld = TP_InWorld;
            for (int i = 0; i < inWorld.Count; i++) {
                TileProcessor tp = inWorld[i];
                if (tp == null || !tp.Active) {
                    continue;
                }
                switch (tp.ParallelKind) {
                    case ParallelExecutionKind.Independent:
                        independentBucket.Add(tp);
                        break;
                    case ParallelExecutionKind.Grouped:
                        //由 TileProcessorParallel 的岛屿调度处理
                        break;
                    default:
                        serialBucket.Add(tp);
                        break;
                }
            }
        }

        /// <summary>
        /// 在并行阶段之前，于主线程对每个非串行的已注册TP类型调用一次<see cref="TileProcessor.PreParallel"/>
        /// </summary>
        private static void RunPreParallel() {
            foreach (TileProcessor tp in TP_ID_To_Instance.Values) {
                if (tp.ParallelKind == ParallelExecutionKind.Serial) {
                    continue;
                }
                try {
                    tp.PreParallel();
                } catch (Exception ex) {
                    TPErrorHandle(tp, ex, "PreParallel");
                }
            }
        }

        /// <summary>
        /// 单个TP实例的统一更新体，串行与并行两种调度共用<br/>
        /// 顺序：悬停状态 → 世界计数 → 死亡判定 → 错误冷却 → 实际更新
        /// </summary>
        internal static void UpdateOneProcessor(TileProcessor tileProcessor) {
            if (!tileProcessor.Active) {
                return;
            }

            //整个更新体包一层防御：悬停/计数/死亡判定等若抛出，按单个TP隔离处理(进入ignoreBug冷却)，
            //绝不让单个实体的异常上浮到调度层去触发整套并行的回退
            try {
                //更新鼠标悬浮状态，同时考虑智能光标 snap 到 TP 的情况
                //每个 TP 通过 CanSmartCursorSelect 决定是否接收智能光标的 snap
                bool hoverByMouse = tileProcessor.HitBox.Intersects(parallelMouseRec);
                bool hoverBySmart = parallelSmartCursorActive
                    && tileProcessor.CanSmartCursorSelect
                    && tileProcessor.HitBox.Intersects(parallelSmartRec);
                tileProcessor.HoverTP = tileProcessor.InScreen && (hoverByMouse || hoverBySmart);

                TileProcessorParallel.AddInWorldCount(tileProcessor.ID);

                if (TileProcessorIsDead(tileProcessor)) {
                    return;
                }

                if (tileProcessor.ignoreBug > 0) {
                    tileProcessor.ignoreBug--;
                    return; //如果刚出过错或正在冷却，则跳过本次更新
                }

                DoRun(tileProcessor, TileProcessorUpdate, "Update");
            } catch (Exception ex) {
                TPErrorHandle(tileProcessor, ex, "UpdateOneProcessor");
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
        /// 检查一个TP实例是否应被销毁，命中则在安全时机执行击杀<br/>
        /// 并行阶段会把击杀延迟到Phase 2主线程，串行阶段立即执行
        /// </summary>
        internal static bool TileProcessorIsDead(TileProcessor tileProcessor) {
            bool isDead = EvaluateIsDead(tileProcessor);
            //在多人游戏中，只有服务器能销毁TP实体
            if (isDead && !VaultUtils.isClient) {
                if (TileProcessorParallel.InParallelPhase) {
                    TileProcessorParallel.EnqueueDeath(tileProcessor);
                }
                else {
                    KillNow(tileProcessor);
                }
                return true;
            }
            return false;
        }

        /// <summary>
        /// 只读地判定一个TP是否应当死亡（不产生任何副作用），可安全地在工作线程调用
        /// </summary>
        internal static bool EvaluateIsDead(TileProcessor tileProcessor) {
            try {
                bool isDead = tileProcessor.IsDaed();

                foreach (var tpGlobal in HookIsDaed.Enumerate()) {
                    bool? reset = tpGlobal.IsDaed(tileProcessor);
                    if (reset.HasValue) {
                        isDead = reset.Value;
                    }
                }

                return isDead;
            } catch (Exception ex) {
                TPErrorHandle(tileProcessor, ex, "TileProcessorIsDead");
            }

            return false;
        }

        /// <summary>
        /// 在主线程立即执行一次死亡击杀：服务器先广播死亡，再调用<see cref="TileProcessor.Kill"/>
        /// </summary>
        internal static void KillNow(TileProcessor tileProcessor) {
            try {
                if (VaultUtils.isServer) {
                    TileProcessorNetWork.SendTPDeathByServer(tileProcessor);
                }
                tileProcessor.Kill();
            } catch (Exception ex) {
                TPErrorHandle(tileProcessor, ex, "TileProcessorKill");
            }
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
                long idleDistanceSQ = (long)tileProcessor.IdleDistance * tileProcessor.IdleDistance;
                Vector2 posInWorld = tileProcessor.PosInWorld;
                bool isPlayerInRange = false;
                for (int pi = 0; pi < Main.maxPlayers; pi++) {
                    Player p = Main.player[pi];
                    if (p.active && p.position.DistanceSQ(posInWorld) < idleDistanceSQ) {
                        isPlayerInRange = true;
                        break;
                    }
                }
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
            //并行阶段聊天框写入非线程安全，延迟到主线程统一输出
            if (TileProcessorParallel.InParallelPhase) {
                TileProcessorParallel.EnqueueErrorText(errorMessage);
            }
            else {
                VaultUtils.Text(errorMessage, Color.Red);
            }
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
            //并行阶段聊天框写入非线程安全，延迟到主线程统一输出
            if (TileProcessorParallel.InParallelPhase) {
                TileProcessorParallel.EnqueueErrorText(errorMessage);
            }
            else {
                VaultUtils.Text(errorMessage, Color.Red);
            }
            VaultMod.Instance.Logger.Error(errorMessage);
        }
    }
}