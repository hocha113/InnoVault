using Microsoft.Xna.Framework;

namespace InnoVault.Narrative.Presentation.Anchors
{
    /// <summary>
    /// 为选择框、弹窗等附属 UI 提供当前叙事主面板锚点。
    /// Consumer 自定义对话视图时只需要注册自己的 provider，附属 UI 不必知道具体视图类型。
    /// </summary>
    public interface INarrativePanelAnchorProvider
    {
        /// <summary>当前对话面板矩形；无有效面板时返回 <see cref="Rectangle.Empty"/>。</summary>
        Rectangle DialoguePanelRect { get; }
    }
}
