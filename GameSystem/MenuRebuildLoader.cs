using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using Terraria;
using Terraria.ModLoader;
using Terraria.UI;
using static InnoVault.GameSystem.MenuOverride;

namespace InnoVault.GameSystem
{
    /// <summary>
    /// 关于菜单重制节点的钩子均挂载于此处
    /// </summary>
    public class MenuRebuildLoader : IVaultLoader
    {
#pragma warning disable CS1591 //缺少对公共可见类型或成员的 XML 注释
        public delegate void On_DrawMenu_Dlelgate(Main main, GameTime gameTime);
        public delegate void On_AddMenuButtons_Dlelgate(Main main, int selectedMenu, string[] buttonNames, float[] buttonScales, ref int offY, ref int spacing, ref int buttonIndex, ref int numButtons);
#pragma warning restore CS1591 //缺少对公共可见类型或成员的 XML 注释
        private static readonly FixedTickTimer menuTickTimer = new();
        /// <summary>激活过的菜单界面实例，用来区分首次激活</summary>
        private static readonly ConditionalWeakTable<UIState, object> activatedMenuStates = new();

        void IVaultLoader.LoadData() {
            menuTickTimer.Reset();
            VaultHook.Add(typeof(Main).GetMethod("DrawMenu", BindingFlags.Instance | BindingFlags.NonPublic), OnDrawMenuHook);
            VaultHook.Add(typeof(MenuLoader).GetMethod("UpdateAndDrawModMenu", BindingFlags.Static | BindingFlags.NonPublic), OnUpdateAndDrawModMenuHook);
            VaultHook.Add(typeof(Main).Assembly.GetType("Terraria.ModLoader.UI.Interface").GetMethod("AddMenuButtons", BindingFlags.Static | BindingFlags.NonPublic), OnAddMenuButtonsHook);
            VaultHook.Add(typeof(UserInterface).GetMethod(nameof(UserInterface.SetState), BindingFlags.Instance | BindingFlags.Public), OnUserInterfaceSetStateHook);
            VaultHook.Add(typeof(UserInterface).GetMethod(nameof(UserInterface.Update), BindingFlags.Instance | BindingFlags.Public), OnUserInterfaceUpdateHook);
            VaultHook.Add(typeof(UserInterface).GetMethod(nameof(UserInterface.Draw), BindingFlags.Instance | BindingFlags.Public), OnUserInterfaceDrawHook);
        }

        //UserInterface 的三个钩子对所有界面都会触发，只在菜单界面持有 UIState 时派发
        private static void OnUserInterfaceSetStateHook(Action<UserInterface, UIState> orig, UserInterface self, UIState state) {
            UIState previous = self.CurrentState;
            orig.Invoke(self, state);
            if (self != Main.MenuUI || state == null || state == previous || self.CurrentState != state) {
                return;
            }
            bool firstActivation = !activatedMenuStates.TryGetValue(state, out _);
            if (firstActivation) {
                activatedMenuStates.Add(state, null);
            }
            bool any = false;
            foreach (var inds in Instances.ToArray()) {
                if (inds.ignoreBug > 0) {
                    continue;
                }
                try {
                    if (!inds.CanOverride()) {
                        continue;
                    }
                    inds.OnMenuStateActivated(state, firstActivation);
                    any = true;
                } catch (Exception ex) {
                    inds.ignoreBug = 600;
                    inds.errorCount++;
                    VaultMod.Instance.Logger.Error($"{inds} encountered an error {inds.errorCount} times in OnMenuStateActivated: {ex}");
                }
            }
            if (any) {
                state.Recalculate();
            }
        }

        private static void OnUserInterfaceUpdateHook(Action<UserInterface, GameTime> orig, UserInterface self, GameTime time) {
            orig.Invoke(self, time);
            UIState state = self.CurrentState;
            if (self != Main.MenuUI || state == null) {
                return;
            }
            foreach (var inds in Instances.ToArray()) {
                if (inds.ignoreBug > 0) {
                    continue;
                }
                try {
                    if (!inds.CanOverride()) {
                        continue;
                    }
                    inds.PostUpdateMenuState(state);
                } catch (Exception ex) {
                    inds.ignoreBug = 600;
                    inds.errorCount++;
                    VaultMod.Instance.Logger.Error($"{inds} encountered an error {inds.errorCount} times in PostUpdateMenuState: {ex}");
                }
            }
        }

        private static void OnUserInterfaceDrawHook(Action<UserInterface, SpriteBatch, GameTime> orig, UserInterface self, SpriteBatch spriteBatch, GameTime time) {
            orig.Invoke(self, spriteBatch, time);
            UIState state = self.CurrentState;
            if (self != Main.MenuUI || state == null) {
                return;
            }
            foreach (var inds in Instances.ToArray()) {
                if (inds.ignoreBug > 0) {
                    continue;
                }
                try {
                    if (!inds.CanOverride()) {
                        continue;
                    }
                    inds.PostDrawMenuState(state, spriteBatch);
                } catch (Exception ex) {
                    inds.ignoreBug = 600;
                    inds.errorCount++;
                    VaultMod.Instance.Logger.Error($"{inds} encountered an error {inds.errorCount} times in PostDrawMenuState: {ex}");
                }
            }
        }

        //这个函数内部已经是处理好了画布状态的，所以不需要再去管理画布相关
        private static void OnUpdateAndDrawModMenuHook(Action<SpriteBatch, GameTime, Color, float, float> orig, SpriteBatch spriteBatch, GameTime gameTime, Color color, float logoRotation, float logoScale) {
            bool result = true;
            var snapshot = Instances.ToArray();
            foreach (var inds in snapshot) {
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

            foreach (var inds in snapshot) {
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

        /// <summary>
        /// 处理主菜单的固定步长逻辑更新，以60tick/s的频率驱动 <see cref="MenuOverride.MenuLogicUpdate"/><br/>
        /// 通过 <see cref="FixedTickTimer"/> 在绘制线程中模拟稳定的逻辑更新频率，不受帧率影响
        /// </summary>
        internal static void ProcessMenuLogicUpdates() {
            menuTickTimer.Update();
            var snapshot = Instances.ToArray();
            while (menuTickTimer.Tick()) {
                foreach (var inds in snapshot) {
                    if (inds.ignoreBug > 0) {
                        continue;
                    }

                    try {
                        if (!inds.CanOverride()) {
                            continue;
                        }
                        inds.MenuLogicUpdate();
                    } catch (Exception ex) {
                        inds.ignoreBug = 600;
                        inds.errorCount++;
                        VaultMod.Instance.Logger.Error($"{inds} encountered an error {inds.errorCount} times in MenuLogicUpdate: {ex}");
                    }
                }
            }
        }

        private static void OnDrawMenuHook(On_DrawMenu_Dlelgate orig, Main main, GameTime gameTime) {
            ProcessMenuLogicUpdates();

            bool result = true;
            var snapshot = Instances.ToArray();
            foreach (var inds in snapshot) {
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

            foreach (var inds in snapshot) {
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
