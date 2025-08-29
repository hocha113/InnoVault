using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Collections.Generic;
using Terraria;
using Terraria.Graphics.Effects;

namespace InnoVault.RenderHandles
{
    /// <summary>
    /// 在系统中注册和管理渲染实例
    /// </summary>
    public abstract class RenderHandle : VaultType
    {
        /// <remarks>
        /// 存储所有已注册的 <see cref="RenderHandle"/> 实例，按 <see cref="Weight"/> 升序排序<br/>
        /// 所有 <see cref="RenderHandle"/> 的生命周期由 <see cref="RenderHandleLoader"/> 管理，
        /// 在卸载时会统一释放其持有的 <see cref="RenderTarget2D"/> 并清空 <see cref="Instances"/>
        /// </remarks>
        public static List<RenderHandle> Instances { get; private set; } = [];
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
        /// FilterManager 引用，可用于处理后期滤镜
        /// </summary>
        public FilterManager filterManager;
        /// <summary>
        /// 最终渲染的 RenderTarget
        /// </summary>
        public RenderTarget2D finalTexture;
        /// <summary>
        /// 屏幕渲染目标1
        /// </summary>
        public RenderTarget2D screenTarget1;
        /// <summary>
        /// 屏幕渲染目标2
        /// </summary>
        public RenderTarget2D screenTarget2;
        /// <summary>
        /// 密封内容
        /// </summary>
        protected override void Register() {
            if (!CanLoad()) {
                return;
            }

            if (ScreenSlot > 0) {
                Main.QueueMainThreadAction(() => {
                    InitializeScreenTargets(true);
                });
            }
            
            Instances.Add(this);
            Instances.Sort((a, b) => a.Weight.CompareTo(b.Weight));
        }

        /// <summary>
        /// 内容初始化方法，在加载内容时调用
        /// </summary>
        public override void SetupContent() {
            if (!CanLoad()) {
                return;
            }

            SetStaticDefaults();
        }

        /// <summary>
        /// 分辨率变化时调用，可以重置 RenderTarget 或进行布局调整
        /// </summary>
        /// <param name="screenSize">屏幕大小向量</param>
        public virtual void OnResolutionChanged(Vector2 screenSize) {

        }

        /// <summary>
        /// 捕获结束时绘制的回调，可以自定义渲染逻辑
        /// </summary>
        /// <param name="spriteBatch">绘制画布，等价于 <see cref="Main.spriteBatch"/> </param>
        /// <param name="graphicsDevice">渲染对象，等价于 Main.instance.GraphicsDevice </param>
        /// <param name="screenSwap">一个主动给予和自动维护的中间屏幕对象，作用类似于 <see cref="Main.screenTargetSwap"/> ，
        /// 如果需要实际修改画面，请使用 <see cref="Main.screenTarget"/> </param>
        public virtual void EndCaptureDraw(SpriteBatch spriteBatch, GraphicsDevice graphicsDevice, RenderTarget2D screenSwap) {

        }

        /// <summary>
        /// 捕获结束时绘制的回调，可以自定义渲染逻辑，运行在 <see cref="EndCaptureDraw"/>之后 ，<br/>
        /// 在 <see cref="Main.gameMenu"/> 为 <see langword="true"/> 的情况下仍旧会被调用
        /// </summary>
        /// <param name="spriteBatch">绘制画布，等价于 <see cref="Main.spriteBatch"/> </param>
        /// <param name="graphicsDevice">渲染对象，等价于 Main.instance.GraphicsDevice </param>
        /// <param name="screenSwap">一个主动给予和自动维护的中间屏幕对象，作用类似于 <see cref="Main.screenTargetSwap"/> ，
        /// 如果需要实际修改画面，请使用 <see cref="Main.screenTarget"/> </param>
        public virtual void PostEndCaptureDraw(SpriteBatch spriteBatch, GraphicsDevice graphicsDevice, RenderTarget2D screenSwap) {

        }

        /// <summary>
        /// 实体绘制结束后的回调，可以在此绘制额外效果
        /// </summary>
        public virtual void EndEntityDraw(SpriteBatch spriteBatch, Main main) {

        }

        /// <summary>
        /// 释放缓存的屏幕字段和渲染对象
        /// 不要在游戏中途调用该方法，除非知道自己在做什么
        /// </summary>
        public void DisposeRenderTargets() {
            if (finalTexture?.IsDisposed == false) {
                finalTexture.Dispose();
            }
            if (screenTarget1?.IsDisposed == false) {
                screenTarget1.Dispose();
            }
            if (screenTarget2?.IsDisposed == false) {
                screenTarget2.Dispose();
            }
            finalTexture = null;
            screenTarget1 = null;
            screenTarget2 = null;
        }

        /// <summary>
        /// 设置实例屏幕数组
        /// </summary>
        /// <param name="create">是否进行创建，如果为 <see langword="false"/> 则充当释放函数</param>
        public void InitializeScreenTargets(bool create) {
            if (create && ScreenTargets?.Length != ScreenSlot) {
                if (ScreenTargets != null) {
                    foreach (var render in ScreenTargets) {
                        render?.Dispose();
                    }
                }
                ScreenTargets = new RenderTarget2D[ScreenSlot];
            }

            for (int i = 0; i < ScreenTargets?.Length; i++) {
                ScreenTargets[i]?.Dispose();
                ScreenTargets[i] = null;
                if (!create) {
                    continue;
                }
                ScreenTargets[i] = new RenderTarget2D(Main.instance.GraphicsDevice, Main.screenWidth, Main.screenHeight);
            }

            if (!create) {
                ScreenTargets = null;
            }
        }
    }
}
