using InnoVault.Narrative.History;
using InnoVault.Narrative.Presentation;
using InnoVault.Narrative.Presentation.Backlog;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Graphics;
using System;
using Terraria;

namespace InnoVault.Narrative.Styling
{
    /// <summary>
    /// Backlog（历史对话）皮肤。负责面板 / 标题 / 行列表 / 滚动条 / 关闭区的布局与绘制，<br/>
    /// 不持有任何运行状态。通过 <see cref="StyleRegistry"/> 以 <see cref="Core.StyleId"/> 注册，新增主题无需改核心
    /// </summary>
    public abstract class BacklogSkin
    {
        /// <summary>面板宽度占屏比例</summary>
        public virtual float PanelWidthRatio => 0.62f;
        /// <summary>面板宽度上限</summary>
        public virtual float PanelMaxWidth => 760f;
        /// <summary>面板高度占屏比例</summary>
        public virtual float PanelHeightRatio => 0.72f;
        /// <summary>面板高度上限</summary>
        public virtual float PanelMaxHeight => 640f;
        /// <summary>面板内边距</summary>
        public virtual float Padding => 22f;
        /// <summary>标题栏高度</summary>
        public virtual float HeaderHeight => 48f;
        /// <summary>底栏高度</summary>
        public virtual float FooterHeight => 36f;
        /// <summary>行间距</summary>
        public virtual float RowGap => 14f;
        /// <summary>段（一次对话）之间的额外间距</summary>
        public virtual float ConversationGap => 18f;
        /// <summary>文本行距</summary>
        public virtual float LineSpacing => 4f;
        /// <summary>正文缩放</summary>
        public virtual float TextScale => 0.9f;
        /// <summary>说话者名缩放</summary>
        public virtual float NameScale => 0.9f;
        /// <summary>标题缩放</summary>
        public virtual float TitleScale => 1.1f;
        /// <summary>提示文字缩放</summary>
        public virtual float HintScale => 0.78f;
        /// <summary>立绘缩略图尺寸</summary>
        public virtual float PortraitSize => 40f;
        /// <summary>立绘与文本间距</summary>
        public virtual float PortraitGap => 10f;
        /// <summary>选择记录的额外缩进</summary>
        public virtual float ChoiceIndent => 26f;
        /// <summary>滚动条宽度</summary>
        public virtual float ScrollbarWidth => 5f;

        /// <summary>面板填充色</summary>
        public virtual Color PanelColor => new(14, 19, 30);
        /// <summary>面板边框色</summary>
        public virtual Color EdgeColor => new(70, 130, 200);
        /// <summary>标题色</summary>
        public virtual Color TitleColor => new(200, 225, 255);
        /// <summary>说话者名色</summary>
        public virtual Color NameColor => new(150, 200, 255);
        /// <summary>正文色</summary>
        public virtual Color TextColor => new(224, 231, 244);
        /// <summary>选择记录色</summary>
        public virtual Color ChoiceColor => new(255, 208, 138);
        /// <summary>提示 / 次要色</summary>
        public virtual Color HintColor => new(150, 190, 235);
        /// <summary>剪影立绘色</summary>
        public virtual Color SilhouetteColor => new(12, 18, 28);
        /// <summary>选择记录前缀标记</summary>
        public virtual string ChoiceMarker => "> ";

        /// <summary>标题文案，消费者可重写以接入本地化</summary>
        public virtual string ResolveTitle() => "Backlog";
        /// <summary>关闭提示文案，消费者可重写以接入本地化</summary>
        public virtual string ResolveCloseHint() => "Esc / Click outside to close";
        /// <summary>空历史提示文案，消费者可重写以接入本地化</summary>
        public virtual string ResolveEmptyHint() => "No dialogue history yet.";

        /// <summary>每帧更新皮肤状态，默认无状态</summary>
        public virtual void Update(BacklogLayoutContext context) { }
        /// <summary>样式切换或重新打开时重置皮肤状态</summary>
        public virtual void Reset() { }

        /// <summary>计算本帧布局。复杂 consumer 可重写以完全接管</summary>
        public virtual void Layout(BacklogLayoutInput input, BacklogLayoutContext context) {
            context.Font = input.Font;
            context.GlobalTimer = input.GlobalTimer;

            float width = Math.Min(PanelMaxWidth, Main.screenWidth * PanelWidthRatio);
            float height = Math.Min(PanelMaxHeight, Main.screenHeight * PanelHeightRatio);
            float x = (Main.screenWidth - width) * 0.5f;
            float y = (Main.screenHeight - height) * 0.5f;
            y += NarrativePanelMotion.ResolveSlide(input.OpenProgress, input.IsClosing, NarrativePanelMotion.Profile.Popup);

            context.PanelRect = new Rectangle((int)x, (int)y, (int)width, (int)height);
            context.TitleRect = new Rectangle(context.PanelRect.X, context.PanelRect.Y, context.PanelRect.Width, (int)HeaderHeight);

            int closeSize = (int)(HeaderHeight - 16f);
            context.CloseRect = new Rectangle(context.PanelRect.Right - closeSize - 12, context.PanelRect.Y + 8, closeSize, closeSize);

            int listTop = context.PanelRect.Y + (int)HeaderHeight;
            int listBottom = context.PanelRect.Bottom - (int)FooterHeight;
            context.ListRect = new Rectangle(
                context.PanelRect.X + (int)Padding,
                listTop,
                context.PanelRect.Width - (int)(Padding * 2f),
                Math.Max(0, listBottom - listTop));

            BuildRows(input, context);

            context.MaxScroll = Math.Max(0f, context.ContentHeight - context.ListRect.Height);
            context.HasScroll = context.MaxScroll > 0.5f;
            context.ScrollOffset = Math.Clamp(input.ScrollOffset, 0f, context.MaxScroll);
            context.IsEmpty = context.Rows.Count == 0;
        }

        private void BuildRows(BacklogLayoutInput input, BacklogLayoutContext context) {
            context.Rows.Clear();
            float lineHeight = LineHeightFor(context.Font, TextScale);
            float nameHeight = LineHeightFor(context.Font, NameScale);
            float top = 0f;

            if (input.Rows == null) {
                context.ContentHeight = 0f;
                return;
            }

            for (int i = 0; i < input.Rows.Count; i++) {
                BacklogRowPresentation row = input.Rows[i];
                if (i > 0 && row.StartsConversation) {
                    top += ConversationGap;
                }

                float textLeft = GetRowTextLeft(context.ListRect, row);
                float wrapWidth = Math.Max(40f, context.ListRect.Right - textLeft);
                string content = row.Kind == NarrativeLogKind.Choice ? ChoiceMarker + (row.Text ?? string.Empty) : row.Text ?? string.Empty;
                string[] lines = VaultUtils.WrapText(content, context.Font, wrapWidth, TextScale).ToArray();

                bool hasName = row.Kind == NarrativeLogKind.Line && !string.IsNullOrEmpty(row.SpeakerName);
                float height = (hasName ? nameHeight : 0f) + lines.Length * lineHeight;
                if (row.Portrait != null) {
                    height = Math.Max(height, PortraitSize);
                }

                context.Rows.Add(new BacklogRowLayout {
                    Source = row,
                    WrappedText = lines,
                    Top = top,
                    Height = height,
                });

                top += height + RowGap;
            }

            context.ContentHeight = top;
        }

        /// <summary>计算某行正文的左边界（区分立绘 / 选择缩进）</summary>
        protected float GetRowTextLeft(Rectangle listRect, BacklogRowPresentation row) {
            float left = listRect.X;
            if (row.Kind == NarrativeLogKind.Choice) {
                return left + ChoiceIndent;
            }
            if (row.Portrait != null) {
                return left + PortraitSize + PortraitGap;
            }
            return left;
        }

        /// <summary>按缩放计算文本行高</summary>
        protected float LineHeightFor(DynamicSpriteFont font, float scale)
            => font.MeasureString("A").Y * scale + LineSpacing;

        /// <summary>绘制面板背景（阴影 + 填充，不含边框）</summary>
        public virtual void DrawBackground(SpriteBatch sb, BacklogLayoutContext context) {
            Rectangle shadow = context.PanelRect;
            shadow.Offset(5, 6);
            NarrativeSkinDraw.FillRect(sb, shadow, Color.Black * (context.Alpha * 0.45f));
            NarrativeSkinDraw.FillRect(sb, context.PanelRect, PanelColor * context.Alpha);
        }

        /// <summary>绘制滚动行列表（裁剪由上层 <see cref="DrawChrome"/> 的不透明遮挡带完成）</summary>
        public virtual void DrawRows(SpriteBatch sb, BacklogLayoutContext context) {
            if (context.IsEmpty) {
                return;
            }

            float lineHeight = LineHeightFor(context.Font, TextScale);
            float nameHeight = LineHeightFor(context.Font, NameScale);
            float originY = context.ListRect.Y - context.ScrollOffset;

            foreach (BacklogRowLayout row in context.Rows) {
                float screenTop = originY + row.Top;
                if (screenTop + row.Height < context.ListRect.Top - 6f) {
                    continue;
                }
                if (screenTop > context.ListRect.Bottom + 6f) {
                    break;
                }

                BacklogRowPresentation src = row.Source;
                if (src.StartsConversation && row.Top > 0.5f) {
                    Rectangle divider = new(context.ListRect.X, (int)(screenTop - ConversationGap * 0.5f), context.ListRect.Width, 1);
                    NarrativeSkinDraw.FillRect(sb, divider, EdgeColor * (context.ContentAlpha * 0.4f));
                }

                float textLeft = GetRowTextLeft(context.ListRect, src);
                float textY = screenTop;

                if (src.Kind == NarrativeLogKind.Line && src.Portrait != null) {
                    DrawPortraitThumb(sb, src, new Rectangle(context.ListRect.X, (int)screenTop, (int)PortraitSize, (int)PortraitSize), context.ContentAlpha);
                }

                bool hasName = src.Kind == NarrativeLogKind.Line && !string.IsNullOrEmpty(src.SpeakerName);
                if (hasName) {
                    Utils.DrawBorderString(sb, src.SpeakerName, new Vector2(textLeft, textY), NameColor * context.ContentAlpha, NameScale);
                    textY += nameHeight;
                }

                Color bodyColor = src.Kind == NarrativeLogKind.Choice ? ChoiceColor : TextColor;
                for (int i = 0; i < row.WrappedText.Length; i++) {
                    Utils.DrawBorderString(sb, row.WrappedText[i], new Vector2(textLeft, textY + i * lineHeight), bodyColor * context.ContentAlpha, TextScale);
                }
            }
        }

        /// <summary>绘制立绘缩略图（等比缩放进方框）</summary>
        protected void DrawPortraitThumb(SpriteBatch sb, BacklogRowPresentation row, Rectangle box, float alpha) {
            Texture2D tex = row.Portrait;
            if (tex == null) {
                return;
            }
            Rectangle? source = row.PortraitSource;
            Vector2 size = source.HasValue ? new Vector2(source.Value.Width, source.Value.Height) : tex.Size();
            if (size.X <= 0f || size.Y <= 0f) {
                return;
            }
            float scale = Math.Min(box.Width / size.X, box.Height / size.Y);
            float w = size.X * scale;
            float h = size.Y * scale;
            Vector2 pos = new(box.X + (box.Width - w) * 0.5f, box.Y + (box.Height - h) * 0.5f);
            Color color = (row.Silhouette ? SilhouetteColor : Color.White) * alpha;
            sb.Draw(tex, pos, source, color, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
        }

        /// <summary>绘制标题 / 底栏遮挡带、标题文字、关闭按钮与底部提示</summary>
        public virtual void DrawChrome(SpriteBatch sb, BacklogLayoutContext context) {
            Rectangle headerBand = new(context.PanelRect.X, context.PanelRect.Y, context.PanelRect.Width, (int)HeaderHeight);
            Rectangle footerBand = new(context.PanelRect.X, context.PanelRect.Bottom - (int)FooterHeight, context.PanelRect.Width, (int)FooterHeight);
            NarrativeSkinDraw.FillRect(sb, headerBand, PanelColor * context.Alpha);
            NarrativeSkinDraw.FillRect(sb, footerBand, PanelColor * context.Alpha);
            NarrativeSkinDraw.FillRect(sb, new Rectangle(headerBand.X, headerBand.Bottom - 1, headerBand.Width, 1), EdgeColor * (context.Alpha * 0.6f));
            NarrativeSkinDraw.FillRect(sb, new Rectangle(footerBand.X, footerBand.Y, footerBand.Width, 1), EdgeColor * (context.Alpha * 0.4f));

            string title = ResolveTitle();
            Vector2 titleSize = context.Font.MeasureString(title) * TitleScale;
            Vector2 titlePos = new(context.PanelRect.X + Padding, headerBand.Y + (HeaderHeight - titleSize.Y) * 0.5f);
            Utils.DrawBorderString(sb, title, titlePos, TitleColor * context.Alpha, TitleScale);

            DrawCloseButton(sb, context);

            string hint = ResolveCloseHint();
            Vector2 hintSize = context.Font.MeasureString(hint) * HintScale;
            Vector2 hintPos = new(context.PanelRect.X + (context.PanelRect.Width - hintSize.X) * 0.5f, footerBand.Y + (FooterHeight - hintSize.Y) * 0.5f);
            Utils.DrawBorderString(sb, hint, hintPos, HintColor * (context.Alpha * 0.8f), HintScale);
        }

        /// <summary>绘制关闭按钮</summary>
        public virtual void DrawCloseButton(SpriteBatch sb, BacklogLayoutContext context) {
            Color color = (context.HoverClose ? Color.White : HintColor) * context.Alpha;
            NarrativeSkinDraw.DrawBorder(sb, context.CloseRect, color, 2);
            const string x = "X";
            Vector2 size = context.Font.MeasureString(x) * 0.8f;
            Vector2 pos = new(
                context.CloseRect.X + (context.CloseRect.Width - size.X) * 0.5f,
                context.CloseRect.Y + (context.CloseRect.Height - size.Y) * 0.5f);
            Utils.DrawBorderString(sb, x, pos, color, 0.8f);
        }

        /// <summary>绘制滚动条</summary>
        public virtual void DrawScrollbar(SpriteBatch sb, BacklogLayoutContext context) {
            if (!context.HasScroll) {
                return;
            }
            int trackX = context.ListRect.Right - (int)ScrollbarWidth;
            Rectangle track = new(trackX, context.ListRect.Y, (int)ScrollbarWidth, context.ListRect.Height);
            NarrativeSkinDraw.FillRect(sb, track, EdgeColor * (context.Alpha * 0.18f));

            float viewRatio = context.ListRect.Height / Math.Max(1f, context.ContentHeight);
            int thumbHeight = Math.Max(24, (int)(context.ListRect.Height * Math.Clamp(viewRatio, 0f, 1f)));
            float scrollRatio = context.MaxScroll <= 0f ? 0f : context.ScrollOffset / context.MaxScroll;
            int thumbY = context.ListRect.Y + (int)((context.ListRect.Height - thumbHeight) * scrollRatio);
            Rectangle thumb = new(trackX, thumbY, (int)ScrollbarWidth, thumbHeight);
            NarrativeSkinDraw.FillRect(sb, thumb, EdgeColor * (context.Alpha * 0.7f));
        }

        /// <summary>绘制面板边框（最上层，避免被遮挡带覆盖）</summary>
        public virtual void DrawFrame(SpriteBatch sb, BacklogLayoutContext context)
            => NarrativeSkinDraw.DrawBorder(sb, context.PanelRect, EdgeColor * context.Alpha);

        /// <summary>无历史时绘制占位提示</summary>
        public virtual void DrawEmpty(SpriteBatch sb, BacklogLayoutContext context) {
            if (!context.IsEmpty) {
                return;
            }
            string hint = ResolveEmptyHint();
            Vector2 size = context.Font.MeasureString(hint) * TextScale;
            Vector2 pos = new(
                context.ListRect.X + (context.ListRect.Width - size.X) * 0.5f,
                context.ListRect.Y + (context.ListRect.Height - size.Y) * 0.5f);
            Utils.DrawBorderString(sb, hint, pos, HintColor * (context.ContentAlpha * 0.8f), TextScale);
        }
    }

    /// <summary>框架内置的朴素默认 backlog 皮肤</summary>
    public sealed class BasicBacklogSkin : BacklogSkin { }
}
