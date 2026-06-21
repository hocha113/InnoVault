using InnoVault.Narrative.Core;
using InnoVault.Narrative.Presentation.Anchors;
using InnoVault.Narrative.Runtime;
using InnoVault.Narrative.Styling;
using InnoVault.UIHandles;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using Terraria;
using Terraria.GameContent;

namespace InnoVault.Narrative.Presentation.Popups
{
    /// <summary>
    /// 可复用的功能弹窗视图基座。负责读取当前 <see cref="PopupPayload"/>、
    /// 处理领取 / 关闭意图，具体表现由 <see cref="PopupSkin"/> 负责。
    /// </summary>
    public abstract class NarrativePopupViewBase<TSelf> : NarrativePanelViewBase<TSelf>, INarrativeView
        where TSelf : NarrativePopupViewBase<TSelf>
    {
        protected PopupSkin Skin = new BasicPopupSkin();
        protected readonly PopupLayoutContext Layout = new();
        protected readonly PopupPresentationState State = new();

        /// <inheritdoc/>
        protected override float ShowDurationFrames => 24f;

        /// <inheritdoc/>
        protected override float HideDurationFrames => 16f;

        private PopupPayload _lastPayload;
        private PopupSkin _lastSkin;
        private bool _openSoundPlayed;

        /// <inheritdoc/>
        public override LayersModeEnum LayersMode => LayersModeEnum.Vanilla_Mouse_Text;
        /// <inheritdoc/>
        public override float RenderPriority => 3f;

        /// <summary>该视图是否为 InnoVault 内置默认弹窗视图。</summary>
        protected virtual bool IsDefaultPopupView => false;

        /// <summary>当前视图是否应注册到 NarrativeViews。</summary>
        protected virtual bool ShouldRegisterView => !IsDefaultPopupView || NarrativeViews.UseDefaultPopupView;

        /// <inheritdoc/>
        public override void VaultSetup() {
            base.VaultSetup();
            if (ShouldRegisterView) {
                NarrativeViews.Register(this);
            }
        }

        /// <inheritdoc/>
        public virtual void Sync(NarrativeSession active) {
            if (active != null && active.ActivePopup != null) {
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
            PopupPayload payload = session?.ActivePopup;
            if (payload == null) {
                return;
            }

            Skin = StyleRegistry.GetPopup(session.Style);
            bool skinChanged = !ReferenceEquals(Skin, _lastSkin);
            if (skinChanged) {
                _lastSkin = Skin;
                Skin.Reset();
            }
            if (!ReferenceEquals(payload, _lastPayload)) {
                _lastPayload = payload;
                State.Reset();
                _openSoundPlayed = false;
                if (!skinChanged) {
                    Skin.Reset();
                }
            }
            if (!_openSoundPlayed) {
                Skin.PlayOpenSound();
                _openSoundPlayed = true;
            }
            Layout.Payload = payload;
            Layout.State = State;
            Layout.Font = FontAssets.MouseText.Value;
            Layout.Title = payload.Title ?? string.Empty;
            Layout.Body = payload.BodyText;
            Layout.IconItemType = payload.IconItemType;
            Layout.RequireClaim = payload.RequireClaim;
            Layout.Alpha = NarrativePanelMotion.ResolveAlpha(MotionProgress, NarrativePanelMotion.Profile.Popup);
            Layout.GlobalTimer = GlobalTimer;

            Skin.Layout(ResolvePopupAnchor(payload), MotionProgress, GlobalTimer, Layout);
            UpdatePresentationState();
            Skin.Update(Layout);
            HandleInput(session);
        }

        protected virtual void UpdatePresentationState() {
            State.Timer++;
            State.Alpha = NarrativePanelMotion.ResolveAlpha(MotionProgress, NarrativePanelMotion.Profile.Popup);
            State.Appear = Math.Min(1f, State.Appear + 0.12f);
            State.Scale = MathHelper.Lerp(State.Scale <= 0f ? 0.6f : State.Scale, 1f, 0.18f);
            Layout.ContentAppear = State.Appear;
        }

        private void UpdateClosingPresentation() {
            if (Layout.PanelRect == Rectangle.Empty) {
                return;
            }

            Layout.Alpha = NarrativePanelMotion.ResolveAlpha(MotionProgress, NarrativePanelMotion.Profile.Popup);
            Layout.GlobalTimer = GlobalTimer;
            State.Alpha = Layout.Alpha;

            Skin.Layout(ResolvePopupAnchor(_lastPayload), MotionProgress, GlobalTimer, Layout, isClosing: true);
            Skin.Update(Layout);
        }

        /// <summary>解析弹窗锚点；consumer 可覆写以匹配 ADV 面板奖励定位</summary>
        protected virtual Vector2 ResolvePopupAnchor(PopupPayload payload)
            => PanelAnchorResolver.AboveDialogue(Skin.PanelSize.Y + 24f);

        protected virtual void HandleInput(NarrativeSession session) {
            Point mouse = new(Main.mouseX, Main.mouseY);
            State.Hover = Layout.PanelRect.Contains(mouse);
            if (!State.Hover) {
                return;
            }

            player.mouseInterface = true;
            if (keyLeftPressState != KeyPressState.Pressed) {
                return;
            }

            if (Layout.RequireClaim) {
                Skin.PlayClaimSound();
                session.ClaimPopup();
            }
            else {
                session.DismissPopup();
            }
        }

        /// <inheritdoc/>
        public override void Draw(SpriteBatch spriteBatch) {
            if (OpenProgress.Current <= 0.01f || Layout.PanelRect == Rectangle.Empty) {
                return;
            }

            Skin.DrawPanel(spriteBatch, Layout);
            Skin.DrawFrame(spriteBatch, Layout);
            Skin.DrawParticles(spriteBatch, Layout);
            Skin.DrawIcon(spriteBatch, Layout);
            Skin.DrawTitle(spriteBatch, Layout);
            Skin.DrawBody(spriteBatch, Layout);
            Skin.DrawHint(spriteBatch, Layout);
        }
    }
}
