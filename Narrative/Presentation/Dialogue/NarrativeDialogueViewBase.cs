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
    public abstract class NarrativeDialogueViewBase<TSelf> : UIHandle<TSelf>, INarrativeView, INarrativePanelAnchorProvider
        where TSelf : NarrativeDialogueViewBase<TSelf>
    {
        protected DialogueSkin Skin = new BasicDialogueSkin();
        protected readonly DialogueLayoutContext Layout = new();
        private DialogueSkin _lastSkin;

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
            NarrativeSession session = NarrativeRunner.Active;
            if (session == null || !session.DialogueVisible) {
                return;
            }

            Skin = StyleRegistry.GetDialogue(session.Style);
            if (!ReferenceEquals(Skin, _lastSkin)) {
                _lastSkin = Skin;
                Skin.Reset();
            }
            LinePresentation line = session.Line;
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
                OpenProgress = OpenProgress.Current,
                GlobalTimer = GlobalTimer,
            };

            Skin.Layout(input, Layout);
            UpdateLineLayout(line, font);
            PopulateRuntimeState(session, line);
            Skin.Update(Layout);
            HandleInput(session);
        }

        private void UpdateLineLayout(LinePresentation line, DynamicSpriteFont font) {
            float width = Math.Max(60f, Layout.TextRect.Width / Math.Max(0.01f, Layout.TextScale));
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

        private void PopulateRuntimeState(NarrativeSession session, LinePresentation line) {
            Layout.Alpha = OpenProgress.Current;
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

            float scale = Math.Min((Layout.PortraitRect.Width - 6f) / portrait.Width, (Layout.PortraitRect.Height - 6f) / portrait.Height);
            Vector2 center = Layout.PortraitRect.Center.ToVector2();
            Color color = Layout.Silhouette ? Skin.SilhouetteColor * Layout.Alpha : Color.White * Layout.Alpha;
            spriteBatch.Draw(portrait, center, null, color, 0f, portrait.Size() / 2f, scale, SpriteEffects.None, 0f);
        }
    }
}
