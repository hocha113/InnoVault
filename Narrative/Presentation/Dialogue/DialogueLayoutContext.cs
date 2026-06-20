using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Graphics;

namespace InnoVault.Narrative.Presentation.Dialogue
{
    /// <summary>
    /// 对话视图单帧布局结果。skin 的绘制 hook 只读该上下文，不应推进剧情。
    /// </summary>
    public sealed class DialogueLayoutContext
    {
        public Rectangle PanelRect;
        public Rectangle PortraitRect;
        public Rectangle SpeakerRect;
        public Rectangle TextRect;
        public Rectangle ContinueRect;
        public Rectangle AutoRect;
        public Rectangle FastRect;
        public Rectangle SkipRect;
        public string[] WrappedLines = [];
        public int VisibleChars;
        public int TotalChars;
        public bool HasPortrait;
        public Texture2D Portrait;
        public bool Silhouette;
        public string SpeakerName = string.Empty;
        public DynamicSpriteFont Font;
        public float TextScale;
        public float NameScale;
        public float HintScale;
        public float LineHeight;
        public float Alpha;
        public float TimedProgress;
        public bool TimedActive;
        public bool ShowHints;
        public bool WaitingAdvance;
        public bool AutoMode;
        public bool FastMode;
        public bool HoverAuto;
        public bool HoverFast;
        public bool HoverSkip;
        public bool HoverContinue;
        public float GlobalTimer;
    }
}
