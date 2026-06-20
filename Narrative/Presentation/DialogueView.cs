using System;
using System.Linq;
using InnoVault.UIHandles;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Graphics;
using Terraria;
using Terraria.GameContent;

namespace InnoVault.Narrative
{
    /// <summary>
    /// 默认对话框视图。负责打字显示、立绘、说话者名、底部命令提示与限时指示，<br/>
    /// 并把折行后的总字符数回填给会话；它<b>只</b>读取会话状态并回传输入意图，<br/>
    /// 不直接推进剧情、不写存档、不发奖励
    /// </summary>
    public sealed class DialogueView : UIHandle<DialogueView>, INarrativeView
    {
        private const float Padding = 18f;
        private const float LineSpacing = 6f;
        private const float PortraitSize = 92f;
        private const float PortraitGap = 14f;
        private const float MinHeight = 110f;
        private const float MaxHeight = 460f;
        private const float HeaderHeight = 30f;

        /// <summary>叙事对话框使用鼠标文本层，绘制在大多数原版 UI 之上</summary>
        public override LayersModeEnum LayersMode => LayersModeEnum.Vanilla_Mouse_Text;
        /// <inheritdoc/>
        public override bool CanDrag => false;

        /// <summary>最近一次布局得到的面板矩形（供弹窗 / 选择框锚定）</summary>
        public Rectangle PanelRect { get; private set; }

        //渲染缓存：在 Update 中根据会话填充，Draw 仅消费缓存，使关闭淡出时仍能显示最后内容
        private DialogueSkin _skin = new BasicDialogueSkin();
        private string[] _wrappedLines = [];
        private string _speakerName = string.Empty;
        private Texture2D _portrait;
        private bool _silhouette;
        private int _visibleCount;
        private bool _hasCache;
        private bool _showHints;
        private bool _waitingAdvance;
        private bool _autoMode;
        private bool _fastMode;
        private bool _timedActive;
        private float _timedProgress;

        private Rectangle _autoRect, _fastRect, _skipRect;
        private bool _overHint;

        /// <inheritdoc/>
        public override void VaultSetup()
        {
            base.VaultSetup();
            NarrativeViews.Register(this);
        }

        /// <inheritdoc/>
        public void Sync(NarrativeSession active)
        {
            if (active != null && active.DialogueVisible)
            {
                Open();
            }
            else
            {
                Close();
            }
        }

        /// <inheritdoc/>
        public override void Update()
        {
            NarrativeSession session = NarrativeRunner.Active;
            if (session == null || !session.DialogueVisible)
            {
                return;
            }

            _skin = StyleRegistry.GetDialogue(session.Style);
            LinePresentation line = session.Line;
            DynamicSpriteFont font = FontAssets.MouseText.Value;
            float textScale = _skin.TextScale;

            _portrait = PortraitRegistry.ResolvePortrait(line.Speaker, line.Expression);
            _silhouette = PortraitRegistry.IsSilhouette(line.Speaker);
            _speakerName = PortraitRegistry.ResolveName(line.Speaker);

            float textAreaWidth = _skin.PanelWidth - Padding * 2f - (_portrait != null ? PortraitSize + PortraitGap : 0f);
            if (textAreaWidth < 60f)
            {
                textAreaWidth = 60f;
            }

            _wrappedLines = VaultUtils.WrapText(line.Text ?? string.Empty, font, textAreaWidth, textScale).ToArray();
            int total = 0;
            foreach (string l in _wrappedLines)
            {
                total += l.Length;
            }
            line.TotalChars = total;
            line.LayoutReady = true;

            float lineHeight = font.MeasureString("A").Y * textScale + LineSpacing;
            float contentHeight = _wrappedLines.Length * lineHeight;
            float panelHeight = MathHelper.Clamp(contentHeight + Padding * 2f + HeaderHeight, MinHeight, MaxHeight);
            if (_portrait != null)
            {
                panelHeight = Math.Max(panelHeight, PortraitSize + Padding * 2f);
            }

            float eased = VaultUtils.EaseOutCubic(OpenProgress);
            Vector2 anchor = new(Main.screenWidth / 2f, Main.screenHeight - 140f);
            Vector2 size = new(_skin.PanelWidth, panelHeight);
            Vector2 pos = anchor - new Vector2(size.X / 2f, size.Y);
            pos.Y += (1f - eased) * 60f;
            PanelRect = new Rectangle((int)pos.X, (int)pos.Y, (int)size.X, (int)size.Y);

            _visibleCount = Math.Clamp(line.VisibleCharCount, 0, total);
            _autoMode = session.Options.AutoMode;
            _fastMode = session.Options.FastMode;
            _showHints = session.Phase == NarrativeSessionPhase.Playing;
            _waitingAdvance = line.Finished && session.Phase == NarrativeSessionPhase.Playing && session.PendingChoice == null;
            _timedActive = line.IsTimed && line.ShowTimedIndicator && line.Finished;
            _timedProgress = line.TimedProgress;
            _hasCache = line.HasContent;

            LayoutHintRects();
            HandleInput(session);
        }

        private void LayoutHintRects()
        {
            float y = PanelRect.Bottom - 22f;
            float x = PanelRect.X + Padding;
            DynamicSpriteFont font = FontAssets.MouseText.Value;
            const float hintScale = 0.7f;

            float autoW = font.MeasureString(NarrativeUIText.Auto).X * hintScale;
            float fastW = font.MeasureString(NarrativeUIText.Fast).X * hintScale;
            float skipW = font.MeasureString(NarrativeUIText.Skip).X * hintScale;
            _autoRect = new Rectangle((int)x, (int)y, (int)autoW, 18);
            x += autoW + 14f;
            _fastRect = new Rectangle((int)x, (int)y, (int)fastW, 18);
            x += fastW + 14f;
            _skipRect = new Rectangle((int)x, (int)y, (int)skipW, 18);
        }

        private void HandleInput(NarrativeSession session)
        {
            Point mouse = new(Main.mouseX, Main.mouseY);
            bool hover = PanelRect.Contains(mouse);
            if (hover)
            {
                player.mouseInterface = true;
            }

            bool pressed = keyLeftPressState == KeyPressState.Pressed;
            _overHint = false;

            if (_showHints && pressed)
            {
                if (_autoRect.Contains(mouse))
                {
                    _overHint = true;
                    session.ToggleAuto();
                }
                else if (_fastRect.Contains(mouse))
                {
                    _overHint = true;
                    session.ToggleFast();
                }
                else if (_skipRect.Contains(mouse))
                {
                    _overHint = true;
                    session.RequestSkipLine();
                }
            }
            else
            {
                _overHint = _autoRect.Contains(mouse) || _fastRect.Contains(mouse) || _skipRect.Contains(mouse);
            }

            if (hover && !_overHint && pressed)
            {
                session.RequestAdvance();
            }
        }

        /// <inheritdoc/>
        public override void Draw(SpriteBatch spriteBatch)
        {
            if (!_hasCache)
            {
                return;
            }
            float alpha = OpenProgress;
            if (alpha <= 0.01f)
            {
                return;
            }

            _skin.DrawPanel(spriteBatch, PanelRect, alpha);

            float textLeft = PanelRect.X + Padding;
            if (_portrait != null)
            {
                Rectangle frame = new((int)(PanelRect.X + Padding), (int)(PanelRect.Y + Padding), (int)PortraitSize, (int)PortraitSize);
                _skin.DrawPortraitFrame(spriteBatch, frame, alpha);
                DrawPortrait(spriteBatch, frame, alpha);
                textLeft += PortraitSize + PortraitGap;
            }

            DynamicSpriteFont font = FontAssets.MouseText.Value;
            float textScale = _skin.TextScale;

            if (!string.IsNullOrEmpty(_speakerName))
            {
                Utils.DrawBorderString(spriteBatch, _speakerName, new Vector2(textLeft, PanelRect.Y + Padding - 2f), _skin.SpeakerColor * alpha, _skin.NameScale);
            }

            float lineHeight = font.MeasureString("A").Y * textScale + LineSpacing;
            float y = PanelRect.Y + Padding + HeaderHeight;
            int remaining = _visibleCount;
            foreach (string fullLine in _wrappedLines)
            {
                string draw = fullLine;
                if (remaining < fullLine.Length)
                {
                    draw = fullLine[..Math.Max(0, remaining)];
                }
                if (draw.Length > 0)
                {
                    Utils.DrawBorderString(spriteBatch, draw, new Vector2(textLeft, y), _skin.TextColor * alpha, textScale);
                }
                y += lineHeight;
                remaining -= fullLine.Length;
                if (remaining <= 0)
                {
                    break;
                }
            }

            if (_timedActive)
            {
                DrawTimedBar(spriteBatch, alpha);
            }

            if (_showHints)
            {
                DrawHints(spriteBatch, alpha);
            }

            if (_waitingAdvance)
            {
                float blink = (float)(Math.Sin(GlobalTimer * 6f) * 0.5 + 0.5);
                Utils.DrawBorderString(spriteBatch, NarrativeUIText.ContinueGlyph,
                    new Vector2(PanelRect.Right - 24f, PanelRect.Bottom - 26f), _skin.HintColor * (alpha * blink), 0.9f);
            }
        }

        private void DrawPortrait(SpriteBatch spriteBatch, Rectangle frame, float alpha)
        {
            float scale = Math.Min((frame.Width - 6f) / _portrait.Width, (frame.Height - 6f) / _portrait.Height);
            Vector2 center = frame.Center.ToVector2();
            Color color = _silhouette ? _skin.SilhouetteColor * alpha : Color.White * alpha;
            spriteBatch.Draw(_portrait, center, null, color, 0f, _portrait.Size() / 2f, scale, SpriteEffects.None, 0f);
        }

        private void DrawTimedBar(SpriteBatch spriteBatch, float alpha)
        {
            int barWidth = (int)(PanelRect.Width * _timedProgress);
            Rectangle bar = new(PanelRect.X, PanelRect.Y - 4, barWidth, 3);
            Color color = Color.Lerp(new Color(255, 90, 80), _skin.HintColor, _timedProgress);
            NarrativeSkinDraw.FillRect(spriteBatch, bar, color * alpha);
        }

        private void DrawHints(SpriteBatch spriteBatch, float alpha)
        {
            const float hintScale = 0.7f;
            Color on = _skin.HintColor * alpha;
            Color off = _skin.HintColor * (alpha * 0.4f);
            Utils.DrawBorderString(spriteBatch, NarrativeUIText.Auto, new Vector2(_autoRect.X, _autoRect.Y), _autoMode ? on : off, hintScale);
            Utils.DrawBorderString(spriteBatch, NarrativeUIText.Fast, new Vector2(_fastRect.X, _fastRect.Y), _fastMode ? on : off, hintScale);
            Utils.DrawBorderString(spriteBatch, NarrativeUIText.Skip, new Vector2(_skipRect.X, _skipRect.Y), off, hintScale);
        }
    }
}
