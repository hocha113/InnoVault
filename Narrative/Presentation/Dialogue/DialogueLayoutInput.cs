using InnoVault.Narrative.Runtime;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Graphics;

namespace InnoVault.Narrative.Presentation.Dialogue
{
    /// <summary>
    /// 对话布局输入。由 view 收集运行时、字体、头像等只读信息，skin 可据此测量和布局
    /// </summary>
    public sealed class DialogueLayoutInput
    {
        /// <summary>当前叙事会话</summary>
        public NarrativeSession Session { get; init; }
        /// <summary>当前行的展示态</summary>
        public LinePresentation Line { get; init; }
        /// <summary>绘制正文与提示使用的字体</summary>
        public DynamicSpriteFont Font { get; init; }
        /// <summary>当前说话者的头像纹理</summary>
        public Texture2D Portrait { get; init; }
        /// <summary>是否以剪影形式绘制头像</summary>
        public bool Silhouette { get; init; }
        /// <summary>当前说话者显示名称</summary>
        public string SpeakerName { get; init; }
        /// <summary>对话框锚点（屏幕坐标）</summary>
        public Vector2 Anchor { get; init; }
        /// <summary>面板进出场进度 0~1</summary>
        public float OpenProgress { get; init; }
        /// <summary>全局动画计时器（秒）</summary>
        public float GlobalTimer { get; init; }
        /// <summary>是否处于关闭过渡。关闭时使用与打开不同的缓动曲线</summary>
        public bool IsClosing { get; init; }
    }
}
