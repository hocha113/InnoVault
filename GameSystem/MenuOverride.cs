using Microsoft.Xna.Framework;
using Terraria;

namespace InnoVault.GameSystem
{
    /// <summary>
    /// 用于修改游戏菜单的基类
    /// </summary>
    public abstract class MenuOverride : VaultType<MenuOverride>
    {
        /// <summary>
        /// 封闭内容
        /// </summary>
        protected sealed override void VaultRegister() { }
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
