using Microsoft.Xna.Framework.Graphics;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using rail;
using System;
using System.Collections.Generic;
using Terraria;
using Terraria.ModLoader;
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
        public static List<UIHandle> UIHandles { get; private set; } = [];
        /// <summary>
        /// 全局的 UI 处理器列表包含所有 UI 元素的处理器实例
        /// </summary>
        public static List<UIHandleGlobal> UIHandleGlobalHooks { get; private set; } = [];
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
        /// UI关于Type到所属模组的映射
        /// </summary>
        internal static Dictionary<Type, Mod> UIHandle_Type_To_Mod { get; private set; } = [];
        /// <summary>
        /// <see cref="UIHandleGlobal"/>关于Type到所属模组的映射
        /// </summary>
        internal static Dictionary<Type, Mod> UIHandleGlobal_Type_To_Mod { get; private set; } = [];
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

        private static readonly LayersModeEnum[] allLayersModes = (LayersModeEnum[])Enum.GetValues(typeof(LayersModeEnum));
        #endregion
        internal void Initialize() {
            UIHandles = [];
            UIHandleGlobalHooks = [];
            UIHandles_Vanilla_Mouse_Text = [];
            UIHandles_Vanilla_Interface_Logic_1 = [];
            UIHandles_Vanilla_MP_Player_Names = [];
            UIHandles_Vanilla_Hide_UI_Toggle = [];
            UIHandles_Vanilla_Resource_Bars = [];
            UIHandles_Vanilla_Ingame_Options = [];
            UIHandles_Vanilla_Diagnose_Net = [];
            UIHandles_Mod_MenuLoad = [];
            UIHandle_Type_To_Mod = [];
            UIHandleGlobal_Type_To_Mod = [];
            UIHandle_Name_To_ID = [];
            UIHandle_Type_To_ID = [];
            UIHandle_ID_To_Instance = [];
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
        public static int GetUIhandleID(string name) {
            if (!UIHandle_Name_To_ID.TryGetValue(name, out int id)) {
                throw new Exception($"{name} That doesn't exist.");
            }
            return id;
        }

        //加载UIHandle
        private static void UIHandlesLoad() {
            UIHandles = VaultUtils.HanderSubclass<UIHandle>();
            //我们需要在第一时间去寻找Mod,这样后续的UnLoad和Load才可以正常访问这些实例的Mod属性
            for (int i = 0; i < UIHandles.Count; i++) {
                var hander = UIHandles[i];
                VaultUtils.AddTypeModAssociation(UIHandle_Type_To_Mod, hander.GetType(), ModLoader.Mods);
                UIHandle_Type_To_ID.Add(hander.GetType(), i);
                UIHandle_ID_To_Instance.Add(i, hander);
            }

            UIHandles.RemoveAll(ui => !ui.CanLoad());
            foreach (var hander in UIHandles) {
                hander.Load();
                GetLayerModeHandlers(hander.LayersMode).Add(hander);
            }

            foreach (var layersMode in allLayersModes) {
                GetLayerModeHandlers(layersMode).Sort((x, y) => x.RenderPriority.CompareTo(y.RenderPriority));//按照升序排列
            }
        }

        //加载Global实例
        private static void UIHandleGlobalLoad() {
            UIHandleGlobalHooks = VaultUtils.HanderSubclass<UIHandleGlobal>();
            //我们需要在第一时间去寻找Mod,这样后续的UnLoad和Load才可以正常访问这些实例的Mod属性
            foreach (var global in UIHandleGlobalHooks) {
                VaultUtils.AddTypeModAssociation(UIHandleGlobal_Type_To_Mod, global.GetType(), ModLoader.Mods);
                global.Load();
            }
        }

        /// <inheritdoc/>
        public override void Load() {
            Initialize();
            UIHandlesLoad();
            UIHandleGlobalLoad();

            IL_Main.DrawMenu += IL_MenuLoadDraw_Hook;
        }

        /// <inheritdoc/>
        public override void Unload() {
            foreach (var hander in UIHandles) {
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

            foreach (var global in UIHandleGlobalHooks) {
                global.UpdateKeyState();
            }
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
            return true;
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
                _ => null
            };
        }
        /// <inheritdoc/>
        public static void UIHanderElementUpdate(UIHandle hander) {
            if (!hander.Active) {
                return;
            }

            hander.Update();
            hander.Draw(Main.spriteBatch);

            foreach (var global in UIHandleGlobalHooks) {
                global.UIHanderElementUpdate(hander);
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
                    foreach (var hander in Handers) {
                        UIHanderElementUpdate(hander);
                    }
                    return true;
                }, InterfaceScaleType.UI));
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
                string conxt2 = VaultUtils.Translation("IL 挂载失败，是否是目标流已经更改或者移除框架?"
                    , "IL mount failed. Has the target stream changed or the frame has been removed?");
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
                foreach (var hander in UIHandles_Mod_MenuLoad) {
                    UIHanderElementUpdate(hander);
                }
                spriteBatch.End();
            }
        }
    }
}
