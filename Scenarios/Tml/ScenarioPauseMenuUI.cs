using InnoVault.Scenarios.Presets;
using InnoVault.UIHandles;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.ID;

namespace InnoVault.Scenarios
{
    //暂停菜单旁的返回按钮：当前舞台场景挂了 PauseMenuReturn 时，设置窗口打开期间画在窗口下方；去哪里、怎么发起由那个组件决定。
    //原版设置窗口左列的条目数与缩放数组是写死的，往里插一项要改好几处常量；这里不动窗口本身，按钮独立成一块，挨着窗口底边
    //（窗口尺寸对照 Terraria/IngameOptions.cs Draw：内容区 670×480、居中、四周各留 20 像素）
    internal sealed class ScenarioPauseMenuUI : UIHandle
    {
        private const float PanelHalfHeight = 480f / 2f + 20f;
        private const float Gap = 10f;
        private const float PaddingX = 28f;
        private const float ButtonHeight = 40f;

        private string _text = string.Empty;

        public override LayersModeEnum LayersMode => LayersModeEnum.Vanilla_Ingame_Options;

        public override bool AutoUpdateHitBox => true;

        public override bool BlockMouseWhenHovered => true;

        public override bool Active {
            get => !Main.gameMenu && Main.ingameOptionsWindow && Current != null;
            set { }
        }

        //当前舞台场景上启用着的那个预设组件；身处主世界、会话之外或场景没挂时为空
        private static PauseMenuReturn Current
            => ScenarioDirector.Stage is VaultScenario stage and not RootScenario ? stage.FindEnabled<PauseMenuReturn>() : null;

        public override void Update() {
            PauseMenuReturn preset = Current;
            if (preset == null) {
                return;
            }
            _text = preset.Text?.Value ?? (preset.Destination == null ? ScenarioText.ReturnHome : ScenarioText.Return).Value;
            Vector2 textSize = FontAssets.MouseText.Value.MeasureString(_text);
            Size = new Vector2(textSize.X + PaddingX * 2f, ButtonHeight);
            float top = Main.screenHeight / 2f + PanelHalfHeight + Gap;
            //窗口比屏幕还高时贴着屏幕底边，不画到屏幕外面去
            top = Math.Min(top, Main.screenHeight - ButtonHeight - 4f);
            DrawPosition = new Vector2(Main.screenWidth / 2f - Size.X / 2f, top);
            if (!hoverInMainPage || keyLeftPressState != KeyPressState.Pressed) {
                return;
            }
            //此刻发起不了（正在换舞台、联机时还有人在路上）或者请求没被受理：只给一声提示音，窗口留着
            bool accepted = false;
            try {
                accepted = preset.Available && preset.Request();
            } catch (Exception ex) {
                ScenarioPlatform.Error($"Scenario.PauseMenuReturn.{preset.Scenario?.Key}", $"[Scenarios] {preset.GetType().Name}.Request threw: {ex}");
            }
            if (accepted) {
                //原版关掉设置窗口会回到背包界面（并播关闭音效）。单人下设置窗口开着时游戏是暂停的，开着自动暂停时背包界面也一样：
                //背包一并收起，游戏恢复运行，合幕与换舞台才推进得下去
                IngameOptions.Close();
                Main.playerInventory = false;
            }
            else {
                SoundEngine.PlaySound(SoundID.MenuTick);
            }
        }

        public override void Draw(SpriteBatch spriteBatch) {
            if (Size == Vector2.Zero) {
                return;
            }
            float hover = HoverProgress.Current;
            //与设置窗口同一种底板与底色，悬停时提亮
            Color panel = Color.Lerp(new Color(33, 15, 91, 255), new Color(63, 65, 151, 255), hover) * 0.785f;
            Utils.DrawInvBG(spriteBatch, DrawPosition.X, DrawPosition.Y, Size.X, Size.Y, panel);
            Vector2 textSize = FontAssets.MouseText.Value.MeasureString(_text);
            Vector2 center = DrawPosition + Size / 2f;
            Color color = Color.Lerp(new Color(200, 200, 200), Color.White, hover);
            Utils.DrawBorderString(spriteBatch, _text, new Vector2(center.X - textSize.X / 2f, center.Y - textSize.Y / 2f + 4f), color);
        }
    }
}
