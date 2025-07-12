using InnoVault.GameSystem;
using InnoVault.TileProcessors;
using InnoVault.UIHandles;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using Terraria;
using Terraria.GameContent;
using Terraria.Localization;
using Terraria.ModLoader;

namespace InnoVault.GameContent
{
    internal class WorldLoadingText : ModType, ILocalizedModType
    {
        public string LocalizationCategory => "Loadings";
        public static WorldLoadingText Instance { get; private set; }
        public static LocalizedText Text1 { get; private set; }
        public static LocalizedText Text2 { get; private set; }
        public static LocalizedText Text3 { get; private set; }
        protected override void Register() => Instance = this;
        public override void SetupContent() => SetStaticDefaults();
        public override void SetStaticDefaults() {
            Text1 = this.GetLocalization(nameof(Text1), () => "Loading The World");
            Text2 = this.GetLocalization(nameof(Text2), () => "Loading Save Data");
            Text3 = this.GetLocalization(nameof(Text3), () => "Loading Tile Processor");
        }
    }

    internal class WorldLoadingUI : UIHandle
    {
        private static bool DoActive {
            get {
                if (VaultUtils.isClient) {
                    return !TileProcessorNetWork.LoadenTPByNetWork;
                }
                return !TileProcessorLoader.LoadenTP;
            }
        }
        public override bool Active => DoActive || sengs > 0f;
        private int time;
        private float sengs;
        private float rotation;
        private int dotCounter;
        public override void Draw(SpriteBatch spriteBatch) {
            // 更新透明度
            float opacity = DoActive ? 1f : Math.Max(sengs - 0.1f, 0f); // 防止透明度为负

            // 更新旋转角度
            time++;
            rotation += 0.03f + 0.03f * (float)Math.Sin(time / 3f); // 平滑变速旋转

            // 绘制背景
            Rectangle rectangle = new Rectangle(0, 0, Main.screenWidth, Main.screenHeight);
            spriteBatch.Draw(VaultAsset.placeholder2.Value, rectangle, Color.Black * 0.8f * opacity);

            // 绘制齿轮
            DrawGear(spriteBatch, opacity);

            // 绘制动态文本
            DrawDynamicText(spriteBatch, opacity);
        }

        private void DrawGear(SpriteBatch spriteBatch, float opacity) {
            Texture2D gear = VaultAsset.GearWheel.Value;
            Vector2 drawPos = new Vector2(Main.screenWidth / 2f, Main.screenHeight * 0.7f);
            Vector2 origin = gear.Size() / 2f;

            spriteBatch.Draw(gear, drawPos, null, Color.White * 0.9f * opacity
                , rotation, origin, 2f * opacity, SpriteEffects.None, 0f);
        }

        private void DrawDynamicText(SpriteBatch spriteBatch, float opacity) {
            // 动态省略号
            dotCounter++;
            string dots = new string('.', (dotCounter / 20) % 4); // 0~3 个点
            // 文本内容
            string text1 = WorldLoadingText.Text1.Value + dots;
            string text2 = VaultSave.LoadenWorld ? WorldLoadingText.Text3.Value + dots : WorldLoadingText.Text2.Value + dots;
            // 绘制第一行文本
            Vector2 drawPos = new Vector2(0f, VaultAsset.GearWheel.Value.Height * 1.5f);
            DrawText(spriteBatch, text1, opacity, drawPos);
            // 绘制第二行文本
            drawPos = new Vector2(0f, drawPos.Y + FontAssets.MouseText.Value.MeasureString(text2).Y);
            DrawText(spriteBatch, text2, opacity, drawPos);
        }

        private void DrawText(SpriteBatch spriteBatch, string text, float opacity, Vector2 offset) {
            Vector2 textSize = FontAssets.MouseText.Value.MeasureString(text);
            Vector2 textOrigin = textSize / 2f;
            Vector2 drawPos = new Vector2(Main.screenWidth / 2f, Main.screenHeight * 0.7f) + offset;

            Utils.DrawBorderStringFourWay(spriteBatch, FontAssets.MouseText.Value, text
                , drawPos.X - textOrigin.X, drawPos.Y, Color.White * opacity, Color.Black * opacity, Vector2.Zero, 1f);
        }
    }
}
