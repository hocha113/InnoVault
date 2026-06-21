using InnoVault.Narrative.Core;
using InnoVault.Narrative.Portraits;
using InnoVault.Narrative.Presentation.Anchors;
using InnoVault.Narrative.Runtime;
using InnoVault.Narrative.Styling;
using InnoVault.UIHandles;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Graphics;
using System;
using System.Linq;
using Terraria;
using Terraria.GameContent;

namespace InnoVault.Narrative.Presentation.Dialogue
{
    /// <summary>
    /// 可复用的叙事对话视图基座。它负责读取 <see cref="NarrativeSession"/>、
    /// 处理输入意图、回填文本布局，并把实际视觉表现交给 <see cref="DialogueSkin"/>。
    /// </summary>
    public abstract class NarrativeDialogueViewBase<TSelf> : NarrativePanelViewBase<TSelf>, INarrativeView, INarrativePanelAnchorProvider
        where TSelf : NarrativeDialogueViewBase<TSelf>
    {
        protected DialogueSkin Skin = new BasicDialogueSkin();
        protected readonly DialogueLayoutContext Layout = new();
        private DialogueSkin _lastSkin;
        private DialogueLayoutInput _lastLayoutInput;
        private float _contentFade;
        private float _speakerSwitchEase = 1f;
        private CharacterId _lastSpeaker;
        private string _lastLineKey = string.Empty;
        private bool _lineKeyInitialized;

        private const float ContentFadeSpeed = 0.12f;
        private const float SpeakerSwitchSpeed = 0.14f;

        /// <summary>最近一次有效对话面板矩形。</summary>
        public Rectangle PanelRect => Layout.PanelRect;

        Rectangle INarrativePanelAnchorProvider.DialoguePanelRect => PanelRect;

        /// <inheritdoc/>
        public override LayersModeEnum LayersMode => LayersModeEnum.Vanilla_Mouse_Text;
        /// <inheritdoc/>
        public override bool CanDrag => false;

        /// <summary>该视图是否为 InnoVault 内置默认对话视图。</summary>
        protected virtual bool IsDefaultDialogueView => false;

        /// <summary>当前视图是否应注册到 NarrativeViews。</summary>
        protected virtual bool ShouldRegisterView => !IsDefaultDialogueView || NarrativeViews.UseDefaultDialogueView;

        /// <summary>对话框锚点，默认位于屏幕底部中央。</summary>
        protected virtual Vector2 DialogueAnchor => new(Main.screenWidth / 2f, Main.screenHeight - 140f);

        /// <inheritdoc/>
        public override void VaultSetup() {
            base.VaultSetup();
            if (ShouldRegisterView) {
                NarrativeViews.Register(this);
                PanelAnchorResolver.RegisterProvider(this);
            }
        }

        /// <inheritdoc/>
        public virtual void Sync(NarrativeSession active) {
            if (active != null && active.DialogueVisible) {
                Open();
            }
            else {
                Close();
            }
        }

        /// <inheritdoc/>
        public override void Update() {
            if (IsPanelClosing) {
                UpdateClosingPresentation();
                return;
            }

            NarrativeSession session = NarrativeRunner.Active;
            if (session == null || !session.DialogueVisible) {
                return;
            }

            Skin = StyleRegistry.GetDialogue(session.Style);
            if (!ReferenceEquals(Skin, _lastSkin)) {
                _lastSkin = Skin;
                Skin.Reset();
                _contentFade = 0f;
                _speakerSwitchEase = 1f;
                _lineKeyInitialized = false;
            }
            LinePresentation line = session.Line;
            UpdatePresentationFades(line);
            DynamicSpriteFont font = FontAssets.MouseText.Value;
            Texture2D portrait = PortraitRegistry.ResolvePortrait(line.Speaker, line.Expression);

            DialogueLayoutInput input = new() {
                Session = session,
                Line = line,
                Font = font,
                Portrait = portrait,
                Silhouette = PortraitRegistry.IsSilhouette(line.Speaker),
                SpeakerName = PortraitRegistry.ResolveName(line.Speaker),
                Anchor = DialogueAnchor,
                OpenProgress = MotionProgress,
                GlobalTimer = GlobalTimer,
                IsClosing = false,
            };

            _lastLayoutInput = input;
            Skin.Layout(input, Layout);
            Layout.PortraitSourceRect = PortraitRegistry.ResolvePortraitSource(line.Speaker, line.Expression);
            UpdateLineLayout(line, font);
            PopulateRuntimeState(session, line);
            ApplyContentAlpha();
            Skin.Update(Layout);
            HandleInput(session);
        }

        private void UpdatePresentationFades(LinePresentation line) {
            string lineKey = $"{line.Speaker}:{line.Text}";
            if (!_lineKeyInitialized || lineKey != _lastLineKey) {
                _lastLineKey = lineKey;
                _lineKeyInitialized = true;
                if (MotionProgress >= 0.99f) {
                    _contentFade = 1f;
                }
                else {
                    _contentFade = 0f;
                }
            }

            if (line.Speaker != _lastSpeaker) {
                _lastSpeaker = line.Speaker;
                _speakerSwitchEase = 0f;
            }

            if (_contentFade < 1f) {
                _contentFade = Math.Min(1f, _contentFade + ContentFadeSpeed);
            }

            if (_speakerSwitchEase < 1f) {
                _speakerSwitchEase = Math.Min(1f, _speakerSwitchEase + SpeakerSwitchSpeed);
            }
        }

        private void ApplyContentAlpha() {
            Layout.ContentAlpha = _contentFade * Layout.Alpha;
            Layout.SpeakerSwitchEase = _speakerSwitchEase;
        }

        private void UpdateLineLayout(LinePresentation line, DynamicSpriteFont font) {
            // TextRect 已是屏幕像素宽度；WrapText 内部会再除以 TextScale，勿重复缩放。
            float width = Math.Max(60f, Layout.TextRect.Width);
            Layout.WrappedLines = VaultUtils.WrapText(line.Text ?? string.Empty, font, width, Layout.TextScale).ToArray();

            int total = 0;
            foreach (string wrappedLine in Layout.WrappedLines) {
                total += wrappedLine.Length;
            }

            line.TotalChars = total;
            line.LayoutReady = true;
            Layout.TotalChars = total;
            Layout.VisibleChars = Math.Clamp(line.VisibleCharCount, 0, total);
        }

        private void UpdateClosingPresentation() {
            if (_lastLayoutInput == null) {
                return;
            }

            DialogueLayoutInput input = new() {
                Session = _lastLayoutInput.Session,
                Line = _lastLayoutInput.Line,
                Font = _lastLayoutInput.Font,
                Portrait = _lastLayoutInput.Portrait,
                Silhouette = _lastLayoutInput.Silhouette,
                SpeakerName = _lastLayoutInput.SpeakerName,
                Anchor = _lastLayoutInput.Anchor,
                OpenProgress = MotionProgress,
                GlobalTimer = GlobalTimer,
                IsClosing = true,
            };

            Skin.Layout(input, Layout);
            Layout.Alpha = NarrativePanelMotion.ResolveAlpha(MotionProgress, NarrativePanelMotion.Profile.Dialogue);
            Layout.GlobalTimer = GlobalTimer;
            Layout.ShowHints = false;
            ApplyContentAlpha();
            Skin.Update(Layout);
        }

        private void PopulateRuntimeState(NarrativeSession session, LinePresentation line) {
            Layout.Alpha = NarrativePanelMotion.ResolveAlpha(MotionProgress, NarrativePanelMotion.Profile.Dialogue);
            Layout.TimedActive = line.IsTimed && line.ShowTimedIndicator && line.Finished;
            Layout.TimedProgress = line.TimedProgress;
            Layout.ShowHints = session.Phase == NarrativeSessionPhase.Playing;
            Layout.WaitingAdvance = line.Finished && session.Phase == NarrativeSessionPhase.Playing && session.PendingChoice == null;
            Layout.AutoMode = session.Options.AutoMode;
            Layout.FastMode = session.Options.FastMode;
            Layout.GlobalTimer = GlobalTimer;
        }

        protected virtual void HandleInput(NarrativeSession session) {
            Point mouse = new(Main.mouseX, Main.mouseY);
            bool hoverPanel = Layout.PanelRect.Contains(mouse);
            if (hoverPanel) {
                player.mouseInterface = true;
            }

            Layout.HoverAuto = Layout.AutoRect.Contains(mouse);
            Layout.HoverFast = Layout.FastRect.Contains(mouse);
            Layout.HoverSkip = Layout.SkipRect.Contains(mouse);
            Layout.HoverContinue = Layout.ContinueRect.Contains(mouse);

            bool pressed = keyLeftPressState == KeyPressState.Pressed;
            if (!pressed) {
                return;
            }

            if (Layout.ShowHints && Layout.HoverAuto) {
                session.ToggleAuto();
                return;
            }
            if (Layout.ShowHints && Layout.HoverFast) {
                session.ToggleFast();
                return;
            }
            if (Layout.ShowHints && Layout.HoverSkip) {
                StyleRegistry.GetDialogue(session.Style).PlaySkipSound();
                session.RequestSkipToNextStop();
                return;
            }
            if (hoverPanel) {
                session.RequestAdvance();
            }
        }

        /// <inheritdoc/>
        public override void Draw(SpriteBatch spriteBatch) {
            if (OpenProgress.Current <= 0.01f || Layout.PanelRect == Rectangle.Empty) {
                return;
            }

            Skin.DrawBackground(spriteBatch, Layout);
            Skin.DrawBackgroundDecorations(spriteBatch, Layout);

            if (Layout.HasPortrait && Layout.Portrait != null) {
                Skin.DrawPortraitFrame(spriteBatch, Layout);
                DrawPortrait(spriteBatch);
            }

            Skin.DrawSpeakerName(spriteBatch, Layout);
            Skin.DrawDivider(spriteBatch, Layout);
            Skin.DrawText(spriteBatch, Layout);
            Skin.DrawTimedIndicator(spriteBatch, Layout);
            Skin.DrawCommandHints(spriteBatch, Layout);
            Skin.DrawForegroundDecorations(spriteBatch, Layout);
        }

        protected virtual void DrawPortrait(SpriteBatch spriteBatch) {
            Texture2D portrait = Layout.Portrait;
            if (portrait == null || Layout.PortraitRect == Rectangle.Empty) {
                return;
            }

            Rectangle? sourceRect = Layout.PortraitSourceRect;
            Vector2 sourceSize = sourceRect.HasValue
                ? new Vector2(sourceRect.Value.Width, sourceRect.Value.Height)
                : portrait.Size();

            float border = Skin.PortraitFrameBorder + Skin.PortraitInnerPadding;
            Rectangle inner = Layout.PortraitRect;
            inner.X += (int)border;
            inner.Y += (int)border;
            inner.Width -= (int)(border * 2f);
            inner.Height -= (int)(border * 2f);
            if (inner.Width <= 0 || inner.Height <= 0) {
                return;
            }

            float scale = Math.Min(inner.Width / sourceSize.X, inner.Height / sourceSize.Y);
            float drawnW = sourceSize.X * scale;
            float drawnH = sourceSize.Y * scale;
            Vector2 pos = new(inner.X + (inner.Width - drawnW) * 0.5f, inner.Bottom - drawnH);
            Color color = Layout.Silhouette ? Skin.SilhouetteColor * Layout.Alpha : Color.White * Layout.Alpha;
            spriteBatch.Draw(portrait, pos, sourceRect, color, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
        }
    }
}
