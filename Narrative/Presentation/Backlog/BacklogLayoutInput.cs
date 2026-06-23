using InnoVault.Narrative.History;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Graphics;
using System.Collections.Generic;

namespace InnoVault.Narrative.Presentation.Backlog
{
    /// <summary>
    /// 单条历史记录的展示态快照。由 backlog 视图把 <see cref="NarrativeLogEntry"/> 经
    /// <see cref="Portraits.PortraitRegistry"/> 解析成显示名 / 立绘后交给 <see cref="Styling.BacklogSkin"/> 绘制
    /// </summary>
    public sealed class BacklogRowPresentation
    {
        /// <summary>记录种类（台词 / 选择）</summary>
        public NarrativeLogKind Kind;
        /// <summary>说话者显示名（选择记录通常为空）</summary>
        public string SpeakerName = string.Empty;
        /// <summary>说话者立绘（可空）</summary>
        public Texture2D Portrait;
        /// <summary>立绘裁剪区域，为空时使用整张纹理</summary>
        public Rectangle? PortraitSource;
        /// <summary>是否以剪影绘制立绘</summary>
        public bool Silhouette;
        /// <summary>正文 / 选择文本</summary>
        public string Text = string.Empty;
        /// <summary>是否为某段对话的起始（用于在视图里加分隔）</summary>
        public bool StartsConversation;
    }

    /// <summary>
    /// backlog 皮肤布局所需的单帧输入。由视图填充，皮肤据此计算 <see cref="BacklogLayoutContext"/>
    /// </summary>
    public sealed class BacklogLayoutInput
    {
        /// <summary>全部行的展示态（最旧在前）</summary>
        public IReadOnlyList<BacklogRowPresentation> Rows;
        /// <summary>绘制使用的字体</summary>
        public DynamicSpriteFont Font;
        /// <summary>面板打开进度 0~1</summary>
        public float OpenProgress;
        /// <summary>全局动画计时器（秒）</summary>
        public float GlobalTimer;
        /// <summary>当前滚动偏移（像素，由视图维护）</summary>
        public float ScrollOffset;
        /// <summary>是否处于关闭过渡</summary>
        public bool IsClosing;
    }
}
