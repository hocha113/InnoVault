using InnoVault.GameSystem;
using InnoVault.UIHandles;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using Terraria;
using Terraria.GameContent;
using Terraria.ModLoader;
using Terraria.WorldBuilding;

namespace InnoVault.Scenarios
{
    //换舞台期间在菜单绘制层（IL_Main.DrawMenu 里的 Mod_MenuLoad）上画场景的加载画面，盖住原版加载文字与生成进度条
    internal sealed class ScenarioLoadingUI : UIHandle
    {
        private static readonly DefaultScenarioLoadingScreen _default = new();

        private ScenarioTransition _shown;
        private int _frames;

        //进入时用目标场景的；目标是主世界时用离开的那个场景的；都没有且幕布开启时用缺省加载画面
        internal static ScenarioLoadingScreen Current {
            get {
                if (!ScenarioDirector.IsTraveling) {
                    return null;
                }
                ScenarioTransition travel = ScenarioDirector.CurrentTravel;
                ScenarioLoadingScreen screen = travel?.To?.GetComponent<ScenarioLoadingScreen>() ?? travel?.From?.GetComponent<ScenarioLoadingScreen>();
                if (screen != null) {
                    return screen;
                }
                ScenarioCurtain curtain = ScenarioDirector.ActiveCurtain;
                return curtain == null || curtain.Enabled ? _default : null;
            }
        }

        //为真时 InnoVault 自己的存盘加载画面让位
        internal static bool Covering => Main.gameMenu && Current != null;

        public override LayersModeEnum LayersMode => LayersModeEnum.Mod_MenuLoad;

        public override float RenderPriority => 100f;

        public override bool Active {
            get => Covering;
            set { }
        }

        public override void Draw(SpriteBatch spriteBatch) {
            ScenarioLoadingScreen screen = Current;
            ScenarioTransition travel = ScenarioDirector.CurrentTravel;
            if (screen == null || travel == null) {
                return;
            }
            if (!ReferenceEquals(travel, _shown)) {
                _shown = travel;
                _frames = 0;
            }
            _frames++;
            GenerationProgress progress = WorldGenerator.CurrentGenerationProgress;
            ScenarioLoadingContext context = new(travel, ScenarioDirector.TravelPhase, Main.statusText,
                progress?.TotalProgress, progress?.Value, progress?.Message, _frames);
            try {
                screen.Draw(spriteBatch, context);
            } catch (Exception ex) {
                ScenarioPlatform.Error($"Scenario.LoadingScreen.{screen.Scenario?.Key}", $"[Scenarios] {screen.GetType().Name}.Draw threw: {ex}");
            }
        }
    }

    //缺省加载画面：幕布颜色铺满，中间一行加载文字，生成阶段多画一条进度条。与幕布衔接：合上时是这个颜色，拉开时也从这个颜色开始
    internal sealed class DefaultScenarioLoadingScreen : ScenarioLoadingScreen
    {
        public override void Draw(SpriteBatch spriteBatch, in ScenarioLoadingContext context) {
            Texture2D pixel = TextureAssets.MagicPixel.Value;
            Color fill = ScenarioDirector.ActiveCurtain?.Color ?? Color.Black;
            spriteBatch.Draw(pixel, new Rectangle(0, 0, Main.screenWidth, Main.screenHeight), fill);

            string text = context.PassMessage ?? context.Status;
            if (!string.IsNullOrEmpty(text)) {
                Vector2 size = FontAssets.DeathText.Value.MeasureString(text) * 0.5f;
                Vector2 center = new(Main.screenWidth / 2f, Main.screenHeight / 2f);
                Utils.DrawBorderStringFourWay(spriteBatch, FontAssets.DeathText.Value, text,
                    center.X - size.X / 2f, center.Y - size.Y, Color.White, Color.Black, Vector2.Zero, 0.5f);
            }
            if (context.OverallProgress is double overall) {
                Rectangle bar = new(Main.screenWidth / 2 - 200, Main.screenHeight / 2 + 16, 400, 6);
                spriteBatch.Draw(pixel, bar, Color.DimGray);
                spriteBatch.Draw(pixel, new Rectangle(bar.X, bar.Y, (int)(bar.Width * MathHelper.Clamp((float)overall, 0f, 1f)), bar.Height), Color.White);
            }
        }
    }

    //游戏内的幕布：换舞台前合上、抵达后拉开，画在界面层最上面（PostDrawInterface 在鼠标文字层里，SpriteBatch 已按 UI 缩放开启）
    internal sealed class ScenarioCurtainSystem : ModSystem
    {
        private static readonly ScenarioCurtain _default = new();

        public override void PostDrawInterface(SpriteBatch spriteBatch) {
            if (Main.gameMenu) {
                return;
            }
            float coverage = ScenarioDirector.CurtainCoverage;
            if (coverage <= 0f) {
                return;
            }
            ScenarioCurtain curtain = ScenarioDirector.ActiveCurtain ?? _default;
            try {
                curtain.Draw(spriteBatch, coverage);
            } catch (Exception ex) {
                ScenarioPlatform.Error($"Scenario.Curtain.{curtain.Scenario?.Key}", $"[Scenarios] {curtain.GetType().Name}.Draw threw: {ex}");
            }
        }
    }

    //加载期间的音乐。菜单期间原版也走 UpdateAudio_DecideOnNewMusic，InnoVault 在其后派发 DecideMusic
    internal sealed class ScenarioLoadingAudio : IUpdateAudio
    {
        public void DecideMusic() {
            if (!Main.gameMenu) {
                return;
            }
            ScenarioLoadingScreen screen = ScenarioLoadingUI.Current;
            if (screen != null && screen.Music >= 0) {
                Main.newMusic = screen.Music;
            }
        }
    }
}
