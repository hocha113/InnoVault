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
        internal static List<RenderHandle> Instances { get; set; } = new List<RenderHandle>();

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
        /// 注册方法，会在 VaultType 初始化时调用
        /// 添加实例到 Instances 列表并按 Weight 排序
        /// </summary>
        protected override void Register() {
            if (!CanLoad()) {
                return; // 不加载的情况直接返回
            }

            Instances.Add(this);
            // 按权重排序（默认从小到大）
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
        /// <param name="screenPos">屏幕大小向量</param>
        public virtual void OnResolutionChanged(Vector2 screenPos) {

        }

        /// <summary>
        /// 捕获结束时绘制的回调，可以自定义渲染逻辑
        /// </summary>
        /// <param name="screen">当前屏幕渲染目标</param>
        public virtual void EndCaptureDraw(RenderTarget2D screen) {

        }

        /// <summary>
        /// 实体绘制结束后的回调，可以在此绘制额外效果
        /// </summary>
        /// <param name="spriteBatch">用于绘制的 SpriteBatch</param>
        /// <param name="main">游戏主类</param>
        public virtual void EndEntityDraw(SpriteBatch spriteBatch, Main main) {

        }
    }
}
