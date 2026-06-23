using Microsoft.Xna.Framework;
using ReLogic.Graphics;
using System.Collections.Generic;

namespace InnoVault.Narrative.Presentation.Backlog
{
    /// <summary>单行历史记录折行布局后的几何信息（<see cref="Top"/> 为内容坐标，绘制时再叠加滚动偏移）</summary>
    public sealed class BacklogRowLayout
    {
        /// <summary>对应的展示态</summary>
        public BacklogRowPresentation Source;
        /// <summary>折行后的正文</summary>
        public string[] WrappedText = [];
        /// <summary>相对内容顶部的 Y（未叠加滚动）</summary>
        public float Top;
        /// <summary>本行总高度</summary>
        public float Height;
    }

    /// <summary>backlog 单帧布局与展示上下文。皮肤的绘制 hook 只读该上下文</summary>
    public sealed class BacklogLayoutContext
    {
        /// <summary>面板整体矩形</summary>
        public Rectangle PanelRect;
        /// <summary>标题栏矩形</summary>
        public Rectangle TitleRect;
        /// <summary>可滚动内容视口矩形</summary>
        public Rectangle ListRect;
        /// <summary>关闭按钮的可点击矩形</summary>
        public Rectangle CloseRect;
        /// <summary>折行后的各行布局</summary>
        public readonly List<BacklogRowLayout> Rows = [];
        /// <summary>绘制字体</summary>
        public DynamicSpriteFont Font;
        /// <summary>面板整体透明度 0~1</summary>
        public float Alpha;
        /// <summary>内容透明度（通常等于 <see cref="Alpha"/>）</summary>
        public float ContentAlpha;
        /// <summary>全局动画计时器（秒）</summary>
        public float GlobalTimer;
        /// <summary>当前滚动偏移（像素）</summary>
        public float ScrollOffset;
        /// <summary>内容总高度</summary>
        public float ContentHeight;
        /// <summary>最大可滚动偏移（>=0）</summary>
        public float MaxScroll;
        /// <summary>是否需要滚动</summary>
        public bool HasScroll;
        /// <summary>滚动条轨道矩形（由皮肤在布局阶段算出，供拖拽命中测试）</summary>
        public Rectangle ScrollTrackRect;
        /// <summary>滚动条滑块矩形（由皮肤在布局阶段算出，供拖拽命中测试）</summary>
        public Rectangle ScrollThumbRect;
        /// <summary>鼠标是否悬停在滚动条滑块上</summary>
        public bool HoverScrollThumb;
        /// <summary>是否正在拖拽滚动条滑块</summary>
        public bool DraggingScroll;
        /// <summary>鼠标是否悬停在关闭按钮上</summary>
        public bool HoverClose;
        /// <summary>当前是否无任何历史记录</summary>
        public bool IsEmpty;
    }
}
