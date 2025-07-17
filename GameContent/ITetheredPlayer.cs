using Microsoft.Xna.Framework;
using Terraria;

namespace InnoVault.GameContent
{
    /// <summary>
    /// 规定一些附着玩家的基本属性
    /// </summary>
    public interface ITetheredPlayer
    {
        /// <summary>
        /// 获取该附着实例的主人玩家
        /// </summary>
        public abstract Player Owner { get; }
        /// <summary>
        /// 手持物品实例
        /// </summary>
        public abstract Item Item { get; }
        /// <summary>
        /// 玩家左键控制
        /// </summary>
        public abstract bool DownLeft { get; set; }
        /// <summary>
        /// 玩家右键控制
        /// </summary>
        public abstract bool DownRight { get; set; }
        /// <summary>
        /// 获取玩家到鼠标的向量
        /// </summary>
        public abstract Vector2 ToMouse { get; set; }
        /// <summary>
        /// 获取玩家鼠标的位置
        /// </summary>
        public abstract Vector2 InMousePos { get; set; }
        /// <summary>
        /// 获取玩家鼠标的单位向量
        /// </summary>
        public abstract Vector2 UnitToMouseV { get; set; }
        /// <summary>
        /// 获取玩家到鼠标的角度
        /// </summary>
        public abstract float ToMouseA { get; set; }
    }
}
