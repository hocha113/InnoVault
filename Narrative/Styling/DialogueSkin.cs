using InnoVault.Narrative.Core;
using InnoVault.Narrative.Presentation;
using InnoVault.Narrative.Presentation.Dialogue;
using InnoVault.Narrative.Runtime;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Linq;
using Terraria;
using Terraria.GameContent;

namespace InnoVault.Narrative.Styling
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
        /// <summary>面板内边距</summary>
        public virtual float Padding => 18f;
        /// <summary>文本行距</summary>
        public virtual float LineSpacing => 6f;
        /// <summary>头像区域大小</summary>
        public virtual float PortraitSize => 92f;
        /// <summary>头像与文本间距</summary>
        public virtual float PortraitGap => 14f;
        /// <summary>面板最小高度</summary>
        public virtual float MinPanelHeight => 110f;
        /// <summary>面板最大高度</summary>
        public virtual float MaxPanelHeight => 460f;
        /// <summary>说话者标题区域高度</summary>
        public virtual float HeaderHeight => 30f;
        /// <summary>底部命令提示文字缩放</summary>
        public virtual float HintScale => 0.7f;

        /// <summary>绘制对话框面板背景与边框</summary>
        public virtual void DrawPanel(SpriteBatch spriteBatch, Rectangle panel, float alpha)
            => NarrativeSkinDraw.DrawPanel(spriteBatch, panel, new Color(16, 22, 34), new Color(70, 130, 200), alpha);

        /// <summary>绘制立绘外框</summary>
        public virtual void DrawPortraitFrame(SpriteBatch spriteBatch, Rectangle frame, float alpha) {
            NarrativeSkinDraw.FillRect(spriteBatch, frame, new Color(8, 12, 20) * (alpha * 0.85f));
            NarrativeSkinDraw.DrawBorder(spriteBatch, frame, new Color(70, 130, 200) * alpha);
        }

        /// <summary>根据输入计算本帧布局。复杂 consumer 可重写以完全接管对话框布局。</summary>
        public virtual void Layout(DialogueLayoutInput input, DialogueLayoutContext context) {
            context.Font = input.Font;
            context.Portrait = input.Portrait;
            context.HasPortrait = input.Portrait != null;
            context.Silhouette = input.Silhouette;
            context.SpeakerName = input.SpeakerName ?? string.Empty;
            context.TextScale = TextScale;
            context.NameScale = NameScale;
            context.HintScale = HintScale;

            float textAreaWidth = PanelWidth - Padding * 2f - (context.HasPortrait ? PortraitSize + PortraitGap : 0f);
            if (textAreaWidth < 60f) {
                textAreaWidth = 60f;
            }

            string[] previewLines = VaultUtils.WrapText(input.Line.Text ?? string.Empty, input.Font, textAreaWidth, TextScale).ToArray();
            float lineHeight = input.Font.MeasureString("A").Y * TextScale + LineSpacing;
            float contentHeight = previewLines.Length * lineHeight;
            float panelHeight = MathHelper.Clamp(contentHeight + Padding * 2f + HeaderHeight, MinPanelHeight, MaxPanelHeight);
            if (context.HasPortrait) {
                panelHeight = Math.Max(panelHeight, PortraitSize + Padding * 2f);
            }

            float eased = VaultUtils.EaseOutCubic(input.OpenProgress);
            Vector2 size = new(PanelWidth, panelHeight);
            Vector2 pos = input.Anchor - new Vector2(size.X / 2f, size.Y);
            pos.Y += (1f - eased) * 60f;

            context.PanelRect = new Rectangle((int)pos.X, (int)pos.Y, (int)size.X, (int)size.Y);

            float textLeft = context.PanelRect.X + Padding;
            if (context.HasPortrait) {
                context.PortraitRect = new Rectangle((int)(context.PanelRect.X + Padding), (int)(context.PanelRect.Y + Padding), (int)PortraitSize, (int)PortraitSize);
                textLeft += PortraitSize + PortraitGap;
            }
            else {
                context.PortraitRect = Rectangle.Empty;
            }

            context.SpeakerRect = new Rectangle((int)textLeft, (int)(context.PanelRect.Y + Padding - 2f), (int)(context.PanelRect.Right - textLeft - Padding), (int)HeaderHeight);
            context.TextRect = new Rectangle((int)textLeft, (int)(context.PanelRect.Y + Padding + HeaderHeight), (int)(context.PanelRect.Right - textLeft - Padding), (int)(context.PanelRect.Height - Padding * 2f - HeaderHeight));
            context.LineHeight = lineHeight;

            LayoutCommandHints(context);
        }

        /// <summary>每帧更新皮肤状态。默认无状态。</summary>
        public virtual void Update(DialogueLayoutContext context) { }

        /// <summary>样式切换或新会话开始时重置皮肤状态。</summary>
        public virtual void Reset() { }

        /// <summary>布局底部命令提示的点击区域。</summary>
        public virtual void LayoutCommandHints(DialogueLayoutContext context) {
            float y = context.PanelRect.Bottom - 22f;
            float x = context.PanelRect.X + Padding;
            float autoW = context.Font.MeasureString(NarrativeUIText.Auto).X * HintScale;
            float fastW = context.Font.MeasureString(NarrativeUIText.Fast).X * HintScale;
            float skipW = context.Font.MeasureString(NarrativeUIText.Skip).X * HintScale;
            float continueW = context.Font.MeasureString(NarrativeUIText.ContinueGlyph).X * 0.9f;

            context.AutoRect = new Rectangle((int)x, (int)y, (int)autoW, 18);
            x += autoW + 14f;
            context.FastRect = new Rectangle((int)x, (int)y, (int)fastW, 18);
            x += fastW + 14f;
            context.SkipRect = new Rectangle((int)x, (int)y, (int)skipW, 18);
            context.ContinueRect = new Rectangle((int)(context.PanelRect.Right - continueW - 20f), context.PanelRect.Bottom - 28, (int)continueW + 12, 22);
        }

        /// <summary>绘制对话框背景。</summary>
        public virtual void DrawBackground(SpriteBatch spriteBatch, DialogueLayoutContext context)
            => DrawPanel(spriteBatch, context.PanelRect, context.Alpha);

        /// <summary>绘制头像框。</summary>
        public virtual void DrawPortraitFrame(SpriteBatch spriteBatch, DialogueLayoutContext context)
            => DrawPortraitFrame(spriteBatch, context.PortraitRect, context.Alpha);

        /// <summary>绘制说话者名称。</summary>
        public virtual void DrawSpeakerName(SpriteBatch spriteBatch, DialogueLayoutContext context) {
            if (!string.IsNullOrEmpty(context.SpeakerName)) {
                Utils.DrawBorderString(spriteBatch, context.SpeakerName, context.SpeakerRect.Location.ToVector2(), SpeakerColor * context.Alpha, NameScale);
            }
        }

        /// <summary>绘制说话者与正文间的装饰分隔线。</summary>
        public virtual void DrawDivider(SpriteBatch spriteBatch, DialogueLayoutContext context) { }

        /// <summary>绘制正文。</summary>
        public virtual void DrawText(SpriteBatch spriteBatch, DialogueLayoutContext context) {
            int remaining = context.VisibleChars;
            for (int i = 0; i < context.WrappedLines.Length; i++) {
                string fullLine = context.WrappedLines[i];
                string draw = fullLine;
                if (remaining < fullLine.Length) {
                    draw = fullLine[..Math.Max(0, remaining)];
                }
                if (draw.Length > 0) {
                    Vector2 pos = new(context.TextRect.X, context.TextRect.Y + i * context.LineHeight);
                    Utils.DrawBorderString(spriteBatch, draw, pos, TextColor * context.Alpha, TextScale);
                }
                remaining -= fullLine.Length;
                if (remaining <= 0) {
                    break;
                }
            }
        }

        /// <summary>绘制限时指示。</summary>
        public virtual void DrawTimedIndicator(SpriteBatch spriteBatch, DialogueLayoutContext context) {
            if (!context.TimedActive) {
                return;
            }
            int barWidth = (int)(context.PanelRect.Width * context.TimedProgress);
            Rectangle bar = new(context.PanelRect.X, context.PanelRect.Y - 4, barWidth, 3);
            Color color = Color.Lerp(new Color(255, 90, 80), HintColor, context.TimedProgress);
            NarrativeSkinDraw.FillRect(spriteBatch, bar, color * context.Alpha);
        }

        /// <summary>绘制底部命令提示。</summary>
        public virtual void DrawCommandHints(SpriteBatch spriteBatch, DialogueLayoutContext context) {
            if (!context.ShowHints) {
                return;
            }

            Color on = HintColor * context.Alpha;
            Color off = HintColor * (context.Alpha * 0.4f);
            Utils.DrawBorderString(spriteBatch, NarrativeUIText.Auto, context.AutoRect.Location.ToVector2(), context.AutoMode ? on : off, HintScale);
            Utils.DrawBorderString(spriteBatch, NarrativeUIText.Fast, context.FastRect.Location.ToVector2(), context.FastMode ? on : off, HintScale);
            Utils.DrawBorderString(spriteBatch, NarrativeUIText.Skip, context.SkipRect.Location.ToVector2(), off, HintScale);

            if (context.WaitingAdvance) {
                float blink = (float)(Math.Sin(context.GlobalTimer * 6f) * 0.5 + 0.5);
                Utils.DrawBorderString(spriteBatch, NarrativeUIText.ContinueGlyph, context.ContinueRect.Location.ToVector2(), HintColor * (context.Alpha * blink), 0.9f);
            }
        }

        /// <summary>绘制最前景装饰。</summary>
        public virtual void DrawForegroundDecorations(SpriteBatch spriteBatch, DialogueLayoutContext context) { }
    }

    /// <summary>框架内置的朴素默认对话框皮肤</summary>
    public sealed class BasicDialogueSkin : DialogueSkin { }
}
