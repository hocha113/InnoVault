using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using Terraria.ModLoader;

namespace InnoVault.GameSystem
{
    /// <summary>
    /// 用于修改游戏菜单的基类
    /// </summary>
    public class MenuOverride : ModType
    {
        /// <summary>
        /// 所有修改的实例集合
        /// </summary>
        public static List<MenuOverride> Instances { get; internal set; } = [];
        /// <summary>
        /// 从类型映射到实例
        /// </summary>
        public static Dictionary<Type, MenuOverride> TypeToInstance { get; internal set; } = [];
        /// <summary>
        /// 程序进行防御性处理时会用到的值，如果该实例内部发生错误，则会将该值设置为大于0的值，期间不会再自动调用该实例
        /// 这个值每帧减一，直到不再大于0。无论出于什么目的，不要去自行设置它
        /// </summary>
        internal int ignoreBug = -1;
        /// <summary>
        /// 记录发生错误的次数，不要自行设置它
        /// </summary>
        internal int errorCount;
        /// <summary>
        /// 封闭内容
        /// </summary>
        protected override void Register() {
            Instances.Add(this);
            TypeToInstance[GetType()] = this;
        }
        /// <summary>
        /// 是否进行覆盖，在这个函数中需要注意线程安全
        /// </summary>
        /// <returns></returns>
        public virtual bool CanOverride() {
            return true;
        }
        /// <summary>
        /// 绘制菜单主页，在这个函数中需要注意线程安全
        /// </summary>
        /// <param name="gameTime"></param>
        /// <returns></returns>
        public virtual bool DrawMenu(GameTime gameTime) {
            return true;
        }
    }
}
