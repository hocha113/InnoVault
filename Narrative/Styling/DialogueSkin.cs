using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace InnoVault.Narrative
{
    /// <summary>
    /// 对话框皮肤。负责面板 / 立绘框的绘制以及文本配色等视觉参数，<br/>
    /// 不持有任何剧情运行状态——状态属于 <see cref="NarrativeSession"/>。<br/>
    /// 通过 <see cref="StyleRegistry"/> 以 <see cref="StyleId"/> 注册，新增主题无需改动核心
    /// </summary>
    public abstract class DialogueSkin
    {
        /// <summary>面板固定宽度</summary>
        public virtual float PanelWidth => 520f;
        /// <summary>正文文字缩放</summary>
        public virtual float TextScale => 1f;
        /// <summary>说话者名字缩放</summary>
        public virtual float NameScale => 1.1f;
        /// <summary>正文颜色</summary>
        public virtual Color TextColor => new(235, 240, 255);
        /// <summary>说话者名字颜色</summary>
        public virtual Color SpeakerColor => new(180, 220, 255);
        /// <summary>底部提示（继续 / 自动 / 跳过）颜色</summary>
        public virtual Color HintColor => new(150, 190, 235);
        /// <summary>剪影立绘颜色</summary>
        public virtual Color SilhouetteColor => new(12, 18, 28);

        /// <summary>绘制对话框面板背景与边框</summary>
        public virtual void DrawPanel(SpriteBatch spriteBatch, Rectangle panel, float alpha)
            => NarrativeSkinDraw.DrawPanel(spriteBatch, panel, new Color(16, 22, 34), new Color(70, 130, 200), alpha);

        /// <summary>绘制立绘外框</summary>
        public virtual void DrawPortraitFrame(SpriteBatch spriteBatch, Rectangle frame, float alpha)
        {
            NarrativeSkinDraw.FillRect(spriteBatch, frame, new Color(8, 12, 20) * (alpha * 0.85f));
            NarrativeSkinDraw.DrawBorder(spriteBatch, frame, new Color(70, 130, 200) * alpha);
        }
    }

    /// <summary>框架内置的朴素默认对话框皮肤</summary>
    public sealed class BasicDialogueSkin : DialogueSkin { }
}
