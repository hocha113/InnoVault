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
    /// 不持有任何运行状态。默认走 <c>SoftPanel</c> 着色器，呈现中性的 GalGame 悬浮阴影质感；<br/>
    /// 通过 <see cref="StyleRegistry"/> 以 <see cref="Core.StyleId"/> 注册，新增主题无需改核心
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
        public virtual float Padding => 26f;
        /// <summary>标题栏高度</summary>
        public virtual float HeaderHeight => 50f;
        /// <summary>底栏高度</summary>
        public virtual float FooterHeight => 38f;
        /// <summary>行间距</summary>
        public virtual float RowGap => 14f;
        /// <summary>段（一次对话）之间的额外间距</summary>
        public virtual float ConversationGap => 20f;
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
        public virtual float PortraitSize => 42f;
        /// <summary>立绘与文本间距</summary>
        public virtual float PortraitGap => 12f;
        /// <summary>选择记录的额外缩进</summary>
        public virtual float ChoiceIndent => 28f;
        /// <summary>滚动条宽度</summary>
        public virtual float ScrollbarWidth => 6f;
        /// <summary>列表右侧为滚动条预留的留白</summary>
        public virtual float ScrollGutter => 18f;
        /// <summary>滚动条相对列表上下的内缩</summary>
        public virtual float ScrollbarPad => 4f;
        /// <summary>滚动条滑块最小高度</summary>
        public virtual float MinThumbHeight => 30f;

        /// <summary>标题色</summary>
        public virtual Color TitleColor => new(212, 218, 232);
        /// <summary>说话者名色（偏冷、低饱和）</summary>
        public virtual Color NameColor => new(170, 188, 214);
        /// <summary>正文色</summary>
        public virtual Color TextColor => new(220, 224, 234);
        /// <summary>选择记录色（柔和暗金）</summary>
        public virtual Color ChoiceColor => new(208, 184, 140);
        /// <summary>提示 / 次要色</summary>
        public virtual Color HintColor => new(150, 158, 176);
        /// <summary>分隔线 / 描边色</summary>
        public virtual Color EdgeColor => new(120, 134, 158);
        /// <summary>滚动条滑块色</summary>
        public virtual Color ScrollbarColor => new(150, 162, 186);
        /// <summary>剪影立绘色</summary>
        public virtual Color SilhouetteColor => new(12, 18, 28);
        /// <summary>选择记录前缀标记</summary>
        public virtual string ChoiceMarker => "› ";

        /// <summary>面板着色器样式（中性阴影圆角），消费者可重写以微调质感</summary>
        protected virtual SoftPanelStyle PanelStyle => SoftPanelStyle.Default;

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

            int closeSize = (int)(HeaderHeight - 22f);
            context.CloseRect = new Rectangle(context.PanelRect.Right - closeSize - 14, context.PanelRect.Y + (int)((HeaderHeight - closeSize) * 0.5f), closeSize, closeSize);

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

            LayoutScrollbar(context);
        }

        /// <summary>算出滚动条轨道与滑块矩形，写入上下文供绘制与拖拽命中复用</summary>
        protected virtual void LayoutScrollbar(BacklogLayoutContext context) {
            if (!context.HasScroll) {
                context.ScrollTrackRect = Rectangle.Empty;
                context.ScrollThumbRect = Rectangle.Empty;
                return;
            }

            int w = (int)ScrollbarWidth;
            int trackX = context.ListRect.Right - w + (int)((ScrollGutter - ScrollbarWidth) * 0.5f);
            int trackY = context.ListRect.Y + (int)ScrollbarPad;
            int trackH = Math.Max(1, context.ListRect.Height - (int)(ScrollbarPad * 2f));
            context.ScrollTrackRect = new Rectangle(trackX, trackY, w, trackH);

            float viewRatio = context.ListRect.Height / Math.Max(1f, context.ContentHeight);
            int thumbH = Math.Max((int)MinThumbHeight, (int)(trackH * Math.Clamp(viewRatio, 0f, 1f)));
            thumbH = Math.Min(thumbH, trackH);
            float scrollRatio = context.MaxScroll <= 0f ? 0f : context.ScrollOffset / context.MaxScroll;
            int thumbY = trackY + (int)((trackH - thumbH) * scrollRatio);
            context.ScrollThumbRect = new Rectangle(trackX, thumbY, w, thumbH);
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

            float contentRight = context.ListRect.Right - ScrollGutter;
            for (int i = 0; i < input.Rows.Count; i++) {
                BacklogRowPresentation row = input.Rows[i];
                if (i > 0 && row.StartsConversation) {
                    top += ConversationGap;
                }

                float textLeft = GetRowTextLeft(context.ListRect, row);
                float wrapWidth = Math.Max(40f, contentRight - textLeft);
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

        /// <summary>绘制面板：着色器生成的中性阴影圆角面板（含柔和外阴影 / 半透明渐变 / 内缘高光）</summary>
        public virtual void DrawBackground(SpriteBatch sb, BacklogLayoutContext context)
            => NarrativeSkinDraw.DrawSoftPanel(sb, context.PanelRect, PanelStyle, context.Alpha);

        /// <summary>绘制滚动行列表，裁剪在 <see cref="BacklogLayoutContext.ListRect"/> 内</summary>
        public virtual void DrawRows(SpriteBatch sb, BacklogLayoutContext context) {
            if (context.IsEmpty) {
                return;
            }

            NarrativeSkinDraw.BeginClip(sb, context.ListRect);
            try {
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
                        Rectangle divider = new(context.ListRect.X, (int)(screenTop - ConversationGap * 0.5f), context.ListRect.Width - (int)ScrollGutter, 1);
                        NarrativeSkinDraw.FillRect(sb, divider, EdgeColor * (context.ContentAlpha * 0.35f));
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
            finally {
                NarrativeSkinDraw.EndClip(sb);
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

        /// <summary>绘制标题栏 / 底栏的细分隔线、标题文字、关闭按钮与底部提示（无不透明遮挡带）</summary>
        public virtual void DrawChrome(SpriteBatch sb, BacklogLayoutContext context) {
            int sepInset = (int)Padding;
            Rectangle headerSep = new(context.PanelRect.X + sepInset, context.PanelRect.Y + (int)HeaderHeight - 1, context.PanelRect.Width - sepInset * 2, 1);
            Rectangle footerSep = new(context.PanelRect.X + sepInset, context.PanelRect.Bottom - (int)FooterHeight, context.PanelRect.Width - sepInset * 2, 1);
            NarrativeSkinDraw.FillRect(sb, headerSep, EdgeColor * (context.Alpha * 0.5f));
            NarrativeSkinDraw.FillRect(sb, footerSep, EdgeColor * (context.Alpha * 0.3f));

            string title = ResolveTitle();
            Vector2 titleSize = context.Font.MeasureString(title) * TitleScale;
            Vector2 titlePos = new(context.PanelRect.X + Padding, context.PanelRect.Y + (HeaderHeight - titleSize.Y) * 0.5f);
            Utils.DrawBorderString(sb, title, titlePos, TitleColor * context.Alpha, TitleScale);

            DrawCloseButton(sb, context);

            string hint = ResolveCloseHint();
            Vector2 hintSize = context.Font.MeasureString(hint) * HintScale;
            Vector2 hintPos = new(
                context.PanelRect.X + (context.PanelRect.Width - hintSize.X) * 0.5f,
                context.PanelRect.Bottom - FooterHeight + (FooterHeight - hintSize.Y) * 0.5f);
            Utils.DrawBorderString(sb, hint, hintPos, HintColor * (context.Alpha * 0.8f), HintScale);
        }

        /// <summary>绘制关闭按钮（无硬边框，仅悬停提亮的 X）</summary>
        public virtual void DrawCloseButton(SpriteBatch sb, BacklogLayoutContext context) {
            Color color = (context.HoverClose ? Color.White : HintColor) * context.Alpha;
            const string x = "X";
            Vector2 size = context.Font.MeasureString(x) * 0.85f;
            Vector2 pos = new(
                context.CloseRect.X + (context.CloseRect.Width - size.X) * 0.5f,
                context.CloseRect.Y + (context.CloseRect.Height - size.Y) * 0.5f);
            Utils.DrawBorderString(sb, x, pos, color, 0.85f);
        }

        /// <summary>绘制滚动条（细药丸滑块 + 极淡轨道，滑块悬停 / 拖拽时提亮）</summary>
        public virtual void DrawScrollbar(SpriteBatch sb, BacklogLayoutContext context) {
            if (!context.HasScroll || context.ScrollTrackRect == Rectangle.Empty) {
                return;
            }

            NarrativeSkinDraw.FillRect(sb, context.ScrollTrackRect, EdgeColor * (context.Alpha * 0.16f));

            bool active = context.HoverScrollThumb || context.DraggingScroll;
            Color thumbColor = ScrollbarColor * (context.Alpha * (active ? 0.95f : 0.72f));
            float radius = context.ScrollThumbRect.Width * 0.5f;
            NarrativeSkinDraw.DrawSoftPanel(sb, context.ScrollThumbRect, SoftPanelStyle.Pill(thumbColor, radius), context.Alpha);
        }

        /// <summary>面板边框层：中性阴影皮肤的边缘由着色器内缘高光承担，默认不再额外描边</summary>
        public virtual void DrawFrame(SpriteBatch sb, BacklogLayoutContext context) { }

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

    /// <summary>框架内置的中性默认 backlog 皮肤</summary>
    public sealed class BasicBacklogSkin : BacklogSkin { }
}
