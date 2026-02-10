using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Reflection;
using Terraria;
using Terraria.ModLoader;
using static InnoVault.GameSystem.MenuOverride;

namespace InnoVault.GameSystem
{
    /// <summary>
    /// 关于菜单重制节点的钩子均挂载于此处
    /// </summary>
    public class MenuRebuildLoader : IVaultLoader
    {
#pragma warning disable CS1591 // 缺少对公共可见类型或成员的 XML 注释
        public delegate void On_DrawMenu_Dlelgate(Main main, GameTime gameTime);
        public delegate void On_AddMenuButtons_Dlelgate(Main main, int selectedMenu, string[] buttonNames, float[] buttonScales, ref int offY, ref int spacing, ref int buttonIndex, ref int numButtons);
#pragma warning restore CS1591 // 缺少对公共可见类型或成员的 XML 注释
        void IVaultLoader.LoadData() {
            VaultHook.Add(typeof(Main).GetMethod("DrawMenu", BindingFlags.Instance | BindingFlags.NonPublic), OnDrawMenuHook);
            VaultHook.Add(typeof(MenuLoader).GetMethod("UpdateAndDrawModMenu", BindingFlags.Static | BindingFlags.NonPublic), OnUpdateAndDrawModMenuHook);
            VaultHook.Add(typeof(Main).Assembly.GetType("Terraria.ModLoader.UI.Interface").GetMethod("AddMenuButtons", BindingFlags.Static | BindingFlags.NonPublic), OnAddMenuButtonsHook);
        }

        //这个函数内部已经是处理好了画布状态的，所以不需要再去管理画布相关
        private static void OnUpdateAndDrawModMenuHook(Action<SpriteBatch, GameTime, Color, float, float> orig, SpriteBatch spriteBatch, GameTime gameTime, Color color, float logoRotation, float logoScale) {
            bool result = true;
            foreach (var inds in Instances) {
                if (inds.ignoreBug > 0) {
                    inds.ignoreBug--;
                    continue;
                }
                try {
                    if (!inds.CanOverride()) {
                        continue;
                    }
                    bool? newResult = inds.DrawModMenu(gameTime, color, logoRotation, logoScale);
                    if (newResult.HasValue) {
                        result = newResult.Value;
                    }
                } catch (Exception ex) {
                    inds.ignoreBug = 600;
                    inds.errorCount++;
                    VaultMod.Instance.Logger.Error($"{inds} encountered an error {inds.errorCount} times: {ex}");
                }
            }

            if (result) {
                orig.Invoke(spriteBatch, gameTime, color, logoRotation, logoScale);
            }

            foreach (var inds in Instances) {
                if (inds.ignoreBug > 0) {
                    inds.ignoreBug--;
                    continue;
                }
                try {
                    if (!inds.CanOverride()) {
                        continue;
                    }
                    inds.PostDrawModMenu(gameTime, color, logoRotation, logoScale);
                } catch (Exception ex) {
                    inds.ignoreBug = 600;
                    inds.errorCount++;
                    VaultMod.Instance.Logger.Error($"{inds} encountered an error {inds.errorCount} times: {ex}");
                }
            }
        }

        private static void OnDrawMenuHook(On_DrawMenu_Dlelgate orig, Main main, GameTime gameTime) {
            bool result = true;
            foreach (var inds in Instances) {
                if (inds.ignoreBug > 0) {
                    inds.ignoreBug--;
                    continue;
                }
                try {
                    if (!inds.CanOverride()) {
                        continue;
                    }
                    bool? newResult = inds.DrawMenu(gameTime);
                    if (newResult.HasValue) {
                        result = newResult.Value;
                    }
                } catch (Exception ex) {
                    inds.ignoreBug = 600;
                    inds.errorCount++;
                    VaultMod.Instance.Logger.Error($"{inds} encountered an error {inds.errorCount} times: {ex}");
                }
            }

            if (result) {
                orig.Invoke(main, gameTime);
            }
            else {
                Main.spriteBatch.End();
            }

            Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend
                , SamplerState.LinearClamp, DepthStencilState.None, Main.Rasterizer, null, Main.UIScaleMatrix);

            foreach (var inds in Instances) {
                if (inds.ignoreBug > 0) {
                    inds.ignoreBug--;
                    continue;
                }
                try {
                    if (!inds.CanOverride()) {
                        continue;
                    }
                    inds.PostDrawMenu(gameTime);
                } catch (Exception ex) {
                    inds.ignoreBug = 600;
                    inds.errorCount++;
                    VaultMod.Instance.Logger.Error($"{inds} encountered an error {inds.errorCount} times: {ex}");
                }
            }

            Main.spriteBatch.End();
        }

        private static void OnAddMenuButtonsHook(
            On_AddMenuButtons_Dlelgate orig,
            Main main, int selectedMenu,
            string[] buttonNames, float[] buttonScales,
            ref int offY, ref int spacing,
            ref int buttonIndex, ref int numButtons) {
            orig(main, selectedMenu, buttonNames, buttonScales,
                 ref offY, ref spacing, ref buttonIndex, ref numButtons);

            foreach (var inds in Instances) {
                if (inds.ignoreBug > 0) {
                    inds.ignoreBug--;
                    continue;
                }
                try {
                    if (!inds.CanOverride()) {
                        continue;
                    }
                    inds.AddMenuButtons(main, selectedMenu, buttonNames, buttonScales, ref offY, ref spacing, ref buttonIndex, ref numButtons);
                } catch (Exception ex) {
                    inds.ignoreBug = 600;
                    inds.errorCount++;
                    VaultMod.Instance.Logger.Error($"{inds} encountered an error {inds.errorCount} times: {ex}");
                }
            }
        }
    }
}
