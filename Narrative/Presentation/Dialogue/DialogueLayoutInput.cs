using InnoVault.Narrative.Runtime;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Graphics;

namespace InnoVault.Narrative.Presentation.Dialogue
{
    /// <summary>
    /// 对话布局输入。由 view 收集运行时、字体、头像等只读信息，skin 可据此测量和布局。
    /// </summary>
    public sealed class DialogueLayoutInput
    {
        public NarrativeSession Session { get; init; }
        public LinePresentation Line { get; init; }
        public DynamicSpriteFont Font { get; init; }
        public Texture2D Portrait { get; init; }
        public bool Silhouette { get; init; }
        public string SpeakerName { get; init; }
        public Vector2 Anchor { get; init; }
        public float OpenProgress { get; init; }
        public float GlobalTimer { get; init; }
        /// <summary>是否处于关闭过渡。关闭时使用与打开不同的缓动曲线。</summary>
        public bool IsClosing { get; init; }
    }
}
