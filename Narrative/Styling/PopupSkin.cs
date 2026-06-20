using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace InnoVault.Narrative
{
    /// <summary>
    /// 功能弹窗皮肤。负责弹窗面板与配色；具体载荷（奖励 / 提示）的图标与正文<br/>
    /// 由 <see cref="PopupPayload.IconItemType"/> / <see cref="PopupPayload.BodyText"/> 暴露，<br/>
    /// 默认绘制逻辑据此渲染，皮肤层无需对载荷做类型判断
    /// </summary>
    public abstract class PopupSkin
    {
        /// <summary>面板尺寸</summary>
        public virtual Vector2 PanelSize => new(240f, 132f);
        /// <summary>标题颜色</summary>
        public virtual Color TitleColor => Color.White;
        /// <summary>正文颜色</summary>
        public virtual Color BodyColor => new(210, 220, 235);
        /// <summary>提示颜色</summary>
        public virtual Color HintColor => new(150, 200, 255);

        /// <summary>绘制弹窗面板</summary>
        public virtual void DrawPanel(SpriteBatch spriteBatch, Rectangle panel, float alpha)
            => NarrativeSkinDraw.DrawPanel(spriteBatch, panel, new Color(16, 24, 36), new Color(80, 150, 210), alpha);
    }

    /// <summary>框架内置的朴素默认弹窗皮肤</summary>
    public sealed class BasicPopupSkin : PopupSkin { }
}
