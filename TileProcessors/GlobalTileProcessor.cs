﻿using Microsoft.Xna.Framework.Graphics;
using Terraria.ModLoader;

namespace InnoVault.TileProcessors
{
    /// <summary>
    /// 对于TP实体的一个全局类，你可以使用它来进行一些统一的操作
    /// </summary>
    public class GlobalTileProcessor
    {
        /// <summary>
        /// 所属的模组
        /// </summary>
        public Mod Mod => TileProcessorLoader.TPGlobal_Type_To_Mod[GetType()];
        /// <summary>
        /// 游戏加载时调用一次
        /// </summary>
        public virtual void Load() {

        }
        /// <summary>
        /// 更新在<see cref="TileProcessor.Update"/>之前，返回<see langword="false"/>可以阻止其运行
        /// </summary>
        /// <returns></returns>
        public virtual bool PreUpdate(TileProcessor tileProcessor) {
            return true;
        }
        /// <summary>
        /// 更新在<see cref="TileProcessor.Update"/>之后
        /// </summary>
        public virtual void PostUpdate(TileProcessor tileProcessor) {

        }
        /// <summary>
        /// 更新在<see cref="TileProcessor.Draw(SpriteBatch)"/>之前，返回<see langword="false"/>可以阻止其运行
        /// </summary>
        /// <returns></returns>
        public virtual bool PreDraw(TileProcessor tileProcessor, SpriteBatch spriteBatch) {
            return true;
        }
        /// <summary>
        /// 更新在<see cref="TileProcessor.Draw(SpriteBatch)"/>之后
        /// </summary>
        public virtual void PostDraw(TileProcessor tileProcessor, SpriteBatch spriteBatch) {

        }
        /// <summary>
        /// 这个TP实体是否应该死亡
        /// 在<see cref="TileProcessor.IsDaed"/>之后运行，返回的值将会覆盖原有的判定
        /// 返回<see langword="null"/>则不会影响原有判定
        /// </summary>
        /// <param name="tileProcessor"></param>
        /// <returns></returns>
        public virtual bool? IsDaed(TileProcessor tileProcessor) {
            return null;
        }
        /// <summary>
        /// 在<see cref="TileProcessor.OnKill"/>后调用，此时<see cref="TileProcessor.Active"/>已经是<see langword="false"/>
        /// </summary>
        public virtual void OnKill(TileProcessor tileProcessor) {

        }
    }
}
