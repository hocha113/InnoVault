using InnoVault.Narrative.Core;
using InnoVault.Narrative.Presentation;
using InnoVault.Narrative.Presentation.Popups;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using Terraria;
using Terraria.GameContent;

namespace InnoVault.Narrative.Styling
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

        /// <summary>计算弹窗布局。</summary>
        public virtual void Layout(Vector2 anchor, float openProgress, float globalTimer, PopupLayoutContext context) {
            Vector2 size = PanelSize;
            float eased = VaultUtils.EaseOutCubic(openProgress);
            Vector2 pos = new(anchor.X - size.X / 2f, anchor.Y - size.Y / 2f);
            pos.Y -= (1f - eased) * 30f;

            context.PanelRect = new Rectangle((int)pos.X, (int)pos.Y, (int)size.X, (int)size.Y);
            Vector2 center = context.PanelRect.Center.ToVector2();
            context.IconRect = new Rectangle((int)(center.X - 34f), (int)(center.Y - 56f), 68, 68);
            context.TitleRect = new Rectangle(context.PanelRect.X + 12, (int)(center.Y + 10f), context.PanelRect.Width - 24, 24);
            context.BodyRect = new Rectangle(context.PanelRect.X + 12, (int)(center.Y + 32f), context.PanelRect.Width - 24, 24);
            context.HintRect = new Rectangle(context.PanelRect.X + 10, context.PanelRect.Bottom - 28, context.PanelRect.Width - 20, 20);
            context.Alpha = openProgress;
            context.GlobalTimer = globalTimer;
        }

        /// <summary>每帧更新皮肤状态。默认无状态。</summary>
        public virtual void Update(PopupLayoutContext context) { }

        /// <summary>样式切换或载荷变化时重置皮肤状态。</summary>
        public virtual void Reset() { }

        /// <summary>绘制弹窗面板。</summary>
        public virtual void DrawPanel(SpriteBatch spriteBatch, PopupLayoutContext context)
            => DrawPanel(spriteBatch, context.PanelRect, context.Alpha);

        /// <summary>绘制弹窗边框或前景框架。</summary>
        public virtual void DrawFrame(SpriteBatch spriteBatch, PopupLayoutContext context) { }

        /// <summary>绘制弹窗图标。</summary>
        public virtual void DrawIcon(SpriteBatch spriteBatch, PopupLayoutContext context) {
            if (context.IconItemType <= 0) {
                return;
            }

            Main.instance.LoadItem(context.IconItemType);
            Texture2D tex = TextureAssets.Item[context.IconItemType].Value;
            if (tex == null) {
                return;
            }

            float iconScale = Math.Min(48f / tex.Width, 48f / tex.Height);
            float floatOff = (float)Math.Sin(context.GlobalTimer * 3f) * 3f;
            Vector2 center = context.IconRect.Center.ToVector2() - new Vector2(0, floatOff);
            spriteBatch.Draw(tex, center, null, Color.White * context.Alpha, 0f, tex.Size() / 2f, iconScale * 1.4f, SpriteEffects.None, 0f);
        }

        /// <summary>绘制标题。</summary>
        public virtual void DrawTitle(SpriteBatch spriteBatch, PopupLayoutContext context) {
            if (string.IsNullOrEmpty(context.Title)) {
                return;
            }

            Vector2 size = context.Font.MeasureString(context.Title) * 0.8f;
            Utils.DrawBorderString(spriteBatch, context.Title,
                new Vector2(context.TitleRect.Center.X - size.X / 2f, context.TitleRect.Y), TitleColor * context.Alpha, 0.8f);
        }

        /// <summary>绘制正文。</summary>
        public virtual void DrawBody(SpriteBatch spriteBatch, PopupLayoutContext context) {
            if (string.IsNullOrEmpty(context.Body)) {
                return;
            }

            Vector2 size = context.Font.MeasureString(context.Body) * 0.7f;
            Utils.DrawBorderString(spriteBatch, context.Body,
                new Vector2(context.BodyRect.Center.X - size.X / 2f, context.BodyRect.Y), BodyColor * context.Alpha, 0.7f);
        }

        /// <summary>绘制底部提示。</summary>
        public virtual void DrawHint(SpriteBatch spriteBatch, PopupLayoutContext context) {
            string hint = context.RequireClaim ? NarrativeUIText.ClaimHint : NarrativeUIText.ContinueHint;
            Vector2 hintSize = context.Font.MeasureString(hint) * 0.6f;
            float blink = (float)(Math.Sin(context.GlobalTimer * 6f) * 0.5 + 0.5);
            Utils.DrawBorderString(spriteBatch, hint,
                new Vector2(context.HintRect.Right - hintSize.X, context.HintRect.Bottom - hintSize.Y),
                HintColor * (context.Alpha * (0.6f + blink * 0.4f)), 0.6f);
        }

        /// <summary>绘制粒子或额外装饰。</summary>
        public virtual void DrawParticles(SpriteBatch spriteBatch, PopupLayoutContext context) { }
    }

    /// <summary>框架内置的朴素默认弹窗皮肤</summary>
    public sealed class BasicPopupSkin : PopupSkin { }
}
