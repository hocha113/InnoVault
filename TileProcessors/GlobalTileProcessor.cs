﻿using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.DataStructures;

namespace InnoVault.TileProcessors
{
    /// <summary>
    /// 对于TP实体的一个全局类，你可以使用它来进行一些统一的操作
    /// </summary>
    public abstract class GlobalTileProcessor : VaultType<GlobalTileProcessor>
    {
        /// <summary>
        /// 封闭内容
        /// </summary>
        protected sealed override void VaultRegister() => TileProcessorLoader.TPGlobalHooks.Add(this);
        /// <summary>
        /// 加载内容
        /// </summary>
        public sealed override void VaultSetup() => SetStaticDefaults();

        /// <summary>
        /// 用于初始化一些次要信息，只会在实体生成时调用一次
        /// </summary>
        /// <param name="tileProcessor"></param>
        public virtual void Initialize(TileProcessor tileProcessor) {

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
        /// 这个函数是单实例的，在一个更新周期中，它只会运行一次，即使<see cref="TileProcessor.GetInWorldHasNum"/>返回0，也会被调用<br/>
        /// 运行在<see cref="TileProcessor.SingleInstanceUpdate"/>之前，返回<see langword="false"/>可以阻止其运行
        /// </summary>
        public virtual bool PreSingleInstanceUpdate(TileProcessor tileProcessor) {
            return true;
        }

        /// <summary>
        /// 这个函数是单实例的，在一个更新周期中，它只会运行一次，如果<see cref="TileProcessor.GetInWorldHasNum"/>返回0，就不会被调用
        /// </summary>
        public virtual void SingleInstanceUpdate(TileProcessor tileProcessor) {

        }

        /// <summary>
        /// 获取多结构物块的放置原点坐标。如果希望系统使用原生判定，返回<see langword="null"/>，
        /// 若需要自定义放置原点，则返回一个有效的坐标值
        /// 如果改动了这个，务必注重<see cref="TryIsTopLeftPoint"/>的逻辑判定，两者具有强配合性
        /// </summary>
        /// <param name="x">物块的横坐标</param>
        /// <param name="y">物块的纵坐标</param>
        /// <returns>返回放置原点坐标或<see langword="null"/></returns>
        public virtual Point16? GetTopLeftPoint(int x, int y) {
            return null;
        }

        /// <summary>
        /// 判定该位置是否是物块的左上角。如果希望系统使用原生判定，返回<see langword="null"/>，
        /// 若需要自定义放置原点，则返回一个有效的坐标值
        /// 如果改动了这个，务必注重<see cref="GetTopLeftPoint"/>的逻辑判定，两者具有强配合性
        /// </summary>
        /// <param name="x"></param>
        /// <param name="y"></param>
        /// <param name="position"></param>
        /// <returns></returns>
        public virtual bool? TryIsTopLeftPoint(int x, int y, out Point16 position) {
            position = default;
            return null;
        }

        /// <summary>
        /// 获取给定坐标的物块左上角位置，并判断该位置是否为多结构物块的左上角
        /// 返回<see langword="null"/>即不覆盖后续的值，反之亦然
        /// </summary>
        /// <param name="tile">便捷的获取目标物块的实例，等价于 Framing.GetTileSafely(i, j) </param>
        /// <param name="i">目标物块的横坐标</param>
        /// <param name="j">目标物块的纵坐标</param>
        /// <returns></returns>
        public virtual Point16? GetTopLeftOrNull(Tile tile, int i, int j) {
            return null;
        }

        /// <summary>
        /// 更新在所有实例的<see cref="TileProcessor.PreTileDraw(SpriteBatch)"/>之前，返回<see langword="false"/>可以阻止其运行
        /// </summary>
        /// <returns></returns>
        public virtual bool PreTileDrawEverything(SpriteBatch spriteBatch) {
            return true;
        }

        /// <summary>
        /// 更新在所有实例的<see cref="TileProcessor.Draw(SpriteBatch)"/>之前，返回<see langword="false"/>可以阻止其运行<br/>
        /// 画布此时已经关闭，如果要进行绘制，需要自行设置画布开启
        /// </summary>
        /// <returns></returns>
        public virtual bool PreDrawEverything(SpriteBatch spriteBatch) {
            return true;
        }

        /// <summary>
        /// 更新在<see cref="TileProcessor.PreTileDraw(SpriteBatch)"/>之前，返回<see langword="false"/>可以阻止其运行
        /// </summary>
        /// <returns></returns>
        public virtual bool PreTileDraw(TileProcessor tileProcessor, SpriteBatch spriteBatch) {
            return true;
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
        /// 更新在所有实例的<see cref="TileProcessor.Draw(SpriteBatch)"/>之后<br/>
        /// 画布此时已经关闭，如果要进行绘制，需要自行设置画布开启
        /// </summary>
        public virtual void PostDrawEverything(SpriteBatch spriteBatch) {

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
