using InnoVault.Narrative.Core;
using Microsoft.Xna.Framework;
using ReLogic.Graphics;

namespace InnoVault.Narrative.Presentation.Popups
{
    /// <summary>功能弹窗单帧布局与展示上下文</summary>
    public sealed class PopupLayoutContext
    {
        /// <summary>当前弹窗的业务载荷</summary>
        public PopupPayload Payload;
        /// <summary>弹窗展示状态机快照</summary>
        public PopupPresentationState State;
        /// <summary>弹窗面板整体矩形</summary>
        public Rectangle PanelRect;
        /// <summary>图标绘制区域</summary>
        public Rectangle IconRect;
        /// <summary>标题绘制区域</summary>
        public Rectangle TitleRect;
        /// <summary>正文绘制区域</summary>
        public Rectangle BodyRect;
        /// <summary>底部提示文案绘制区域</summary>
        public Rectangle HintRect;
        /// <summary>绘制标题与正文使用的字体</summary>
        public DynamicSpriteFont Font;
        /// <summary>弹窗标题文本</summary>
        public string Title = string.Empty;
        /// <summary>弹窗正文文本</summary>
        public string Body;
        /// <summary>图标对应的物品类型 ID，0 表示不使用物品图标</summary>
        public int IconItemType;
        /// <summary>是否需要点击领取才能关闭</summary>
        public bool RequireClaim;
        /// <summary>面板整体透明度 0~1</summary>
        public float Alpha;
        /// <summary>图标 / 标题等内容出现进度 0~1</summary>
        public float ContentAppear = 1f;
        /// <summary>全局动画计时器（秒），用于装饰性动画</summary>
        public float GlobalTimer;
    }
}
