using InnoVault.Narrative.Core;
using InnoVault.Narrative.Presentation.Anchors;
using InnoVault.Narrative.Runtime;
using InnoVault.Narrative.Styling;
using InnoVault.UIHandles;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Graphics;
using System;
using System.Collections.Generic;
using Terraria;
using Terraria.GameContent;

namespace InnoVault.Narrative.Presentation
{
    /// <summary>
    /// 默认选择框视图。展示选项、处理悬停与点击，并把选择结果以意图形式回传给会话；<br/>
    /// 选择后的流程收尾（关闭、跳转、防重复）全部由 <see cref="NarrativeSession"/> 处理。<br/>
    /// 选项超过 <see cref="MaxVisibleOptions"/> 时自动启用滚轮滚动，面板高度不会无限增长
    /// </summary>
    public sealed class ChoiceView : UIHandle<ChoiceView>, INarrativeView
    {
        private const float Padding = 14f;
        private const float OptionHeight = 32f;
        private const float OptionSpacing = 8f;
        private const float TitleHeight = 24f;
        private const float MinWidth = 200f;
        private const float MaxWidth = 440f;
        private const int MaxVisibleOptions = 8;

        /// <inheritdoc/>
        public override LayersModeEnum LayersMode => LayersModeEnum.Vanilla_Mouse_Text;
        /// <summary>绘制在对话框之上</summary>
        public override float RenderPriority => 2f;

        private readonly struct OptionView(string text, bool enabled, string disabledHint)
        {
            public readonly string Text = text;
            public readonly bool Enabled = enabled;
            public readonly string DisabledHint = disabledHint;
        }

        private ChoiceSkin _skin = new BasicChoiceSkin();
        private readonly List<OptionView> _options = [];
        private Rectangle _panelRect;
        private readonly List<Rectangle> _optionRects = [];
        private int _hoverIndex = -1;
        private bool _timedActive;
        private float _timedProgress;
        private bool _hasCache;

        //滚动状态
        private object _lastOptionsRef;
        private int _scrollOffset;
        private int _visibleCount;
        private bool _hasScroll;

        /// <inheritdoc/>
        public override void VaultSetup() {
            base.VaultSetup();
            NarrativeViews.Register(this);
        }

        /// <inheritdoc/>
        public void Sync(NarrativeSession active) {
            if (active != null && active.IsAwaitingChoice) {
                Open();
            }
            else {
                Close();
            }
        }

        /// <inheritdoc/>
        public override void Update() {
            NarrativeSession session = NarrativeRunner.Active;
            if (session == null || !session.IsAwaitingChoice) {
                return;
            }

            _skin = StyleRegistry.GetChoice(session.Style);
            IReadOnlyList<ChoiceOption> options = session.ChoiceOptions;
            if (options == null || options.Count == 0) {
                return;
            }

            //新的一组选项：重置滚动位置
            if (!ReferenceEquals(options, _lastOptionsRef)) {
                _lastOptionsRef = options;
                _scrollOffset = 0;
            }

            DynamicSpriteFont font = FontAssets.MouseText.Value;
            float textScale = _skin.TextScale;

            _options.Clear();
            float maxTextWidth = font.MeasureString(NarrativeUIText.ChoiceTitle).X * 0.85f;
            foreach (ChoiceOption option in options) {
                bool enabled = option.IsEnabled;
                string text = option.Text ?? string.Empty;
                if (!enabled && !string.IsNullOrEmpty(option.DisabledHint)) {
                    text += $" ({option.DisabledHint})";
                }
                _options.Add(new OptionView(text, enabled, option.DisabledHint));
                float width = font.MeasureString(text).X * textScale;
                if (width > maxTextWidth) {
                    maxTextWidth = width;
                }
            }

            _visibleCount = Math.Min(_options.Count, MaxVisibleOptions);
            _hasScroll = _options.Count > _visibleCount;
            _scrollOffset = Math.Clamp(_scrollOffset, 0, _options.Count - _visibleCount);

            float panelWidth = MathHelper.Clamp(maxTextWidth + Padding * 4f, MinWidth, MaxWidth);
            float panelHeight = Padding * 2f + TitleHeight + _visibleCount * OptionHeight + (_visibleCount - 1) * OptionSpacing;

            Vector2 anchor = PanelAnchorResolver.AboveDialogue();
            float eased = VaultUtils.EaseOutCubic(OpenProgress);
            Vector2 pos = new(anchor.X - panelWidth / 2f, anchor.Y - panelHeight);
            pos.Y += (1f - eased) * 40f;
            _panelRect = new Rectangle((int)pos.X, (int)pos.Y, (int)panelWidth, (int)panelHeight);

            _optionRects.Clear();
            float optionY = _panelRect.Y + Padding + TitleHeight;
            for (int i = 0; i < _visibleCount; i++) {
                _optionRects.Add(new Rectangle(_panelRect.X + (int)Padding, (int)optionY, (int)(panelWidth - Padding * 2f), (int)OptionHeight));
                optionY += OptionHeight + OptionSpacing;
            }

            _timedActive = session.ChoiceIsTimed;
            _timedProgress = session.ChoiceTimedProgress;
            _hasCache = true;

            HandleInput(session);
        }

        private void HandleInput(NarrativeSession session) {
            Point mouse = new(Main.mouseX, Main.mouseY);
            bool overPanel = _panelRect.Contains(mouse);
            if (overPanel) {
                player.mouseInterface = true;
            }

            //滚轮滚动（仅在选项超出可见窗口时）
            if (_hasScroll && overPanel) {
                int scroll = MouseScrollDelta;
                if (scroll != 0) {
                    _scrollOffset = Math.Clamp(_scrollOffset - Math.Sign(scroll), 0, _options.Count - _visibleCount);
                }
            }

            _hoverIndex = -1;
            bool pressed = keyLeftPressState == KeyPressState.Pressed;
            for (int i = 0; i < _optionRects.Count; i++) {
                if (_optionRects[i].Contains(mouse)) {
                    int optionIndex = _scrollOffset + i;
                    _hoverIndex = optionIndex;
                    if (pressed && optionIndex < _options.Count && _options[optionIndex].Enabled) {
                        session.SelectChoice(optionIndex);
                    }
                    break;
                }
            }
            session.ChoiceHoverIndex = _hoverIndex;
        }

        /// <inheritdoc/>
        public override void Draw(SpriteBatch spriteBatch) {
            if (!_hasCache) {
                return;
            }
            float alpha = OpenProgress;
            if (alpha <= 0.01f) {
                return;
            }

            _skin.DrawPanel(spriteBatch, _panelRect, alpha);

            Utils.DrawBorderString(spriteBatch, NarrativeUIText.ChoiceTitle,
                new Vector2(_panelRect.X + Padding, _panelRect.Y + Padding - 2f), _skin.HighlightColor * alpha, 0.85f);

            float textScale = _skin.TextScale;
            for (int i = 0; i < _optionRects.Count; i++) {
                int optionIndex = _scrollOffset + i;
                if (optionIndex >= _options.Count) {
                    break;
                }
                OptionView option = _options[optionIndex];
                Rectangle rect = _optionRects[i];
                float hover = optionIndex == _hoverIndex && option.Enabled ? 1f : 0f;
                _skin.DrawOption(spriteBatch, rect, option.Enabled, hover, alpha);

                Color textColor = option.Enabled ? _skin.TextColor : _skin.DisabledTextColor;
                Vector2 textPos = new(rect.X + 8f, rect.Center.Y - FontAssets.MouseText.Value.MeasureString(option.Text).Y * textScale / 2f);
                Utils.DrawBorderString(spriteBatch, option.Text, textPos, textColor * alpha, textScale);
            }

            if (_hasScroll) {
                DrawScrollHints(spriteBatch, alpha);
            }

            if (_timedActive) {
                int barWidth = (int)(_panelRect.Width * _timedProgress);
                Rectangle bar = new(_panelRect.X, _panelRect.Y - 4, barWidth, 3);
                Color color = Color.Lerp(new Color(255, 90, 80), _skin.HighlightColor, _timedProgress);
                NarrativeSkinDraw.FillRect(spriteBatch, bar, color * alpha);
            }
        }

        private void DrawScrollHints(SpriteBatch spriteBatch, float alpha) {
            int maxOffset = _options.Count - _visibleCount;
            if (_scrollOffset > 0) {
                Utils.DrawBorderString(spriteBatch, "^",
                    new Vector2(_panelRect.Right - 16f, _panelRect.Y + Padding + 2f), _skin.HighlightColor * alpha, 0.8f);
            }
            if (_scrollOffset < maxOffset) {
                Utils.DrawBorderString(spriteBatch, "v",
                    new Vector2(_panelRect.Right - 16f, _panelRect.Bottom - 20f), _skin.HighlightColor * alpha, 0.8f);
            }
        }
    }
}
