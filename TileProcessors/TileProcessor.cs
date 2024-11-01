using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.IO;
using Terraria;
using Terraria.DataStructures;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;

namespace InnoVault.TileProcessors
{
    /// <summary>
    /// 物块处理器，简称TP实体，它的功能目标类似于<see cref="TileEntity"/>
    /// </summary>
    public abstract class TileProcessor
    {
        #region Data
        /// <summary>
        /// 目标物块ID，一般标记<see cref="Tile.TileType"/>的值
        /// 如果返回0，那么这个实体就不会被系统放置，这意味着你不应该尝试给所有泥土提供一个实体，因为泥土的图格ID是0，但是<see cref="ModContent.TileType{T}"/>的失败返回也是0
        /// </summary>
        public virtual int TargetTileID => -1;
        /// <summary>
        /// 这个物块处理器来自什么模组
        /// </summary>
        public Mod Mod => TileProcessorLoader.TP_Type_To_Mod[GetType()];
        /// <summary>
        /// 这个模块所要跟随的物块结构，如果对象是一个多结构物块，那么这个块一般代表左上角。
        /// 如果这个模块还活跃，那么这个物块的值会每帧更新
        /// </summary>
        public Tile Tile;
        /// <summary>
        /// 如果是玩家中途手动放置的物块所生成的模块，这个值会标记为放置所用的物品
        /// ，否则为<see langword="null"/>，比如进入世界初始化生成的模块
        /// </summary>
        public Item TrackItem;
        /// <summary>
        /// 模块的活跃性，如果是<see langword="false"/>，那么模块将不再进行更新和绘制
        /// ，其在列表<see cref="TileProcessorLoader.TP_InWorld"/>中的实例引用将随时可能被顶替为新的模块
        /// </summary>
        public bool Active;
        /// <summary>
        /// 这个模块在世界中的唯一标签，如果模块不再活跃，它将随时被新加入的模块顶替，顶替的模块将继续使用相同WhoAmI值
        /// </summary>
        public int WhoAmI;
        /// <summary>
        /// 模块的ID，在游戏加载时会给每个模块分配一个唯一的ID值
        /// </summary>
        public int ID => TileProcessorLoader.TP_Type_To_ID[GetType()];
        /// <summary>
        /// 这个模块在世界物块坐标系上的位置，通常等价于所跟随的物块的坐标
        /// </summary>
        public Point16 Position;
        /// <summary>
        /// 这个模块在世界实体坐标系上的位置
        /// </summary>
        public Vector2 PosInWorld => new Vector2(Position.X, Position.Y) * 16;
        #endregion
        /// <summary>
        /// 克隆函数，如果制作的模块类型含有独立的字段，一般要重写克隆函数复制这些字段
        /// </summary>
        /// <returns></returns>
        public virtual TileProcessor Clone() => (TileProcessor)Activator.CreateInstance(GetType());

        /// <summary>
        /// 这个TP实体的网络克隆行为，如果存在特殊的自定义字段，重写这个方法并进行适当的额外克隆以保证网络更新下实体保持正常
        /// </summary>
        /// <param name="writer"></param>
        public virtual void NetCloneSend(ref ModPacket writer) {
            writer.Write(Active);
            writer.Write(WhoAmI);
            writer.WritePoint16(Position);
        }
        /// <summary>
        /// 接受网络TP实体克隆的信息，对应<see cref="NetCloneSend"/>方法的处理，务必保证消息的接收是对应的
        /// </summary>
        /// <param name="reader"></param>
        public virtual void NetCloneRead(BinaryReader reader) {
            Active = reader.ReadBoolean();
            WhoAmI = reader.ReadInt32();
            Position = reader.ReadPoint16();
        }
        /// <summary>
        /// 这个TP实体如果在加载世界时已经存在，这个函数会在加载世界时被调用一次，用以用来初始化一些信息
        /// </summary>
        public virtual void LoadInWorld() { }
        /// <summary>
        /// 在世界被卸载时调用一次
        /// </summary>
        public virtual void UnLoadInWorld() { }
        /// <summary>
        /// 这个模块在世界中的存在数量
        /// </summary>
        /// <returns></returns>
        public int GetInWorldHasNum() => TileProcessorLoader.TP_ID_To_InWorld_Count[ID];
        /// <summary>
        /// 这个函数是单实例的，在一个更新周期中，它只会运行一次，如果<see cref="GetInWorldHasNum"/>返回0，就不会被调用
        /// </summary>
        public virtual void SingleInstanceUpdate() {

        }
        /// <summary>
        /// 这个函数在跟随的物块被挖掘或者消失时自动调用一次，
        /// 调用这个函数，将会让模块变得不活跃，同时运行<see cref="OnKill"/>设置死亡事件
        /// </summary>
        public void Kill() {
            OnKill();
            Active = false;
        }
        /// <summary>
        /// 重写这个函数设置一些特殊的死亡事件或者整理数据逻辑。
        /// 一般不直接调用这个函数而是调用<see cref="Kill"/>
        /// </summary>
        public virtual void OnKill() { }
        /// <summary>
        /// 多物块被挖掘时会运行的函数，输入对于动画帧
        /// </summary>
        /// <param name="frameX"></param>
        /// <param name="frameY"></param>
        public virtual void KillMultiTileSet(int frameX, int frameY) { }
        /// <summary>
        /// 游戏加载时调用
        /// </summary>
        public virtual void Load() { }
        /// <summary>
        /// 游戏卸载时调用
        /// </summary>
        public virtual void UnLoad() { }
        /// <summary>
        /// 在游戏加载末期调用一次，一般用于设置一些静态的值
        /// </summary>
        public virtual void SetStaticProperty() { }
        /// <summary>
        /// 在模块被生成时调用一次，用于初始化一些实例数据
        /// </summary>
        public virtual void SetProperty() { }
        /// <summary>
        /// 会在所有本地客户端、服务端上更新，编写程序时需要考虑网络结构
        /// </summary>
        public virtual void Update() { }
        /// <summary>
        /// 这个模块在什么情况下应该被标记为死亡
        /// </summary>
        /// <returns></returns>
        public virtual bool IsDaed() {
            if (Tile == null) {
                return true;
            }

            if (!Tile.HasTile) {
                return true;
            }

            if (Tile.TileType != TargetTileID) {
                return true;
            }

            return false;
        }
        /// <summary>
        /// 保存这个实体的数据
        /// </summary>
        public virtual void SaveData(TagCompound tag) {

        }
        // <summary>
        /// 加载这个实体的数据
        /// </summary>
        public virtual void LoadData(TagCompound tag) {

        }
        /// <summary>
        /// 一个独立的绘制函数，在<see cref="TileProcessorSystem.PostDrawTiles"/>中统一运行
        /// </summary>
        /// <param name="spriteBatch"></param>
        public virtual void Draw(SpriteBatch spriteBatch) { }
        /// <inheritdoc/>
        public override string ToString() => $"Name:{GetType().Name} \nID:{ID} \nwhoAmi:{WhoAmI}";
    }
}
