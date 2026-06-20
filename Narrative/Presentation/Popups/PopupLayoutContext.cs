using InnoVault.Narrative.Core;
using Microsoft.Xna.Framework;
using ReLogic.Graphics;

namespace InnoVault.Narrative.Presentation.Popups
{
    /// <summary>功能弹窗单帧布局与展示上下文。</summary>
    public sealed class PopupLayoutContext
    {
        public PopupPayload Payload;
        public PopupPresentationState State;
        public Rectangle PanelRect;
        public Rectangle IconRect;
        public Rectangle TitleRect;
        public Rectangle BodyRect;
        public Rectangle HintRect;
        public DynamicSpriteFont Font;
        public string Title = string.Empty;
        public string Body;
        public int IconItemType;
        public bool RequireClaim;
        public float Alpha;
        public float GlobalTimer;
    }
}
