using InnoVault.Narrative.Core;
using InnoVault.Narrative.Presentation;
using InnoVault.Narrative.Presentation.Dialogue;
using InnoVault.Narrative.Runtime;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Graphics;
using System;
using Terraria;

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
        /// <summary>正文区额外水平收窄（像素），为 shader 六角/斜切内缘留白</summary>
        public virtual float TextWrapInset => 0f;
        /// <summary>文本行距</summary>
        public virtual float LineSpacing => 6f;
        /// <summary>头像区域大小</summary>
        public virtual float PortraitSize => 92f;
        /// <summary>头像与文本间距</summary>
        public virtual float PortraitGap => 14f;
        /// <summary>头像框内边距（立绘缩放留白，0 表示立绘贴齐框线内侧）</summary>
        public virtual float PortraitInnerPadding => 0f;
        /// <summary>头像框线宽（立绘绘制与裁剪的内缩）</summary>
        public virtual float PortraitFrameBorder => 2f;
        /// <summary>底行命令提示距面板底边</summary>
        public virtual float HintBottomMargin => 0f;
        /// <summary>命令提示按钮间距</summary>
        public virtual float CommandHintGap => 14f;
        /// <summary>命令提示点击热区扩展</summary>
        public virtual float CommandHintHitPad => 6f;
        /// <summary>头像框与底行命令提示之间的最小间距</summary>
        public virtual float PortraitHintClearance => 8f;
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

        /// <summary>根据输入计算本帧布局。复杂 consumer 可重写以完全接管对话框布局</summary>
        public virtual void Layout(DialogueLayoutInput input, DialogueLayoutContext context) {
            context.Font = input.Font;
            context.Portrait = input.Portrait;
            context.HasPortrait = input.Portrait != null;
            context.Silhouette = input.Silhouette;
            context.SpeakerName = input.SpeakerName ?? string.Empty;
            context.TextScale = TextScale;
            context.NameScale = NameScale;
            context.HintScale = HintScale;

            float textAreaWidth = PanelWidth - Padding * 2f - TextWrapInset - (context.HasPortrait ? PortraitSize + PortraitGap : 0f);
            if (textAreaWidth < 60f) {
                textAreaWidth = 60f;
            }

            string[] previewLines = VaultUtils.WrapText(input.Line.Text ?? string.Empty, input.Font, textAreaWidth, TextScale).ToArray();
            float lineHeight = input.Font.MeasureString("A").Y * TextScale + LineSpacing;
            float contentHeight = previewLines.Length * lineHeight;
            float hintReserve = MeasureHintReserve(input.Font);
            float textColumnHeight = Padding + HeaderHeight + contentHeight + hintReserve;
            float panelHeight = MathHelper.Clamp(textColumnHeight, MinPanelHeight, MaxPanelHeight);
            if (context.HasPortrait) {
                float portraitColumnHeight = Padding + PortraitSize + PortraitHintClearance + hintReserve;
                panelHeight = Math.Max(panelHeight, portraitColumnHeight);
            }

            Vector2 size = new(PanelWidth, panelHeight);
            Vector2 pos = input.Anchor - new Vector2(size.X / 2f, size.Y);
            pos.Y += NarrativePanelMotion.ResolveSlide(input.OpenProgress, input.IsClosing, NarrativePanelMotion.Profile.Dialogue);

            context.PanelRect = new Rectangle((int)pos.X, (int)pos.Y, (int)size.X, (int)size.Y);

            float textLeft = context.PanelRect.X + Padding;
            if (context.HasPortrait) {
                context.PortraitRect = new Rectangle(
                    (int)(context.PanelRect.X + Padding),
                    (int)(context.PanelRect.Y + Padding),
                    (int)PortraitSize,
                    (int)PortraitSize);
                textLeft += PortraitSize + PortraitGap;
            }
            else {
                context.PortraitRect = Rectangle.Empty;
            }

            int textRight = context.PanelRect.Right - (int)Padding - (int)TextWrapInset;
            int textColumnWidth = Math.Max(0, textRight - (int)textLeft);
            context.SpeakerRect = new Rectangle((int)textLeft, (int)(context.PanelRect.Y + Padding - 2f), textColumnWidth, (int)HeaderHeight);
            int textTop = (int)(context.PanelRect.Y + Padding + HeaderHeight);
            int textHeight = (int)(context.PanelRect.Bottom - hintReserve - textTop);
            if (textHeight < 0) {
                textHeight = 0;
            }
            context.TextRect = new Rectangle((int)textLeft, textTop, textColumnWidth, textHeight);
            context.LineHeight = lineHeight;

            LayoutCommandHints(context);
        }

        /// <summary>测量底行全部命令提示文字的最大高度</summary>
        protected float MeasureHintRowTextHeight(DynamicSpriteFont font) {
            float height = 0f;
            height = Math.Max(height, font.MeasureString(ResolveAutoHint()).Y * HintScale);
            height = Math.Max(height, font.MeasureString(ResolveFastHint()).Y * HintScale);
            height = Math.Max(height, font.MeasureString(ResolveSkipHint()).Y * HintScale);
            height = Math.Max(height, font.MeasureString(ResolveContinueHint(hover: true)).Y * 0.9f);
            return MathF.Ceiling(height);
        }

        /// <summary>测量单条提示文案在指定缩放下的文字高度</summary>
        /// <param name="font">测量用字体</param>
        /// <param name="text">提示文案</param>
        /// <param name="scale">文字缩放</param>
        protected float MeasureHintRowTextHeight(DynamicSpriteFont font, string text, float scale)
            => MathF.Ceiling(font.MeasureString(text).Y * scale);

        /// <summary>测量底行命令提示区域总高度（含点击热区扩展）</summary>
        protected float MeasureHintBandHeight(DynamicSpriteFont font)
            => MeasureHintRowTextHeight(font) + CommandHintHitPad;

        /// <summary>测量正文区下方为命令提示预留的总高度</summary>
        protected float MeasureHintReserve(DynamicSpriteFont font)
            => MeasureHintBandHeight(font) + HintBottomMargin;

        /// <summary>每帧更新皮肤状态，默认无状态</summary>
        public virtual void Update(DialogueLayoutContext context) { }

        /// <summary>样式切换或新会话开始时重置皮肤状态</summary>
        public virtual void Reset() { }

        /// <summary>自动播放提示文案，消费者可重写以接入本地化</summary>
        protected virtual string ResolveAutoHint() => NarrativeUIText.Auto;
        /// <summary>快进提示文案，消费者可重写以接入本地化</summary>
        protected virtual string ResolveFastHint() => NarrativeUIText.Fast;
        /// <summary>跳过提示文案，消费者可重写以接入本地化</summary>
        protected virtual string ResolveSkipHint() => NarrativeUIText.Skip;
        /// <summary>继续提示文案，消费者可重写以接入本地化</summary>
        protected virtual string ResolveContinueHint(bool hover) => NarrativeUIText.ContinueGlyph;

        /// <summary>布局底部命令提示的点击区域</summary>
        public virtual void LayoutCommandHints(DialogueLayoutContext context) {
            float rowTextHeight = MeasureHintRowTextHeight(context.Font);
            context.HintRowBaseline = context.PanelRect.Bottom - HintBottomMargin;
            float rowTop = context.HintRowBaseline - rowTextHeight;

            float autoW = context.Font.MeasureString(ResolveAutoHint()).X * HintScale;
            float fastW = context.Font.MeasureString(ResolveFastHint()).X * HintScale;
            float skipW = context.Font.MeasureString(ResolveSkipHint()).X * HintScale;
            float continueW = context.Font.MeasureString(ResolveContinueHint(hover: true)).X * 0.9f;

            float commandX;
            if (context.HasPortrait && context.PortraitRect != Rectangle.Empty) {
                float totalW = autoW + CommandHintGap + fastW + CommandHintGap + skipW;
                commandX = context.PortraitRect.X + (context.PortraitRect.Width - totalW) * 0.5f;
            }
            else {
                commandX = context.PanelRect.X + Padding;
            }

            context.AutoRect = BuildHintHitRect(commandX, rowTop, autoW, rowTextHeight, context.HintRowBaseline);
            commandX += autoW + CommandHintGap;
            context.FastRect = BuildHintHitRect(commandX, rowTop, fastW, rowTextHeight, context.HintRowBaseline);
            commandX += fastW + CommandHintGap;
            context.SkipRect = BuildHintHitRect(commandX, rowTop, skipW, rowTextHeight, context.HintRowBaseline);

            float continueX = context.PanelRect.Right - Padding - continueW;
            context.ContinueRect = BuildHintHitRect(continueX, rowTop, continueW, rowTextHeight, context.HintRowBaseline);
        }

        /// <summary>根据文字区域与统一底边构建命令提示点击热区</summary>
        protected Rectangle BuildHintHitRect(float posX, float rowTop, float textWidth, float rowTextHeight, float rowBaseline)
            => new Rectangle(
                (int)(posX - CommandHintHitPad),
                (int)(rowTop - CommandHintHitPad),
                (int)(textWidth + CommandHintHitPad * 2f),
                (int)(rowBaseline - rowTop + CommandHintHitPad));

        /// <summary>根据文字区域构建命令提示点击热区，底边默认为文字区域下沿</summary>
        protected Rectangle BuildHintHitRect(float posX, float rowTop, float textWidth, float rowTextHeight)
            => BuildHintHitRect(posX, rowTop, textWidth, rowTextHeight, rowTop + rowTextHeight);

        /// <summary>绘制对话框背景</summary>
        public virtual void DrawBackground(SpriteBatch spriteBatch, DialogueLayoutContext context)
            => DrawPanel(spriteBatch, context.PanelRect, context.Alpha);

        /// <summary>绘制面板背景之上的装饰（粒子等，位于正文下方）</summary>
        public virtual void DrawBackgroundDecorations(SpriteBatch spriteBatch, DialogueLayoutContext context) { }

        /// <summary>绘制头像框</summary>
        public virtual void DrawPortraitFrame(SpriteBatch spriteBatch, DialogueLayoutContext context)
            => DrawPortraitFrame(spriteBatch, context.PortraitRect, context.Alpha);

        /// <summary>绘制说话者名称</summary>
        public virtual void DrawSpeakerName(SpriteBatch spriteBatch, DialogueLayoutContext context) {
            if (string.IsNullOrEmpty(context.SpeakerName)) {
                return;
            }

            float nameAlpha = context.ContentAlpha * context.SpeakerSwitchEase;
            Vector2 pos = context.SpeakerRect.Location.ToVector2();
            pos.Y -= (1f - context.SpeakerSwitchEase) * 6f;
            Utils.DrawBorderString(spriteBatch, context.SpeakerName, pos, SpeakerColor * nameAlpha, NameScale);
        }

        /// <summary>绘制说话者与正文间的装饰分隔线</summary>
        public virtual void DrawDivider(SpriteBatch spriteBatch, DialogueLayoutContext context) { }

        /// <summary>绘制正文</summary>
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
                    Utils.DrawBorderString(spriteBatch, draw, pos, TextColor * context.ContentAlpha, TextScale);
                }
                remaining -= fullLine.Length;
                if (remaining <= 0) {
                    break;
                }
            }
        }

        /// <summary>绘制限时指示</summary>
        public virtual void DrawTimedIndicator(SpriteBatch spriteBatch, DialogueLayoutContext context) {
            if (!context.TimedActive) {
                return;
            }
            int barWidth = (int)(context.PanelRect.Width * context.TimedProgress);
            Rectangle bar = new(context.PanelRect.X, context.PanelRect.Y - 4, barWidth, 3);
            Color color = Color.Lerp(new Color(255, 90, 80), HintColor, context.TimedProgress);
            NarrativeSkinDraw.FillRect(spriteBatch, bar, color * context.Alpha);
        }

        /// <summary>绘制底部命令提示</summary>
        public virtual void DrawCommandHints(SpriteBatch spriteBatch, DialogueLayoutContext context) {
            if (!context.ShowHints) {
                return;
            }

            Color on = HintColor * context.ContentAlpha;
            Color off = HintColor * (context.ContentAlpha * 0.4f);
            Utils.DrawBorderString(spriteBatch, ResolveAutoHint(), GetHintDrawPosition(context, context.AutoRect, ResolveAutoHint()), context.AutoMode ? on : off, HintScale);
            Utils.DrawBorderString(spriteBatch, ResolveFastHint(), GetHintDrawPosition(context, context.FastRect, ResolveFastHint()), context.FastMode ? on : off, HintScale);
            Utils.DrawBorderString(spriteBatch, ResolveSkipHint(), GetHintDrawPosition(context, context.SkipRect, ResolveSkipHint()), off, HintScale);

            if (context.WaitingAdvance) {
                float blink = (float)(Math.Sin(context.GlobalTimer * 6f) * 0.5 + 0.5);
                string continueText = ResolveContinueHint(context.HoverContinue);
                Utils.DrawBorderString(spriteBatch, continueText, GetHintDrawPosition(context, context.ContinueRect, continueText, 0.9f), HintColor * (context.ContentAlpha * blink), 0.9f);
            }
        }

        /// <summary>计算命令提示文字的绘制位置，按统一底边对齐；scale 小于 0 时使用 <see cref="DialogueLayoutContext.HintScale"/></summary>
        protected Vector2 GetHintDrawPosition(DialogueLayoutContext context, Rectangle hitRect, string text, float scale = -1f) {
            if (scale < 0f) {
                scale = context.HintScale;
            }

            float baseline = context.HintRowBaseline > 0f
                ? context.HintRowBaseline
                : context.PanelRect.Bottom - HintBottomMargin;
            float height = context.Font.MeasureString(text).Y * scale;
            return new Vector2(hitRect.X + CommandHintHitPad, baseline - height);
        }

        /// <summary>绘制最前景装饰（应保持在正文之上，默认无）</summary>
        public virtual void DrawForegroundDecorations(SpriteBatch spriteBatch, DialogueLayoutContext context) { }

        /// <summary>打字音效触发间隔（可见字符数），0 表示禁用</summary>
        public virtual int TypingSoundInterval => 4;

        /// <summary>打字机每推进 <see cref="TypingSoundInterval"/> 个字符时播放</summary>
        public virtual void PlayTypingSound() => NarrativeAudioDefaults.Play(NarrativeAudioDefaults.Typing);

        /// <summary>切换自动播放后播放（<paramref name="autoMode"/> 为切换后的状态）</summary>
        public virtual void PlayToggleAutoSound(bool autoMode)
            => NarrativeAudioDefaults.Play(NarrativeAudioDefaults.ToggleAuto(autoMode));

        /// <summary>切换快进后播放（<paramref name="fastMode"/> 为切换后的状态）</summary>
        public virtual void PlayToggleFastSound(bool fastMode)
            => NarrativeAudioDefaults.Play(NarrativeAudioDefaults.ToggleFast(fastMode));

        /// <summary>点击「跳过至下一停顿点」时播放</summary>
        public virtual void PlaySkipSound() => NarrativeAudioDefaults.Play(NarrativeAudioDefaults.Skip);
    }

    /// <summary>框架内置的朴素默认对话框皮肤</summary>
    public sealed class BasicDialogueSkin : DialogueSkin { }
}
