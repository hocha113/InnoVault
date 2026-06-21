using Microsoft.Xna.Framework;
using ReLogic.Graphics;
using System.Collections.Generic;

namespace InnoVault.Narrative.Presentation.Choices
{
    /// <summary>选择框单帧布局与展示上下文</summary>
    public sealed class ChoiceLayoutContext
    {
        /// <summary>选择面板整体矩形</summary>
        public Rectangle PanelRect;
        /// <summary>标题区域矩形</summary>
        public Rectangle TitleRect;
        /// <summary>标题与选项列表之间的分隔线矩形</summary>
        public Rectangle DividerRect;
        /// <summary>当前可见选项的可点击矩形列表</summary>
        public readonly List<Rectangle> OptionRects = [];
        /// <summary>当前选项的展示态快照</summary>
        public readonly List<ChoiceOptionPresentation> Options = [];
        /// <summary>绘制选项与标题使用的字体</summary>
        public DynamicSpriteFont Font;
        /// <summary>选项列表滚动偏移（从第几个选项开始显示）</summary>
        public int ScrollOffset;
        /// <summary>当前可见选项数量</summary>
        public int VisibleCount;
        /// <summary>鼠标悬停的选项索引，未悬停时为 -1</summary>
        public int HoverIndex = -1;
        /// <summary>选项总数是否超出可视区域，需要滚动</summary>
        public bool HasScroll;
        /// <summary>是否处于限时选择状态</summary>
        public bool TimedActive;
        /// <summary>限时选择的剩余进度 0~1</summary>
        public float TimedProgress;
        /// <summary>面板整体透明度 0~1</summary>
        public float Alpha;
        /// <summary>选项文字缩放</summary>
        public float TextScale;
        /// <summary>全局动画计时器（秒），用于装饰性动画</summary>
        public float GlobalTimer;
    }
}
