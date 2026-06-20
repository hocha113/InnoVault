using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace InnoVault.Narrative
{
    /// <summary>
    /// 选择框皮肤。负责选项面板与单个选项背景的绘制及配色，<br/>
    /// 与 <see cref="DialogueSkin"/> 通过同一 <see cref="StyleId"/> 配对使用
    /// </summary>
    public abstract class ChoiceSkin
    {
        /// <summary>选项文字缩放</summary>
        public virtual float TextScale => 0.85f;
        /// <summary>启用选项的文字颜色</summary>
        public virtual Color TextColor => Color.White;
        /// <summary>禁用选项的文字颜色</summary>
        public virtual Color DisabledTextColor => new(110, 110, 120);
        /// <summary>悬停高亮颜色</summary>
        public virtual Color HighlightColor => new(120, 200, 255);

        /// <summary>绘制选项面板背景</summary>
        public virtual void DrawPanel(SpriteBatch spriteBatch, Rectangle panel, float alpha)
            => NarrativeSkinDraw.DrawPanel(spriteBatch, panel, new Color(14, 20, 32), new Color(70, 130, 200), alpha);

        /// <summary>绘制单个选项的背景</summary>
        public virtual void DrawOption(SpriteBatch spriteBatch, Rectangle rect, bool enabled, float hover, float alpha) {
            Color bg = enabled
                ? Color.Lerp(new Color(24, 36, 54), new Color(46, 78, 116), hover) * (alpha * 0.6f)
                : new Color(18, 18, 24) * (alpha * 0.4f);
            NarrativeSkinDraw.FillRect(spriteBatch, rect, bg);
            if (enabled && hover > 0.01f) {
                NarrativeSkinDraw.DrawBorder(spriteBatch, rect, HighlightColor * (alpha * hover * 0.7f), 1);
            }
        }
    }

    /// <summary>框架内置的朴素默认选择框皮肤</summary>
    public sealed class BasicChoiceSkin : ChoiceSkin { }
}
