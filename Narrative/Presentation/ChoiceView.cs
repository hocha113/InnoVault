using InnoVault.UIHandles;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Graphics;
using System.Collections.Generic;
using Terraria;
using Terraria.GameContent;

namespace InnoVault.Narrative
{
    /// <summary>
    /// 默认选择框视图。展示选项、处理悬停与点击，并把选择结果以意图形式回传给会话；<br/>
    /// 选择后的流程收尾（关闭、跳转、防重复）全部由 <see cref="NarrativeSession"/> 处理
    /// </summary>
    public sealed class ChoiceView : UIHandle<ChoiceView>, INarrativeView
    {
        private const float Padding = 14f;
        private const float OptionHeight = 32f;
        private const float OptionSpacing = 8f;
        private const float TitleHeight = 24f;
        private const float MinWidth = 200f;
        private const float MaxWidth = 440f;

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

            float panelWidth = MathHelper.Clamp(maxTextWidth + Padding * 4f, MinWidth, MaxWidth);
            float panelHeight = Padding * 2f + TitleHeight + _options.Count * OptionHeight + (_options.Count - 1) * OptionSpacing;

            Vector2 anchor = PanelAnchorResolver.AboveDialogue();
            float eased = VaultUtils.EaseOutCubic(OpenProgress);
            Vector2 pos = new(anchor.X - panelWidth / 2f, anchor.Y - panelHeight);
            pos.Y += (1f - eased) * 40f;
            _panelRect = new Rectangle((int)pos.X, (int)pos.Y, (int)panelWidth, (int)panelHeight);

            _optionRects.Clear();
            float optionY = _panelRect.Y + Padding + TitleHeight;
            for (int i = 0; i < _options.Count; i++) {
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
            if (_panelRect.Contains(mouse)) {
                player.mouseInterface = true;
            }

            _hoverIndex = -1;
            bool pressed = keyLeftPressState == KeyPressState.Pressed;
            for (int i = 0; i < _optionRects.Count; i++) {
                if (_optionRects[i].Contains(mouse)) {
                    _hoverIndex = i;
                    if (pressed && _options[i].Enabled) {
                        session.SelectChoice(i);
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
            for (int i = 0; i < _options.Count && i < _optionRects.Count; i++) {
                OptionView option = _options[i];
                Rectangle rect = _optionRects[i];
                float hover = i == _hoverIndex && option.Enabled ? 1f : 0f;
                _skin.DrawOption(spriteBatch, rect, option.Enabled, hover, alpha);

                Color textColor = option.Enabled ? _skin.TextColor : _skin.DisabledTextColor;
                Vector2 textPos = new(rect.X + 8f, rect.Center.Y - FontAssets.MouseText.Value.MeasureString(option.Text).Y * textScale / 2f);
                Utils.DrawBorderString(spriteBatch, option.Text, textPos, textColor * alpha, textScale);
            }

            if (_timedActive) {
                int barWidth = (int)(_panelRect.Width * _timedProgress);
                Rectangle bar = new(_panelRect.X, _panelRect.Y - 4, barWidth, 3);
                Color color = Color.Lerp(new Color(255, 90, 80), _skin.HighlightColor, _timedProgress);
                NarrativeSkinDraw.FillRect(spriteBatch, bar, color * alpha);
            }
        }
    }
}
