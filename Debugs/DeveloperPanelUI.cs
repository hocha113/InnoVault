using InnoVault.UIHandles;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;

namespace InnoVault.Debugs
{
    internal abstract class DebugTab
    {
        public abstract string TabName { get; }
        public abstract string TabIcon { get; }
        public List<DebugCheckbox> Checkboxes { get; protected set; } = [];

        public abstract void Initialize();

        public virtual void Reset() {
            foreach (var checkbox in Checkboxes) {
                checkbox.SetValue(false);
            }
        }
    }

    internal class TileProcessorDebugTab : DebugTab
    {
        public override string TabName => DeveloperPanelUI.TPTabText?.Value ?? "TileProcessor";
        public override string TabIcon => "TP";

        public override void Initialize() {
            Checkboxes.Clear();
            Checkboxes.Add(new DebugCheckbox(DeveloperPanelUI.TPBoxDrawText?.Value ?? "Collision Box",
                () => DebugSettings.TileProcessorBoxSizeDraw,
                v => DebugSettings.TileProcessorBoxSizeDraw = v));
            Checkboxes.Add(new DebugCheckbox(DeveloperPanelUI.TPShowNameText?.Value ?? "Name Display",
                () => DebugSettings.TileProcessorShowName,
                v => DebugSettings.TileProcessorShowName = v));
            Checkboxes.Add(new DebugCheckbox(DeveloperPanelUI.TPShowPositionText?.Value ?? "Position Display",
                () => DebugSettings.TileProcessorShowPosition,
                v => DebugSettings.TileProcessorShowPosition = v));
            Checkboxes.Add(new DebugCheckbox(DeveloperPanelUI.TPShowIDText?.Value ?? "ID Display",
                () => DebugSettings.TileProcessorShowID,
                v => DebugSettings.TileProcessorShowID = v));
            //并行更新的排障开关：勾选即强制走历史的单线程路径
            Checkboxes.Add(new DebugCheckbox(DeveloperPanelUI.TPForceSerialText?.Value ?? "Force Serial Update",
                () => !TileProcessors.TileProcessorParallel.EnableParallel,
                v => TileProcessors.TileProcessorParallel.EnableParallel = !v));
        }

        public override void Reset() {
            DebugSettings.ResetTileProcessor();
        }
    }

    internal class ActorDebugTab : DebugTab
    {
        public override string TabName => DeveloperPanelUI.ActorTabText?.Value ?? "Actor";
        public override string TabIcon => "AC";

        public override void Initialize() {
            Checkboxes.Clear();
            Checkboxes.Add(new DebugCheckbox(DeveloperPanelUI.ActorBoxDrawText?.Value ?? "Collision Box",
                () => DebugSettings.ActorBoxSizeDraw,
                v => DebugSettings.ActorBoxSizeDraw = v));
            Checkboxes.Add(new DebugCheckbox(DeveloperPanelUI.ActorShowNameText?.Value ?? "Name Display",
                () => DebugSettings.ActorShowName,
                v => DebugSettings.ActorShowName = v));
            Checkboxes.Add(new DebugCheckbox(DeveloperPanelUI.ActorShowPositionText?.Value ?? "Position Display",
                () => DebugSettings.ActorShowPosition,
                v => DebugSettings.ActorShowPosition = v));
            Checkboxes.Add(new DebugCheckbox(DeveloperPanelUI.ActorShowIDText?.Value ?? "ID Display",
                () => DebugSettings.ActorShowID,
                v => DebugSettings.ActorShowID = v));
            Checkboxes.Add(new DebugCheckbox(DeveloperPanelUI.ActorShowVelocityText?.Value ?? "Velocity Display",
                () => DebugSettings.ActorShowVelocity,
                v => DebugSettings.ActorShowVelocity = v));
            //并行更新的排障开关（与 TileProcessor 共用同一个全局开关）：勾选即强制走单线程路径
            Checkboxes.Add(new DebugCheckbox(DeveloperPanelUI.ActorForceSerialText?.Value ?? "Force Serial Update",
                () => !Concurrent.VaultParallel.EnableParallel,
                v => Concurrent.VaultParallel.EnableParallel = !v));
        }

        public override void Reset() {
            DebugSettings.ResetActor();
        }
    }

    internal class StateMachineDebugTab : DebugTab
    {
        public override string TabName => DeveloperPanelUI.StateMachineTabText?.Value ?? "StateMachine";
        public override string TabIcon => "SM";

        public override void Initialize() {
            Checkboxes.Clear();
            Checkboxes.Add(new DebugCheckbox(DeveloperPanelUI.StateMachineOverlayText?.Value ?? "FSM Overlay",
                () => DebugSettings.StateMachineShowOverlay,
                v => DebugSettings.StateMachineShowOverlay = v));
            Checkboxes.Add(new DebugCheckbox(DeveloperPanelUI.BehaviorTreeOverlayText?.Value ?? "BT Overlay",
                () => DebugSettings.BehaviorTreeShowOverlay,
                v => DebugSettings.BehaviorTreeShowOverlay = v));
        }

        public override void Reset() {
            DebugSettings.ResetStateMachine();
        }
    }

    internal class DebugCheckbox
    {
        public string Label;
        public Func<bool> GetValue;
        public Action<bool> SetValue;

        public DebugCheckbox(string label, Func<bool> getValue, Action<bool> setValue) {
            Label = label;
            GetValue = getValue;
            SetValue = setValue;
        }
    }

    internal class DebugMiniIndicator : UIHandle
    {
        private const float IndicatorWidth = 140f;
        private const float IndicatorHeight = 36f;

        private float pulseTimer;
        private Rectangle indicatorRect;
        private bool hoveringIndicator;
        private bool hoveringCloseAll;

        public override bool Active => DebugSettings.AnyDebugEnabled && !DeveloperPanelUI.Instance.Active;
        public override bool AutoUpdateHitBox => true;
        public override bool BlockMouseWhenHovered => true;
        public override bool CanDrag => true;
        public override MouseButtonType DragMouseButton => MouseButtonType.Left;
        public override Rectangle? DragHandleRect
            => new((int)DrawPosition.X, (int)DrawPosition.Y, (int)(IndicatorWidth - 34), (int)IndicatorHeight);

        internal DebugMiniIndicator() {
            Size = new Vector2(IndicatorWidth, IndicatorHeight);
        }

        public override void OnEnterWorld() {
            Size = new Vector2(IndicatorWidth, IndicatorHeight);
            DrawPosition = new Vector2(Main.screenWidth - IndicatorWidth - 20, 100);
        }

        public override void Update() {
            pulseTimer += 0.05f;
            if (pulseTimer > MathHelper.TwoPi) pulseTimer -= MathHelper.TwoPi;

            DrawPosition.X = MathHelper.Clamp(DrawPosition.X, 10, Main.screenWidth - IndicatorWidth - 10);
            DrawPosition.Y = MathHelper.Clamp(DrawPosition.Y, 10, Main.screenHeight - IndicatorHeight - 10);

            indicatorRect = new Rectangle((int)DrawPosition.X, (int)DrawPosition.Y, (int)IndicatorWidth, (int)IndicatorHeight);
            Rectangle closeAllRect = new(indicatorRect.Right - 28, indicatorRect.Y + 6, 22, 24);

            Vector2 mousePos = new(Main.mouseX, Main.mouseY);
            hoveringIndicator = indicatorRect.Contains(mousePos.ToPoint());
            hoveringCloseAll = closeAllRect.Contains(mousePos.ToPoint()) && !IsDragging;
            HandleClicks();
        }

        protected override void OnDragStart() {
            SoundEngine.PlaySound(SoundID.MenuTick with { Volume = 0.3f });
        }

        private void HandleClicks() {
            if (UIHandleLoader.keyRightPressState != KeyPressState.Pressed) {
                return;
            }

            if (hoveringCloseAll) {
                DebugSettings.ResetAll();
                SoundEngine.PlaySound(SoundID.MenuClose);
            }
            else if (hoveringIndicator && !IsDragging) {
                DeveloperPanelUI.Instance?.Toggle();
            }
        }

        public override void Draw(SpriteBatch spriteBatch) {
            Texture2D px = VaultAsset.placeholder2.Value;
            float pulse = (float)Math.Sin(pulseTimer) * 0.3f + 0.7f;

            spriteBatch.Draw(px, indicatorRect, new Rectangle(0, 0, 1, 1), new Color(10, 25, 45) * 0.92f);

            Color borderColor = Color.Lerp(new Color(80, 150, 220), new Color(120, 200, 255), pulse);
            spriteBatch.Draw(px, new Rectangle(indicatorRect.X, indicatorRect.Y, indicatorRect.Width, 2), new Rectangle(0, 0, 1, 1), borderColor * 0.9f);
            spriteBatch.Draw(px, new Rectangle(indicatorRect.X, indicatorRect.Bottom - 2, indicatorRect.Width, 2), new Rectangle(0, 0, 1, 1), borderColor * 0.5f);
            spriteBatch.Draw(px, new Rectangle(indicatorRect.X, indicatorRect.Y, 2, indicatorRect.Height), new Rectangle(0, 0, 1, 1), borderColor * 0.7f);
            spriteBatch.Draw(px, new Rectangle(indicatorRect.Right - 2, indicatorRect.Y, 2, indicatorRect.Height), new Rectangle(0, 0, 1, 1), borderColor * 0.7f);

            Vector2 ledPos = new(indicatorRect.X + 14, indicatorRect.Center.Y);
            Color ledColor = Color.Lerp(new Color(100, 200, 255), new Color(150, 230, 255), pulse);
            spriteBatch.Draw(px, ledPos, new Rectangle(0, 0, 1, 1), ledColor, 0f, new Vector2(0.5f), 6f, SpriteEffects.None, 0f);
            spriteBatch.Draw(px, ledPos, new Rectangle(0, 0, 1, 1), Color.White * 0.5f, 0f, new Vector2(0.5f), 3f, SpriteEffects.None, 0f);

            string text = $"DEBUG [{DebugSettings.EnabledCount}]";
            Color textColor = hoveringIndicator && !hoveringCloseAll ? new Color(200, 230, 255) : new Color(150, 190, 230);
            Utils.DrawBorderString(spriteBatch, text, new Vector2(indicatorRect.X + 26, indicatorRect.Y + 8), textColor, 0.7f);

            Rectangle closeAllRect = new(indicatorRect.Right - 28, indicatorRect.Y + 6, 22, 24);
            Color closeBg = hoveringCloseAll ? new Color(80, 40, 40) : new Color(40, 30, 35);
            Color closeBorder = hoveringCloseAll ? new Color(255, 120, 120) : new Color(120, 80, 80);

            spriteBatch.Draw(px, closeAllRect, new Rectangle(0, 0, 1, 1), closeBg * 0.9f);
            spriteBatch.Draw(px, new Rectangle(closeAllRect.X, closeAllRect.Y, closeAllRect.Width, 1), new Rectangle(0, 0, 1, 1), closeBorder * 0.7f);
            spriteBatch.Draw(px, new Rectangle(closeAllRect.X, closeAllRect.Y, 1, closeAllRect.Height), new Rectangle(0, 0, 1, 1), closeBorder * 0.5f);

            Vector2 xCenter = closeAllRect.Center.ToVector2();
            Color xColor = hoveringCloseAll ? new Color(255, 150, 150) : new Color(180, 120, 120);
            spriteBatch.Draw(px, xCenter, new Rectangle(0, 0, 1, 1), xColor, MathHelper.PiOver4, new Vector2(0.5f), new Vector2(10f, 1.5f), SpriteEffects.None, 0f);
            spriteBatch.Draw(px, xCenter, new Rectangle(0, 0, 1, 1), xColor, -MathHelper.PiOver4, new Vector2(0.5f), new Vector2(10f, 1.5f), SpriteEffects.None, 0f);
        }
    }

    internal class DeveloperPanelUI : UIHandle, ILocalizedModType
    {
        private const float PanelWidth = 400f;
        private const float PanelHeight = 380f;
        private const float TabHeight = 35f;
        private const float TitleHeight = 45f;

        private float scanLineTimer;
        private float pulseTimer;
        private float glowTimer;
        private float dataFlowTimer;

        public static DeveloperPanelUI Instance => UIHandleLoader.GetUIHandleOfType<DeveloperPanelUI>();

        public bool IsPanelOpen => IsOpen;
        private float uiFadeAlpha => OpenProgress.Current;
        public override bool CloseOnEscape => true;
        public override SoundStyle? OpenSound => SoundID.MenuOpen;
        public override SoundStyle? CloseSound => SoundID.MenuClose;
        public override bool CanDrag => true;
        public override MouseButtonType DragMouseButton => MouseButtonType.Left;
        public override Rectangle? DragHandleRect
            => new(titleRect.X, titleRect.Y, Math.Max(0, titleRect.Width - 45), titleRect.Height);

        private Rectangle panelRect;
        private Rectangle titleRect;
        private Rectangle tabBarRect;
        private Rectangle contentRect;
        private Rectangle closeButtonRect;
        private Rectangle resetButtonRect;
        private Rectangle resetAllButtonRect;

        private readonly List<DebugTab> tabs = [];
        private int currentTabIndex;
        private readonly List<Rectangle> tabRects = [];

        private bool hoveringPanel;
        private bool hoveringCloseButton;
        private bool hoveringResetButton;
        private bool hoveringResetAllButton;
        private int hoveringTab = -1;
        private int hoveringCheckbox = -1;

        protected internal static LocalizedText TitleText;
        protected internal static LocalizedText TPTabText;
        protected internal static LocalizedText ActorTabText;
        protected internal static LocalizedText TPBoxDrawText;
        protected internal static LocalizedText TPShowNameText;
        protected internal static LocalizedText TPShowPositionText;
        protected internal static LocalizedText TPShowIDText;
        protected internal static LocalizedText TPForceSerialText;
        protected internal static LocalizedText ActorBoxDrawText;
        protected internal static LocalizedText ActorShowNameText;
        protected internal static LocalizedText ActorShowPositionText;
        protected internal static LocalizedText ActorShowIDText;
        protected internal static LocalizedText ActorShowVelocityText;
        protected internal static LocalizedText ActorForceSerialText;
        protected internal static LocalizedText StateMachineTabText;
        protected internal static LocalizedText StateMachineOverlayText;
        protected internal static LocalizedText BehaviorTreeOverlayText;
        protected internal static LocalizedText ResetText;
        protected internal static LocalizedText ResetAllText;
        protected internal static LocalizedText CloseText;

        public string LocalizationCategory => "UI";

        public override void SetStaticDefaults() {
            TitleText = this.GetLocalization(nameof(TitleText), () => "Developer Debug Panel");
            TPTabText = this.GetLocalization(nameof(TPTabText), () => "TileProcessor");
            ActorTabText = this.GetLocalization(nameof(ActorTabText), () => "Actor");
            TPBoxDrawText = this.GetLocalization(nameof(TPBoxDrawText), () => "Collision Box");
            TPShowNameText = this.GetLocalization(nameof(TPShowNameText), () => "Name Display");
            TPShowPositionText = this.GetLocalization(nameof(TPShowPositionText), () => "Position Display");
            TPShowIDText = this.GetLocalization(nameof(TPShowIDText), () => "ID Display");
            TPForceSerialText = this.GetLocalization(nameof(TPForceSerialText), () => "Force Serial Update");
            ActorBoxDrawText = this.GetLocalization(nameof(ActorBoxDrawText), () => "Collision Box");
            ActorShowNameText = this.GetLocalization(nameof(ActorShowNameText), () => "Name Display");
            ActorShowPositionText = this.GetLocalization(nameof(ActorShowPositionText), () => "Position Display");
            ActorShowIDText = this.GetLocalization(nameof(ActorShowIDText), () => "ID Display");
            ActorShowVelocityText = this.GetLocalization(nameof(ActorShowVelocityText), () => "Velocity Display");
            ActorForceSerialText = this.GetLocalization(nameof(ActorForceSerialText), () => "Force Serial Update");
            StateMachineTabText = this.GetLocalization(nameof(StateMachineTabText), () => "StateMachine");
            StateMachineOverlayText = this.GetLocalization(nameof(StateMachineOverlayText), () => "FSM Overlay");
            BehaviorTreeOverlayText = this.GetLocalization(nameof(BehaviorTreeOverlayText), () => "BT Overlay");
            ResetText = this.GetLocalization(nameof(ResetText), () => "Reset");
            ResetAllText = this.GetLocalization(nameof(ResetAllText), () => "Reset All");
            CloseText = this.GetLocalization(nameof(CloseText), () => "Close");
        }

        public override void OnEnterWorld() {
            InitializeTabs();
        }

        private void InitializeTabs() {
            tabs.Clear();
            tabs.Add(new TileProcessorDebugTab());
            tabs.Add(new ActorDebugTab());
            tabs.Add(new StateMachineDebugTab());

            foreach (var tab in tabs) {
                tab.Initialize();
            }

            currentTabIndex = 0;
        }

        protected override void OnOpen() {
            DrawPosition = new Vector2(Main.screenWidth / 2, Main.screenHeight / 2);
            if (tabs.Count == 0) {
                InitializeTabs();
            }
            foreach (var tab in tabs) {
                tab.Initialize();
            }
        }

        protected override void OnClose() {
            hoveringTab = -1;
            hoveringCheckbox = -1;
        }

        public override void Update() {
            if (uiFadeAlpha < 0.01f) {
                return;
            }

            DrawPosition.X = MathHelper.Clamp(DrawPosition.X, PanelWidth / 2 + 10, Main.screenWidth - PanelWidth / 2 - 10);
            DrawPosition.Y = MathHelper.Clamp(DrawPosition.Y, PanelHeight / 2 + 10, Main.screenHeight - PanelHeight / 2 - 10);

            scanLineTimer += 0.03f;
            pulseTimer += 0.02f;
            glowTimer += 0.035f;
            dataFlowTimer += 0.04f;

            if (scanLineTimer > MathHelper.TwoPi) scanLineTimer -= MathHelper.TwoPi;
            if (pulseTimer > MathHelper.TwoPi) pulseTimer -= MathHelper.TwoPi;
            if (glowTimer > MathHelper.TwoPi) glowTimer -= MathHelper.TwoPi;
            if (dataFlowTimer > MathHelper.TwoPi) dataFlowTimer -= MathHelper.TwoPi;

            Vector2 mousePos = new(Main.mouseX, Main.mouseY);
            UpdateLayout();
            UpdateHoverStates(mousePos);

            if (hoveringPanel) {
                player.mouseInterface = true;
            }

            if (IsOpen) {
                HandleButtonClicks();
            }
        }

        private void UpdateLayout() {
            Vector2 topLeft = DrawPosition - new Vector2(PanelWidth / 2, PanelHeight / 2);
            panelRect = new Rectangle((int)topLeft.X, (int)topLeft.Y, (int)PanelWidth, (int)PanelHeight);
            UIHitBox = panelRect;
            titleRect = new Rectangle(panelRect.X, panelRect.Y, panelRect.Width, (int)TitleHeight);
            tabBarRect = new Rectangle(panelRect.X, panelRect.Y + (int)TitleHeight, panelRect.Width, (int)TabHeight);
            contentRect = new Rectangle(panelRect.X + 15, tabBarRect.Bottom + 10, panelRect.Width - 30, panelRect.Height - (int)TitleHeight - (int)TabHeight - 60);
            closeButtonRect = new Rectangle(panelRect.Right - 35, panelRect.Y + 10, 25, 25);
            resetButtonRect = new Rectangle(panelRect.X + 15, panelRect.Bottom - 40, 80, 28);
            resetAllButtonRect = new Rectangle(panelRect.X + 105, panelRect.Bottom - 40, 90, 28);
            UpdateTabRects();
        }

        private void UpdateHoverStates(Vector2 mousePos) {
            Point mousePoint = mousePos.ToPoint();
            hoveringPanel = panelRect.Contains(mousePoint);
            hoverInMainPage = hoveringPanel;
            hoveringCloseButton = closeButtonRect.Contains(mousePoint) && !IsDragging;
            hoveringResetButton = resetButtonRect.Contains(mousePoint) && !IsDragging;
            hoveringResetAllButton = resetAllButtonRect.Contains(mousePoint) && !IsDragging;

            hoveringTab = -1;
            if (!IsDragging) {
                for (int i = 0; i < tabRects.Count; i++) {
                    if (tabRects[i].Contains(mousePoint)) {
                        hoveringTab = i;
                        break;
                    }
                }
            }

            hoveringCheckbox = -1;
            if (!IsDragging && currentTabIndex >= 0 && currentTabIndex < tabs.Count) {
                var currentTab = tabs[currentTabIndex];
                int checkboxY = contentRect.Y + 5;
                for (int i = 0; i < currentTab.Checkboxes.Count; i++) {
                    Rectangle checkboxRect = new(contentRect.X, checkboxY + i * 38, contentRect.Width, 34);
                    if (checkboxRect.Contains(mousePoint)) {
                        hoveringCheckbox = i;
                        break;
                    }
                }
            }
        }

        private void UpdateTabRects() {
            tabRects.Clear();
            if (tabs.Count == 0) return;

            int tabWidth = (tabBarRect.Width - 20) / tabs.Count;
            for (int i = 0; i < tabs.Count; i++) {
                tabRects.Add(new Rectangle(tabBarRect.X + 10 + i * tabWidth, tabBarRect.Y + 3, tabWidth - 4, tabBarRect.Height - 6));
            }
        }

        protected override void OnDragStart() {
            SoundEngine.PlaySound(SoundID.MenuTick with { Volume = 0.3f });
        }

        protected override void OnDragEnd() {
            SoundEngine.PlaySound(SoundID.MenuTick with { Volume = 0.3f });
        }

        private void HandleButtonClicks() {
            if (UIHandleLoader.keyLeftPressState != KeyPressState.Pressed) {
                return;
            }

            if (hoveringCloseButton) {
                Close();
            }
            else if (hoveringResetAllButton) {
                DebugSettings.ResetAll();
                SoundEngine.PlaySound(SoundID.MenuTick);
            }
            else if (hoveringResetButton && currentTabIndex >= 0 && currentTabIndex < tabs.Count) {
                tabs[currentTabIndex].Reset();
                SoundEngine.PlaySound(SoundID.MenuTick);
            }
            else if (hoveringTab >= 0 && hoveringTab < tabs.Count && hoveringTab != currentTabIndex) {
                currentTabIndex = hoveringTab;
                SoundEngine.PlaySound(SoundID.MenuTick);
            }
            else if (hoveringCheckbox >= 0 && currentTabIndex >= 0 && currentTabIndex < tabs.Count) {
                var currentTab = tabs[currentTabIndex];
                if (hoveringCheckbox < currentTab.Checkboxes.Count) {
                    var checkbox = currentTab.Checkboxes[hoveringCheckbox];
                    checkbox.SetValue(!checkbox.GetValue());
                    SoundEngine.PlaySound(SoundID.MenuTick);
                }
            }
        }

        public override void Draw(SpriteBatch spriteBatch) {
            if (uiFadeAlpha < 0.01f) return;

            DrawMainPanel(spriteBatch);
            DrawTitle(spriteBatch);
            DrawCloseButton(spriteBatch);
            DrawTabBar(spriteBatch);
            DrawCheckboxes(spriteBatch);
            DrawBottomButtons(spriteBatch);
        }

        private void DrawMainPanel(SpriteBatch sb) {
            Texture2D px = VaultAsset.placeholder2.Value;
            float alpha = uiFadeAlpha;

            int segments = 40;
            for (int i = 0; i < segments; i++) {
                float t = i / (float)segments;
                float t2 = (i + 1) / (float)segments;
                int y1 = panelRect.Y + (int)(t * panelRect.Height);
                int y2 = panelRect.Y + (int)(t2 * panelRect.Height);
                Rectangle r = new(panelRect.X, y1, panelRect.Width, Math.Max(1, y2 - y1));

                Color darkBase = new(6, 12, 24);
                Color blueMid = new(12, 28, 48);
                Color lightEdge = new(18, 40, 65);

                float pulse = (float)Math.Sin(pulseTimer * 0.8f + t * 2.5f) * 0.5f + 0.5f;
                Color baseColor = Color.Lerp(darkBase, blueMid, pulse * 0.5f);
                Color finalColor = Color.Lerp(baseColor, lightEdge, t * 0.25f);
                finalColor *= alpha * 0.94f;

                sb.Draw(px, r, new Rectangle(0, 0, 1, 1), finalColor);
            }

            DrawTechGrid(sb, panelRect, alpha * 0.5f);
            DrawScanLines(sb, panelRect, alpha * 0.6f);
            DrawTechFrame(sb, panelRect, alpha);
        }

        private void DrawTechGrid(SpriteBatch sb, Rectangle rect, float alpha) {
            Texture2D px = VaultAsset.placeholder2.Value;
            int gridRows = 12;
            float rowHeight = rect.Height / (float)gridRows;

            for (int row = 0; row < gridRows; row++) {
                float t = row / (float)gridRows;
                float y = rect.Y + row * rowHeight;
                float phase = dataFlowTimer + t * MathHelper.Pi * 0.6f;
                float brightness = (float)Math.Sin(phase) * 0.5f + 0.5f;

                Color gridColor = new Color(40, 100, 180) * (alpha * 0.03f * brightness);
                sb.Draw(px, new Rectangle(rect.X + 12, (int)y, rect.Width - 24, 1), new Rectangle(0, 0, 1, 1), gridColor);
            }
        }

        private void DrawScanLines(SpriteBatch sb, Rectangle rect, float alpha) {
            Texture2D px = VaultAsset.placeholder2.Value;
            float scanY = rect.Y + (float)Math.Sin(scanLineTimer) * 0.5f * rect.Height + rect.Height * 0.5f;

            for (int i = -1; i <= 1; i++) {
                float offsetY = scanY + i * 2f;
                if (offsetY < rect.Y || offsetY > rect.Bottom) continue;

                float intensity = 1f - Math.Abs(i) * 0.35f;
                Color scanColor = new Color(80, 160, 255) * (alpha * 0.08f * intensity);
                sb.Draw(px, new Rectangle(rect.X + 10, (int)offsetY, rect.Width - 20, 1), new Rectangle(0, 0, 1, 1), scanColor);
            }
        }

        private void DrawTechFrame(SpriteBatch sb, Rectangle rect, float alpha) {
            Texture2D px = VaultAsset.placeholder2.Value;
            float pulse = (float)Math.Sin(pulseTimer * 1.2f) * 0.3f + 0.7f;

            Color blueEdge = Color.Lerp(new Color(50, 120, 200), new Color(100, 180, 255), pulse) * (alpha * 0.85f);
            sb.Draw(px, new Rectangle(rect.X, rect.Y, rect.Width, 2), new Rectangle(0, 0, 1, 1), blueEdge);
            sb.Draw(px, new Rectangle(rect.X, rect.Bottom - 2, rect.Width, 2), new Rectangle(0, 0, 1, 1), blueEdge * 0.6f);
            sb.Draw(px, new Rectangle(rect.X, rect.Y, 2, rect.Height), new Rectangle(0, 0, 1, 1), blueEdge * 0.75f);
            sb.Draw(px, new Rectangle(rect.Right - 2, rect.Y, 2, rect.Height), new Rectangle(0, 0, 1, 1), blueEdge * 0.75f);

            Rectangle inner = rect;
            inner.Inflate(-6, -6);
            Color innerGlow = new Color(60, 140, 220) * (alpha * 0.12f * pulse);
            sb.Draw(px, new Rectangle(inner.X, inner.Y, inner.Width, 1), new Rectangle(0, 0, 1, 1), innerGlow);
            sb.Draw(px, new Rectangle(inner.X, inner.Bottom - 1, inner.Width, 1), new Rectangle(0, 0, 1, 1), innerGlow * 0.5f);
            sb.Draw(px, new Rectangle(inner.X, inner.Y, 1, inner.Height), new Rectangle(0, 0, 1, 1), innerGlow * 0.65f);
            sb.Draw(px, new Rectangle(inner.Right - 1, inner.Y, 1, inner.Height), new Rectangle(0, 0, 1, 1), innerGlow * 0.65f);

            DrawCornerMark(sb, new Vector2(rect.X + 12, rect.Y + 12), alpha * 0.9f);
            DrawCornerMark(sb, new Vector2(rect.Right - 12, rect.Y + 12), alpha * 0.9f);
            DrawCornerMark(sb, new Vector2(rect.X + 12, rect.Bottom - 12), alpha * 0.55f);
            DrawCornerMark(sb, new Vector2(rect.Right - 12, rect.Bottom - 12), alpha * 0.55f);
        }

        private static void DrawCornerMark(SpriteBatch sb, Vector2 pos, float alpha) {
            Texture2D px = VaultAsset.placeholder2.Value;
            float size = 5f;
            Color markColor = new Color(100, 180, 255) * alpha;

            sb.Draw(px, pos, new Rectangle(0, 0, 1, 1), markColor, 0f, new Vector2(0.5f), new Vector2(size, size * 0.15f), SpriteEffects.None, 0f);
            sb.Draw(px, pos, new Rectangle(0, 0, 1, 1), markColor * 0.7f, MathHelper.PiOver2, new Vector2(0.5f), new Vector2(size, size * 0.15f), SpriteEffects.None, 0f);
            sb.Draw(px, pos, new Rectangle(0, 0, 1, 1), markColor * 0.35f, 0f, new Vector2(0.5f), new Vector2(size * 0.3f, size * 0.3f), SpriteEffects.None, 0f);
        }

        private void DrawTitle(SpriteBatch sb) {
            float alpha = uiFadeAlpha;
            Texture2D px = VaultAsset.placeholder2.Value;

            sb.Draw(px, titleRect, new Rectangle(0, 0, 1, 1), new Color(15, 35, 60) * (alpha * 0.7f));
            sb.Draw(px, new Rectangle(titleRect.X + 10, titleRect.Bottom - 2, titleRect.Width - 20, 2),
                new Rectangle(0, 0, 1, 1), new Color(60, 140, 220) * (alpha * 0.6f));

            string title = TitleText?.Value ?? "Developer Debug Panel";
            Vector2 titlePos = new(titleRect.Center.X, titleRect.Center.Y);
            Vector2 titleSize = FontAssets.MouseText.Value.MeasureString(title) * 0.8f;

            Color glowColor = new Color(100, 180, 255) * (alpha * 0.4f);
            for (int i = 0; i < 4; i++) {
                float angle = MathHelper.TwoPi * i / 4f;
                Vector2 offset = angle.ToRotationVector2() * 1.5f;
                Utils.DrawBorderString(sb, title, titlePos - titleSize / 2 + offset, glowColor, 0.8f);
            }
            Utils.DrawBorderString(sb, title, titlePos - titleSize / 2, new Color(200, 230, 255) * alpha, 0.8f);

            DrawTechIcon(sb, new Vector2(titleRect.X + 25, titleRect.Center.Y), alpha);
        }

        private static void DrawTechIcon(SpriteBatch sb, Vector2 pos, float alpha) {
            Texture2D px = VaultAsset.placeholder2.Value;
            Color iconColor = new Color(100, 180, 255) * alpha;

            for (int i = 0; i < 6; i++) {
                float angle = MathHelper.TwoPi * i / 6f;
                Vector2 offset = angle.ToRotationVector2() * 7f;
                sb.Draw(px, pos + offset, new Rectangle(0, 0, 1, 1), iconColor, angle, new Vector2(0.5f), new Vector2(5f, 1.5f), SpriteEffects.None, 0f);
            }
            sb.Draw(px, pos, new Rectangle(0, 0, 1, 1), iconColor * 0.7f, 0f, new Vector2(0.5f), new Vector2(4f, 4f), SpriteEffects.None, 0f);
        }

        private void DrawCloseButton(SpriteBatch sb) {
            float alpha = uiFadeAlpha;
            Texture2D px = VaultAsset.placeholder2.Value;

            Color bgColor = hoveringCloseButton ? new Color(80, 40, 40) : new Color(40, 25, 30);
            Color borderColor = hoveringCloseButton ? new Color(255, 100, 100) : new Color(150, 80, 80);

            sb.Draw(px, closeButtonRect, new Rectangle(0, 0, 1, 1), bgColor * (alpha * 0.9f));

            sb.Draw(px, new Rectangle(closeButtonRect.X, closeButtonRect.Y, closeButtonRect.Width, 1), new Rectangle(0, 0, 1, 1), borderColor * (alpha * 0.8f));
            sb.Draw(px, new Rectangle(closeButtonRect.X, closeButtonRect.Bottom - 1, closeButtonRect.Width, 1), new Rectangle(0, 0, 1, 1), borderColor * (alpha * 0.5f));
            sb.Draw(px, new Rectangle(closeButtonRect.X, closeButtonRect.Y, 1, closeButtonRect.Height), new Rectangle(0, 0, 1, 1), borderColor * (alpha * 0.65f));
            sb.Draw(px, new Rectangle(closeButtonRect.Right - 1, closeButtonRect.Y, 1, closeButtonRect.Height), new Rectangle(0, 0, 1, 1), borderColor * (alpha * 0.65f));

            Color xColor = hoveringCloseButton ? new Color(255, 150, 150) : new Color(200, 120, 120);
            Vector2 center = closeButtonRect.Center.ToVector2();
            float size = 6f;
            sb.Draw(px, center, new Rectangle(0, 0, 1, 1), xColor * alpha, MathHelper.PiOver4, new Vector2(0.5f), new Vector2(size * 2f, 2f), SpriteEffects.None, 0f);
            sb.Draw(px, center, new Rectangle(0, 0, 1, 1), xColor * alpha, -MathHelper.PiOver4, new Vector2(0.5f), new Vector2(size * 2f, 2f), SpriteEffects.None, 0f);
        }

        private void DrawTabBar(SpriteBatch sb) {
            float alpha = uiFadeAlpha;
            Texture2D px = VaultAsset.placeholder2.Value;

            sb.Draw(px, tabBarRect, new Rectangle(0, 0, 1, 1), new Color(10, 22, 38) * (alpha * 0.8f));

            for (int i = 0; i < tabs.Count && i < tabRects.Count; i++) {
                var tab = tabs[i];
                var rect = tabRects[i];
                bool isActive = i == currentTabIndex;
                bool isHovering = i == hoveringTab;

                Color tabBg;
                if (isActive) {
                    tabBg = new Color(30, 70, 120);
                }
                else if (isHovering) {
                    tabBg = new Color(25, 55, 90);
                }
                else {
                    tabBg = new Color(15, 35, 60);
                }

                sb.Draw(px, rect, new Rectangle(0, 0, 1, 1), tabBg * (alpha * 0.9f));

                Color borderColor = isActive ? new Color(100, 180, 255) : (isHovering ? new Color(70, 140, 210) : new Color(50, 100, 160));
                sb.Draw(px, new Rectangle(rect.X, rect.Y, rect.Width, 1), new Rectangle(0, 0, 1, 1), borderColor * (alpha * 0.7f));
                sb.Draw(px, new Rectangle(rect.X, rect.Y, 1, rect.Height), new Rectangle(0, 0, 1, 1), borderColor * (alpha * 0.5f));
                sb.Draw(px, new Rectangle(rect.Right - 1, rect.Y, 1, rect.Height), new Rectangle(0, 0, 1, 1), borderColor * (alpha * 0.5f));

                if (isActive) {
                    sb.Draw(px, new Rectangle(rect.X, rect.Bottom - 2, rect.Width, 2), new Rectangle(0, 0, 1, 1), new Color(100, 200, 255) * alpha);
                }

                Vector2 iconPos = new(rect.X + 20, rect.Center.Y);
                Color iconBg = isActive ? new Color(60, 140, 220) : new Color(40, 100, 160);
                sb.Draw(px, iconPos, new Rectangle(0, 0, 1, 1), iconBg * (alpha * 0.8f), 0f, new Vector2(0.5f), new Vector2(18, 18), SpriteEffects.None, 0f);

                Color iconTextColor = isActive ? new Color(200, 240, 255) : new Color(140, 180, 220);
                Utils.DrawBorderString(sb, tab.TabIcon, iconPos - new Vector2(7, 8), iconTextColor * alpha, 0.55f);

                Color textColor = isActive ? new Color(200, 230, 255) : (isHovering ? new Color(170, 210, 250) : new Color(130, 170, 210));
                Utils.DrawBorderString(sb, tab.TabName, new Vector2(rect.X + 40, rect.Y + 6), textColor * alpha, 0.65f);
            }

            sb.Draw(px, new Rectangle(tabBarRect.X + 10, tabBarRect.Bottom - 1, tabBarRect.Width - 20, 1),
                new Rectangle(0, 0, 1, 1), new Color(60, 140, 220) * (alpha * 0.4f));
        }

        private void DrawCheckboxes(SpriteBatch sb) {
            if (currentTabIndex < 0 || currentTabIndex >= tabs.Count) return;

            float alpha = uiFadeAlpha;
            Texture2D px = VaultAsset.placeholder2.Value;
            var currentTab = tabs[currentTabIndex];

            int startY = contentRect.Y + 5;
            for (int i = 0; i < currentTab.Checkboxes.Count; i++) {
                var checkbox = currentTab.Checkboxes[i];
                int y = startY + i * 38;
                bool hovering = hoveringCheckbox == i;
                bool isChecked = checkbox.GetValue();

                Rectangle bgRect = new(contentRect.X, y, contentRect.Width, 34);
                Color bgColor = hovering ? new Color(25, 50, 80) : new Color(15, 30, 50);
                sb.Draw(px, bgRect, new Rectangle(0, 0, 1, 1), bgColor * (alpha * 0.7f));

                Color borderColor = hovering ? new Color(80, 160, 240) : new Color(50, 100, 160);
                sb.Draw(px, new Rectangle(bgRect.X, bgRect.Y, bgRect.Width, 1), new Rectangle(0, 0, 1, 1), borderColor * (alpha * 0.5f));
                sb.Draw(px, new Rectangle(bgRect.X, bgRect.Bottom - 1, bgRect.Width, 1), new Rectangle(0, 0, 1, 1), borderColor * (alpha * 0.3f));

                Rectangle checkRect = new(bgRect.X + 10, y + 7, 20, 20);
                Color checkBg = isChecked ? new Color(30, 80, 130) : new Color(20, 40, 60);
                Color checkBorder = isChecked ? new Color(100, 180, 255) : new Color(60, 120, 180);

                sb.Draw(px, checkRect, new Rectangle(0, 0, 1, 1), checkBg * (alpha * 0.9f));
                sb.Draw(px, new Rectangle(checkRect.X, checkRect.Y, checkRect.Width, 1), new Rectangle(0, 0, 1, 1), checkBorder * alpha);
                sb.Draw(px, new Rectangle(checkRect.X, checkRect.Bottom - 1, checkRect.Width, 1), new Rectangle(0, 0, 1, 1), checkBorder * (alpha * 0.6f));
                sb.Draw(px, new Rectangle(checkRect.X, checkRect.Y, 1, checkRect.Height), new Rectangle(0, 0, 1, 1), checkBorder * (alpha * 0.8f));
                sb.Draw(px, new Rectangle(checkRect.Right - 1, checkRect.Y, 1, checkRect.Height), new Rectangle(0, 0, 1, 1), checkBorder * (alpha * 0.8f));

                if (isChecked) {
                    Vector2 checkCenter = checkRect.Center.ToVector2();
                    Color checkMark = new Color(100, 200, 255) * alpha;
                    sb.Draw(px, checkCenter + new Vector2(-3, 2), new Rectangle(0, 0, 1, 1), checkMark, MathHelper.PiOver4, new Vector2(0.5f), new Vector2(6f, 2f), SpriteEffects.None, 0f);
                    sb.Draw(px, checkCenter + new Vector2(2, -1), new Rectangle(0, 0, 1, 1), checkMark, -MathHelper.PiOver4, new Vector2(0.5f), new Vector2(10f, 2f), SpriteEffects.None, 0f);
                }

                Color textColor = hovering ? new Color(200, 230, 255) : new Color(160, 200, 240);
                Utils.DrawBorderString(sb, checkbox.Label, new Vector2(bgRect.X + 40, y + 7), textColor * alpha, 0.7f);
            }
        }

        private void DrawBottomButtons(SpriteBatch sb) {
            float alpha = uiFadeAlpha;
            Texture2D px = VaultAsset.placeholder2.Value;

            DrawButton(sb, resetButtonRect, ResetText?.Value ?? "Reset", hoveringResetButton, alpha,
                new Color(25, 45, 75), new Color(40, 60, 100), new Color(60, 120, 180), new Color(100, 160, 220));

            DrawButton(sb, resetAllButtonRect, ResetAllText?.Value ?? "Reset All", hoveringResetAllButton, alpha,
                new Color(50, 35, 30), new Color(70, 45, 40), new Color(180, 100, 80), new Color(220, 140, 100));
        }

        private static void DrawButton(SpriteBatch sb, Rectangle rect, string text, bool hovering, float alpha,
            Color bgNormal, Color bgHover, Color borderNormal, Color borderHover) {
            Texture2D px = VaultAsset.placeholder2.Value;

            Color bgColor = hovering ? bgHover : bgNormal;
            Color borderColor = hovering ? borderHover : borderNormal;

            sb.Draw(px, rect, new Rectangle(0, 0, 1, 1), bgColor * (alpha * 0.9f));

            sb.Draw(px, new Rectangle(rect.X, rect.Y, rect.Width, 1), new Rectangle(0, 0, 1, 1), borderColor * (alpha * 0.7f));
            sb.Draw(px, new Rectangle(rect.X, rect.Bottom - 1, rect.Width, 1), new Rectangle(0, 0, 1, 1), borderColor * (alpha * 0.4f));
            sb.Draw(px, new Rectangle(rect.X, rect.Y, 1, rect.Height), new Rectangle(0, 0, 1, 1), borderColor * (alpha * 0.55f));
            sb.Draw(px, new Rectangle(rect.Right - 1, rect.Y, 1, rect.Height), new Rectangle(0, 0, 1, 1), borderColor * (alpha * 0.55f));

            Color textColor = hovering ? new Color(220, 240, 255) : new Color(160, 200, 240);
            Vector2 textSize = FontAssets.MouseText.Value.MeasureString(text) * 0.65f;
            Vector2 textPos = rect.Center.ToVector2() - textSize / 2;
            Utils.DrawBorderString(sb, text, textPos, textColor * alpha, 0.65f);
        }
    }
}
