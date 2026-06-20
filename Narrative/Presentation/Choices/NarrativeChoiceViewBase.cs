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

namespace InnoVault.Narrative.Presentation.Choices
{
    /// <summary>
    /// 可复用的叙事选择框视图基座。负责读取选项、滚动、悬停与点击选择，
    /// 具体布局和绘制交给 <see cref="ChoiceSkin"/>。
    /// </summary>
    public abstract class NarrativeChoiceViewBase<TSelf> : UIHandle<TSelf>, INarrativeView
        where TSelf : NarrativeChoiceViewBase<TSelf>
    {
        protected ChoiceSkin Skin = new BasicChoiceSkin();
        protected readonly ChoiceLayoutContext Layout = new();

        private object _lastOptionsRef;
        private ChoiceSkin _lastSkin;
        private int _scrollOffset;

        /// <inheritdoc/>
        public override LayersModeEnum LayersMode => LayersModeEnum.Vanilla_Mouse_Text;
        /// <inheritdoc/>
        public override float RenderPriority => 2f;

        /// <summary>该视图是否为 InnoVault 内置默认选择框视图。</summary>
        protected virtual bool IsDefaultChoiceView => false;

        /// <summary>当前视图是否应注册到 NarrativeViews。</summary>
        protected virtual bool ShouldRegisterView => !IsDefaultChoiceView || NarrativeViews.UseDefaultChoiceView;

        /// <inheritdoc/>
        public override void VaultSetup() {
            base.VaultSetup();
            if (ShouldRegisterView) {
                NarrativeViews.Register(this);
            }
        }

        /// <inheritdoc/>
        public virtual void Sync(NarrativeSession active) {
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

            IReadOnlyList<ChoiceOption> options = session.ChoiceOptions;
            if (options == null || options.Count == 0) {
                return;
            }

            Skin = StyleRegistry.GetChoice(session.Style);
            bool skinChanged = !ReferenceEquals(Skin, _lastSkin);
            if (skinChanged) {
                _lastSkin = Skin;
                Skin.Reset();
            }
            if (!ReferenceEquals(options, _lastOptionsRef)) {
                _lastOptionsRef = options;
                _scrollOffset = 0;
                if (!skinChanged) {
                    Skin.Reset();
                }
            }
            BuildOptions(options);

            DynamicSpriteFont font = FontAssets.MouseText.Value;
            _scrollOffset = Math.Clamp(_scrollOffset, 0, Math.Max(0, Layout.Options.Count - Skin.MaxVisibleOptions));
            Skin.Layout(font, PanelAnchorResolver.AboveDialogue(), OpenProgress.Current, _scrollOffset, session.ChoiceIsTimed, session.ChoiceTimedProgress, GlobalTimer, Layout);
            Skin.Update(Layout);
            HandleInput(session);
        }

        private void BuildOptions(IReadOnlyList<ChoiceOption> options) {
            Layout.Options.Clear();
            foreach (ChoiceOption option in options) {
                bool enabled = option.IsEnabled;
                Layout.Options.Add(new ChoiceOptionPresentation(option.Text ?? string.Empty, enabled, option.DisabledHint));
            }
        }

        protected virtual void HandleInput(NarrativeSession session) {
            Point mouse = new(Main.mouseX, Main.mouseY);
            bool overPanel = Layout.PanelRect.Contains(mouse);
            if (overPanel) {
                player.mouseInterface = true;
            }

            if (Layout.HasScroll && overPanel) {
                int scroll = MouseScrollDelta;
                if (scroll != 0) {
                    _scrollOffset = Math.Clamp(_scrollOffset - Math.Sign(scroll), 0, Layout.Options.Count - Layout.VisibleCount);
                }
            }

            Layout.HoverIndex = -1;
            bool pressed = keyLeftPressState == KeyPressState.Pressed;
            for (int i = 0; i < Layout.OptionRects.Count; i++) {
                if (Layout.OptionRects[i].Contains(mouse)) {
                    int optionIndex = Layout.ScrollOffset + i;
                    Layout.HoverIndex = optionIndex;
                    if (pressed && optionIndex < Layout.Options.Count && Layout.Options[optionIndex].Enabled) {
                        session.SelectChoice(optionIndex);
                    }
                    break;
                }
            }
            session.ChoiceHoverIndex = Layout.HoverIndex;
        }

        /// <inheritdoc/>
        public override void Draw(SpriteBatch spriteBatch) {
            if (OpenProgress.Current <= 0.01f || Layout.PanelRect == Rectangle.Empty) {
                return;
            }

            Skin.DrawPanel(spriteBatch, Layout);
            Skin.DrawTitle(spriteBatch, Layout);
            Skin.DrawDivider(spriteBatch, Layout);
            Skin.DrawOptions(spriteBatch, Layout);
            Skin.DrawScrollHints(spriteBatch, Layout);
            Skin.DrawTimedIndicator(spriteBatch, Layout);
            Skin.DrawForegroundDecorations(spriteBatch, Layout);
        }
    }
}
