using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using Terraria;
using Terraria.ModLoader;

namespace InnoVault.GameSystem
{
    /// <summary>
    /// 覆盖对应物块的行为
    /// </summary>
    public class TileOverride : VaultType<TileOverride>
    {
        /// <summary>
        /// 所有修改的实例集合
        /// </summary>
        public static List<TileOverride> Instances { get; internal set; } = [];
        /// <summary>
        /// 一个字典，可以根据目标ID来获得对应的修改实例
        /// </summary>
        public static Dictionary<int, Dictionary<Type, TileOverride>> ByID { get; internal set; } = [];
        /// <summary>
        /// 目标ID，如果为 -2 则会生效到所有物块上
        /// </summary>
        public virtual int TargetID => -1;
        /// <summary>
        /// 封闭加载
        /// </summary>
        protected override void VaultRegister() {
            if (!CanLoad()) {
                return;
            }

            Instances.Add(this);
        }
        /// <summary>
        /// 加载内容
        /// </summary>
        public override void VaultSetup() {
            if (!CanLoad()) {
                return;
            }

            SetStaticDefaults();

            if (TargetID == -1) {
                return;
            }

            if (TargetID == -2) {
                for (int i = 0; i < TileLoader.TileCount; i++) {
                    //嵌套字典需要提前挖坑
                    ByID.TryAdd(i, []);
                    ByID[i][GetType()] = this;
                }
                return;
            }

            //嵌套字典需要提前挖坑
            ByID.TryAdd(TargetID, []);
            ByID[TargetID][GetType()] = this;
        }
        /// <summary>
        /// 按照 ID 给出对应的物块重载实例
        /// </summary>
        /// <param name="id"></param>
        /// <param name="tileOverrides"></param>
        /// <returns></returns>
        public static bool TryFetchByID(int id, out Dictionary<Type, TileOverride> tileOverrides)
            => ByID.TryGetValue(id, out tileOverrides);
        /// <summary>
        /// 是否掉落物品
        /// </summary>
        /// <param name="i"></param>
        /// <param name="j"></param>
        /// <param name="type"></param>
        /// <returns></returns>
        public virtual bool? CanDrop(int i, int j, int type) => null;
        /// <summary>
        /// 修改右键物块的行为
        /// </summary>
        /// <param name="i"></param>
        /// <param name="j"></param>
        /// <param name="tile"></param>
        /// <returns></returns>
        public virtual bool? RightClick(int i, int j, Tile tile) => null;
        /// <summary>
        /// 鼠标悬停在上方的行为
        /// </summary>
        /// <param name="i"></param>
        /// <param name="j"></param>
        public virtual void MouseOver(int i, int j) { }
        /// <summary>
        /// 修改物块的绘制
        /// </summary>
        /// <param name="i"></param>
        /// <param name="j"></param>
        /// <param name="type"></param>
        /// <param name="spriteBatch"></param>
        /// <returns></returns>
        public virtual bool? PreDraw(int i, int j, int type, SpriteBatch spriteBatch) => null;
    }
}
