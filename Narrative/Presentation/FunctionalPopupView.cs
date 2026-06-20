using InnoVault.UIHandles;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using Terraria;
using Terraria.GameContent;

namespace InnoVault.Narrative
{
    /// <summary>
    /// 默认功能弹窗视图（奖励 / 提示）。只展示载荷并回传领取 / 关闭意图，<br/>
    /// 真正的发放副作用由载荷 <see cref="PopupPayload.OnClaimed"/> 委托给宿主服务执行
    /// </summary>
    public sealed class FunctionalPopupView : UIHandle<FunctionalPopupView>, INarrativeView
    {
        /// <inheritdoc/>
        public override LayersModeEnum LayersMode => LayersModeEnum.Vanilla_Mouse_Text;
        /// <summary>绘制在对话框与选择框之上</summary>
        public override float RenderPriority => 3f;

        private PopupSkin _skin = new BasicPopupSkin();
        private Rectangle _panelRect;
        private string _title = string.Empty;
        private string _body;
        private int _iconItemType;
        private bool _requireClaim;
        private bool _hasCache;

        /// <inheritdoc/>
        public override void VaultSetup() {
            base.VaultSetup();
            NarrativeViews.Register(this);
        }

        /// <inheritdoc/>
        public void Sync(NarrativeSession active) {
            if (active != null && active.ActivePopup != null) {
                Open();
            }
            else {
                Close();
            }
        }

        /// <inheritdoc/>
        public override void Update() {
            NarrativeSession session = NarrativeRunner.Active;
            PopupPayload payload = session?.ActivePopup;
            if (payload == null) {
                return;
            }

            _skin = StyleRegistry.GetPopup(session.Style);
            _title = payload.Title ?? string.Empty;
            _body = payload.BodyText;
            _iconItemType = payload.IconItemType;
            _requireClaim = payload.RequireClaim;

            Vector2 size = _skin.PanelSize;
            Vector2 anchor = PanelAnchorResolver.AboveDialogue(size.Y + 24f);
            float eased = VaultUtils.EaseOutCubic(OpenProgress);
            Vector2 pos = new(anchor.X - size.X / 2f, anchor.Y - size.Y / 2f);
            pos.Y -= (1f - eased) * 30f;
            _panelRect = new Rectangle((int)pos.X, (int)pos.Y, (int)size.X, (int)size.Y);
            _hasCache = true;

            Point mouse = new(Main.mouseX, Main.mouseY);
            bool hover = _panelRect.Contains(mouse);
            if (hover) {
                player.mouseInterface = true;
                if (keyLeftPressState == KeyPressState.Pressed) {
                    session.ClaimPopup();
                }
            }
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

            Vector2 center = _panelRect.Center.ToVector2();

            if (_iconItemType > 0) {
                Main.instance.LoadItem(_iconItemType);
                Texture2D tex = TextureAssets.Item[_iconItemType].Value;
                if (tex != null) {
                    float iconScale = Math.Min(48f / tex.Width, 48f / tex.Height);
                    float floatOff = (float)Math.Sin(GlobalTimer * 3f) * 3f;
                    spriteBatch.Draw(tex, center - new Vector2(0, 22f - floatOff), null, Color.White * alpha, 0f, tex.Size() / 2f, iconScale * 1.4f, SpriteEffects.None, 0f);
                }
            }

            if (!string.IsNullOrEmpty(_title)) {
                Vector2 size = FontAssets.MouseText.Value.MeasureString(_title) * 0.8f;
                Utils.DrawBorderString(spriteBatch, _title, center + new Vector2(-size.X / 2f, 12f), _skin.TitleColor * alpha, 0.8f);
            }

            if (!string.IsNullOrEmpty(_body)) {
                Vector2 size = FontAssets.MouseText.Value.MeasureString(_body) * 0.7f;
                Utils.DrawBorderString(spriteBatch, _body, center + new Vector2(-size.X / 2f, 34f), _skin.BodyColor * alpha, 0.7f);
            }

            string hint = _requireClaim ? NarrativeUIText.ClaimHint : NarrativeUIText.ContinueHint;
            Vector2 hintSize = FontAssets.MouseText.Value.MeasureString(hint) * 0.6f;
            float blink = (float)(Math.Sin(GlobalTimer * 6f) * 0.5 + 0.5);
            Utils.DrawBorderString(spriteBatch, hint,
                new Vector2(_panelRect.Right - hintSize.X - 10f, _panelRect.Bottom - hintSize.Y - 8f),
                _skin.HintColor * (alpha * (0.6f + blink * 0.4f)), 0.6f);
        }
    }
}
