using InnoVault.Narrative.Core;
using InnoVault.Narrative.Presentation;
using InnoVault.Narrative.Presentation.Choices;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Graphics;
using System;
using Terraria;

namespace InnoVault.Narrative.Styling
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
        /// <summary>最多可见选项数</summary>
        public virtual int MaxVisibleOptions => 8;
        /// <summary>面板内边距</summary>
        public virtual float Padding => 14f;
        /// <summary>选项高度</summary>
        public virtual float OptionHeight => 32f;
        /// <summary>选项间距</summary>
        public virtual float OptionSpacing => 8f;
        /// <summary>标题区域高度</summary>
        public virtual float TitleHeight => 24f;
        /// <summary>最小面板宽度</summary>
        public virtual float MinWidth => 200f;
        /// <summary>最大面板宽度</summary>
        public virtual float MaxWidth => 440f;

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

        /// <summary>计算选择框布局</summary>
        public virtual void Layout(DynamicSpriteFont font, Vector2 anchor, float openProgress, int scrollOffset,
            bool timedActive, float timedProgress, float globalTimer, ChoiceLayoutContext context, bool isClosing = false) {
            context.Font = font;
            context.TextScale = TextScale;
            context.ScrollOffset = scrollOffset;
            context.VisibleCount = Math.Min(context.Options.Count, MaxVisibleOptions);
            context.HasScroll = context.Options.Count > context.VisibleCount;
            context.TimedActive = timedActive;
            context.TimedProgress = timedProgress;
            context.Alpha = NarrativePanelMotion.ResolveAlpha(openProgress, NarrativePanelMotion.Profile.Choice);
            context.GlobalTimer = globalTimer;

            float maxTextWidth = font.MeasureString(ResolveChoiceTitle()).X * 0.85f;
            foreach (ChoiceOptionPresentation option in context.Options) {
                string text = GetOptionDisplayText(option);
                float width = font.MeasureString(text).X * TextScale;
                if (width > maxTextWidth) {
                    maxTextWidth = width;
                }
            }

            float panelWidth = MathHelper.Clamp(maxTextWidth + Padding * 4f, MinWidth, MaxWidth);
            float panelHeight = Padding * 2f + TitleHeight + context.VisibleCount * OptionHeight + Math.Max(0, context.VisibleCount - 1) * OptionSpacing;

            Vector2 pos = new(anchor.X - panelWidth / 2f, anchor.Y - panelHeight);
            pos.Y += NarrativePanelMotion.ResolveSlide(openProgress, isClosing, NarrativePanelMotion.Profile.Choice);
            context.PanelRect = new Rectangle((int)pos.X, (int)pos.Y, (int)panelWidth, (int)panelHeight);
            context.TitleRect = new Rectangle(context.PanelRect.X + (int)Padding, context.PanelRect.Y + (int)Padding - 2, (int)(panelWidth - Padding * 2f), (int)TitleHeight);
            context.DividerRect = new Rectangle(context.TitleRect.X, context.TitleRect.Bottom - 2, context.TitleRect.Width, 1);

            context.OptionRects.Clear();
            float optionY = context.PanelRect.Y + Padding + TitleHeight;
            for (int i = 0; i < context.VisibleCount; i++) {
                context.OptionRects.Add(new Rectangle(context.PanelRect.X + (int)Padding, (int)optionY, (int)(panelWidth - Padding * 2f), (int)OptionHeight));
                optionY += OptionHeight + OptionSpacing;
            }
        }

        /// <summary>每帧更新皮肤状态。默认无状态</summary>
        public virtual void Update(ChoiceLayoutContext context) { }

        /// <summary>样式切换或选项列表变化时重置皮肤状态</summary>
        public virtual void Reset() { }

        /// <summary>选择框标题文案，消费者可重写以接入本地化</summary>
        protected virtual string ResolveChoiceTitle() => "Choose";

        /// <summary>绘制选择框面板</summary>
        public virtual void DrawPanel(SpriteBatch spriteBatch, ChoiceLayoutContext context)
            => DrawPanel(spriteBatch, context.PanelRect, context.Alpha);

        /// <summary>绘制面板背景之上的装饰（粒子等，位于选项文字下方）</summary>
        public virtual void DrawBackgroundDecorations(SpriteBatch spriteBatch, ChoiceLayoutContext context) { }

        /// <summary>绘制标题</summary>
        public virtual void DrawTitle(SpriteBatch spriteBatch, ChoiceLayoutContext context) {
            DrawTitleDecoration(spriteBatch, context);
            Utils.DrawBorderString(spriteBatch, ResolveChoiceTitle(), context.TitleRect.Location.ToVector2(), HighlightColor * context.Alpha, 0.85f);
        }

        /// <summary>绘制标题装饰</summary>
        public virtual void DrawTitleDecoration(SpriteBatch spriteBatch, ChoiceLayoutContext context) { }

        /// <summary>绘制标题分割线</summary>
        public virtual void DrawDivider(SpriteBatch spriteBatch, ChoiceLayoutContext context)
            => NarrativeSkinDraw.FillRect(spriteBatch, context.DividerRect, HighlightColor * (context.Alpha * 0.45f));

        /// <summary>绘制全部可见选项</summary>
        public virtual void DrawOptions(SpriteBatch spriteBatch, ChoiceLayoutContext context) {
            for (int i = 0; i < context.OptionRects.Count; i++) {
                int optionIndex = context.ScrollOffset + i;
                if (optionIndex >= context.Options.Count) {
                    break;
                }
                ChoiceOptionPresentation option = context.Options[optionIndex];
                Rectangle rect = context.OptionRects[i];
                float hover = optionIndex == context.HoverIndex && option.Enabled ? 1f : 0f;
                DrawOptionBackground(spriteBatch, context, option, rect, optionIndex, hover);
                DrawOptionText(spriteBatch, context, option, rect, optionIndex, hover);
            }
        }

        /// <summary>绘制单个选项背景</summary>
        public virtual void DrawOptionBackground(SpriteBatch spriteBatch, ChoiceLayoutContext context, ChoiceOptionPresentation option, Rectangle rect, int optionIndex, float hover)
            => DrawOption(spriteBatch, rect, option.Enabled, hover, context.Alpha);

        /// <summary>绘制单个选项文字</summary>
        public virtual void DrawOptionText(SpriteBatch spriteBatch, ChoiceLayoutContext context, ChoiceOptionPresentation option, Rectangle rect, int optionIndex, float hover) {
            string text = GetOptionDisplayText(option);
            Color textColor = option.Enabled ? TextColor : DisabledTextColor;
            Vector2 textPos = new(rect.X + 8f, rect.Center.Y - context.Font.MeasureString(text).Y * TextScale / 2f);
            Utils.DrawBorderString(spriteBatch, text, textPos, textColor * context.Alpha, TextScale);
        }

        /// <summary>绘制滚动提示</summary>
        public virtual void DrawScrollHints(SpriteBatch spriteBatch, ChoiceLayoutContext context) {
            if (!context.HasScroll) {
                return;
            }

            int maxOffset = context.Options.Count - context.VisibleCount;
            if (context.ScrollOffset > 0) {
                Utils.DrawBorderString(spriteBatch, "^", new Vector2(context.PanelRect.Right - 16f, context.PanelRect.Y + Padding + 2f), HighlightColor * context.Alpha, 0.8f);
            }
            if (context.ScrollOffset < maxOffset) {
                Utils.DrawBorderString(spriteBatch, "v", new Vector2(context.PanelRect.Right - 16f, context.PanelRect.Bottom - 20f), HighlightColor * context.Alpha, 0.8f);
            }
        }

        /// <summary>绘制限时指示</summary>
        public virtual void DrawTimedIndicator(SpriteBatch spriteBatch, ChoiceLayoutContext context) {
            if (!context.TimedActive) {
                return;
            }
            int barWidth = (int)(context.PanelRect.Width * context.TimedProgress);
            Rectangle bar = new(context.PanelRect.X, context.PanelRect.Y - 4, barWidth, 3);
            Color color = Color.Lerp(new Color(255, 90, 80), HighlightColor, context.TimedProgress);
            NarrativeSkinDraw.FillRect(spriteBatch, bar, color * context.Alpha);
        }

        /// <summary>绘制最前景装饰（应保持在选项文字之上，默认无）</summary>
        public virtual void DrawForegroundDecorations(SpriteBatch spriteBatch, ChoiceLayoutContext context) { }

        /// <summary>获取选项展示文本（含禁用提示）</summary>
        protected virtual string GetOptionDisplayText(ChoiceOptionPresentation option) {
            if (!option.Enabled && !string.IsNullOrEmpty(option.DisabledHint)) {
                return $"{option.Text} ({option.DisabledHint})";
            }
            return option.Text ?? string.Empty;
        }

        /// <summary>点击有效选项时播放</summary>
        public virtual void PlaySelectSound() => NarrativeAudioDefaults.Play(NarrativeAudioDefaults.ChoiceSelect);

        /// <summary>点击禁用选项时播放</summary>
        public virtual void PlayDisabledSelectSound() => NarrativeAudioDefaults.Play(NarrativeAudioDefaults.ChoiceDisabled);
    }

    /// <summary>框架内置的朴素默认选择框皮肤</summary>
    public sealed class BasicChoiceSkin : ChoiceSkin { }
}
