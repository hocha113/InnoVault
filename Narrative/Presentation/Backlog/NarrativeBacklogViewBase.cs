using InnoVault.Narrative.Core;
using InnoVault.Narrative.History;
using InnoVault.Narrative.Portraits;
using InnoVault.Narrative.Runtime;
using InnoVault.Narrative.Styling;
using InnoVault.UIHandles;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using Terraria;
using Terraria.GameContent;

namespace InnoVault.Narrative.Presentation.Backlog
{
    /// <summary>
    /// 可复用的 backlog（历史对话）视图基座。读取 <see cref="NarrativeHistory.Entries"/>、把条目经
    /// <see cref="PortraitRegistry"/> 解析为展示态、处理滚动与关闭，绘制委托给 <see cref="BacklogSkin"/>。<br/>
    /// 与逐帧同步的对话 / 选择视图不同，它由玩家主动开关，通过 <see cref="NarrativeHistory"/> 注册为当前 backlog 视图
    /// </summary>
    public abstract class NarrativeBacklogViewBase<TSelf> : UIHandle<TSelf>, INarrativeBacklogView
        where TSelf : NarrativeBacklogViewBase<TSelf>
    {
        /// <summary>当前使用的 backlog 皮肤，由 <see cref="StyleRegistry.GetBacklog"/> 解析</summary>
        protected BacklogSkin Skin = new BasicBacklogSkin();
        /// <summary>单帧布局结果，供 <see cref="BacklogSkin"/> 读写</summary>
        protected readonly BacklogLayoutContext Layout = new();

        private readonly List<BacklogRowPresentation> _rows = [];
        private BacklogSkin _lastSkin;
        private float _scrollOffset;
        private bool _scrollToBottomPending;

        /// <summary>每次滚轮一格的滚动像素</summary>
        protected virtual float ScrollStep => 64f;

        /// <inheritdoc/>
        public override LayersModeEnum LayersMode => LayersModeEnum.Vanilla_Mouse_Text;
        /// <inheritdoc/>
        public override float RenderPriority => 10f;
        /// <inheritdoc/>
        public override bool CloseOnEscape => true;
        /// <inheritdoc/>
        public override bool CanDrag => false;

        /// <summary>该视图是否为 InnoVault 内置默认 backlog 视图</summary>
        protected virtual bool IsDefaultBacklogView => false;

        /// <summary>当前视图是否应注册为生效的 backlog 视图</summary>
        protected virtual bool ShouldRegisterView => !IsDefaultBacklogView || NarrativeViews.UseDefaultBacklogView;

        /// <summary>解析 backlog 皮肤所用样式：有活动会话时跟随其主题，否则用默认</summary>
        protected virtual StyleId BacklogStyle => NarrativeRunner.Active?.Style ?? StyleId.Default;

        /// <inheritdoc/>
        public override void VaultSetup() {
            base.VaultSetup();
            if (ShouldRegisterView) {
                NarrativeHistory.Register(this);
            }
        }

        /// <inheritdoc/>
        public override void Open() {
            _scrollToBottomPending = true;
            base.Open();
        }

        /// <inheritdoc/>
        public override void Update() {
            Skin = StyleRegistry.GetBacklog(BacklogStyle);
            if (!ReferenceEquals(Skin, _lastSkin)) {
                _lastSkin = Skin;
                Skin.Reset();
            }

            BuildRows();

            BacklogLayoutInput input = new() {
                Rows = _rows,
                Font = FontAssets.MouseText.Value,
                OpenProgress = OpenProgress.Current,
                GlobalTimer = GlobalTimer,
                ScrollOffset = _scrollOffset,
                IsClosing = IsClosing,
            };
            Skin.Layout(input, Layout);

            Layout.Alpha = OpenProgress.Current;
            Layout.ContentAlpha = OpenProgress.Current;
            Layout.GlobalTimer = GlobalTimer;

            if (_scrollToBottomPending && Layout.MaxScroll > 0f) {
                _scrollOffset = Layout.MaxScroll;
                _scrollToBottomPending = false;
            }
            _scrollOffset = MathHelper.Clamp(_scrollOffset, 0f, Layout.MaxScroll);
            Layout.ScrollOffset = _scrollOffset;

            Skin.Update(Layout);

            if (IsOpen) {
                HandleInput();
            }
        }

        private void BuildRows() {
            _rows.Clear();
            IReadOnlyList<NarrativeLogEntry> entries = NarrativeHistory.Entries;
            for (int i = 0; i < entries.Count; i++) {
                NarrativeLogEntry entry = entries[i];
                BacklogRowPresentation row = new() {
                    Kind = entry.Kind,
                    Text = entry.Text ?? string.Empty,
                    StartsConversation = entry.StartsConversation,
                };
                if (entry.Kind == NarrativeLogKind.Line) {
                    row.SpeakerName = PortraitRegistry.ResolveName(entry.Speaker);
                    row.Portrait = PortraitRegistry.ResolvePortrait(entry.Speaker, entry.Expression);
                    row.PortraitSource = PortraitRegistry.ResolvePortraitSource(entry.Speaker, entry.Expression);
                    row.Silhouette = PortraitRegistry.IsSilhouette(entry.Speaker);
                }
                _rows.Add(row);
            }
        }

        /// <summary>处理滚动、关闭按钮与点击空白关闭，consumer 可覆写以扩展</summary>
        protected virtual void HandleInput() {
            Point mouse = new(Main.mouseX, Main.mouseY);
            bool overPanel = Layout.PanelRect.Contains(mouse);
            if (overPanel) {
                player.mouseInterface = true;
            }

            if (Layout.HasScroll && overPanel) {
                int scroll = MouseScrollDelta;
                if (scroll != 0) {
                    _scrollOffset = MathHelper.Clamp(_scrollOffset - Math.Sign(scroll) * ScrollStep, 0f, Layout.MaxScroll);
                }
            }

            Layout.HoverClose = Layout.CloseRect.Contains(mouse);

            if (keyLeftPressState != KeyPressState.Pressed) {
                return;
            }
            if (Layout.HoverClose) {
                Close();
                return;
            }
            if (!overPanel) {
                Close();
            }
        }

        /// <inheritdoc/>
        public override void Draw(SpriteBatch spriteBatch) {
            if (OpenProgress.Current <= 0.01f || Layout.PanelRect == Rectangle.Empty) {
                return;
            }

            Skin.DrawBackground(spriteBatch, Layout);
            Skin.DrawRows(spriteBatch, Layout);
            Skin.DrawEmpty(spriteBatch, Layout);
            Skin.DrawChrome(spriteBatch, Layout);
            Skin.DrawScrollbar(spriteBatch, Layout);
            Skin.DrawFrame(spriteBatch, Layout);
        }
    }
}
