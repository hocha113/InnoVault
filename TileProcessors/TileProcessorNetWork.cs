using System;
using System.Collections.Generic;
using System.IO;
using Terraria;
using Terraria.DataStructures;
using Terraria.ModLoader;
using static InnoVault.TileProcessors.TileProcessorLoader;
using static InnoVault.VaultNetWork;

namespace InnoVault.TileProcessors
{
    /// <summary>
    /// 负责关于TP实体的网络工作
    /// </summary>
    public class TileProcessorNetWork
    {
        /// <summary>
        /// 属于TP实体网络工作的独特GUID标签
        /// </summary>
        public const string TP_START_GUID = "{VaultMod_TP_START_GUID_94AE-XYZ-AB34-BY27}";
        /// <summary>
        /// 单次发布的最大TP实体容量
        /// </summary>
        public const int MaxTPSendPackCount = 400;
        /// <summary>
        /// 单个包的最大文件流长度
        /// </summary>
        public const int MaxStreamSize = 65535;
        /// <summary>
        /// 当前是否正在进行初始化世界的网络工作
        /// </summary>
        public static bool InitializeWorld { get; private set; }

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
        /// 发送TP网络数据
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
        /// 接收TP网络数据
        /// </summary>
        public static void TileProcessorReceiveData(BinaryReader reader, int whoAmI) {
            //"TileProcessorLoader-ReceiveData:正在接收数据".LoggerDomp();
            string loadenName = reader.ReadString();
            Point16 position = reader.ReadPoint16();
            TileProcessor tileProcessor = null;

            //使用字典查询节省性能
            if (ByPositionGetTP(loadenName, position, out var tp)) {
                tileProcessor = tp;
                tileProcessor.ReceiveData(reader, whoAmI);
            }

            //如果找到实体了就尝试进行广播
            if (tileProcessor != null) {
                if (Main.dedServ) {
                    ModPacket modPacket = VaultMod.Instance.GetPacket();
                    modPacket.Write((byte)MessageType.TPNetWork);
                    modPacket.Write(loadenName);
                    modPacket.WritePoint16(position);
                    tileProcessor.SendData(modPacket);
                    modPacket.Send(-1, whoAmI);
                }
            }
            else {
                throw new Exception($"TileProcessorLoader-ReceiveData: No Corresponding TileProcessor Instance Found : {loadenName}-position[{position}]");
            }
        }

        //我们需要先明白，在多人模式中地图加载是不完全的，对于客户端，完整的图格加载往往只在玩家生成点周围
        //所以如果不进行处理，客户端的TP实体就会在进入地图后消失，但服务端的不受影响
        //为了解决这个问题，我禁止了客户端自行杀死TP的更新，转而让服务端广播TP的死亡消息
        //目前来讲，这个问题被很好的解决了。但是要小心由这种解决方法所诞生出来的不安全字段，比如TP的Tile字段将有可能为默认值
        //并且，在客户端进入服务器后可能会需要自行加载大量的TP实体

        /// <summary>
        /// 由服务端向所有客户端广播这个TP实体死亡的消息
        /// </summary>
        /// <param name="tileProcessor"></param>
        public static void SendTPDeathByServer(TileProcessor tileProcessor) {
            if (!VaultUtils.isServer) {
                return;
            }
            ModPacket modPacket = VaultMod.Instance.GetPacket();
            modPacket.Write((byte)MessageType.ServerTPDeathVerify);
            modPacket.Write(tileProcessor.ID);
            modPacket.WritePoint16(tileProcessor.Position);
            modPacket.Send();
        }

        /// <summary>
        /// 客户端接收死亡消息后设置其死亡
        /// </summary>
        /// <param name="reader"></param>
        public static void HandlerTPDeathByClient(BinaryReader reader) {
            if (!VaultUtils.isClient) {
                return;
            }
            int id = reader.ReadInt32();
            Point16 point = reader.ReadPoint16();
            if (ByPositionGetTP(id, point, out var tileProcessor)) {
                tileProcessor.Kill();
            }
        }

        //服务端是唯一的，即使是主机本质上也只是在本机上托管运行了一个服务端
        //这意味着，如果进入多人模式，这个玩家的客户端是加载于服务端之后的，这里的延时发包将不能解决所有情景下的问题，只能让作为主机的玩家接收到来自服务器的发包信息
        //其他更晚加入的客户端依旧接收不到发包
        //之所以将延时加长到30后主机能接收到发包信息，是因为主机的客户端的加载完成时间于其所托管的服务端是接近的
        //可能只有2~5帧的延迟，所以这个sendTime设置在绝大多数情况下可以让主机客户端吃到发包，但其他客户端则大概率不行
        //对此我给出的解决方案是，让每个客户端在加入后手动向服务器请求一次TP信息，由客户端发送一个代表数据索取的包，服务器收到后向该客户端发送正确的数据并让其接收

        /// <summary>
        /// 让客户端向服务器发出数据请求，请求一个完整的TP数据链
        /// </summary>
        public static void ClientRequest_TPData_Send(bool initializeWorld = false) {
            if (!VaultUtils.isClient) {
                return;
            }
            //$"TileProcessorLoader-ClientRequest_TPData:玩家{Main.LocalPlayer.name}/客户端id{Main.myPlayer}正在请求服务端TP数据链".LoggerDomp();
            ModPacket modPacket = VaultMod.Instance.GetPacket();
            modPacket.Write((byte)MessageType.ClientRequest_TPData_Send);
            modPacket.Write(initializeWorld);
            modPacket.Send();
        }

        //另一个需要注意的概念是，世界数据存储在服务端上，退出世界、进入世界的相关钩子如OnWorldLoad和OnWorldUnLoad不会在客户端上运行
        //而存档数据会在服务端关闭后自动写入存档文件，这意味着任意客户端的退出都不能主动更新自己本地的存档数据，只有等待服务端被关闭，这是存储世界数据的一个网络特点
        //所以，不需要试图在卸载世界或者玩家退出服务器时进行任何世界网络数据的同步请求，没必要也没用，服务器自身在开启和关闭时便会处理这一切

        /// <summary>
        /// 向指定客户端发送一个完整的TP数据链
        /// </summary>
        public static void ServerRecovery_TPData(BinaryReader reader, int whoAmI) {
            if (!Main.dedServ) {
                return;
            }

            InitializeWorld = reader.ReadBoolean();

            List<TileProcessor> activeTPs = [];//建立一个临时的活跃实体列表用于后续的遍历发包
            // 统计活跃的TP数量
            foreach (TileProcessor tp in TP_InWorld) {
                if (!tp.Active || !tp.LoadenWorldSendData) {
                    continue;
                }
                activeTPs.Add(tp);
            }

            int sendTPCount = activeTPs.Count;

            if (sendTPCount > MaxTPSendPackCount) {//如果数量大于MaxTPSendPackCount
                int splits = sendTPCount / MaxTPSendPackCount + 1;
                for (int i = 0; i < splits; i++) {
                    int start = i * MaxTPSendPackCount;
                    int size = MaxTPSendPackCount;
                    if (start + size > sendTPCount) {//如果大于，说明已经到了末流
                        size = sendTPCount - start;//重新计算一下末流的长度
                    }

                    ModPacket modPacket = VaultMod.Instance.GetPacket();//创建一个新的数据包对象
                    modPacket.Write((byte)MessageType.Handle_TPData_Receive); // 包类型
                    modPacket.Write(InitializeWorld);
                    modPacket.Write(size);// 写入活跃的TP数量

                    for (int j = start; j < start + size; j++) {
                        TileProcessor tp = activeTPs[j];
                        modPacket.Write(TP_START_GUID);//标记节点开始，添加分隔标签
                        modPacket.Write(tp.LoadenName);
                        modPacket.WritePoint16(tp.Position);
                        //宽高不太可能超过255，所以转化为byte发送节省空间，注意这里除了16所以表示的是物块格子
                        modPacket.Write((byte)(tp.Width / 16));
                        modPacket.Write((byte)(tp.Height / 16));
                        // 发送TileProcessor数据
                        tp.SendData(modPacket);
                    }

                    if (modPacket.BaseStream.Length > MaxStreamSize) {
                        HandlerPackMeltingAway(modPacket);
                        continue;
                    }

                    modPacket.Send(whoAmI); // 将数据包发送给客户端
                }

                InitializeWorld = false;
                return;
            }

            // 数据量未超上限，直接发送一个完整包
            ModPacket fullPacket = VaultMod.Instance.GetPacket();
            fullPacket.Write((byte)MessageType.Handle_TPData_Receive);
            fullPacket.Write(InitializeWorld);
            fullPacket.Write(sendTPCount);

            foreach (TileProcessor tp in activeTPs) {
                fullPacket.Write(TP_START_GUID);
                fullPacket.Write(tp.LoadenName);
                fullPacket.WritePoint16(tp.Position);
                //宽高不太可能超过255，所以转化为byte发送节省空间，注意这里除了16所以表示的是物块格子
                fullPacket.Write((byte)(tp.Width / 16));
                fullPacket.Write((byte)(tp.Height / 16));
                // 发送TileProcessor数据
                tp.SendData(fullPacket);
            }

            if (fullPacket.BaseStream.Length > MaxStreamSize) {
                HandlerPackMeltingAway(fullPacket);
                InitializeWorld = false;
                return;
            }

            fullPacket.Send(whoAmI);// 将数据包发送给客户端
            InitializeWorld = false;
        }
        /// <summary>
        /// 服务端响应TP数据链的请求后，接收数据
        /// </summary>
        public static void Handle_TPData_Receive(BinaryReader reader) {
            if (!VaultUtils.isClient) {
                return;
            }

            InitializeWorld = reader.ReadBoolean();

            int tpCount = reader.ReadInt32(); // 读取 TP 数量
            if (tpCount < 0 || tpCount > MaxTPInWorldCount) {
                "TileProcessorLoader-ClientRequest_TPData_Receive: Received invalid TP count, terminating read".LoggerDomp(VaultMod.Instance);
                return;
            }

            for (int i = 0; i < tpCount; i++) {
                // 确保是合法的标记
                if (reader.ReadString() != TP_START_GUID) {
                    $"TileProcessorLoader-ClientRequest_TPData_Receive: Invalid markID: {i}, skipping to the next node".LoggerDomp(VaultMod.Instance);
                    SkipToNextMarker(reader);
                    continue;
                }

                string loadenName = reader.ReadString();
                Point16 position = reader.ReadPoint16();
                byte widthByTile = reader.ReadByte();
                byte heightByTile = reader.ReadByte();

                // 先检查字典中是否已有该 TileProcessor
                if (ByPositionGetTP(loadenName, position, out TileProcessor tp)) {
                    tp.ReceiveData(reader, -1);
                    continue;
                }

                // 通过 name 获取 TP ID
                if (!TryGetTpID(loadenName, out int tpID)) {
                    DompTPinstanceNotFound(loadenName, position);
                    SkipToNextMarker(reader);
                    continue;
                }

                // 先尝试从现有的 TileProcessor 列表中查找
                if (ByPositionGetTP(tpID, position.X, position.Y, out TileProcessor existingTP)) {
                    existingTP.ReceiveData(reader, -1);
                    continue;
                }

                // 如果找不到，尝试新建
                if (TP_ID_To_Instance.TryGetValue(tpID, out TileProcessor template)) {
                    TileProcessor newTP = AddInWorld(template.TargetTileID, position, null);
                    if (newTP != null) {
                        //因为客户端上的物块加载不完整，所以客户端生成的TP大小很可能不正确，这里接收服务器的数据来进行覆盖矫正
                        newTP.Width = widthByTile * 16;//乘以16转化为像素宽度
                        newTP.Height = heightByTile * 16;
                        newTP.ReceiveData(reader, -1);
                        continue;
                    }
                }

                // 仍然失败，则记录日志并跳过
                DompTPinstanceNotFound(loadenName, position);
                SkipToNextMarker(reader);
            }

            InitializeWorld = false;
        }

        private static void HandlerPackMeltingAway(ModPacket modPacket) {
            $"ServerRecovery_TPData: Packet too large ({modPacket.BaseStream.Length}), aborting".LoggerDomp(VaultMod.Instance);
            modPacket.Dispose();
        }

        private static void DompTPinstanceNotFound(string name, Point16 position)
            => $"TileProcessorLoader-ClientRequest_TPData_Receive: No corresponding TileProcessor instance found: {name}-position[{position}]，Skip".LoggerDomp(VaultMod.Instance);

        /// <summary>
        /// 跳到下一个标记节点
        /// </summary>
        private static void SkipToNextMarker(BinaryReader reader) {
            while (reader.BaseStream.Position < reader.BaseStream.Length) {
                string marker = reader.ReadString();
                if (marker == TP_START_GUID) {
                    // 回退以便下一个处理块能够正确读取标记
                    reader.BaseStream.Position -= marker.Length;
                    break;
                }
            }
        }
    }
}
