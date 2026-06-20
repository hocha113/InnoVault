using Microsoft.Xna.Framework;
using Terraria;

namespace InnoVault.Narrative.Presentation.Anchors
{
    /// <summary>
    /// 锚点解析助手。让选择框 / 功能弹窗可以稳定地相对对话框定位（例如悬浮在对话框上方），<br/>
    /// 当对话框不存在时回退到屏幕中心
    /// </summary>
    public static class PanelAnchorResolver
    {
        /// <summary>当前对话框面板矩形，无则为 <see cref="Rectangle.Empty"/></summary>
        public static Rectangle DialoguePanelRect
            => DialogueView.InstanceOrNull?.PanelRect ?? Rectangle.Empty;

        /// <summary>屏幕中心</summary>
        public static Vector2 ScreenCenter => new(Main.screenWidth / 2f, Main.screenHeight * 0.5f);

        /// <summary>对话框上方的锚点；无对话框时回退到屏幕中心偏上</summary>
        public static Vector2 AboveDialogue(float gap = 72f) {
            Rectangle rect = DialoguePanelRect;
            if (rect == Rectangle.Empty) {
                return new Vector2(Main.screenWidth / 2f, Main.screenHeight * 0.42f);
            }
            return new Vector2(rect.Center.X, rect.Y - gap);
        }
    }
}
