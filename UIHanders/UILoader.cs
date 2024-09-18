using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using System;
using System.Collections.Generic;
using Terraria;
using Terraria.ModLoader;
using Terraria.UI;

namespace InnoVault.UIHanders
{
    /// <summary>
    /// 关于UI系统的大部分逻辑与钩子挂载与此处
    /// </summary>
    public class UILoader : ModSystem
    {
        /// <summary>
        /// 旧的左键按下状态
        /// </summary>
        public static bool oldDownL;
        /// <summary>
        /// 当前的左键按下状态
        /// </summary>
        public static bool downL;
        /// <summary>
        /// 旧的右键按下状态
        /// </summary>
        public static bool oldDownR;
        /// <summary>
        /// 当前的右键按下状态
        /// </summary>
        public static bool downR;
        /// <summary>
        /// 左键按键状态
        /// </summary>
        public static KeyPressState keyLeftPressState;
        /// <summary>
        /// 右键按键状态
        /// </summary>
        public static KeyPressState keyRightPressState;
        /// <summary>
        /// 当左键保持按下时触发的事件用于捕捉玩家持续按住左键的行为
        /// </summary>
        public static event Action LeftHeldEvent;
        /// <summary>
        /// 当左键被按下时触发的事件用于捕捉玩家按下左键的瞬间
        /// </summary>
        public static event Action LeftPressedEvent;
        /// <summary>
        /// 当左键释放时触发的事件用于捕捉玩家松开左键的瞬间
        /// </summary>
        public static event Action LeftReleasedEvent;
        /// <summary>
        /// 当右键保持按下时触发的事件用于捕捉玩家持续按住右键的行为
        /// </summary>
        public static event Action RightHeldEvent;
        /// <summary>
        /// 当右键被按下时触发的事件用于捕捉玩家按下右键的瞬间
        /// </summary>
        public static event Action RightPressedEvent;
        /// <summary>
        /// 当右键释放时触发的事件用于捕捉玩家松开右键的瞬间
        /// </summary>
        public static event Action RightReleasedEvent;
        /// <summary>
        /// 全局的 UI 处理器列表包含所有 UI 元素的处理器实例
        /// </summary>
        public static List<UIHander> UIHanders { get; private set; } = [];
        /// <summary>
        /// 与 Vanilla 的“鼠标文本”相关的 UI 处理器列表，负责显示和处理鼠标文本的逻辑
        /// </summary>
        public static List<UIHander> UIHanders_Vanilla_Mouse_Text { get; private set; } = [];
        /// <summary>
        /// 与 Vanilla 的“接口逻辑 1”相关的 UI 处理器列表，负责处理与鼠标输入相关的逻辑
        /// </summary>
        public static List<UIHander> UIHanders_Vanilla_Interface_Logic_1 { get; private set; } = [];
        /// <summary>
        /// 与 Vanilla 的“MP 玩家名字”相关的 UI 处理器列表，负责绘制其他玩家的名字、距离、健康状态等
        /// </summary>
        public static List<UIHander> UIHanders_Vanilla_MP_Player_Names { get; private set; } = [];
        /// <summary>
        /// 与 Vanilla 的“隐藏 UI 切换”相关的 UI 处理器列表，负责处理隐藏用户界面的切换逻辑
        /// </summary>
        public static List<UIHander> UIHanders_Vanilla_Hide_UI_Toggle { get; private set; } = [];
        /// <summary>
        /// 与 Vanilla 的“资源条”相关的 UI 处理器列表，负责绘制和处理生命值、法力值和其他资源条的逻辑
        /// </summary>
        public static List<UIHander> UIHanders_Vanilla_Resource_Bars { get; private set; } = [];
        /// <summary>
        /// 与 Vanilla 的“游戏内选项”相关的 UI 处理器列表，负责处理游戏内选项菜单的显示和交互
        /// </summary>
        public static List<UIHander> UIHanders_Vanilla_Ingame_Options { get; private set; } = [];
        /// <summary>
        /// 与 Vanilla 的“网络诊断”相关的 UI 处理器列表，负责绘制网络诊断相关的 UI 元素
        /// </summary>
        public static List<UIHander> UIHanders_Vanilla_Diagnose_Net { get; private set; } = [];
        /// <summary>
        /// 与模组菜单加载相关的 UI 处理器列表，负责处理菜单加载时的界面和交互逻辑
        /// </summary>
        public static List<UIHander> UIHanders_Mod_MenuLoad { get; private set; } = [];
        internal void Initialize() {
            UIHanders = [];
            UIHanders_Vanilla_Mouse_Text = [];
            UIHanders_Vanilla_Interface_Logic_1 = [];
            UIHanders_Vanilla_MP_Player_Names = [];
            UIHanders_Vanilla_Hide_UI_Toggle = [];
            UIHanders_Vanilla_Resource_Bars = [];
            UIHanders_Vanilla_Ingame_Options = [];
            UIHanders_Vanilla_Diagnose_Net = [];
            UIHanders_Mod_MenuLoad = [];
        }
        /// <inheritdoc/>
        public override void Load() {
            Initialize();
            UIHanders = VaultUtils.HanderSubclass<UIHander>();
            UIHanders.RemoveAll(ui => !ui.CanLoad());
            foreach (var hander in UIHanders) {
                hander.Load();
                LayersModeGetInstanceList(hander.LayersMode).Add(hander);
            }

            IL_Main.DrawMenu += IL_MenuLoadDraw_Hook;
        }
        /// <inheritdoc/>
        public override void Unload() {
            foreach (var hander in UIHanders) {
                hander.UnLoad();
            }
            Initialize();
            LeftHeldEvent = null;
            LeftPressedEvent = null;
            LeftReleasedEvent = null;
            RightHeldEvent = null;
            RightPressedEvent = null;
            RightReleasedEvent = null;
            IL_Main.DrawMenu -= IL_MenuLoadDraw_Hook;
        }

        /// <summary>
        /// 检查左键按键状态的变化，返回按键状态枚举
        /// </summary>
        public static KeyPressState CheckLeftKeyState() {
            oldDownL = downL;
            downL = Main.LocalPlayer.PressKey(); // 检查左键是否按下
            if (downL && oldDownL) return KeyPressState.Held;
            if (downL && !oldDownL) return KeyPressState.Pressed;
            if (!downL && oldDownL) return KeyPressState.Released;
            return KeyPressState.None;
        }

        /// <summary>
        /// 检查右键按键状态的变化，返回按键状态枚举
        /// </summary>
        public static KeyPressState CheckRightKeyState() {
            oldDownR = downR;
            downR = Main.LocalPlayer.PressKey(false); // 检查右键是否按下
            if (downR && oldDownR) return KeyPressState.Held;
            if (downR && !oldDownR) return KeyPressState.Pressed;
            if (!downR && oldDownR) return KeyPressState.Released;
            return KeyPressState.None;
        }

        internal static void UpdateKeyState() {
            keyLeftPressState = CheckLeftKeyState();
            if (keyLeftPressState != KeyPressState.None) {
                switch (keyLeftPressState) {
                    case KeyPressState.Held:
                        if (LeftHeldEvent != null) {
                            LeftHeldEvent.Invoke();
                        }
                        break;
                    case KeyPressState.Pressed:
                        if (LeftPressedEvent != null) {
                            LeftPressedEvent.Invoke();
                        }
                        break;
                    case KeyPressState.Released:
                        if (LeftReleasedEvent != null) {
                            LeftReleasedEvent.Invoke();
                        }
                        break;
                }
            }
            keyRightPressState = CheckRightKeyState();
            if (keyRightPressState != KeyPressState.None) {
                switch (keyRightPressState) {
                    case KeyPressState.Held:
                        if (RightHeldEvent != null) {
                            RightHeldEvent.Invoke();
                        }
                        break;
                    case KeyPressState.Pressed:
                        if (RightPressedEvent != null) {
                            RightPressedEvent.Invoke();
                        }
                        break;
                    case KeyPressState.Released:
                        if (RightReleasedEvent != null) {
                            RightReleasedEvent.Invoke();
                        }
                        break;
                }
            }
        }

        /// <inheritdoc/>
        public string LayersModeGetCodeName(LayersModeEnum layersMode) {
            string reset = "None";
            switch (layersMode) {
                case LayersModeEnum.Vanilla_Mouse_Text:
                    reset = "Vanilla: Mouse Text";
                    break;
                case LayersModeEnum.Vanilla_Interface_Logic_1:
                    reset = "Vanilla: Interface Logic 1";
                    break;
                case LayersModeEnum.Vanilla_MP_Player_Names:
                    reset = "Vanilla: MP Player Names";
                    break;
                case LayersModeEnum.Vanilla_Hide_UI_Toggle:
                    reset = "Vanilla: Hide UI Toggle";
                    break;
                case LayersModeEnum.Vanilla_Resource_Bars:
                    reset = "Vanilla: Resource Bars";
                    break;
                case LayersModeEnum.Vanilla_Ingame_Options:
                    reset = "Vanilla: Ingame Options";
                    break;
                case LayersModeEnum.Vanilla_Diagnose_Net:
                    reset = "Vanilla: Diagnose Net";
                    break;
                case LayersModeEnum.Mod_MenuLoad:
                    reset = "Mod_MenuLoad";
                    break;
            }
            return reset;
        }
        /// <inheritdoc/>
        public List<UIHander> LayersModeGetInstanceList(LayersModeEnum layersMode) {
            List<UIHander> uiHanders = [];
            switch (layersMode) {
                case LayersModeEnum.Vanilla_Mouse_Text:
                    uiHanders = UIHanders_Vanilla_Mouse_Text;
                    break;
                case LayersModeEnum.Vanilla_Interface_Logic_1:
                    uiHanders = UIHanders_Vanilla_Interface_Logic_1;
                    break;
                case LayersModeEnum.Vanilla_MP_Player_Names:
                    uiHanders = UIHanders_Vanilla_MP_Player_Names;
                    break;
                case LayersModeEnum.Vanilla_Hide_UI_Toggle:
                    uiHanders = UIHanders_Vanilla_Hide_UI_Toggle;
                    break;
                case LayersModeEnum.Vanilla_Resource_Bars:
                    uiHanders = UIHanders_Vanilla_Resource_Bars;
                    break;
                case LayersModeEnum.Vanilla_Ingame_Options:
                    uiHanders = UIHanders_Vanilla_Ingame_Options;
                    break;
                case LayersModeEnum.Vanilla_Diagnose_Net:
                    uiHanders = UIHanders_Vanilla_Diagnose_Net;
                    break;
                case LayersModeEnum.Mod_MenuLoad:
                    uiHanders = UIHanders_Mod_MenuLoad;
                    break;
            }
            return uiHanders;
        }
        /// <inheritdoc/>
        public static void UIHanderElementUpdate(UIHander hander) {
            if (!hander.Active) {
                return;
            }
            hander.Update();
            hander.Draw(Main.spriteBatch);
        }
        /// <inheritdoc/>
        public override void ModifyInterfaceLayers(List<GameInterfaceLayer> layers) {
            //为什么是6？因为关于香草的钩子所代表的枚举值是从0到6的，后续可能扩展，但所有香草的枚举都会排在前列
            UpdateKeyState();
            for (int i = 0; i < 7; i++) {
                LayersModeEnum layersMode = (LayersModeEnum)i;
                List<UIHander> Handers = LayersModeGetInstanceList(layersMode);

                if (Handers.Count <= 0) {
                    continue;
                }

                string layerName = LayersModeGetCodeName(layersMode);
                int index = layers.FindIndex((layer) => layer.Name == layerName);

                if (index == -1) {
                    continue;
                }

                layers.Insert(index, new LegacyGameInterfaceLayer("UIHander: " + layerName, delegate {
                    foreach (var hander in Handers) {
                        UIHanderElementUpdate(hander);
                    }
                    return true;
                }, InterfaceScaleType.UI));
            }
        }
        /// <inheritdoc/>
        public static void IL_MenuLoadDraw_Hook(ILContext il) {
            ILCursor potlevel = new(il);

            if (!potlevel.TryGotoNext(
                i => i.MatchLdsfld(typeof(Main), nameof(Main.spriteBatch)),
                i => i.Match(OpCodes.Ldc_I4_0),
                i => i.MatchLdsfld(typeof(BlendState), nameof(BlendState.AlphaBlend)),
                i => i.MatchLdsfld(typeof(Main), nameof(Main.SamplerStateForCursor)),
                i => i.MatchLdsfld(typeof(DepthStencilState), nameof(DepthStencilState.None)),
                i => i.MatchLdsfld(typeof(RasterizerState), nameof(RasterizerState.CullCounterClockwise)),
                i => i.Match(OpCodes.Ldnull),
                i => i.MatchCall(typeof(Main), $"get_{nameof(Main.UIScaleMatrix)}")
            )) {
                string conxt2 = VaultUtils.Translation("IL 挂载失败，是否是目标流已经更改或者移除框架?"
                    , "IL mount failed. Has the target stream changed or the frame has been removed?");
                string errortext = $"{nameof(UILoader)}: {conxt2} ";
                VaultMod.Instance.Logger.Info(errortext);
                throw new Exception(errortext);
            }

            _ = potlevel.EmitDelegate(() => MenuLoadDraw(Main.spriteBatch));
        }
        /// <inheritdoc/>
        public static void MenuLoadDraw(SpriteBatch spriteBatch) {
            if (Main.gameMenu && UIHanders_Mod_MenuLoad != null && UIHanders_Mod_MenuLoad.Count > 0) {
                spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.LinearClamp
                    , DepthStencilState.None, RasterizerState.CullCounterClockwise, null, Main.UIScaleMatrix);
                UpdateKeyState();
                foreach (var hander in UIHanders_Mod_MenuLoad) {
                    UIHanderElementUpdate(hander);
                }
                spriteBatch.End();
            }
        }
    }
}
