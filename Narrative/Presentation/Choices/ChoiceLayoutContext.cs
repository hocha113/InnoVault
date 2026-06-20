using Microsoft.Xna.Framework;
using ReLogic.Graphics;
using System.Collections.Generic;

namespace InnoVault.Narrative.Presentation.Choices
{
    /// <summary>选择框单帧布局与展示上下文。</summary>
    public sealed class ChoiceLayoutContext
    {
        public Rectangle PanelRect;
        public Rectangle TitleRect;
        public Rectangle DividerRect;
        public readonly List<Rectangle> OptionRects = [];
        public readonly List<ChoiceOptionPresentation> Options = [];
        public DynamicSpriteFont Font;
        public int ScrollOffset;
        public int VisibleCount;
        public int HoverIndex = -1;
        public bool HasScroll;
        public bool TimedActive;
        public float TimedProgress;
        public float Alpha;
        public float TextScale;
        public float GlobalTimer;
    }
}
