using InnoVault.GameSystem;
using Microsoft.Xna.Framework.Graphics;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Terraria;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using Terraria.UI;

namespace InnoVault.UIHandles
{
    /// <summary>
    /// 关于UI系统的大部分逻辑与钩子挂载与此处
    /// </summary>
    public sealed class UIHandleLoader : ModSystem
    {
        #region Data
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
        /// 左键按键状态，此值伴随于绘制线程更新，不建议在绘制线程以外的地方使用
        /// </summary>
        public static KeyPressState keyLeftPressState;
        /// <summary>
        /// 右键按键状态，此值伴随于绘制线程更新，不建议在绘制线程以外的地方使用
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
        public static List<UIHandle> UIHandles { get; private set; } = [];
        /// <summary>
        /// 全局的 UI 处理器列表包含所有 UI 元素的处理器实例
        /// </summary>
        public static List<GlobalUIHandle> UIHandleGlobalHooks { get; private set; } = [];
        /// <summary>
        /// 选择None模式的实例，这个列表不会被自动更新或者管理
        /// </summary>
        public static List<UIHandle> UIHandles_NoneMode_DontSimdUpdate { get; private set; } = [];
        /// <summary>
        /// 与 Vanilla 的“鼠标文本”相关的 UI 处理器列表，负责显示和处理鼠标文本的逻辑
        /// </summary>
        public static List<UIHandle> UIHandles_Vanilla_Mouse_Text { get; private set; } = [];
        /// <summary>
        /// 与 Vanilla 的“接口逻辑 1”相关的 UI 处理器列表，负责处理与鼠标输入相关的逻辑
        /// </summary>
        public static List<UIHandle> UIHandles_Vanilla_Interface_Logic_1 { get; private set; } = [];
        /// <summary>
        /// 与 Vanilla 的“MP 玩家名字”相关的 UI 处理器列表，负责绘制其他玩家的名字、距离、健康状态等
        /// </summary>
        public static List<UIHandle> UIHandles_Vanilla_MP_Player_Names { get; private set; } = [];
        /// <summary>
        /// 与 Vanilla 的“隐藏 UI 切换”相关的 UI 处理器列表，负责处理隐藏用户界面的切换逻辑
        /// </summary>
        public static List<UIHandle> UIHandles_Vanilla_Hide_UI_Toggle { get; private set; } = [];
        /// <summary>
        /// 与 Vanilla 的“资源条”相关的 UI 处理器列表，负责绘制和处理生命值、法力值和其他资源条的逻辑
        /// </summary>
        public static List<UIHandle> UIHandles_Vanilla_Resource_Bars { get; private set; } = [];
        /// <summary>
        /// 与 Vanilla 的“游戏内选项”相关的 UI 处理器列表，负责处理游戏内选项菜单的显示和交互
        /// </summary>
        public static List<UIHandle> UIHandles_Vanilla_Ingame_Options { get; private set; } = [];
        /// <summary>
        /// 与 Vanilla 的“网络诊断”相关的 UI 处理器列表，负责绘制网络诊断相关的 UI 元素
        /// </summary>
        public static List<UIHandle> UIHandles_Vanilla_Diagnose_Net { get; private set; } = [];
        /// <summary>
        /// 与模组菜单加载相关的 UI 处理器列表，负责处理菜单加载时的界面和交互逻辑
        /// </summary>
        public static List<UIHandle> UIHandles_Mod_MenuLoad { get; private set; } = [];
        /// <summary>
        /// 与模组图标界面相关的 UI 处理器列表，用于覆盖模组图标界面的绘制或者进行挂载的绘制更新
        /// </summary>
        public static List<UIHandle> UIHandles_Mod_UIModItem { get; private set; } = [];
        /// <summary>
        /// 从内部名到ID的映射
        /// </summary>
        internal static Dictionary<string, int> UIHandle_Name_To_ID { get; private set; } = [];
        /// <summary>
        /// 从Type到ID的映射
        /// </summary>
        internal static Dictionary<Type, int> UIHandle_Type_To_ID { get; private set; } = [];
        /// <summary>
        /// 从ID到实例的映射
        /// </summary>
        internal static Dictionary<int, UIHandle> UIHandle_ID_To_Instance { get; private set; } = [];

        /// <summary>
        /// 用于绘制挂载的委托类型
        /// </summary>
        /// <param name="uiModItem"></param>
        /// <param name="spriteBatch"></param>
        public delegate void On_Draw_Delegate(object uiModItem, SpriteBatch spriteBatch);
        /// <summary>
        /// UIModItem的类型
        /// </summary>
        public static Type UIModItemType { get; private set; }
        /// <summary>
        /// 存储传递UIModItem的实例
        /// </summary>
        public static object UIModItemInstance { get; private set; }
        /// <summary>
        /// 存储传递UIModItem的画布对象
        /// </summary>
        public static object UIModItemSpriteBatch { get; private set; }

        internal static readonly LayersModeEnum[] allLayersModes = (LayersModeEnum[])Enum.GetValues(typeof(LayersModeEnum));
        #endregion

        /// <summary>
        /// 获取目标UI实例
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        public static UIHandle GetUIHandleInstance<T>() where T : UIHandle {
            if (!UIHandle_ID_To_Instance.TryGetValue(GetUIHandleID<T>(), out UIHandle ui)) {
                throw new Exception($"{nameof(T)} That doesn't exist.");
            }
            return ui;
        }

        /// <summary>
        /// 获取目标UI实例
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        public static T GetUIHandleOfType<T>() where T : UIHandle => GetUIHandleInstance<T>() as T;

        /// <summary>
        /// 获取目标UI实例
        /// </summary>
        /// <param name="name"></param>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        public static UIHandle GetUIHandleInstance(string name) {
            if (!UIHandle_ID_To_Instance.TryGetValue(GetUIHandleID(name), out UIHandle ui)) {
                throw new Exception($"{name} That doesn't exist.");
            }
            return ui;
        }

        /// <summary>
        /// 获取目标UI实例
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        public static UIHandle GetUIHandleInstance(int id) {
            if (!UIHandle_ID_To_Instance.TryGetValue(id, out UIHandle ui)) {
                throw new Exception($"{id} That doesn't exist.");
            }
            return ui;
        }

        /// <summary>
        /// 获取目标UI元素的ID
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public static int GetUIHandleID<T>() where T : UIHandle {
            if (!UIHandle_Type_To_ID.TryGetValue(typeof(T), out int id)) {
                throw new Exception($"{nameof(T)} That doesn't exist.");
            }
            return id;
        }
        /// <summary>
        /// 获取目标UI元素的ID
        /// </summary>
        /// <param name="name"></param>
        /// <returns></returns>
        public static int GetUIHandleID(string name) {
            if (!UIHandle_Name_To_ID.TryGetValue(name, out int id)) {
                throw new Exception($"{name} That doesn't exist.");
            }
            return id;
        }

        /// <summary>
        /// 根据指定的 <see cref="LayersModeEnum"/>，判断该模式是否包含在需要修改接口层的钩子中
        /// </summary>
        /// <param name="layersMode">图层模式枚举 <see cref="LayersModeEnum"/></param>
        /// <returns>如果该模式需要修改接口层，则返回 <see langword="true"/>；否则返回 <see langword="false"/></returns>
        public static bool ShouldModifyInterfaceLayers(LayersModeEnum layersMode) {
            if (layersMode == LayersModeEnum.None) {
                return false;
            }
            if (layersMode == LayersModeEnum.Mod_MenuLoad) {
                return false;
            }
            if (layersMode == LayersModeEnum.Mod_UIModItem) {
                return false;
            }
            return true;
        }

        /// <summary>
        /// 进入世界时调用
        /// </summary>
        public static void OnEnterWorld() {
            foreach (var ui in UIHandles) {
                if (!ShouldModifyInterfaceLayers(ui.LayersMode)) {
                    continue;
                }
                ui.OnEnterWorld();
            }
        }

        /// <summary>
        /// 保存UI数据
        /// </summary>
        /// <param name="tag"></param>
        public static void SaveUIData(TagCompound tag) {
            foreach (var ui in UIHandles) {
                if (!ShouldModifyInterfaceLayers(ui.LayersMode)) {
                    continue;
                }
                ui.SaveUIData(tag);
            }
        }

        /// <summary>
        /// 加载UI数据
        /// </summary>
        /// <param name="tag"></param>
        public static void LoadUIData(TagCompound tag) {
            foreach (var ui in UIHandles) {
                if (!ShouldModifyInterfaceLayers(ui.LayersMode)) {
                    continue;
                }
                ui.LoadUIData(tag);
            }
        }

        /// <inheritdoc/>
        public override void Load() {
            UIModItemType = typeof(Main).Assembly.GetTypes().First(t => t.Name == "UIModItem");
            IL_Main.DrawMenu += IL_MenuLoadDraw_Hook;
            VaultHook.Add(UIModItemType.GetMethod("Draw", BindingFlags.Instance | BindingFlags.Public), On_UIModItem_DrawHook);
        }

        /// <inheritdoc/>
        public override void Unload() {
            foreach (var hander in UIHandles) {
                hander.UnLoad();
            }

            UIHandles?.Clear();
            UIHandleGlobalHooks?.Clear();
            UIHandles_Vanilla_Mouse_Text?.Clear();
            UIHandles_Vanilla_Interface_Logic_1?.Clear();
            UIHandles_Vanilla_MP_Player_Names?.Clear();
            UIHandles_Vanilla_Hide_UI_Toggle?.Clear();
            UIHandles_Vanilla_Resource_Bars?.Clear();
            UIHandles_Vanilla_Ingame_Options?.Clear();
            UIHandles_Vanilla_Diagnose_Net?.Clear();
            UIHandles_Mod_MenuLoad?.Clear();
            UIHandles_Mod_UIModItem?.Clear();
            UIHandle_Name_To_ID?.Clear();
            UIHandle_Type_To_ID?.Clear();
            UIHandle_ID_To_Instance?.Clear();

            LeftHeldEvent = null;
            LeftPressedEvent = null;
            LeftReleasedEvent = null;
            RightHeldEvent = null;
            RightPressedEvent = null;
            RightReleasedEvent = null;
            IL_Main.DrawMenu -= IL_MenuLoadDraw_Hook;

            UIModItemType = null;
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
                        LeftHeldEvent?.Invoke();
                        break;
                    case KeyPressState.Pressed:
                        LeftPressedEvent?.Invoke();
                        break;
                    case KeyPressState.Released:
                        LeftReleasedEvent?.Invoke();
                        break;
                }
            }

            keyRightPressState = CheckRightKeyState();
            if (keyRightPressState != KeyPressState.None) {
                switch (keyRightPressState) {
                    case KeyPressState.Held:
                        RightHeldEvent?.Invoke();
                        break;
                    case KeyPressState.Pressed:
                        RightPressedEvent?.Invoke();
                        break;
                    case KeyPressState.Released:
                        RightReleasedEvent?.Invoke();
                        break;
                }
            }

            foreach (var global in UIHandleGlobalHooks) {
                global.UpdateKeyState();
            }
        }

        /// <summary>
        /// 根据指定的 <see cref="LayersModeEnum"/>，返回相应的图层模式的代码名称字符串
        /// </summary>
        /// <param name="layersMode">图层模式枚举 <see cref="LayersModeEnum"/></param>
        /// <returns>图层模式的代码名称字符串</returns>
        public static string GetLayerModeCodeName(LayersModeEnum layersMode) {
            return layersMode switch {
                LayersModeEnum.None => "Mod_None",
                LayersModeEnum.Vanilla_Mouse_Text => "Vanilla: Mouse Text",
                LayersModeEnum.Vanilla_Interface_Logic_1 => "Vanilla: Interface Logic 1",
                LayersModeEnum.Vanilla_MP_Player_Names => "Vanilla: MP Player Names",
                LayersModeEnum.Vanilla_Hide_UI_Toggle => "Vanilla: Hide UI Toggle",
                LayersModeEnum.Vanilla_Resource_Bars => "Vanilla: Resource Bars",
                LayersModeEnum.Vanilla_Ingame_Options => "Vanilla: Ingame Options",
                LayersModeEnum.Vanilla_Diagnose_Net => "Vanilla: Diagnose Net",
                LayersModeEnum.Mod_MenuLoad => "Mod_MenuLoad",
                LayersModeEnum.Mod_UIModItem => "Mod_UIModItem",
                _ => "Null"
            };
        }
        /// <summary>
        /// 根据指定的 <see cref="LayersModeEnum"/>，返回与之关联的 <see cref="UIHandle"/> 实例列表
        /// </summary>
        /// <param name="layersMode">图层模式枚举 <see cref="LayersModeEnum"/></param>
        /// <returns>与指定图层模式相关联的 <see cref="UIHandle"/> 实例列表</returns>
        public static List<UIHandle> GetLayerModeHandlers(LayersModeEnum layersMode) {
            return layersMode switch {
                LayersModeEnum.None => UIHandles_NoneMode_DontSimdUpdate,
                LayersModeEnum.Vanilla_Mouse_Text => UIHandles_Vanilla_Mouse_Text,
                LayersModeEnum.Vanilla_Interface_Logic_1 => UIHandles_Vanilla_Interface_Logic_1,
                LayersModeEnum.Vanilla_MP_Player_Names => UIHandles_Vanilla_MP_Player_Names,
                LayersModeEnum.Vanilla_Hide_UI_Toggle => UIHandles_Vanilla_Hide_UI_Toggle,
                LayersModeEnum.Vanilla_Resource_Bars => UIHandles_Vanilla_Resource_Bars,
                LayersModeEnum.Vanilla_Ingame_Options => UIHandles_Vanilla_Ingame_Options,
                LayersModeEnum.Vanilla_Diagnose_Net => UIHandles_Vanilla_Diagnose_Net,
                LayersModeEnum.Mod_MenuLoad => UIHandles_Mod_MenuLoad,
                LayersModeEnum.Mod_UIModItem => UIHandles_Mod_UIModItem,
                _ => null
            };
        }
        /// <inheritdoc/>
        public static void UIHanderElementUpdate(UIHandle hander) {
            if (hander.ignoreBug > 0) {
                hander.ignoreBug--;
                return;
            }

            try {
                if (!hander.Active) {
                    return;
                }

                bool reset = true;
                foreach (var global in UIHandleGlobalHooks) {
                    reset = global.PreUIHanderElementUpdate(hander);
                }

                if (reset) {
                    hander.Update();
                    hander.Draw(Main.spriteBatch);
                }

                foreach (var global in UIHandleGlobalHooks) {
                    global.PostUIHanderElementUpdate(hander);
                }
            } catch (Exception ex) {
                hander.ignoreBug = 600;
                hander.errorCount++;
                VaultMod.Instance.Logger.Error($"{hander} encountered an error {hander.errorCount} times: {ex}");
            }
        }
        /// <inheritdoc/>
        public override void ModifyInterfaceLayers(List<GameInterfaceLayer> layers) {
            UpdateKeyState();

            foreach (var layersMode in allLayersModes) {
                if (!ShouldModifyInterfaceLayers(layersMode)) {
                    continue;
                }

                List<UIHandle> Handers = GetLayerModeHandlers(layersMode);
                if (Handers.Count <= 0) {
                    continue;
                }

                string layerName = GetLayerModeCodeName(layersMode);
                int index = layers.FindIndex((layer) => layer.Name == layerName);
                if (index == -1) {
                    continue;
                }

                layers.Insert(index, new LegacyGameInterfaceLayer("UIHander: " + layerName, delegate {
                    for (int i = 0; i < Handers.Count; i++) {
                        UIHanderElementUpdate(Handers[i]);
                    }
                    return true;
                }, InterfaceScaleType.UI));
            }

            foreach (var global in UIHandleGlobalHooks) {
                global.PostUpdataInUIEverything();
            }
        }

        private static void IL_MenuLoadDraw_Hook(ILContext il) {
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
                string conxt2 = "IL mount failed. Has the target stream changed or the frame has been removed?";
                string errortext = $"{nameof(UIHandleLoader)}: {conxt2} ";
                VaultMod.Instance.Logger.Info(errortext);
                throw new Exception(errortext);
            }

            _ = potlevel.EmitDelegate(() => MenuLoadDraw(Main.spriteBatch));
        }

        private static void MenuLoadDraw(SpriteBatch spriteBatch) {
            if (Main.gameMenu && UIHandles_Mod_MenuLoad != null && UIHandles_Mod_MenuLoad.Count > 0) {
                spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.LinearClamp
                    , DepthStencilState.None, RasterizerState.CullCounterClockwise, null, Main.UIScaleMatrix);
                UpdateKeyState();
                for (int i = 0; i < UIHandles_Mod_MenuLoad.Count; i++) {
                    UIHanderElementUpdate(UIHandles_Mod_MenuLoad[i]);
                }
                foreach (var global in UIHandleGlobalHooks) {
                    global.PostUpdataInUIEverything();
                }
                spriteBatch.End();
            }
        }

        private static void On_UIModItem_DrawHook(On_Draw_Delegate orig, object instance, SpriteBatch spriteBatch) {
            UIModItemInstance = instance;
            UIModItemSpriteBatch = spriteBatch;
            orig.Invoke(instance, spriteBatch);
            if (Main.gameMenu && UIHandles_Mod_UIModItem != null && UIHandles_Mod_UIModItem.Count > 0) {
                UpdateKeyState();
                for (int i = 0; i < UIHandles_Mod_UIModItem.Count; i++) {
                    UIHanderElementUpdate(UIHandles_Mod_UIModItem[i]);
                }
                foreach (var global in UIHandleGlobalHooks) {
                    global.PostUpdataInUIEverything();
                }
            }
            UIModItemInstance = null;
            UIModItemSpriteBatch = null;
        }
    }
}
