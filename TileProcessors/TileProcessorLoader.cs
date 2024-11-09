using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Terraria;
using Terraria.DataStructures;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using static InnoVault.VaultNetWork;

namespace InnoVault.TileProcessors
{
    /// <summary>
    /// 关于TP系统的大部分逻辑与钩子挂载与此处
    /// </summary>
    public sealed class TileProcessorLoader : GlobalTile, IVaultLoader
    {
        #region Data
        private static readonly string key_TPData_TagList = "TPData_TagList";
        
        /// <summary>
        /// 在世界中的Tile模块的最大存在数量
        /// </summary>
        public const int MaxTileModuleInWorldCount = 1000;
        /// <summary>
        /// 当前世界的数据
        /// </summary>
        public static TagCompound ActiveWorldTagData;
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
        /// 将<see cref="TileProcessor"/>对应的Type映射到所属模组
        /// </summary>
        public static Dictionary<Type, Mod> TP_Type_To_Mod { get; private set; } = [];
        /// <summary>
        /// 将<see cref="GlobalTileProcessor"/>对应的Type映射到所属模组
        /// </summary>
        public static Dictionary<Type, Mod> TPGlobal_Type_To_Mod { get; private set; } = [];
        /// <summary>
        /// 将目标Tile的ID映射到模块实例的字典
        /// </summary>
        public static Dictionary<int, List<TileProcessor>> TargetTile_To_TPInstance { get; private set; } = [];
        /// <summary>
        /// 关于目标物块键的哈希列表
        /// </summary>
        public static HashSet<int> targetTileTypes { get; private set; } = [];
        /// <summary>
        /// 所有的<see cref="GlobalTileProcessor"/>实例在此处储存
        /// </summary>
        internal static List<GlobalTileProcessor> TPGlobalHooks { get; private set; } = [];

        private static Type tileLoaderType;
        private static MethodBase onTile_KillMultiTile_Method;
        private delegate void On_Tile_KillMultiTile_Dalegate(int i, int j, int frameX, int frameY, int type);
        #endregion

        void IVaultLoader.LoadData() {
            TP_Instances = VaultUtils.GetSubclassInstances<TileProcessor>();
            foreach (var tpInds in TP_Instances) {
                VaultUtils.AddTypeModAssociation(TP_Type_To_Mod, tpInds.GetType(), ModLoader.Mods);
                tpInds.Load();
            }

            TPGlobalHooks = VaultUtils.GetSubclassInstances<GlobalTileProcessor>();
            foreach (var tpGlobal in TPGlobalHooks) {
                VaultUtils.AddTypeModAssociation(TPGlobal_Type_To_Mod, tpGlobal.GetType(), ModLoader.Mods);
                tpGlobal.Load();
            }

            tileLoaderType = typeof(TileLoader);
            onTile_KillMultiTile_Method = tileLoaderType.GetMethod("KillMultiTile", BindingFlags.Public | BindingFlags.Static);
            //实际上我并不信任MonoModHook, 但考虑到稳定性和代码必要性，外加OnKillMultiTileHook这个钩子如果突然失效并不致命，我选择再次使用MonoModHook，祈祷它不会再让钩子被回收
            MonoModHooks.Add(onTile_KillMultiTile_Method, OnKillMultiTileHook);

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
            targetTileTypes = new HashSet<int>(TargetTile_To_TPInstance.Keys);
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
            ActiveWorldTagData = null;

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
            if (tileID == 0 || TP_InWorld.Count >= MaxTileModuleInWorldCount) {//是的，我们拒绝泥土
                return;
            }

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

                    if (add) {
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
                    if (tile == null || !tile.HasTile) {
                        continue;
                    }

                    if (!targetTileTypes.Contains(tile.TileType)) {
                        continue;
                    }
                    if (TileProcessorIsTopLeft(x, y, out Point16 point)) {
                        AddInWorld(tile.TileType, point, null);
                    }
                }
            }

            if (ActiveWorldTagData != null) {
                LoadWorldData(ActiveWorldTagData);
            }

            foreach (TileProcessor module in TP_InWorld) {
                if (!module.Active) {
                    continue;
                }
                module.LoadInWorld();
            }
        }

        /// <inheritdoc/>
        internal static void LoadWorldData(TagCompound tag) {
            if (!tag.ContainsKey(key_TPData_TagList)) {
                return;
            }
            IList<TagCompound> list = tag.GetList<TagCompound>(key_TPData_TagList);
            // 将 TP_InWorld 转化为一个字典，以便快速查找
            Dictionary<(string, string, Point16), TileProcessor> tpDictionary = new Dictionary<(string, string, Point16), TileProcessor>();
            //VaultMod.Instance.Logger.Info(TP_InWorld.ToString() + " Load Count: " + TP_InWorld.Count);
            foreach (TileProcessor tp in TP_InWorld) {
                if (tp != null) {
                    tpDictionary[(tp.Mod.Name, tp.GetType().Name, tp.Position)] = tp;
                }
            }
            // 遍历标签列表并在字典中查找匹配的 TileProcessor
            foreach (TagCompound thisTag in list) {
                if (!thisTag.ContainsKey("data")) {
                    continue;
                }
                string modName = thisTag.GetString("mod");
                string name = thisTag.GetString("name");
                Point16 point = new Point16(thisTag.GetShort("X"), thisTag.GetShort("Y"));
                // 从字典中查找匹配项
                if (tpDictionary.TryGetValue((modName, name, point), out TileProcessor tp)) {
                    tp.LoadData(thisTag.GetCompound("data"));
                }
            }
        }

        /// <inheritdoc/>
        internal static void SaveWorldData(TagCompound tag) {
            List<TagCompound> list = new List<TagCompound>();
            TagCompound saveData = new TagCompound();
            //VaultMod.Instance.Logger.Info(TP_InWorld.ToString() + " Save Count: " + TP_InWorld.Count);
            foreach (TileProcessor tp in TP_InWorld) {
                if (tp == null || !tp.Active) {
                    continue;
                }
                tp.SaveData(saveData);
                TagCompound thisTag = new TagCompound {
                    ["mod"] = tp?.Mod.Name,
                    ["name"] = tp?.GetType().Name,
                    ["X"] = tp.Position.X,
                    ["Y"] = tp.Position.Y
                };
                if (saveData.Count != 0) {
                    thisTag["data"] = saveData;
                    saveData = new TagCompound();
                }
                list.Add(thisTag);
            }
            tag[key_TPData_TagList] = list;
            ActiveWorldTagData = tag;
            if (Main.dedServ) {
                ServerRecovery_TPData(-1);
            }
        }

        #region NetWork

        /// <summary>
        /// 发送数据
        /// </summary>
        public static void TileProcessorSendData(TileProcessor tp) {
            if (VaultUtils.isSinglePlayer) {
                return;
            }
            //$"{LoadenName}-SendData: 正在发送数据".LoggerDomp();
            ModPacket modPacket = VaultMod.Instance.GetPacket();
            modPacket.Write((byte)MessageType.TPNetWork);
            modPacket.Write(tp.LoadenName);
            modPacket.WritePoint16(tp.Position);
            tp.SendData(modPacket);
            modPacket.Send();
        }

        /// <summary>
        /// 接收数据
        /// </summary>
        public static void TileProcessorReceiveData(BinaryReader reader, int whoAmI) {
            //"TileProcessorLoader-ReceiveData:正在接收数据".LoggerDomp();
            Dictionary<string, object> data = [];
            string name = reader.ReadString();
            Point16 position = reader.ReadPoint16();
            TileProcessor tileProcessor = null;
            foreach (TileProcessor tp in TP_InWorld) {
                if (tp.LoadenName == name && tp.Position == position) {
                    tileProcessor = tp;
                    tileProcessor.ReceiveData(reader, whoAmI);
                }
            }
            if (tileProcessor != null) {
                if (Main.dedServ) {
                    ModPacket modPacket = VaultMod.Instance.GetPacket();
                    modPacket.Write((byte)MessageType.TPNetWork);
                    modPacket.Write(name);
                    modPacket.WritePoint16(position);
                    tileProcessor.SendData(modPacket);
                    modPacket.Send(-1, whoAmI);
                }
            }
            else {
                throw new Exception($"TileProcessorLoader-ReceiveData: No Corresponding TileProcessor Instance Found : {name}-position[{position}]");
            }
        }

        //服务端是唯一的，即使是主机本质上也只是在本机上托管运行了一个服务端
        //这意味者，如果进入多人模式，这个玩家的客户端是加载于服务端之后的，这里的延时发包将不能解决所有情景下的问题，只能让作为主机的玩家接收到来自服务器的发包信息
        //其他更晚加入的客户端依旧接收不到发包
        //之所以将延时加长到30后主机能接收到发包信息，是因为主机的客户端的加载完成时间于其所托管的服务端是接近的
        //可能只有2~5帧的延迟，所以这个sendTime设置在绝大多数情况下可以让主机客户端吃到发包，但其他客户端则大概率不行
        //对此我给出的解决方案是，让每个客户端在加入后手动向服务器请求一次TP信息，由客户端发送一个代表数据索取的包，服务器收到后向该客户端发送正确的数据并让其接收

        /// <summary>
        /// 让客户端向服务器发出数据请求，请求一个完整的TP数据链
        /// </summary>
        public static void ClientRequest_TPData_Send() {
            if (!VaultUtils.isClient) {
                return;
            }
            //$"TileProcessorLoader-ClientRequest_TPData:玩家{Main.LocalPlayer.name}/客户端id{Main.myPlayer}正在请求服务端TP数据链".LoggerDomp();
            ModPacket modPacket = VaultMod.Instance.GetPacket();
            modPacket.Write((byte)MessageType.ClientRequest_TPData_Send);
            modPacket.Send();
        }
        /// <summary>
        /// 向指定客户端发送一个完整的TP数据链
        /// </summary>
        public static void ServerRecovery_TPData(int whoAmI) {
            if (!Main.dedServ) {
                return;
            }
            //"TileProcessorLoader-ServerRecovery_TPData:服务器数据正在响应请求".LoggerDomp();
            ModPacket modPacket = VaultMod.Instance.GetPacket();// 创建一个数据包，用于批量发送多个TP数据
            modPacket.Write((byte)MessageType.Handle_TPData_Receive); // 包类型
            int sendTPCount = 0;

            // 统计活跃的TP数量
            foreach (TileProcessor tp in TP_InWorld) {
                if (!tp.Active || !tp.LoadenWorldSendData) {
                    continue;
                }
                sendTPCount++;
            }
            modPacket.Write(sendTPCount); // 写入活跃的TP数量

            // 发送每个活跃的TP数据
            foreach (TileProcessor tp in TP_InWorld) {
                if (!tp.Active || !tp.LoadenWorldSendData) {
                    continue;
                }

                // 标记节点开始
                modPacket.Write("TP_START"); // 添加分隔标签
                modPacket.Write(tp.LoadenName);
                modPacket.WritePoint16(tp.Position);

                // 发送TileProcessor数据
                tp.SendData(modPacket);
            }

            modPacket.Send(whoAmI); // 将数据包发送给客户端
        }
        /// <summary>
        /// 服务端响应TP数据链的请求后，接收数据
        /// </summary>
        public static void Handle_TPData_Receive(BinaryReader reader) {
            if (!VaultUtils.isClient) {
                return;
            }

            int tpCount = reader.ReadInt32(); // 读取TP数量
            Dictionary<(string, Point16), TileProcessor> tpDictionary = GetTileProcessorDictionaryByNameAndPosition();

            for (int i = 0; i < tpCount; i++) {
                string marker = reader.ReadString();

                // 确认是否为有效的起始标记
                if (marker != "TP_START") {
                    $"TileProcessorLoader-ClientRequest_TPData_Receive: 无效的标记 {marker}，跳过至下一个节点".LoggerDomp();
                    continue;
                }

                string name = reader.ReadString();
                Point16 position = reader.ReadPoint16();

                if (tpDictionary.TryGetValue((name, position), out TileProcessor tp)) {
                    tp.ReceiveData(reader, -1);
                }
                else {
                    // 跳过该TileProcessor的数据
                    $"TileProcessorLoader-ClientRequest_TPData_Receive: 未找到对应的TileProcessor实例: {name}-位置[{position}]，跳过".LoggerDomp();
                    SkipToNextMarker(reader);
                }
            }
        }

        /// <summary>
        /// 跳到下一个标记节点
        /// </summary>
        private static void SkipToNextMarker(BinaryReader reader) {
            while (reader.BaseStream.Position < reader.BaseStream.Length) {
                string marker = reader.ReadString();
                if (marker == "TP_START") {
                    // 回退以便下一个处理块能够正确读取标记
                    reader.BaseStream.Position -= marker.Length;
                    break;
                }
            }
        }

        #endregion

        #region Utils

        /// <inheritdoc/>
        internal static bool? TileProcessorPlaceInWorldTryIsTopLeftPoint(int i, int j, out Point16 position) {
            bool? reset = null;
            position = default;
            foreach (var tpGlobal in TPGlobalHooks) {
                reset = tpGlobal.TryIsTopLeftPoint(i, j, out position);
            }
            return reset;
        }

        /// <summary>
        /// 判断给定坐标是否为多结构物块的左上角位置，并输出左上角的坐标
        /// 会考虑到<see cref="TileProcessorPlaceInWorldTryIsTopLeftPoint"/>的修改
        /// </summary>
        /// <param name="i">物块的x坐标</param>
        /// <param name="j">物块的y坐标</param>
        /// <param name="point">输出的左上角坐标，如果不是左上角则为(0,0)</param>
        /// <returns>如果是左上角，返回true，否则返回<see langword="false"/></returns>
        public static bool TileProcessorIsTopLeft(int i, int j, out Point16 point) {
            bool flag = VaultUtils.IsTopLeft(i, j, out point);
            bool? gflag = TileProcessorPlaceInWorldTryIsTopLeftPoint(i, j, out Point16 gpoint);
            if (gflag.HasValue) {
                point = gpoint;
                flag = gflag.Value;
            }

            return flag;
        }

        /// <inheritdoc/>
        internal static Point16? TileProcessorPlaceInWorldGetTopLeftPoint(int i, int j) {
            Point16? point16 = null;
            foreach (var tpGlobal in TPGlobalHooks) {
                point16 = tpGlobal.GetTopLeftPoint(i, j);
            }
            return point16;
        }

        /// <summary>
        /// 安全的获取多结构物块左上角的位置，给定一个物块坐标，自动寻找到该坐标对应的左上原点位置输出
        /// 会考虑到<see cref="TileProcessorPlaceInWorldGetTopLeftPoint"/>的修改
        /// </summary>
        /// <param name="i">物块的x坐标</param>
        /// <param name="j">物块的y坐标</param>
        /// <param name="point">输出的左上角坐标</param>
        /// <returns>如果没能找到，则输出(0,0)，并返回<see langword="false"/></returns>
        public static bool TileProcessorSafeGetTopLeft(int i, int j, out Point16 point) {
            bool flag = VaultUtils.SafeGetTopLeft(i, j, out point);
            Point16? gpoint = TileProcessorPlaceInWorldGetTopLeftPoint(i, j);
            if (gpoint.HasValue) {
                point = gpoint.Value;
                flag = true;
            }

            return flag;
        }

        /// <inheritdoc/>
        public override void PlaceInWorld(int i, int j, int type, Item item) {
            if (TileProcessorSafeGetTopLeft(i, j, out Point16 point)) {
                AddInWorld(type, point, item);
                if (VaultUtils.isClient) {
                    PlaceInWorldNetSend(Mod, type, point);
                }
            }
        }
        /// <inheritdoc/>
        public static void PlaceInWorldNetSend(Mod mod, int type, Point16 point) {
            // 客户端发送同步请求到服务器
            ModPacket packet = mod.GetPacket();
            packet.Write((byte)MessageType.PlaceInWorldSync);
            packet.Write(type);
            packet.WritePoint16(point);
            packet.Send(); //发送到服务器
        }
        /// <inheritdoc/>
        internal static void PlaceInWorldNetReceive(Mod mod, BinaryReader reader, int whoAmI) {
            // 读取放置方块的数据
            int tileType = reader.ReadInt32();
            Point16 point = reader.ReadPoint16();
            AddInWorld(tileType, point, null);
            if (VaultUtils.isServer) {
                ModPacket packet = mod.GetPacket();
                packet.Write((byte)MessageType.PlaceInWorldSync);
                packet.Write(tileType);
                packet.WritePoint16(point);
                packet.Send(-1, whoAmI); //广播给所有其他客户端
            }
        }

        /// <summary>
        /// 将当前世界的<see cref="TP_InWorld"/>转化为一个字典列表
        /// </summary>
        /// <returns></returns>
        public static Dictionary<(string, Point16), TileProcessor> GetTileProcessorDictionaryByNameAndPosition() {
            Dictionary<(string, Point16), TileProcessor> tpDictionary = new Dictionary<(string, Point16), TileProcessor>();
            foreach (TileProcessor tp in TP_InWorld) {
                if (tp != null) {
                    tpDictionary[(tp.LoadenName, tp.Position)] = tp;
                }
            }
            return tpDictionary;
        }

        /// <summary>
        /// 根据指定类型获取对应的模块ID
        /// </summary>
        /// <returns>返回该类型对应的模块ID</returns>
        public static int GetModuleID<T>() where T : TileProcessor => TP_Type_To_ID[typeof(T)];
        /// <summary>
        /// 根据指定类型获取对应的模块ID
        /// </summary>
        /// <param name="type">模块的类型</param>
        /// <returns>返回该类型对应的模块ID</returns>
        public static int GetModuleID(Type type) => TP_Type_To_ID[type];

        /// <summary>
        /// 根据点来寻找对于的TP实体实例
        /// </summary>
        /// <param name="position"></param>
        /// <param name="tileProcessor"></param>
        /// <returns></returns>
        public static bool ByPositionGetTP<T>(Point16 position, out T tileProcessor) where T : TileProcessor {
            tileProcessor = FindModulePreciseSearch<T>(position.X, position.Y);
            return tileProcessor != null;
        }

        /// <summary>
        /// 使用精确搜索查找与指定ID及坐标对应的模块，并将其转换为指定类型的模块
        /// </summary>
        /// <typeparam name="T">要返回的模块的类型，必须继承自 <see cref="TileProcessor"/></typeparam>
        /// <param name="x">要查找的模块的x坐标</param>
        /// <param name="y">要查找的模块的y坐标</param>
        /// <returns>返回与指定ID及坐标对应的模块，如果未找到则返回<see langword="null"/></returns>
        public static T FindModulePreciseSearch<T>(int x, int y) where T : TileProcessor => FindModulePreciseSearch(GetModuleID<T>(), x, y) as T;
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
            if (TileProcessorIsTopLeft(x, y, out Point16 point)) {
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
        /// 使用精确搜索查找与指定ID及坐标对应的模块，并将其转换为指定类型的模块
        /// </summary>
        /// <param name="id">目标id</param>
        /// <param name="x">要查找的模块的x坐标</param>
        /// <param name="y">要查找的模块的y坐标</param>
        /// <returns>返回与指定ID及坐标对应的模块，如果未找到则返回<see langword="null"/></returns>
        [Obsolete]
        public static T FindModulePreciseSearch<T>(int id, int x, int y) where T : TileProcessor => FindModulePreciseSearch(id, x, y) as T;

        /// <summary>
        /// 在指定范围内查找与指定ID和坐标最接近的模块，并将其转换为指定类型的模块
        /// </summary>
        /// <typeparam name="T">要返回的模块的类型，必须继承自 <see cref="TileProcessor"/></typeparam>
        /// <param name="x">要查找的模块的x坐标</param>
        /// <param name="y">要查找的模块的y坐标</param>
        /// <param name="maxFindLeng">搜索范围的最大距离</param>
        /// <returns>返回与指定ID及坐标最接近的模块，如果未找到则返回<see langword="null"/></returns>
        public static T FindModuleRangeSearch<T>(int x, int y, int maxFindLeng) where T : TileProcessor => FindModuleRangeSearch(GetModuleID<T>(), x, y, maxFindLeng) as T;
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
                float value = (new Vector2(x, y) * 16 - inds.PosInWorld).Length();
                if (value > findValue) {
                    continue;
                }
                // 更新最接近的模块及其距离
                module = inds;
                findValue = value;
            }
            return module;
        }

        #endregion
    }
}
