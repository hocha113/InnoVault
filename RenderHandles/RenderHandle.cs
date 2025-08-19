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
        /// <summary>
        /// 所有 <see cref="RenderHandle"/> 的单实例均存储于此
        /// </summary>
        public static List<RenderHandle> Instances { get; private set; } = [];
        /// <summary>
        /// 渲染权重，用于排序默认值为 1
        /// Weight 越大，在排序中越靠后
        /// </summary>
        public virtual float Weight => 1f;
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
        /// 释放缓存的屏幕资源
        /// </summary>
        internal void DisposeRender() {
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
    }
}
