using InnoVault.GameSystem;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using Terraria;
using Terraria.Graphics;
using Terraria.Graphics.Effects;
using Terraria.Graphics.Renderers;

namespace InnoVault.RenderHandles
{
    /// <summary>
    /// 所有关于渲染实例的钩子均挂载于此处，并提供拷屏、回写、RT 作用域等公共 helper
    /// </summary>
    public sealed class RenderHandleLoader : IVaultLoader
    {
        #region 状态
        /// <summary>
        /// 一个主动给予和自动维护的中间屏幕对象，作用类似于 <see cref="Main.screenTargetSwap"/> ，
        /// 如果需要实际修改画面，请使用 <see cref="Main.screenTarget"/>
        /// </summary>
        public static RenderTarget2D ScreenSwap { get; set; }

        /// <summary>
        /// 本帧主画面是否已被捕获进 <see cref="Main.screenTarget"/><br/>
        /// 原版只在任一场景滤镜激活（或任一 <c>ModSystem.RequiresScreenTarget</c> 返回真）时才捕获，
        /// 未捕获的帧里世界直接画在后备缓冲上，拷屏与回写都拿不到画面。
        /// 每帧 <see cref="Main.OnPreDraw"/> 复位，<c>FilterManager.BeginCapture</c> 之后按实际绑定置位；
        /// 相机模式截图等非主帧捕获不会置位
        /// </summary>
        public static bool IsScreenCaptured { get; private set; }

        /// <summary>
        /// 当前正处于哪一轮玩家绘制，仅在 <see cref="RenderHandle.DrawBeforePlayers"/> /
        /// <see cref="RenderHandle.DrawAfterPlayers"/> 调用期间有意义，其余时刻为 <see cref="PlayerDrawPass.None"/>
        /// </summary>
        public static PlayerDrawPass CurrentPlayerDrawPass { get; private set; }

        /// <summary>
        /// <see cref="Main.screenTarget"/> 管线在技术上是否可用：复古 / 迷幻光照下原版直绘屏幕并释放 RT，此时为 <see langword="false"/><br/>
        /// 这只说明 RT 存在，本帧是否真的捕获了画面看 <see cref="IsScreenCaptured"/>
        /// </summary>
        public static bool ScreenTargetAvailable => !Main.drawToScreen && Main.screenTarget != null && !Main.screenTarget.IsDisposed;

        private static bool forceScreenCapture;
        /// <summary>
        /// 手动强制主画面每帧捕获，不依赖任何实例的 <see cref="RenderHandle.RequireScreenCapture"/> 声明
        /// </summary>
        public static bool ForceScreenCapture {
            get => forceScreenCapture;
            set {
                forceScreenCapture = value;
                RefreshCaptureKeepAlive();
            }
        }

        private static bool autoScreenCapture = true;
        /// <summary>
        /// 是否依据实例的 <see cref="RenderHandle.RequireScreenCapture"/> 自动让捕获常驻，默认开启。
        /// 关闭后仅 <see cref="ForceScreenCapture"/> 能强制捕获
        /// </summary>
        public static bool AutoScreenCapture {
            get => autoScreenCapture;
            set {
                autoScreenCapture = value;
                RefreshCaptureKeepAlive();
            }
        }

        //PostSetupContent 时汇总的自动捕获需求
        private static bool anyInstanceRequiresCapture;
        /// <summary>
        /// 本帧是否要求原版把主画面捕获进 <see cref="Main.screenTarget"/>，
        /// 由 <see cref="RenderHandleSystem.RequiresScreenTarget"/> 转交给 tML 的 <c>ModSystem.RequiresScreenTarget</c><br/>
        /// 1.4.4 时代靠往 <c>Filters.Scene.OnPostDraw</c> 挂空委托让 <c>FilterManager.CanCapture</c> 恒真，
        /// 1.4.5 移除了该事件，改走 tML 官方口子
        /// </summary>
        internal static bool CaptureKeepAlive { get; private set; }

        //阶段名常量，日志用，避免每帧拼字符串
        private const string StageEndCaptureDraw = "EndCaptureDraw";
        private const string StagePostEndCaptureDraw = "PostEndCaptureDraw";
        private const string StageDrawBeforeTiles = "DrawBeforeTiles";
        private const string StageDrawNPCsOverTiles = "DrawNPCsOverTiles";
        private const string StageDrawAfterTiles = "DrawAfterTiles";
        private const string StageDrawBeforePlayers = "DrawBeforePlayers";
        private const string StageDrawAfterPlayers = "DrawAfterPlayers";
        private const string StageDrawBeforeInfernoRings = "DrawBeforeInfernoRings";
        private const string StageOldEndEntityDraw = "OldEndEntityDraw";
        private const string StageEndEntityDraw = "EndEntityDraw";
        private const string StageDrawAfterEntities = "DrawAfterEntities";
        #endregion

        #region 加载与卸载
        void IVaultLoader.LoadData() {
            On_FilterManager.BeginCapture += FilterManager_BeginCapture;
            //1.4.5 拆出了两个 EndCapture 重载，三参版内部转调六参版，挂六参版即可同时覆盖主帧与相机模式截图
            On_FilterManager.EndCapture_RenderTarget2D_RenderTarget2D_RenderTarget2D_Vector2_Vector2_Vector2 += FilterManager_EndCapture;
            Main.OnResolutionChanged += Main_OnResolutionChanged;
            Main.OnPreDraw += Main_OnPreDraw;
            On_Main.DrawDust += DrawDustHook;
            On_Main.DoDraw_WallsAndBlacks += DrawBeforeTilesHook;
            On_Main.DoDraw_DrawNPCsOverTiles += DrawNPCsOverTilesHook;
            On_Main.DrawPlayers_BehindNPCs += DrawPlayersBehindNPCsHook;
            On_Main.DrawPlayers_AfterProjectiles += DrawPlayersAfterProjectilesHook;
            On_LegacyPlayerRenderer.DrawPlayers += DrawPlayersHook;
            On_Main.DrawInfernoRings += DrawInfernoRingsHook;
        }

        void IVaultLoader.SetupData() {
            if (VaultUtils.isServer) {
                return;
            }

            //PostSetupContent：所有实例已注册完毕，在这里汇总谁需要常驻捕获
            anyInstanceRequiresCapture = false;
            foreach (var render in RenderHandle.Instances) {
                try {
                    if (render.RequireScreenCapture) {
                        anyInstanceRequiresCapture = true;
                        break;
                    }
                } catch (Exception ex) {
                    VaultMod.LoggerError($"[RenderHandleLoader:{render}]", $"RequireScreenCapture threw: {ex.Message}");
                }
            }
            RefreshCaptureKeepAlive();
        }

        void IVaultLoader.UnLoadData() {
            On_FilterManager.BeginCapture -= FilterManager_BeginCapture;
            On_FilterManager.EndCapture_RenderTarget2D_RenderTarget2D_RenderTarget2D_Vector2_Vector2_Vector2 -= FilterManager_EndCapture;
            Main.OnResolutionChanged -= Main_OnResolutionChanged;
            Main.OnPreDraw -= Main_OnPreDraw;
            On_Main.DrawDust -= DrawDustHook;
            On_Main.DoDraw_WallsAndBlacks -= DrawBeforeTilesHook;
            On_Main.DoDraw_DrawNPCsOverTiles -= DrawNPCsOverTilesHook;
            On_Main.DrawPlayers_BehindNPCs -= DrawPlayersBehindNPCsHook;
            On_Main.DrawPlayers_AfterProjectiles -= DrawPlayersAfterProjectilesHook;
            On_LegacyPlayerRenderer.DrawPlayers -= DrawPlayersHook;
            On_Main.DrawInfernoRings -= DrawInfernoRingsHook;

            anyInstanceRequiresCapture = false;
            forceScreenCapture = false;
            autoScreenCapture = true;
            CaptureKeepAlive = false;
            IsScreenCaptured = false;
            CurrentPlayerDrawPass = PlayerDrawPass.None;

            if (VaultUtils.isServer) {
                return;
            }

            Main.QueueMainThreadAction(() => {
                DisposeScreen();
                foreach (var render in RenderHandle.Instances) {
                    render.DisposeScreenTargets();
                }

                RenderHandle.Instances?.Clear();
            });
        }

        private static void RefreshCaptureKeepAlive() {
            if (VaultUtils.isServer) {
                return;
            }

            CaptureKeepAlive = forceScreenCapture || (autoScreenCapture && anyInstanceRequiresCapture);
        }

        private void Main_OnResolutionChanged(Vector2 screenSize) {
            DisposeScreen();
            ScreenSwap = new RenderTarget2D(Main.instance.GraphicsDevice, Main.screenWidth, Main.screenHeight);
            foreach (var render in RenderHandle.Instances) {
                render.CreateScreenTargets();
                render.OnResolutionChanged(screenSize);
            }
        }

        private static void Main_OnPreDraw(GameTime gameTime) {
            //每帧开头复位，未走到 BeginCapture 的帧（无滤镜、复古光照、全屏地图）就保持未捕获
            IsScreenCaptured = false;
            CurrentPlayerDrawPass = PlayerDrawPass.None;
        }

        //确保旧的RenderTarget2D对象被正确释放
        private static void DisposeScreen() {
            ScreenSwap?.Dispose();
            ScreenSwap = null;
        }

        /// <summary>
        /// 确保 <see cref="ScreenSwap"/> 及各实例的 <see cref="RenderHandle.ScreenTargets"/> 已初始化，
        /// 任何绘制阶段均可安全调用
        /// </summary>
        internal static void EnsureScreenSwap() {
            if (ScreenSwap != null && !ScreenSwap.IsDisposed) {
                return;
            }

            ScreenSwap?.Dispose();
            ScreenSwap = new RenderTarget2D(Main.instance.GraphicsDevice, Main.screenWidth, Main.screenHeight);
            foreach (var render in RenderHandle.Instances) {
                render.CreateScreenTargets();
            }
        }
        #endregion

        #region EndCapture 阶段
        private static void FilterManager_BeginCapture(On_FilterManager.orig_BeginCapture orig
            , FilterManager filterManager, RenderTarget2D screenTarget1) {
            orig.Invoke(filterManager, screenTarget1);

            //只认主帧：相机模式截图把自己的缓冲传进来，不算
            if (screenTarget1 != Main.screenTarget) {
                return;
            }
            IsScreenCaptured = IsBound(Main.instance.GraphicsDevice, screenTarget1);
        }

        private static void FilterManager_EndCapture(On_FilterManager.orig_EndCapture_RenderTarget2D_RenderTarget2D_RenderTarget2D_Vector2_Vector2_Vector2 orig
            , FilterManager filterManager
            , RenderTarget2D finalTexture
            , RenderTarget2D screenTarget1
            , RenderTarget2D screenTarget2
            , Vector2 screenSize
            , Vector2 sceneSize
            , Vector2 sceneOffset) {

            //相机模式截图与他模组的手工捕获也会走到这里，只在主帧、且本帧确实捕获了画面时分发
            //BeginCapture 已置位的帧即便被前面别的钩子换了绑定也照常分发，消费者会自行重绑 screenTarget
            bool mainFrame = RenderHandle.Instances.Count > 0 && screenTarget1 == Main.screenTarget
                && (IsScreenCaptured || IsBound(Main.instance.GraphicsDevice, screenTarget1));
            if (!mainFrame) {
                orig.Invoke(filterManager, finalTexture, screenTarget1, screenTarget2, screenSize, sceneSize, sceneOffset);
                return;
            }

            IsScreenCaptured = true;
            EnsureScreenSwap();

            foreach (var render in RenderHandle.Instances) {
                render.filterManager = filterManager;
                render.finalTexture = finalTexture;
                render.screenTarget1 = screenTarget1;
                render.screenTarget2 = screenTarget2;
            }

            if (!Main.gameMenu) {
                DrawBatch(StageEndCaptureDraw, false
                    , static render => render.EndCaptureDraw(Main.spriteBatch, Main.instance.GraphicsDevice, ScreenSwap));
            }

            DrawBatch(StagePostEndCaptureDraw, false
                , static render => render.PostEndCaptureDraw(Main.spriteBatch, Main.instance.GraphicsDevice, ScreenSwap));

            orig.Invoke(filterManager, finalTexture, screenTarget1, screenTarget2, screenSize, sceneSize, sceneOffset);
        }
        #endregion

        #region 分层绘制阶段
        private static void DrawBeforeTilesHook(On_Main.orig_DoDraw_WallsAndBlacks orig, Main self) {
            orig(self);

            if (Main.gameMenu) {
                return;
            }

            EnsureScreenSwap();
            DrawBatch(StageDrawBeforeTiles, true
                , static render => render.DrawBeforeTiles(Main.spriteBatch, Main.instance.GraphicsDevice, ScreenSwap));
        }

        private static void DrawNPCsOverTilesHook(On_Main.orig_DoDraw_DrawNPCsOverTiles orig, Main self) {
            if (Main.gameMenu) {
                orig(self);
                return;
            }

            EnsureScreenSwap();
            DrawBatch(StageDrawNPCsOverTiles, false
                , static render => render.DrawNPCsOverTiles(Main.spriteBatch, Main.instance.GraphicsDevice, ScreenSwap));

            orig(self);
        }

        //两轮玩家绘制各自标记轮次，DrawPlayersHook 据此让实例只响应自己声明的那一轮
        private static void DrawPlayersBehindNPCsHook(On_Main.orig_DrawPlayers_BehindNPCs orig, Main self) {
            CurrentPlayerDrawPass = PlayerDrawPass.BehindNPCs;
            try {
                orig(self);
            } finally {
                CurrentPlayerDrawPass = PlayerDrawPass.None;
            }
        }

        private static void DrawPlayersAfterProjectilesHook(On_Main.orig_DrawPlayers_AfterProjectiles orig, Main self) {
            CurrentPlayerDrawPass = PlayerDrawPass.AfterProjectiles;
            try {
                orig(self);
            } finally {
                CurrentPlayerDrawPass = PlayerDrawPass.None;
            }
        }

        private static void DrawPlayersHook(On_LegacyPlayerRenderer.orig_DrawPlayers orig, LegacyPlayerRenderer self, Camera camera, IEnumerable<Player> players) {
            if (Main.gameMenu) {
                orig(self, camera, players);
                return;
            }

            //在交还给原版绘制前剔除半构造的玩家实例，防止 tML 的 ModPlayer 钩子枚举越界
            //两处 DrawPlayers 钩子的挂载顺序不受保证，故各自过滤一次，保证无论链序如何原版拿到的都是安全集合
            players = PlayerRebuildLoader.FilterReadyPlayers(players);

            EnsureScreenSwap();
            //轮次为 None 说明是未知调用方直接调了 DrawPlayers，此时不过滤，保持旧行为
            PlayerDrawPass pass = CurrentPlayerDrawPass;
            DrawBatch(StageDrawBeforePlayers, false, pass
                , static render => render.DrawBeforePlayers(Main.spriteBatch, Main.instance.GraphicsDevice, ScreenSwap));

            orig(self, camera, players);

            DrawBatch(StageDrawAfterPlayers, false, pass
                , static render => render.DrawAfterPlayers(Main.spriteBatch, Main.instance.GraphicsDevice, ScreenSwap));
        }

        private static void DrawInfernoRingsHook(On_Main.orig_DrawInfernoRings orig, Main self) {
            if (Main.gameMenu) {
                orig(self);
                return;
            }

            EnsureScreenSwap();
            DrawBatch(StageDrawBeforeInfernoRings, true
                , static render => render.DrawBeforeInfernoRings(Main.spriteBatch, Main.instance.GraphicsDevice, ScreenSwap));

            orig(self);
        }

        private void DrawDustHook(On_Main.orig_DrawDust orig, Main main) {
            orig(main);
            if (Main.gameMenu) {
                return;
            }

            EnsureScreenSwap();
            DrawBatch(StageOldEndEntityDraw, false
                , static render => render.EndEntityDraw(Main.spriteBatch, Main.instance));
            DrawBatch(StageEndEntityDraw, false
                , static render => render.EndEntityDraw(Main.spriteBatch, Main.instance, Main.instance.GraphicsDevice, ScreenSwap));
            DrawBatch(StageDrawAfterEntities, false
                , static render => render.DrawAfterEntities(Main.spriteBatch, Main.instance.GraphicsDevice, ScreenSwap));
        }
        #endregion

        #region 分发与错误隔离
        /// <summary>
        /// 按 Weight 顺序把一个阶段分发给所有实例，跳过 <see cref="RenderHandle.Active"/> 为假或处于错误冷却中的实例
        /// </summary>
        /// <param name="stage">阶段名，仅用于日志</param>
        /// <param name="activeOnExit">该阶段契约要求返回时 SpriteBatch 是否处于活跃状态，异常后据此修复</param>
        /// <param name="drawAction">对单个实例的调用</param>
        internal static void DrawBatch(string stage, bool activeOnExit, Action<RenderHandle> drawAction)
            => DrawBatch(stage, activeOnExit, PlayerDrawPass.None, drawAction);

        /// <summary>
        /// 带玩家轮次过滤的分发，<paramref name="pass"/> 为 <see cref="PlayerDrawPass.None"/> 时不过滤
        /// </summary>
        internal static void DrawBatch(string stage, bool activeOnExit, PlayerDrawPass pass, Action<RenderHandle> drawAction) {
            List<RenderHandle> instances = RenderHandle.Instances;
            if (instances.Count == 0) {
                return;
            }

            GraphicsDevice graphicsDevice = Main.instance.GraphicsDevice;
            //进入本阶段时的 RT 绑定，只在真的有实例要画时取一次，异常后据此还原
            RenderTargetBinding[] bindings = null;

            for (int i = 0; i < instances.Count; i++) {
                RenderHandle render = instances[i];
                if (render.ignoreBug > 0) {
                    continue;
                }

                try {
                    //Active 与 PlayerDrawPasses 是消费者代码，放进 try 里，抛了也只影响这一实例
                    if (!render.Active) {
                        continue;
                    }
                    if (pass != PlayerDrawPass.None && (render.PlayerDrawPasses & pass) == 0) {
                        continue;
                    }

                    bindings ??= graphicsDevice.GetRenderTargets();
                    drawAction(render);
                } catch (Exception ex) {
                    ReportStageFailure(render, stage, ex);
                    RecoverDeviceState(graphicsDevice, bindings, activeOnExit);
                }
            }
        }

        private static void ReportStageFailure(RenderHandle render, string stage, Exception ex) {
            render.ignoreBug = 60;//暂时屏蔽一段时间，避免帧帧报错
            render.errorCount++;
            string message = $"Stage [{stage}] failed: {ex.Message}. errorCount={render.errorCount}";
            VaultMod.LoggerError($"[RenderHandleLoader:{render}{stage}]", message);
            VaultUtils.Text(message, Color.Red);
        }

        //实例抛异常后把设备拉回阶段契约：RT 绑定还原到进入阶段时的状态，SpriteBatch 按契约补 End 或 Begin
        //否则错绑遗留会让后续所有绘制落进交换缓冲（整屏消失、闪烁），SpriteBatch 状态错乱会让原版下一次 Begin/End 连锁抛出
        private static void RecoverDeviceState(GraphicsDevice graphicsDevice, RenderTargetBinding[] bindings, bool activeOnExit) {
            try {
                if (bindings != null) {
                    RestoreBindings(graphicsDevice, bindings);
                }

                SpriteBatch spriteBatch = Main.spriteBatch;
                bool active = spriteBatch.GetBeginCalledBool();
                if (active && !activeOnExit) {
                    spriteBatch.End();
                }
                else if (!active && activeOnExit) {
                    spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, Main.DefaultSamplerState
                        , DepthStencilState.None, Main.Rasterizer, null, Main.Transform);
                }
            } catch (Exception ex) {
                VaultMod.LoggerError("[RenderHandleLoader:Recover]", $"Failed to recover device state: {ex.Message}");
            }
        }

        private static void RestoreBindings(GraphicsDevice graphicsDevice, RenderTargetBinding[] bindings) {
            if (bindings == null || bindings.Length == 0) {
                graphicsDevice.SetRenderTarget(null);
            }
            else {
                graphicsDevice.SetRenderTargets(bindings);
            }
        }

        private static bool IsBound(GraphicsDevice graphicsDevice, RenderTarget2D target) {
            if (target == null || target.IsDisposed) {
                return false;
            }
            RenderTargetBinding[] bindings = graphicsDevice.GetRenderTargets();
            return bindings != null && bindings.Length > 0 && bindings[0].RenderTarget == target;
        }
        #endregion

        #region 拷屏与回写 helper
        /// <summary>
        /// 把 <see cref="Main.screenTarget"/> 的当前画面原样拷进 <paramref name="dest"/>：绑定、清透明、Opaque 覆盖<br/>
        /// 返回后 <paramref name="dest"/> 保持绑定；进入时 SpriteBatch 须为非活跃状态，返回时同样非活跃<br/>
        /// 前置条件不满足（RT 不可用、目标无效或就是 screenTarget 本身）返回 <see langword="false"/> 且不改变任何状态
        /// </summary>
        public static bool CaptureScreen(SpriteBatch spriteBatch, GraphicsDevice graphicsDevice, RenderTarget2D dest) {
            if (!ScreenTargetAvailable || dest == null || dest.IsDisposed || dest == Main.screenTarget) {
                return false;
            }

            graphicsDevice.SetRenderTarget(dest);
            graphicsDevice.Clear(Color.Transparent);
            spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.Opaque, SamplerState.PointClamp
                , DepthStencilState.None, RasterizerState.CullNone);
            spriteBatch.Draw(Main.screenTarget, Vector2.Zero, Color.White);
            spriteBatch.End();
            return true;
        }

        /// <summary>
        /// 把 <paramref name="source"/> 全屏回写到 <see cref="Main.screenTarget"/>：绑定、清透明、整帧重绘<br/>
        /// 传入 <paramref name="effect"/> 时以 <see cref="SpriteSortMode.Immediate"/> 开批并应用其当前技术的第 0 个 pass，
        /// 着色器参数请在调用前设好；<paramref name="blendState"/> 默认 <see cref="BlendState.Opaque"/>，
        /// <paramref name="samplerState"/> 默认 <see cref="SamplerState.LinearClamp"/><br/>
        /// 返回后 <see cref="Main.screenTarget"/> 保持绑定；进入时 SpriteBatch 须为非活跃状态，返回时同样非活跃
        /// </summary>
        public static bool PresentToScreen(SpriteBatch spriteBatch, GraphicsDevice graphicsDevice, Texture2D source
            , Effect effect = null, BlendState blendState = null, SamplerState samplerState = null) {
            if (!ScreenTargetAvailable || source == null || source.IsDisposed || source == Main.screenTarget) {
                return false;
            }

            graphicsDevice.SetRenderTarget(Main.screenTarget);
            graphicsDevice.Clear(Color.Transparent);
            spriteBatch.Begin(effect != null ? SpriteSortMode.Immediate : SpriteSortMode.Deferred
                , blendState ?? BlendState.Opaque, samplerState ?? SamplerState.LinearClamp
                , DepthStencilState.None, RasterizerState.CullNone);
            effect?.CurrentTechnique.Passes[0].Apply();
            spriteBatch.Draw(source, Vector2.Zero, Color.White);
            spriteBatch.End();
            return true;
        }

        /// <summary>
        /// 一步完成“拷屏 → 设参 → 着色回写”的全屏后处理：<see cref="Main.screenTarget"/> 拷进 <paramref name="scratch"/>，
        /// 调用 <paramref name="configure"/> 给 <paramref name="effect"/> 设参，再把 <paramref name="scratch"/> 经
        /// <paramref name="effect"/> 回写主屏；全程 try/finally 还原进入时的 RT 绑定，中途抛异常也不会把设备留在交换缓冲上<br/>
        /// 要求本帧主画面已捕获（<see cref="IsScreenCaptured"/>），否则返回 <see langword="false"/>，调用方可走低质量回退；
        /// <paramref name="scratch"/> 一般传阶段参数里的 <c>screenSwap</c>，需要多块缓冲时用 <see cref="RenderHandle.ScreenTargets"/><br/>
        /// 进入时 SpriteBatch 须为非活跃状态，返回时同样非活跃
        /// </summary>
        /// <returns>是否实际执行了回写</returns>
        public static bool ApplyScreenEffect(SpriteBatch spriteBatch, GraphicsDevice graphicsDevice, RenderTarget2D scratch, Effect effect
            , Action<Effect> configure = null, BlendState blendState = null, SamplerState samplerState = null) {
            if (!IsScreenCaptured || !ScreenTargetAvailable || effect == null || effect.IsDisposed) {
                return false;
            }
            if (scratch == null || scratch.IsDisposed || scratch == Main.screenTarget) {
                return false;
            }

            RenderTargetBinding[] previous = graphicsDevice.GetRenderTargets();
            try {
                if (!CaptureScreen(spriteBatch, graphicsDevice, scratch)) {
                    return false;
                }
                configure?.Invoke(effect);
                return PresentToScreen(spriteBatch, graphicsDevice, scratch, effect, blendState, samplerState);
            } finally {
                RestoreBindings(graphicsDevice, previous);
            }
        }

        /// <summary>
        /// 进入一段“画进自有 RT”的作用域：绑定 <paramref name="target"/>（可选先清成 <paramref name="clearColor"/>），
        /// <c>using</c> 结束时还原原绑定并把主画面铺回，规避 <see cref="Main.screenTarget"/> 重绑即丢内容的黑屏陷阱<br/>
        /// 进入与离开时 SpriteBatch 都须为非活跃状态；不要把 <see cref="ScreenSwap"/> 当作目标传入
        /// </summary>
        public static RenderTargetScope PushRenderTarget(GraphicsDevice graphicsDevice, RenderTarget2D target, Color? clearColor = null) {
            ArgumentNullException.ThrowIfNull(graphicsDevice);
            ArgumentNullException.ThrowIfNull(target);
            return new RenderTargetScope(graphicsDevice, target, clearColor);
        }
        #endregion
    }
}
