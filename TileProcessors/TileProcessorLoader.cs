using Microsoft.Xna.Framework;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;
using static InnoVault.VaultNetWork;

namespace InnoVault.TileProcessors
{
    /// <summary>
    /// 关于TP系统的大部分逻辑与钩子挂载与此处
    /// </summary>
    public class TileProcessorLoader : GlobalTile, IVaultLoader
    {
        #region Data
        /// <summary>
        /// 在世界中的Tile模块的最大存在数量
        /// </summary>
        public const int MaxTileModuleInWorldCount = 1000;
        /// <summary>
        /// 所有Tile模块的列表该列表在加载时初始化，并包含所有Tile模块的实例
        /// </summary>
        public static List<TileProcessor> TP_Instances { get; private set; } = [];
        /// <summary>
        /// 当前世界中的Tile模块列表此列表在世界加载和操作时动态更新
        /// </summary>
        public static List<TileProcessor> TP_InWorld { get; internal set; } = [];
        /// <summary>
        /// 将Tile模块的类型映射到其对应的ID的字典
        /// </summary>
        public static Dictionary<Type, int> TP_Type_To_ID { get; private set; } = [];
        /// <summary>
        /// 将Tile模块的类型映射到模块实例的字典
        /// </summary>
        public static Dictionary<Type, TileProcessor> TP_Type_To_Instance { get; private set; } = [];
        /// <summary>
        /// 记录当前世界中每个模块ID对应的Tile模块数量
        /// </summary>
        public static Dictionary<int, int> TP_ID_To_InWorld_Count { get; internal set; } = [];
        /// <summary>
        /// 将模块ID映射到模块实例的字典
        /// </summary>
        public static Dictionary<int, TileProcessor> TP_ID_To_Instance { get; private set; } = [];
        /// <summary>
        /// 将对应的Type映射到所属模组
        /// </summary>
        public static Dictionary<Type, Mod> TP_Type_To_Mod { get; private set; } = [];
        /// <summary>
        /// 将目标Tile的ID映射到模块实例的字典
        /// </summary>
        public static Dictionary<int, List<TileProcessor>> TargetTile_To_TPInstance { get; private set; } = [];

        private static Type tileLoaderType;
        private static MethodBase onTile_KillMultiTile_Method;
        private delegate void On_Tile_KillMultiTile_Dalegate(int i, int j, int frameX, int frameY, int type);
        #endregion
        void IVaultLoader.LoadData() {
            TP_Instances = VaultUtils.HanderSubclass<TileProcessor>();
            foreach (var module in TP_Instances) {
                module.Load();
                VaultUtils.AddTypeModAssociation(TP_Type_To_Mod, module.GetType(), ModLoader.Mods);
            }

            tileLoaderType = typeof(TileLoader);
            onTile_KillMultiTile_Method = tileLoaderType.GetMethod("KillMultiTile", BindingFlags.Public | BindingFlags.Static);

            VaultHook.Add(onTile_KillMultiTile_Method, OnKillMultiTileHook);

            WorldGen.Hooks.OnWorldLoad += LoadWorldTileProcessor;
        }

        void IVaultLoader.SetupData() {
            for (int i = 0; i < TP_Instances.Count; i++) {
                TileProcessor module = TP_Instances[i];
                try {
                    module.SetStaticProperty();
                } catch {
                    string errorText = nameof(module) + ": 在进行 SetStaticProperty 时发生了错误，但被跳过";
                    string errorText2 = nameof(module) + ": An error occurred while performing SetStaticProperty, but it was skipped";
                    VaultMod.Instance.Logger.Info(VaultUtils.Translation(errorText, errorText2));
                }

                TP_Type_To_ID.Add(module.GetType(), i);
                TP_Type_To_Instance.Add(module.GetType(), module);
                TP_ID_To_Instance.Add(module.ID, module);
                TP_ID_To_InWorld_Count.Add(module.ID, 0);

                //这里的添加会稍微复杂些
                //如果没有获取到值，说明键刚被创建，这里就执行值序列的创建与初始化，并添加进第一个值
                if (!TargetTile_To_TPInstance.TryGetValue(module.TargetTileID, out List<TileProcessor> tps)) {
                    tps = new List<TileProcessor>();
                    TargetTile_To_TPInstance[module.TargetTileID] = tps;
                }
                //如果成功获取到了值，那么说明已经有了重复的键被创建在列表中，这里就执行一次值扩容
                tps.Add(module);
            }
        }

        void IVaultLoader.UnLoadData() {
            foreach (var module in TP_Instances) {
                module.UnLoad();
            }

            TP_Instances.Clear();
            TP_Type_To_ID.Clear();
            TP_Type_To_Instance.Clear();
            TP_ID_To_Instance.Clear();
            TP_ID_To_InWorld_Count.Clear();
            TP_Type_To_Mod.Clear();
            TargetTile_To_TPInstance.Clear();
            tileLoaderType = null;
            onTile_KillMultiTile_Method = null;

            WorldGen.Hooks.OnWorldLoad -= LoadWorldTileProcessor;
        }

        private static void OnKillMultiTileHook(On_Tile_KillMultiTile_Dalegate orig, int i, int j, int frameX, int frameY, int type) {
            foreach (var module in TP_InWorld) {
                if (!module.Active) {
                    continue;
                }
                module.KillMultiTileSet(frameX, frameY);
            }
            orig.Invoke(i, j, frameX, frameY, type);
        }

        /// <summary>
        /// 向世界中的模块列表添加一个新的 TileModule
        /// </summary>
        /// <param name="tileID">要添加的 Tile 的 ID</param>
        /// <param name="position">该模块的左上角位置</param>
        /// <param name="item">用于跟踪该模块的物品，可以为 null</param>
        /// <remarks>
        /// 该方法会首先尝试从 <see cref="TP_Type_To_ID"/> 获取对应的模块，然后克隆该模块并设置其位置、跟踪物品和激活状态
        /// 如果有空闲的模块槽位，会将新模块放入该槽位，否则会添加到列表的末尾
        /// </remarks>
        public static void AddInWorld(int tileID, Point16 position, Item item) {
            if (TargetTile_To_TPInstance.TryGetValue(tileID, out List<TileProcessor> processorList)) {
                foreach (var processor in processorList) {
                    TileProcessor newProcessor = processor.Clone();
                    newProcessor.Position = position;
                    newProcessor.TrackItem = item;
                    newProcessor.Active = true;
                    newProcessor.SetProperty();

                    bool add = true;
                    for (int i = 0; i < TP_InWorld.Count; i++) {
                        if (!TP_InWorld[i].Active) {
                            newProcessor.WhoAmI = TP_InWorld[i].WhoAmI;
                            TP_InWorld[i] = newProcessor;
                            add = false;
                            break;
                        }
                    }

                    if (add && TP_InWorld.Count < MaxTileModuleInWorldCount) {
                        newProcessor.WhoAmI = TP_InWorld.Count;
                        TP_InWorld.Add(newProcessor);
                    }
                }
            }
        }

        /// <summary>
        /// 加载世界中的所有 TileModule，初始化和激活它们
        /// </summary>
        /// <remarks>
        /// 此方法会首先移除不再活跃的模块，然后扫描整个世界的每一个 Tile，
        /// 识别出多结构物块的左上角 Tile，并将其添加到世界中的模块列表中
        /// 最后，加载所有处于激活状态的模块
        /// </remarks>
        public static void LoadWorldTileProcessor() {
            TP_InWorld = [];

            for (int x = 0; x < Main.tile.Width; x++) {
                for (int y = 0; y < Main.tile.Height; y++) {
                    Tile tile = Main.tile[x, y];
                    if (tile != null && tile.HasTile && VaultUtils.IsTopLeft(x, y, out Point16 point)) {
                        AddInWorld(tile.TileType, point, null);
                    }
                }
            }

            foreach (TileProcessor module in TP_InWorld) {
                if (!module.Active) {
                    continue;
                }
                module.LoadInWorld();
            }
        }

        /// <inheritdoc/>
        public override void PlaceInWorld(int i, int j, int type, Item item) {
            if (VaultUtils.SafeGetTopLeft(i, j, out Point16 point)) {
                AddInWorld(type, point, item);
                if (VaultUtils.isClient) {
                    NetSend(Mod, type, point);
                }
            }
        }
        /// <inheritdoc/>
        public static void NetSend(Mod mod, int type, Point16 point) {
            // 客户端发送同步请求到服务器
            ModPacket packet = mod.GetPacket();
            packet.Write((byte)VaultNetWork.MessageType.PlaceInWorldSync);
            packet.Write(type);
            packet.WritePoint16(point);
            packet.Send(); //发送到服务器
        }
        /// <inheritdoc/>
        public static void NetReceive(Mod mod, BinaryReader reader, int whoAmI) {
            // 读取放置方块的数据
            int tileType = reader.ReadInt32();
            Point16 point = reader.ReadPoint16();
            AddInWorld(tileType, point, null);
            if (Main.netMode == NetmodeID.Server) {
                ModPacket packet = mod.GetPacket();
                packet.Write((byte)MessageType.PlaceInWorldSync);
                packet.Write(tileType);
                packet.WritePoint16(point);
                packet.Send(-1, whoAmI); //广播给所有其他客户端
            }
        }

        /// <summary>
        /// 根据指定类型获取对应的模块ID
        /// </summary>
        /// <param name="type">模块的类型</param>
        /// <returns>返回该类型对应的模块ID</returns>
        public static int GetModuleID(Type type) => TP_Type_To_ID[type];
        /// <summary>
        /// 使用精确搜索查找与指定ID及坐标对应的模块，并将其转换为指定类型的模块
        /// </summary>
        /// <typeparam name="T">要返回的模块的类型，必须继承自 <see cref="TileProcessor"/></typeparam>
        /// <param name="ID">要查找的模块的ID</param>
        /// <param name="x">要查找的模块的x坐标</param>
        /// <param name="y">要查找的模块的y坐标</param>
        /// <returns>返回与指定ID及坐标对应的模块，如果未找到则返回<see langword="null"/></returns>
        public static T FindModulePreciseSearch<T>(int ID, int x, int y) where T : TileProcessor => FindModulePreciseSearch(ID, x, y) as T;
        /// <summary>
        /// 使用精确搜索查找与指定ID及坐标对应的模块
        /// </summary>
        /// <param name="ID">要查找的模块的ID</param>
        /// <param name="x">要查找的模块的x坐标</param>
        /// <param name="y">要查找的模块的y坐标</param>
        /// <returns>返回与指定ID及坐标对应的 <see cref="TileProcessor"/>，如果未找到则返回<see langword="null"/></returns>
        public static TileProcessor FindModulePreciseSearch(int ID, int x, int y) {
            TileProcessor module = null;
            // 判断坐标是否为多结构物块的左上角，并获取其左上角位置
            if (VaultUtils.IsTopLeft(x, y, out var point)) {
                // 遍历世界中的所有模块，查找与指定ID和坐标匹配的模块
                foreach (var inds in TP_InWorld) {
                    if (inds.Position.X == point.X && inds.Position.Y == point.Y && inds.ID == ID) {
                        module = inds;
                        break;
                    }
                }
            }
            return module;
        }
        /// <summary>
        /// 在指定范围内查找与指定ID和坐标最接近的模块，并将其转换为指定类型的模块
        /// </summary>
        /// <typeparam name="T">要返回的模块的类型，必须继承自 <see cref="TileProcessor"/></typeparam>
        /// <param name="ID">要查找的模块的ID</param>
        /// <param name="x">要查找的模块的x坐标</param>
        /// <param name="y">要查找的模块的y坐标</param>
        /// <param name="maxFindLeng">搜索范围的最大距离</param>
        /// <returns>返回与指定ID及坐标最接近的模块，如果未找到则返回<see langword="null"/></returns>
        public static T FindModuleRangeSearch<T>(int ID, int x, int y, int maxFindLeng)
            where T : TileProcessor => FindModuleRangeSearch(ID, x, y, maxFindLeng) as T;
        /// <summary>
        /// 在指定范围内查找与指定ID和坐标最接近的模块
        /// </summary>
        /// <param name="ID">要查找的模块的ID</param>
        /// <param name="x">要查找的模块的x坐标</param>
        /// <param name="y">要查找的模块的y坐标</param>
        /// <param name="maxFindLeng">搜索范围的最大距离</param>
        /// <returns>返回与指定ID及坐标最接近的 <see cref="TileProcessor"/>，如果未找到则返回<see langword="null"/></returns>
        public static TileProcessor FindModuleRangeSearch(int ID, int x, int y, int maxFindLeng) {
            TileProcessor module = null;
            float findValue = maxFindLeng;
            // 遍历世界中的所有模块，查找与指定ID匹配并且距离最近的模块
            foreach (var inds in TP_InWorld) {
                if (inds.ID != ID) {
                    continue;
                }
                // 计算当前模块与指定坐标之间的距离
                float value = inds.PosInWorld.To(new Vector2(x, y) * 16).Length();
                if (value > findValue) {
                    continue;
                }
                // 更新最接近的模块及其距离
                module = inds;
                findValue = value;
            }
            return module;
        }
    }
}
