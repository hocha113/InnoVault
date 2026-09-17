using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Collections.Generic;
using System.Reflection;
using Terraria;
using Terraria.Graphics.Effects;

namespace InnoVault.RenderHandles
{
    /// <summary>
    /// 在系统中注册和管理渲染实例，提供自动管理的画布和 RT 对象，<br/>
    /// 每个绘制阶段对应一个独立的虚方法，实现者可同时重写多个阶段
    /// <br/>该API的使用介绍:<see href="https://innovault.wiki/cn/content/render-handle/"/>
    /// <para>
    /// 世界内各阶段的真实触发顺序（对照 tML 1.4.4 的 <c>Main.DoDraw</c>）：<br/>
    /// 1. <see cref="DrawBeforeTiles"/>：墙壁与黑底之后，SpriteBatch 活跃<br/>
    /// 2. <see cref="DrawBeforePlayers"/> / <see cref="DrawAfterPlayers"/> 第一轮：<see cref="PlayerDrawPass.BehindNPCs"/>，
    /// 只包含 <c>isLockedToATile</c> 的玩家<br/>
    /// 3. <see cref="DrawNPCsOverTiles"/>：实心物块之后、NPC 之前<br/>
    /// 4. <see cref="DrawAfterTiles"/>：<c>ModSystem.PostDrawTiles</c> 时机，晚于第 3 步<br/>
    /// 5. 原版弹幕<br/>
    /// 6. <see cref="DrawBeforePlayers"/> / <see cref="DrawAfterPlayers"/> 第二轮：<see cref="PlayerDrawPass.AfterProjectiles"/>，主玩家层<br/>
    /// 7. 原版 NPC 覆盖层、物品、雨、残骸<br/>
    /// 8. <see cref="EndEntityDraw(SpriteBatch, Main)"/> → <see cref="EndEntityDraw(SpriteBatch, Main, GraphicsDevice, RenderTarget2D)"/>
    /// → <see cref="DrawAfterEntities"/>：<c>DrawDust</c> 之后<br/>
    /// 9. 原版水体与电线<br/>
    /// 10. <see cref="DrawBeforeInfernoRings"/>：SpriteBatch 活跃<br/>
    /// 11. <see cref="EndCaptureDraw"/> → <see cref="PostEndCaptureDraw"/>：<c>FilterManager.EndCapture</c> 之前，仅主帧捕获时
    /// </para>
    /// </summary>
    public abstract class RenderHandle : VaultType<RenderHandle>
    {
        #region Data
        /// <remarks>
        /// 存储所有已注册的 <see cref="RenderHandle"/> 实例，按 <see cref="Weight"/> 升序排序，
        /// 权重相同时按注册先后保持稳定次序<br/>
        /// 所有 <see cref="RenderHandle"/> 的生命周期由 <see cref="RenderHandleLoader"/> 管理，
        /// 在卸载时会统一释放其持有的 <see cref="RenderTarget2D"/> 并清空 <see cref="Instances"/>
        /// </remarks>
        public new static List<RenderHandle> Instances { get; private set; } = [];
        /// <summary>
        /// 渲染权重，用于排序默认值为 1，Weight 越大，在排序中越靠后<br/>
        /// 权重相同的实例按注册先后排序，不必再用小数位刻意避撞
        /// </summary>
        public virtual float Weight => 1f;
        /// <summary>
        /// 屏幕数量，决定 <see cref="ScreenTargets"/> 可以包含并管理多少块屏幕对象，
        /// 不要设置为过大的值，这可能会造成明显的游戏性能问题
        /// </summary>
        public virtual int ScreenSlot => 0;
        /// <summary>
        /// 绘制总闸。为 <see langword="false"/> 时 <see cref="RenderHandleLoader"/> 跳过本实例的全部绘制阶段，
        /// 连 try/catch 与虚调用都不进入；<see cref="UpdateBySystem"/>、<see cref="OnResolutionChanged"/>
        /// 与世界钩子照常运行，便于实例在逻辑侧自行开关<br/>
        /// 默认 <see langword="true"/>，与旧行为一致
        /// </summary>
        public virtual bool Active => true;
        /// <summary>
        /// 玩家层阶段（<see cref="DrawBeforePlayers"/> / <see cref="DrawAfterPlayers"/>）响应哪几轮触发<br/>
        /// 原版每帧调用两次 <c>DrawPlayers</c>：先画 <c>isLockedToATile</c> 的玩家（<see cref="PlayerDrawPass.BehindNPCs"/>），
        /// 弹幕之后再画其余玩家（<see cref="PlayerDrawPass.AfterProjectiles"/>）。
        /// 默认 <see cref="PlayerDrawPass.Both"/> 与旧行为一致，每帧触发两次；
        /// 只想每帧画一次、且落在弹幕之上时返回 <see cref="PlayerDrawPass.AfterProjectiles"/>
        /// </summary>
        public virtual PlayerDrawPass PlayerDrawPasses => PlayerDrawPass.Both;
        /// <summary>
        /// 是否需要框架保证 <see cref="Main.screenTarget"/> 每帧都被捕获<br/>
        /// 原版只有在任一场景滤镜激活时才把世界画进 <see cref="Main.screenTarget"/> 并调用 <c>FilterManager.EndCapture</c>，
        /// 否则 <see cref="EndCaptureDraw"/> 根本不会触发，拷屏类效果也拿不到有效画面。
        /// 任一已加载实例返回 <see langword="true"/> 时，<see cref="RenderHandleLoader"/> 会让捕获常驻<br/>
        /// 默认值：本类型重写了 <see cref="EndCaptureDraw"/> 或 <see cref="PostEndCaptureDraw"/> 即为 <see langword="true"/>。
        /// 在其他阶段拷屏的实例可显式返回 <see langword="true"/>；不希望参与自动检测的实例返回 <see langword="false"/>
        /// </summary>
        public virtual bool RequireScreenCapture => OverridesCaptureStage();
        /// <summary>
        /// 用于存储管理多个屏幕画面实例，配合 <see cref="ScreenSlot"/> 使用
        /// </summary>
        public RenderTarget2D[] ScreenTargets { get; private set; }
        /// <summary>
        /// FilterManager 引用，可用于处理后期滤镜，仅在 <see cref="EndCaptureDraw"/> 和
        /// <see cref="PostEndCaptureDraw"/> 调用期间有效
        /// </summary>
        public FilterManager filterManager;
        /// <summary>
        /// 最终渲染的 RenderTarget，仅在 <see cref="EndCaptureDraw"/> 和
        /// <see cref="PostEndCaptureDraw"/> 调用期间有效
        /// </summary>
        public RenderTarget2D finalTexture;
        /// <summary>
        /// 屏幕渲染目标1，仅在 <see cref="EndCaptureDraw"/> 和
        /// <see cref="PostEndCaptureDraw"/> 调用期间有效
        /// </summary>
        public RenderTarget2D screenTarget1;
        /// <summary>
        /// 屏幕渲染目标2，仅在 <see cref="EndCaptureDraw"/> 和
        /// <see cref="PostEndCaptureDraw"/> 调用期间有效
        /// </summary>
        public RenderTarget2D screenTarget2;
        /// <summary>
        /// 注册序号，权重相同时作为稳定排序的次级键
        /// </summary>
        internal int registerOrder;
        //RequireScreenCapture 默认值的缓存：0 未计算，1 否，2 是。存在实例上而不是静态字典，避免持有已卸载模组的 Type
        private byte captureStageOverrideState;
        #endregion

        /// <summary>
        /// 密封内容
        /// </summary>
        protected override void VaultRegister() {
            if (!VaultUtils.isServer && ScreenSlot > 0) {
                Main.QueueMainThreadAction(() => {
                    CreateScreenTargets();
                });
            }

            registerOrder = Instances.Count;
            Instances.Add(this);
            Instances.Sort(CompareOrder);
        }

        //先比权重，再比注册先后，保证权重相同的实例次序不随 List.Sort 的不稳定性漂移
        private static int CompareOrder(RenderHandle a, RenderHandle b) {
            int byWeight = a.Weight.CompareTo(b.Weight);
            return byWeight != 0 ? byWeight : a.registerOrder.CompareTo(b.registerOrder);
        }

        //本类型是否重写了任一 EndCapture 阶段，结果按实例缓存
        private bool OverridesCaptureStage() {
            if (captureStageOverrideState == 0) {
                bool overridden = IsStageOverridden(nameof(EndCaptureDraw)) || IsStageOverridden(nameof(PostEndCaptureDraw));
                captureStageOverrideState = overridden ? (byte)2 : (byte)1;
            }
            return captureStageOverrideState == 2;
        }

        private bool IsStageOverridden(string stageName) {
            MethodInfo method = GetType().GetMethod(stageName, BindingFlags.Public | BindingFlags.Instance, null
                , [typeof(SpriteBatch), typeof(GraphicsDevice), typeof(RenderTarget2D)], null);
            return method != null && method.DeclaringType != typeof(RenderHandle);
        }

        /// <summary>
        /// 内容初始化方法，在加载内容时调用
        /// </summary>
        public override void VaultSetup() {
            SetStaticDefaults();
        }

        /// <summary>
        /// 分辨率变化时调用，可以重置 RenderTarget 或进行布局调整<br/>
        /// 调用前框架已重建 <see cref="RenderHandleLoader.ScreenSwap"/> 与本实例的 <see cref="ScreenTargets"/>，旧内容已丢失
        /// </summary>
        /// <param name="screenSize">屏幕大小向量</param>
        public virtual void OnResolutionChanged(Vector2 screenSize) {

        }

        #region 世界生命周期
        /// <summary>
        /// 进入世界时调用，对应 <c>ModSystem.OnWorldLoad</c>，不会在服务器上调用
        /// </summary>
        public virtual void OnWorldLoad() {

        }

        /// <summary>
        /// 离开世界时调用，不会在服务器上调用<br/>
        /// 覆盖 <c>ModSystem.OnWorldUnload</c>（存档退出、断线）与 <c>ModSystem.ClearWorld</c>（子世界切换、进入新世界前的清空）两条路，
        /// 框架保证每离开一个世界只触发一次<br/>
        /// 需要在回到主菜单或切换世界时复位的状态放在这里；<see cref="UpdateBySystem"/> 在主菜单不运行，无法承担这个职责
        /// </summary>
        public virtual void OnWorldUnload() {

        }
        #endregion

        #region 渲染管线阶段（EndCapture）
        /// <summary>
        /// 捕获结束时绘制的回调，用于 RT 管线级操作（如自定义后处理）<br/>
        /// SpriteBatch 状态由实现者完全自行管理，进入时未活跃，返回前必须 End<br/>
        /// 仅在 <see cref="Main.gameMenu"/> 为 <see langword="false"/> 且本帧确实捕获了主画面时调用：
        /// 原版只在任一场景滤镜激活时捕获，框架依据 <see cref="RequireScreenCapture"/> 自动让捕获常驻；
        /// 相机模式截图与其他模组的手工捕获不会触发本阶段<br/>
        /// 调用时 <see cref="Main.screenTarget"/> 为当前绑定的 RT，典型写法见
        /// <see cref="RenderHandleLoader.ApplyScreenEffect"/>
        /// </summary>
        /// <param name="spriteBatch">绘制画布，等价于 <see cref="Main.spriteBatch"/></param>
        /// <param name="graphicsDevice">渲染对象，等价于 Main.instance.GraphicsDevice</param>
        /// <param name="screenSwap">一个主动给予和自动维护的中间屏幕对象，作用类似于 <see cref="Main.screenTargetSwap"/>，
        /// 如果需要实际修改画面，请使用 <see cref="Main.screenTarget"/></param>
        public virtual void EndCaptureDraw(SpriteBatch spriteBatch, GraphicsDevice graphicsDevice, RenderTarget2D screenSwap) {

        }

        /// <summary>
        /// 捕获结束时绘制的回调，运行在 <see cref="EndCaptureDraw"/> 之后<br/>
        /// SpriteBatch 状态由实现者完全自行管理，进入时未活跃，返回前必须 End<br/>
        /// 在 <see cref="Main.gameMenu"/> 为 <see langword="true"/> 的情况下仍旧会被调用，
        /// 但主菜单本身不捕获画面，实际只有世界生成期间的 Sepia 滤镜等少数情形能触发
        /// </summary>
        /// <param name="spriteBatch">绘制画布，等价于 <see cref="Main.spriteBatch"/></param>
        /// <param name="graphicsDevice">渲染对象，等价于 Main.instance.GraphicsDevice</param>
        /// <param name="screenSwap">一个主动给予和自动维护的中间屏幕对象，作用类似于 <see cref="Main.screenTargetSwap"/>，
        /// 如果需要实际修改画面，请使用 <see cref="Main.screenTarget"/></param>
        public virtual void PostEndCaptureDraw(SpriteBatch spriteBatch, GraphicsDevice graphicsDevice, RenderTarget2D screenSwap) {

        }
        #endregion

        #region 分层绘制阶段
        /// <summary>
        /// 在物块绘制之前（墙壁和黑色背景之后）绘制<br/>
        /// 调用时 SpriteBatch 处于活跃状态，函数内部如需切换渲染状态应先 End 再 Begin，返回时必须保持 Active
        /// </summary>
        /// <param name="spriteBatch">绘制画布，等价于 <see cref="Main.spriteBatch"/></param>
        /// <param name="graphicsDevice">渲染对象，等价于 Main.instance.GraphicsDevice</param>
        /// <param name="screenSwap">自动维护的中间屏幕对象，可用于 RT 管线级操作</param>
        public virtual void DrawBeforeTiles(SpriteBatch spriteBatch, GraphicsDevice graphicsDevice, RenderTarget2D screenSwap) {

        }

        /// <summary>
        /// 在实心物块绘制之后、NPC 绘制之前绘制<br/>
        /// 注意它早于 <see cref="DrawAfterTiles"/> 触发，也早于弹幕层<br/>
        /// 调用时 SpriteBatch 未处于活跃状态，函数内部需自行 Begin 绘制，返回前必须 End
        /// </summary>
        /// <param name="spriteBatch">绘制画布，等价于 <see cref="Main.spriteBatch"/></param>
        /// <param name="graphicsDevice">渲染对象，等价于 Main.instance.GraphicsDevice</param>
        /// <param name="screenSwap">自动维护的中间屏幕对象，可用于 RT 管线级操作</param>
        public virtual void DrawNPCsOverTiles(SpriteBatch spriteBatch, GraphicsDevice graphicsDevice, RenderTarget2D screenSwap) {

        }

        /// <summary>
        /// 在 <c>ModSystem.PostDrawTiles</c> 时机绘制：物块与贴地 NPC 之后、弹幕之前<br/>
        /// 调用时 SpriteBatch 未处于活跃状态，函数内部需自行 Begin 绘制，返回前必须 End
        /// </summary>
        /// <param name="spriteBatch">绘制画布，等价于 <see cref="Main.spriteBatch"/></param>
        /// <param name="graphicsDevice">渲染对象，等价于 Main.instance.GraphicsDevice</param>
        /// <param name="screenSwap">自动维护的中间屏幕对象，可用于 RT 管线级操作</param>
        public virtual void DrawAfterTiles(SpriteBatch spriteBatch, GraphicsDevice graphicsDevice, RenderTarget2D screenSwap) {

        }

        /// <summary>
        /// 在玩家绘制之前绘制<br/>
        /// 原版每帧画两轮玩家，本阶段默认随之触发两次；用 <see cref="PlayerDrawPasses"/> 选择响应哪一轮，
        /// 当前轮次可读 <see cref="RenderHandleLoader.CurrentPlayerDrawPass"/><br/>
        /// 调用时 SpriteBatch 未处于活跃状态，函数内部需自行 Begin 绘制，返回前必须 End
        /// </summary>
        /// <param name="spriteBatch">绘制画布，等价于 <see cref="Main.spriteBatch"/></param>
        /// <param name="graphicsDevice">渲染对象，等价于 Main.instance.GraphicsDevice</param>
        /// <param name="screenSwap">自动维护的中间屏幕对象，可用于 RT 管线级操作</param>
        public virtual void DrawBeforePlayers(SpriteBatch spriteBatch, GraphicsDevice graphicsDevice, RenderTarget2D screenSwap) {

        }

        /// <summary>
        /// 在玩家绘制之后绘制<br/>
        /// 触发轮次规则与 <see cref="DrawBeforePlayers"/> 相同<br/>
        /// 调用时 SpriteBatch 未处于活跃状态，函数内部需自行 Begin 绘制，返回前必须 End
        /// </summary>
        /// <param name="spriteBatch">绘制画布，等价于 <see cref="Main.spriteBatch"/></param>
        /// <param name="graphicsDevice">渲染对象，等价于 Main.instance.GraphicsDevice</param>
        /// <param name="screenSwap">自动维护的中间屏幕对象，可用于 RT 管线级操作</param>
        public virtual void DrawAfterPlayers(SpriteBatch spriteBatch, GraphicsDevice graphicsDevice, RenderTarget2D screenSwap) {

        }

        /// <summary>
        /// 在 DrawInfernoRings 调用之前绘制，此时实体、粒子、水体与电线均已画完<br/>
        /// 调用时 SpriteBatch 处于活跃状态，函数内部如需切换渲染状态应先 End 再 Begin，返回时必须保持 Active
        /// </summary>
        /// <param name="spriteBatch">绘制画布，等价于 <see cref="Main.spriteBatch"/></param>
        /// <param name="graphicsDevice">渲染对象，等价于 Main.instance.GraphicsDevice</param>
        /// <param name="screenSwap">自动维护的中间屏幕对象，可用于 RT 管线级操作</param>
        public virtual void DrawBeforeInfernoRings(SpriteBatch spriteBatch, GraphicsDevice graphicsDevice, RenderTarget2D screenSwap) {

        }

        /// <summary>
        /// 在实体（粒子等）绘制结束后绘制，与两个 <c>EndEntityDraw</c> 重载同一时机、在它们之后调用<br/>
        /// 此函数在调用时不会自动设置画布，需要自行管理 SpriteBatch 的状态，返回前必须结束
        /// </summary>
        /// <param name="spriteBatch">绘制画布，等价于 <see cref="Main.spriteBatch"/></param>
        /// <param name="graphicsDevice">渲染对象，等价于 Main.instance.GraphicsDevice</param>
        /// <param name="screenSwap">自动维护的中间屏幕对象，可用于 RT 管线级操作</param>
        public virtual void DrawAfterEntities(SpriteBatch spriteBatch, GraphicsDevice graphicsDevice, RenderTarget2D screenSwap) {

        }

        /// <summary>
        /// 实体绘制结束后的回调，可以在此绘制额外效果<br/>
        /// 此函数在调用时不会自动设置画布，需要自行管理 SpriteBatch 的状态，返回前必须结束
        /// </summary>
        /// <param name="spriteBatch">绘制画布，等价于 <see cref="Main.spriteBatch"/></param>
        /// <param name="main">Main 实例</param>
        /// <param name="graphicsDevice">渲染对象，等价于 Main.instance.GraphicsDevice</param>
        /// <param name="screenSwap">自动维护的中间屏幕对象，可用于 RT 管线级操作</param>
        public virtual void EndEntityDraw(SpriteBatch spriteBatch, Main main, GraphicsDevice graphicsDevice, RenderTarget2D screenSwap) {

        }

        /// <summary>
        /// 实体绘制结束后的回调，可以在此绘制额外效果<br/>
        /// 遗留重载，与四参重载同一时机、先于它调用，继续保留兼容；新代码优先使用
        /// <see cref="EndEntityDraw(SpriteBatch, Main, GraphicsDevice, RenderTarget2D)"/> 或 <see cref="DrawAfterEntities"/><br/>
        /// 此函数在调用时不会自动设置画布，需要自行管理 SpriteBatch 的状态，返回前必须结束
        /// </summary>
        /// <param name="spriteBatch">绘制画布，等价于 <see cref="Main.spriteBatch"/></param>
        /// <param name="main">Main 实例</param>
        public virtual void EndEntityDraw(SpriteBatch spriteBatch, Main main) {

        }
        #endregion

        /// <summary>
        /// 逻辑更新函数，不会在服务器上调用，一般用于进行不受刷新速度影响的点滴计算<br/>
        /// 挂在 <c>ModSystem.PostUpdateEverything</c>：客户端在主菜单与暂停时不运行，
        /// 回到主菜单的复位逻辑请放到 <see cref="OnWorldUnload"/><br/>
        /// 不要在此处做绘制通道的初始化操作，该钩子的运行时机不一定在渲染通道之前
        /// </summary>
        /// <param name="index">该实例的更新队列索引</param>
        public virtual void UpdateBySystem(int index) {

        }

        /// <summary>
        /// 创建实例屏幕数组，已有的屏幕对象会先被释放再重新创建
        /// </summary>
        public void CreateScreenTargets() {
            if (ScreenTargets?.Length != ScreenSlot) {
                DisposeScreenTargets();
                ScreenTargets = new RenderTarget2D[ScreenSlot];
            }

            for (int i = 0; i < ScreenTargets.Length; i++) {
                ScreenTargets[i]?.Dispose();
                ScreenTargets[i] = new RenderTarget2D(Main.instance.GraphicsDevice, Main.screenWidth, Main.screenHeight);
            }
        }

        /// <summary>
        /// 释放并清空实例屏幕数组
        /// </summary>
        public void DisposeScreenTargets() {
            if (ScreenTargets == null) {
                return;
            }

            foreach (var rt in ScreenTargets) {
                rt?.Dispose();
            }
            ScreenTargets = null;
        }
    }
}
