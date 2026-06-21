using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Graphics;

namespace InnoVault.Narrative.Presentation.Dialogue
{
    /// <summary>
    /// 对话视图单帧布局结果。skin 的绘制 hook 只读该上下文，不应推进剧情
    /// </summary>
    public sealed class DialogueLayoutContext
    {
        /// <summary>对话面板整体矩形</summary>
        public Rectangle PanelRect;
        /// <summary>头像绘制区域</summary>
        public Rectangle PortraitRect;
        /// <summary>说话者名称绘制区域</summary>
        public Rectangle SpeakerRect;
        /// <summary>正文绘制区域</summary>
        public Rectangle TextRect;
        /// <summary>「继续」提示的可点击区域</summary>
        public Rectangle ContinueRect;
        /// <summary>自动播放开关的可点击区域</summary>
        public Rectangle AutoRect;
        /// <summary>快进开关的可点击区域</summary>
        public Rectangle FastRect;
        /// <summary>跳过至下一停顿点的可点击区域</summary>
        public Rectangle SkipRect;
        /// <summary>按行折行后的正文文本</summary>
        public string[] WrappedLines = [];
        /// <summary>当前已显示的字符数（打字机进度）</summary>
        public int VisibleChars;
        /// <summary>折行后正文总字符数</summary>
        public int TotalChars;
        /// <summary>当前行是否显示头像</summary>
        public bool HasPortrait;
        /// <summary>头像纹理</summary>
        public Texture2D Portrait;
        /// <summary>头像源矩形，为 null 时使用整张纹理</summary>
        public Rectangle? PortraitSourceRect;
        /// <summary>是否以剪影形式绘制头像</summary>
        public bool Silhouette;
        /// <summary>说话者显示名称</summary>
        public string SpeakerName = string.Empty;
        /// <summary>绘制正文与提示使用的字体</summary>
        public DynamicSpriteFont Font;
        /// <summary>正文文字缩放</summary>
        public float TextScale;
        /// <summary>说话者名称缩放</summary>
        public float NameScale;
        /// <summary>底部命令提示缩放</summary>
        public float HintScale;
        /// <summary>正文单行高度（像素）</summary>
        public float LineHeight;
        /// <summary>面板整体透明度 0~1</summary>
        public float Alpha;
        /// <summary>正文 / 名字 / 提示等内容透明度，通常为 contentFade × Alpha</summary>
        public float ContentAlpha;
        /// <summary>说话者切换时的缓入 0~1</summary>
        public float SpeakerSwitchEase = 1f;
        /// <summary>限时行的剩余进度 0~1</summary>
        public float TimedProgress;
        /// <summary>是否显示限时指示条</summary>
        public bool TimedActive;
        /// <summary>是否显示底部命令提示</summary>
        public bool ShowHints;
        /// <summary>当前行已打完且等待玩家推进</summary>
        public bool WaitingAdvance;
        /// <summary>自动播放是否开启</summary>
        public bool AutoMode;
        /// <summary>快进是否开启</summary>
        public bool FastMode;
        /// <summary>鼠标是否悬停在自动播放开关上</summary>
        public bool HoverAuto;
        /// <summary>鼠标是否悬停在快进开关上</summary>
        public bool HoverFast;
        /// <summary>鼠标是否悬停在跳过按钮上</summary>
        public bool HoverSkip;
        /// <summary>鼠标是否悬停在继续提示上</summary>
        public bool HoverContinue;
        /// <summary>全局动画计时器（秒），用于装饰性动画</summary>
        public float GlobalTimer;
        /// <summary>底行命令提示的统一底边 Y（布局与绘制共用）</summary>
        public float HintRowBaseline;
    }
}
