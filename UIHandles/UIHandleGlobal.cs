﻿using InnoVault.TileProcessors;
using Terraria.ModLoader;

namespace InnoVault.UIHandles
{
    /// <summary>
    /// 对于UI的一个全局类，你可以使用它来进行一些统一的操作
    /// </summary>
    public class UIHandleGlobal
    {
        /// <summary>
        /// 所属的Mod
        /// </summary>
        public Mod Mod => UIHandleLoader.UIHandleGlobal_Type_To_Mod[GetType()];
        /// <summary>
        /// 游戏加载时调用一次
        /// </summary>
        public virtual void Load() {

        }
        /// <summary>
        /// 按键状态更新时运行
        /// </summary>
        public virtual void UpdateKeyState() {

        }
        /// <summary>
        /// UI元素更新时运行
        /// </summary>
        public virtual void UIHanderElementUpdate(UIHandle handle) {

        }
    }
}