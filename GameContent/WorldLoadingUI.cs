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
        private static bool DoActive => !TileProcessorLoader.LoadenTP;
        public override bool Active => DoActive || sengs > 0f;
        private int time;
        private float sengs;
        private float rotation;
        private int dotCounter;
        public override void Draw(SpriteBatch spriteBatch) {
            if (DoActive) {
                sengs = 1f;
            }
            else {
                sengs -= 0.1f;
            }

            time++;
            rotation += 0.03f + 0.02f * (float)Math.Sin(time / 3f); // 平滑变速旋转

            Vector2 drawPos = new Vector2(Main.screenWidth / 2f, Main.screenHeight * 0.7f);
            Texture2D gear = VaultAsset.GearWheel.Value;
            Vector2 origin = gear.Size() / 2f;

            spriteBatch.Draw(VaultAsset.placeholder2.Value, new Rectangle(0, 0, Main.screenWidth, Main.screenHeight), Color.Black * 0.8f * sengs);
            // 绘制齿轮图案
            spriteBatch.Draw(gear, drawPos, null, Color.White * 0.9f, rotation, origin, 2f * sengs, SpriteEffects.None, 0f);

            // 动态省略号处理
            string dot = new('.', (dotCounter / 20) % 4); // 0~3 个点
            dotCounter++;

            string idleStr = WorldLoadingText.Text1.Value + dot;
            Vector2 textSize = FontAssets.MouseText.Value.MeasureString(idleStr);
            Vector2 textOrigin = textSize / 2f;
            Vector2 textOffset = new Vector2(0f, gear.Height * 1.5f);

            string idleStr2 = WorldLoadingText.Text2.Value + dot;
            if (VaultSave.LoadenWorld) {
                idleStr2 = WorldLoadingText.Text3.Value + dot;
            }
            Vector2 textSize2 = FontAssets.MouseText.Value.MeasureString(idleStr2);
            Vector2 textOrigin2 = textSize2 / 2f;
            Vector2 textOffset2 = new Vector2(0f, textOffset.Y + gear.Height * 1.5f);

            // 绘制带描边的动态文本
            Utils.DrawBorderStringFourWay(spriteBatch, FontAssets.MouseText.Value, idleStr,
                drawPos.X - textOrigin.X, drawPos.Y + textOffset.Y,
                Color.White * sengs, Color.Black * sengs, Vector2.Zero, 1f);

            Utils.DrawBorderStringFourWay(spriteBatch, FontAssets.MouseText.Value, idleStr2,
                drawPos.X - textOrigin2.X, drawPos.Y + textOffset2.Y,
                Color.White * sengs, Color.Black * sengs, Vector2.Zero, 1f);
        }
    }
}
