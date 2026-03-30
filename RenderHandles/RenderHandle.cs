using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Collections.Generic;
using Terraria;
using Terraria.Graphics.Effects;

namespace InnoVault.RenderHandles
{
    /// <summary>
    /// 在系统中注册和管理渲染实例，提供自动管理的画布和 RT 对象，<br/>
    /// 每个绘制阶段对应一个独立的虚方法，实现者可同时重写多个阶段
    /// </summary>
    public abstract class RenderHandle : VaultType<RenderHandle>
    {
        #region Data
        /// <remarks>
        /// 存储所有已注册的 <see cref="RenderHandle"/> 实例，按 <see cref="Weight"/> 升序排序<br/>
        /// 所有 <see cref="RenderHandle"/> 的生命周期由 <see cref="RenderHandleLoader"/> 管理，
        /// 在卸载时会统一释放其持有的 <see cref="RenderTarget2D"/> 并清空 <see cref="Instances"/>
        /// </remarks>
        public new static List<RenderHandle> Instances { get; private set; } = [];
        /// <summary>
        /// 渲染权重，用于排序默认值为 1，Weight 越大，在排序中越靠后
        /// </summary>
        public virtual float Weight => 1f;
        /// <summary>
        /// 屏幕数量，决定 <see cref="ScreenTargets"/> 可以包含并管理多少块屏幕对象，
        /// 不要设置为过大的值，这可能会造成明显的游戏性能问题
        /// </summary>
        public virtual int ScreenSlot => 0;
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

            Instances.Add(this);
            Instances.Sort((a, b) => a.Weight.CompareTo(b.Weight));
        }

        /// <summary>
        /// 内容初始化方法，在加载内容时调用
        /// </summary>
        public override void VaultSetup() {
            SetStaticDefaults();
        }

        /// <summary>
        /// 分辨率变化时调用，可以重置 RenderTarget 或进行布局调整
        /// </summary>
        /// <param name="screenSize">屏幕大小向量</param>
        public virtual void OnResolutionChanged(Vector2 screenSize) {

        }

        #region 渲染管线阶段（EndCapture）
        /// <summary>
        /// 捕获结束时绘制的回调，用于 RT 管线级操作（如自定义后处理）<br/>
        /// SpriteBatch 状态由实现者完全自行管理<br/>
        /// 仅在 <see cref="Main.gameMenu"/> 为 <see langword="false"/> 时调用
        /// </summary>
        /// <param name="spriteBatch">绘制画布，等价于 <see cref="Main.spriteBatch"/></param>
        /// <param name="graphicsDevice">渲染对象，等价于 Main.instance.GraphicsDevice</param>
        /// <param name="screenSwap">一个主动给予和自动维护的中间屏幕对象，作用类似于 <see cref="Main.screenTargetSwap"/>，
        /// 如果需要实际修改画面，请使用 <see cref="Main.screenTarget"/></param>
        public virtual void EndCaptureDraw(SpriteBatch spriteBatch, GraphicsDevice graphicsDevice, RenderTarget2D screenSwap) {

        }

        /// <summary>
        /// 捕获结束时绘制的回调，运行在 <see cref="EndCaptureDraw"/> 之后<br/>
        /// SpriteBatch 状态由实现者完全自行管理<br/>
        /// 在 <see cref="Main.gameMenu"/> 为 <see langword="true"/> 的情况下仍旧会被调用
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
        /// 此函数在调用时不会自动设置画布，需要自行管理 SpriteBatch 的状态，返回时必须保持 Active
        /// </summary>
        /// <param name="spriteBatch">绘制画布，等价于 <see cref="Main.spriteBatch"/></param>
        /// <param name="graphicsDevice">渲染对象，等价于 Main.instance.GraphicsDevice</param>
        /// <param name="screenSwap">自动维护的中间屏幕对象，可用于 RT 管线级操作</param>
        public virtual void DrawBeforeTiles(SpriteBatch spriteBatch, GraphicsDevice graphicsDevice, RenderTarget2D screenSwap) {

        }

        /// <summary>
        /// 在NPC绘制之前，物块完成绘制之后绘制<br/>
        /// 此函数在调用时不会自动设置画布，需要自行管理 SpriteBatch 的状态，返回前必须结束
        /// </summary>
        /// <param name="spriteBatch">绘制画布，等价于 <see cref="Main.spriteBatch"/></param>
        /// <param name="graphicsDevice">渲染对象，等价于 Main.instance.GraphicsDevice</param>
        /// <param name="screenSwap">自动维护的中间屏幕对象，可用于 RT 管线级操作</param>
        public virtual void DrawNPCsOverTiles(SpriteBatch spriteBatch, GraphicsDevice graphicsDevice, RenderTarget2D screenSwap) {
            
        }

        /// <summary>
        /// 在物块绘制之后绘制<br/>
        /// 此函数在调用时不会自动设置画布，需要自行管理 SpriteBatch 的状态，返回时必须保持 Active
        /// </summary>
        /// <param name="spriteBatch">绘制画布，等价于 <see cref="Main.spriteBatch"/></param>
        /// <param name="graphicsDevice">渲染对象，等价于 Main.instance.GraphicsDevice</param>
        /// <param name="screenSwap">自动维护的中间屏幕对象，可用于 RT 管线级操作</param>
        public virtual void DrawAfterTiles(SpriteBatch spriteBatch, GraphicsDevice graphicsDevice, RenderTarget2D screenSwap) {

        }

        /// <summary>
        /// 在玩家绘制之前绘制<br/>
        /// 此函数在调用时不会自动设置画布，需要自行管理 SpriteBatch 的状态，返回时必须保持 Active
        /// </summary>
        /// <param name="spriteBatch">绘制画布，等价于 <see cref="Main.spriteBatch"/></param>
        /// <param name="graphicsDevice">渲染对象，等价于 Main.instance.GraphicsDevice</param>
        /// <param name="screenSwap">自动维护的中间屏幕对象，可用于 RT 管线级操作</param>
        public virtual void DrawBeforePlayers(SpriteBatch spriteBatch, GraphicsDevice graphicsDevice, RenderTarget2D screenSwap) {

        }

        /// <summary>
        /// 在玩家绘制之后绘制<br/>
        /// 此函数在调用时不会自动设置画布，需要自行管理 SpriteBatch 的状态，返回时必须保持 Active
        /// </summary>
        /// <param name="spriteBatch">绘制画布，等价于 <see cref="Main.spriteBatch"/></param>
        /// <param name="graphicsDevice">渲染对象，等价于 Main.instance.GraphicsDevice</param>
        /// <param name="screenSwap">自动维护的中间屏幕对象，可用于 RT 管线级操作</param>
        public virtual void DrawAfterPlayers(SpriteBatch spriteBatch, GraphicsDevice graphicsDevice, RenderTarget2D screenSwap) {

        }

        /// <summary>
        /// 在实体（粒子等）绘制结束后绘制<br/>
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
        /// 此函数在调用时不会自动设置画布，需要自行管理 SpriteBatch 的状态，返回前必须结束
        /// </summary>
        /// <param name="spriteBatch">绘制画布，等价于 <see cref="Main.spriteBatch"/></param>
        /// <param name="main">Main 实例</param>
        public virtual void EndEntityDraw(SpriteBatch spriteBatch, Main main) {

        }
        #endregion

        /// <summary>
        /// 逻辑更新函数，不会在服务器上调用，一般用于进行不受刷新速度影响的点滴计算<br/>
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
