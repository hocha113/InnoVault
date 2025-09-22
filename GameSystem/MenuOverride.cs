using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using Terraria;

namespace InnoVault.GameSystem
{
    /// <summary>
    /// 用于修改游戏菜单的基类
    /// </summary>
    public abstract class MenuOverride : VaultType<MenuOverride>
    {
        /// <summary>
        /// 所有修改的实例集合
        /// </summary>
        public new static List<MenuOverride> Instances { get; internal set; } = [];
        /// <summary>
        /// 从类型映射到实例
        /// </summary>
        public new static Dictionary<Type, MenuOverride> TypeToInstance { get; internal set; } = [];
        /// <summary>
        /// 封闭内容
        /// </summary>
        protected sealed override void VaultRegister() {
            Instances.Add(this);
            TypeToInstance[GetType()] = this;
        }
        /// <summary>
        /// 加载内容
        /// </summary>
        public sealed override void VaultSetup() {
            SetStaticDefaults();
        }
        /// <summary>
        /// 绘制菜单主页，在这个函数中需要注意线程安全
        /// </summary>
        /// <param name="gameTime"></param>
        /// <returns></returns>
        public virtual bool? DrawMenu(GameTime gameTime) {
            return true;
        }
        /// <summary>
        /// 绘制菜单主页，在这个函数中需要注意线程安全
        /// 调用在<see cref="DrawMenu"/>与<see cref="Main.DrawMenu(GameTime)"/>之后
        /// </summary>
        /// <param name="gameTime"></param>
        /// <returns></returns>
        public virtual void PostDrawMenu(GameTime gameTime) {

        }
    }
}
