﻿using InnoVault.GameSystem;
using InnoVault.TileProcessors;
using InnoVault.UIHandles;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using System;
using System.Globalization;
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
        public static LocalizedText Text4 { get; private set; }
        public static LocalizedText Text5 { get; private set; }
        public static LocalizedText NetWaringTimeoutMsg { get; private set; }
        protected override void Register() => Instance = this;
        public override void SetupContent() => SetStaticDefaults();
        public override void SetStaticDefaults() {
            Text1 = this.GetLocalization(nameof(Text1), () => "Loading The World");
            Text2 = this.GetLocalization(nameof(Text2), () => "Loading Save Data");
            Text3 = this.GetLocalization(nameof(Text3), () => "Loading Tile Processor");
            Text4 = this.GetLocalization(nameof(Text4), () => "Receive Network Data");
            Text5 = this.GetLocalization(nameof(Text5), () => "Save World");
            NetWaringTimeoutMsg = this.GetLocalization(nameof(NetWaringTimeoutMsg), () => "");
        }
    }

    /// <summary>
    /// 一个通用的加载UI基类，用于快速实现缓冲界面的绘制
    /// </summary>
    public abstract class LoadingUI : UIHandle
    {
        /// <inheritdoc/>
        public override LayersModeEnum LayersMode => LayersModeEnum.None;
        /// <summary>
        /// 该UI是否应该活跃
        /// </summary>
        public virtual bool DoActive => false;
        /// <inheritdoc/>
        public override bool Active => DoActive || sengs > 0f;
        /// <summary>
        /// 计时器
        /// </summary>
        protected int time;
        /// <summary>
        /// 渐进值
        /// </summary>
        protected float sengs;
        /// <summary>
        /// 旋转角度
        /// </summary>
        protected float rotation;
        /// <summary>
        /// 文字计数器
        /// </summary>
        protected int dotCounter;
        /// <inheritdoc/>
        public override void Draw(SpriteBatch spriteBatch) {
            UpdateSengs();

            // 更新旋转角度
            time++;
            rotation += 0.03f + 0.03f * (float)Math.Sin(time / 3f); // 平滑变速旋转

            DrawBack(spriteBatch);

            DrawGear(spriteBatch, sengs);

            DrawDynamicText(spriteBatch, sengs);
        }

        /// <summary>
        /// 更新渐变值
        /// </summary>
        protected virtual void UpdateSengs() => sengs = DoActive ? 1f : Math.Max(sengs - 0.1f, 0f); // 防止透明度为负

        /// <summary>
        /// 绘制背景遮罩
        /// </summary>
        /// <param name="spriteBatch"></param>
        protected virtual void DrawBack(SpriteBatch spriteBatch) {
            // 绘制背景
            Rectangle rectangle = new Rectangle(0, 0, Main.screenWidth, Main.screenHeight);
            spriteBatch.Draw(VaultAsset.placeholder2.Value, rectangle, Color.Black * 0.4f * sengs);
        }

        /// <summary>
        /// 绘制齿轮动画
        /// </summary>
        /// <param name="spriteBatch"></param>
        /// <param name="opacity"></param>
        protected virtual void DrawGear(SpriteBatch spriteBatch, float opacity) {
            Texture2D gear = VaultAsset.GearWheel.Value;

            Vector2 drawPos = new Vector2(Main.screenWidth / 2f, Main.screenHeight * 0.7f);
            Vector2 origin = gear.Size() / 2f;

            spriteBatch.Draw(gear, drawPos, null, Color.White * opacity
                , rotation, origin, 2f * opacity, SpriteEffects.None, 0f);
        }

        /// <summary>
        /// 重写函数，用于获取描述文本
        /// </summary>
        /// <returns></returns>
        protected virtual (string, string) GetDynamicText() => (string.Empty, string.Empty);

        /// <summary>
        /// 绘制文字
        /// </summary>
        /// <param name="spriteBatch"></param>
        /// <param name="opacity"></param>
        protected virtual void DrawDynamicText(SpriteBatch spriteBatch, float opacity) {
            (string text1, string text2) = GetDynamicText();

            Vector2 drawPos = new Vector2(0f, VaultAsset.GearWheel.Value.Height * 1.5f);

            // 绘制第一行文本
            if (text1 != string.Empty) {
                DrawText(spriteBatch, text1, opacity, drawPos);
            }

            // 绘制第二行文本
            if (text2 != string.Empty) {
                drawPos = new Vector2(0f, drawPos.Y + FontAssets.MouseText.Value.MeasureString(text2).Y);
                DrawText(spriteBatch, text2, opacity, drawPos);
            }
        }

        /// <summary>
        /// 绘制文字
        /// </summary>
        /// <param name="spriteBatch"></param>
        /// <param name="text"></param>
        /// <param name="opacity"></param>
        /// <param name="offset"></param>
        protected static void DrawText(SpriteBatch spriteBatch, string text, float opacity, Vector2 offset) {
            Vector2 textSize = FontAssets.MouseText.Value.MeasureString(text);
            Vector2 textOrigin = textSize / 2f;
            Vector2 drawPos = new Vector2(Main.screenWidth / 2f, Main.screenHeight * 0.7f) + offset;

            Utils.DrawBorderStringFourWay(spriteBatch, FontAssets.MouseText.Value, text
                , drawPos.X - textOrigin.X, drawPos.Y, Color.White * opacity, Color.Black * opacity, Vector2.Zero, 1f);
        }
    }

    internal class WorldSaveUI : WorldLoadingUI
    {
        public override LayersModeEnum LayersMode => LayersModeEnum.Mod_MenuLoad;
        public override bool DoActive => !VaultClientConfig.Instance.HideWorldLoadingScreen && !VaultSave.SavedWorld;
        protected override float Fadeout => 0.04f;
        protected override (string, string) GetDynamicText() {
            // 动态省略号
            dotCounter++;
            string dots = new string('.', (dotCounter / 20) % 4); // 0~3 个点
            return (WorldLoadingText.Text5.Value + dots, string.Empty);
        }
        protected override void UpdatePercentage() => percentage = 100f;
    }

    internal class WorldLoadingUI : LoadingUI
    {
        [VaultLoaden("InnoVault/Effects/")]
        private static Asset<Effect> GearProgress { get; set; }
        public override LayersModeEnum LayersMode => LayersModeEnum.Vanilla_Mouse_Text;
        public override bool DoActive {
            get {
                if (VaultClientConfig.Instance.HideWorldLoadingScreen) {
                    return false;
                }
                if (VaultUtils.isClient) {
                    return !TileProcessorNetWork.LoadenTPByNetWork;
                }
                return !TileProcessorLoader.LoadenTP;
            }
        }
        protected float percentage;
        protected int idleTime;
        protected virtual float Fadeout => 0.1f;
        public override void OnEnterWorld() {
            percentage = 0f;
            idleTime = 20;//防止在极短加载情况下UI一闪而过造成不适，20tick刚好够人眼看清
        }
        protected sealed override void UpdateSengs() {
            if (DoActive) {
                sengs = 1f;
            }
            else if (--idleTime < 0 && sengs > 0f) {
                sengs = Math.Max(sengs - Fadeout, 0f);
            }
            UpdatePercentage();
        }
        protected override (string, string) GetDynamicText() {
            //动态省略号
            dotCounter++;
            string dots = new string('.', (dotCounter / 20) % 4); //0~3 个点
            //文本内容
            string text1 = WorldLoadingText.Text1.Value + dots;
            string text2 = VaultSave.LoadenWorld ? WorldLoadingText.Text3.Value : WorldLoadingText.Text2.Value;
            if (VaultUtils.isClient) {
                text2 = WorldLoadingText.Text4.Value;
            }
            text2 += dots;
            return (text1, text2);
        }
        protected virtual void UpdatePercentage() {
            if (!VaultSave.LoadenWorld && TileProcessorLoader.WorldLoadProgress < 10f) {
                TileProcessorLoader.WorldLoadProgress = MathHelper.Lerp(TileProcessorLoader.WorldLoadProgress, 10f, 0.01f);
            }

            float origTarget = TileProcessorLoader.WorldLoadProgress;

            if (VaultUtils.isClient) {
                origTarget = origTarget * 0.4f + TileProcessorNetWork.NetworkLoadProgress * 0.6f;
            }
            float target = MathHelper.Clamp(origTarget, 0f, 100f);

            if (target > percentage) {
                percentage = MathHelper.Lerp(percentage, target, 0.1f);
            }

            //如果 UI 即将退出，就强行拉到 100%
            if (!DoActive && sengs > 0f) {
                percentage = 100f;
            }
        }
        protected override void DrawGear(SpriteBatch spriteBatch, float opacity) {
            Texture2D gear = VaultAsset.GearWheel.Value;

            Vector2 drawPos = new Vector2(Main.screenWidth / 2f, Main.screenHeight * 0.7f);
            Vector2 origin = gear.Size() / 2f;

            GearProgress.Value.Parameters["Progress"].SetValue(percentage / 100f);
            GearProgress.Value.Parameters["Rotation"].SetValue(-MathHelper.WrapAngle(rotation) - MathHelper.PiOver2);

            Main.spriteBatch.End();
            Main.spriteBatch.Begin(0, BlendState.AlphaBlend, null, null, null, GearProgress.Value, Main.UIScaleMatrix);

            spriteBatch.Draw(gear, drawPos, null, Color.White * opacity,
                rotation, origin, 2f * opacity, SpriteEffects.None, 0f);

            Main.spriteBatch.End();
            Main.spriteBatch.Begin(0, BlendState.AlphaBlend, null, null, null, null, Main.UIScaleMatrix);

            //延迟一段时间再显示感叹号
            if (VaultUtils.isSinglePlayer ||
                TileProcessorNetWork.loadTPNetworkTickCounter < TileProcessorNetWork.MaxBufferWaitingTimeMark * 2 / 3) {
                return;
            }

            Texture2D axclamation = VaultAsset.AxclamationPoint.Value;
            Vector2 basePos = new Vector2(Main.screenWidth / 2f + 28, Main.screenHeight * 0.7f - 28);
            origin = axclamation.Size() / 2f;

            //动画效果
            float bounceOffset = (float)Math.Sin(time / 3f) * 4f;//垂直跳动
            float alphaPulse = 0.5f + 0.5f * (float)Math.Sin(time * 0.5f);//闪烁透明度
            float scalePulse = 1f + 0.05f * (float)Math.Sin(time * 0.4f);//轻微缩放

            drawPos = basePos + new Vector2(0, bounceOffset);
            float finalOpacity = opacity * (0.6f + 0.4f * alphaPulse);//更柔和的透明度闪烁
            float finalScale = opacity * scalePulse * 2;//缩放跟随整体透明度变化

            spriteBatch.Draw(axclamation, drawPos, null, Color.White * finalOpacity
                , 0f, origin, finalScale, SpriteEffects.None, 0f);
        }
        protected override void DrawDynamicText(SpriteBatch spriteBatch, float opacity) {
            base.DrawDynamicText(spriteBatch, opacity);
            string percentageMag = (percentage / 100f).ToString("P1", CultureInfo.InvariantCulture);
            Vector2 textSize = FontAssets.MouseText.Value.MeasureString(percentageMag);
            Vector2 drawPos = new Vector2(0f, VaultAsset.GearWheel.Value.Height / -2f + textSize.Y / 2);
            DrawText(spriteBatch, percentageMag, opacity, drawPos);
        }
    }
}
