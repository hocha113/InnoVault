using InnoVault.GameContent;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
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
    public class TileProcessorNetWork : IVaultLoader
    {
        #region Data
        /// <summary>
        /// 属于TP实体网络工作的独特魔术标签，更加节省空间
        /// </summary>
        public const uint TP_START_MARKER = 0xDEADCAFE;
        /// <summary>
        /// 单次发布的最大TP实体容量
        /// </summary>
        public const int MaxTPSendPackCount = 400;
        /// <summary>
        /// 单个包的最大文件流长度
        /// </summary>
        public const int MaxStreamSize = short.MaxValue;
        /// <summary>
        /// 当前是否正在进行初始化世界的网络工作
        /// </summary>
        public static bool InitializeWorld { get; private set; } = false;
        /// <summary>
        /// 是否已经完成了TP实体的网络加载
        /// </summary>
        public static bool LoadenTPByNetWork { get; private set; } = true;
        //记录InitializeWorld属性的开启帧
        internal static int initializeWorldTickCounter;
        //记录LoadenTPByNetWork属性的关闭帧
        internal static int loadTPNetworkTickCounter;
        /// <summary>
        /// 网络缓冲加载的最大等待时间刻
        /// </summary>
        public const int MaxBufferWaitingTimeMark = 900;
        /// <summary>
        /// 存储所有玩家从服务端接收到的拼图数据块列表，按玩家WhoAmI组织
        /// 仅服务端用于管理每个客户端的接收状态
        /// </summary>
        internal static Dictionary<int, List<byte[]>> TPDataChunks = [];
        /// <summary>
        /// 当前客户端本地接收到的拼图数据块列表，映射到 <see cref="TPDataChunks"/> 中当前玩家的数据
        /// 用于顺序合并完整拼图数据
        /// </summary>
        internal static List<byte[]> LocalTPDataChunks {
            get => TPDataChunks[Main.myPlayer];
            set => TPDataChunks[Main.myPlayer] = value;
        }
        /// <summary>
        /// 服务端维护的每位客户端拼图块索引映射，方便按序号访问或重传某个数据块
        /// key: 玩家 ID，value: (块索引 → 数据)
        /// </summary>
        internal static Dictionary<int, Dictionary<ushort, byte[]>> TPDataChunks_IndexToChunks = [];
        /// <summary>
        /// 当前客户端本地记录的拼图数据块索引映射，映射到 <see cref="TPDataChunks_IndexToChunks"/> 中当前玩家的数据
        /// 主要用于按 index 访问具体某一块数据，便于重组或重传判断
        /// </summary>
        internal static Dictionary<ushort, byte[]> LocalTPDataChunks_IndexToChunks {
            get => TPDataChunks_IndexToChunks[Main.myPlayer];
            set => TPDataChunks_IndexToChunks[Main.myPlayer] = value;
        }
        /// <summary>
        /// 网络加载进度百分比，范围 0~100用于 UI 显示传输进度
        /// 线程安全字段，可能被后台线程更新
        /// </summary>
        internal static volatile float NetLoadenPercentage;
        /// <summary>
        /// 拼图链式数据流中无响应的 tick 计数器用于判断是否触发超时重传或终止当前数据流
        /// 单位：游戏帧数（tick）
        /// </summary>
        internal static int NetChunkIdleTime;
        /// <summary>
        /// 本次拼图传输中服务端发送或客户端预期接收的拼图块总数
        /// 初始化为 -1 表示未开始任何拼图传输
        /// </summary>
        internal static int MaxTPDataChunkCount = -1;
        /// <summary>
        /// 拼图数据在合并数据流时的起始偏移，用于跳过前置协议包头部分，读取有效内容
        /// 如果为 -1，表示尚未初始化，合并处理将失败
        /// 单位：字节（stream.Position）
        /// </summary>
        internal static long TPDataChunkPacketStartPos = -1;
        #endregion

        void IVaultLoader.LoadData() {
            for (int i = 0; i < 255; i++) {
                TPDataChunks[i] = [];
                TPDataChunks_IndexToChunks[i] = [];
            }
        }

        void IVaultLoader.UnLoadData() {
            NetLoadenPercentage = 0;
            NetChunkIdleTime = 0;
            MaxTPDataChunkCount = -1;
            TPDataChunkPacketStartPos = -1;
            InitializeWorld = false;
            LoadenTPByNetWork = true;
            TPDataChunks.Clear();
            TPDataChunks_IndexToChunks.Clear();
        }

        #region InWorldNet
        /// <summary>
        /// 发送放置一个TP实体到世界中的消息
        /// </summary>
        /// <param name="mod"></param>
        /// <param name="type"></param>
        /// <param name="point"></param>
        public static void PlaceInWorldNetSend(Mod mod, int type, Point16 point) {
            // 客户端发送同步请求到服务器
            ModPacket packet = mod.GetPacket();
            packet.Write((byte)MessageType.Handler_PlaceInWorld);
            packet.Write(type);
            packet.WritePoint16(point);
            packet.Send(); //发送到服务器
        }

        /// <summary>
        /// 接收放置一个TP实体到世界中的消息
        /// </summary>
        /// <param name="mod"></param>
        /// <param name="reader"></param>
        /// <param name="whoAmI"></param>
        internal static void Handler_PlaceInWorld(Mod mod, BinaryReader reader, int whoAmI) {
            // 读取放置方块的数据
            int tileType = reader.ReadInt32();
            Point16 point = reader.ReadPoint16();
            AddInWorld(tileType, point, null);
            if (VaultUtils.isServer) {
                ModPacket packet = mod.GetPacket();
                packet.Write((byte)MessageType.Handler_PlaceInWorld);
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
            modPacket.Write((byte)MessageType.Handler_TileProcessorIndsData);
            modPacket.Write(tp.LoadenName);
            modPacket.WritePoint16(tp.Position);
            tp.SendData(modPacket);
            modPacket.Send();
        }

        /// <summary>
        /// 该函数是对接收逻辑的二次封装，用于在让TP实体接收数据时进行通用的额外处理
        /// </summary>
        /// <param name="tileProcessor"></param>
        /// <param name="reader"></param>
        /// <param name="whoAmI"></param>
        private static void TileProcessorInstanceDoReceiveData(TileProcessor tileProcessor, BinaryReader reader, int whoAmI) {
            try {
                tileProcessor.ReceiveData(reader, whoAmI);
            } catch (Exception ex) {
                string msg = $"TileProcessorInstanceDoReceiveData-Data Reception Failure: {ex.Message}\n{ex.StackTrace}";
                VaultMod.Instance.Logger.Error(msg);
                tileProcessor.SendCooldownTicks = 60;
            }
        }

        /// <summary>
        /// 接收TP网络数据
        /// </summary>
        public static void Handler_TileProcessorIndsData(BinaryReader reader, int whoAmI) {
            //"TileProcessorLoader-ReceiveData:正在接收数据".LoggerDomp();
            string loadenName = reader.ReadString();
            Point16 position = reader.ReadPoint16();
            TileProcessor tileProcessor;

            //使用字典查询节省性能
            if (ByPositionGetTP(loadenName, position, out var tp)) {
                tileProcessor = tp;
                TileProcessorInstanceDoReceiveData(tileProcessor, reader, whoAmI);
            }
            else {//如果没找到就临时新建一个
                VaultMod.Instance.Logger.Error($"TileProcessorLoader-ReceiveData: No Corresponding TileProcessor Instance Found: {loadenName}-Position [{position}]");
                if (!TP_FullName_To_ID.TryGetValue(loadenName, out var tpID)) {
                    VaultMod.Instance.Logger.Error($"TileProcessorLoader-ReceiveData: Unknown TileProcessor Type: {loadenName}");
                    return;
                }
                tileProcessor = AddInWorld(tpID, position, null);
                if (tileProcessor == null) {
                    VaultMod.Instance.Logger.Error($"TileProcessorLoader-ReceiveData: Re-Establishment Failed: {loadenName}-Position [{position}]");
                    return;
                }
                TileProcessorInstanceDoReceiveData(tileProcessor, reader, whoAmI);
            }

            //如果找到实体了就尝试进行广播
            if (Main.dedServ) {
                ModPacket modPacket = VaultMod.Instance.GetPacket();
                modPacket.Write((byte)MessageType.Handler_TileProcessorIndsData);
                modPacket.Write(loadenName);
                modPacket.WritePoint16(position);
                tileProcessor.SendData(modPacket);
                modPacket.Send(-1, whoAmI);
            }
        }
        #endregion

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
            modPacket.Write((byte)MessageType.Handler_TPDeathByClient);
            modPacket.Write(tileProcessor.ID);
            modPacket.WritePoint16(tileProcessor.Position);
            modPacket.Send();
        }

        /// <summary>
        /// 客户端接收死亡消息后设置其死亡
        /// </summary>
        /// <param name="reader"></param>
        public static void Handler_TPDeathByClient(BinaryReader reader) {
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
        public static void ClientRequest_TPData_Send() {
            if (!VaultUtils.isClient) {
                return;
            }

            initializeWorldTickCounter = 0;
            loadTPNetworkTickCounter = 0;
            LoadenTPByNetWork = false;//标记为false，表示开始网络加载
            NetLoadenPercentage = 0f;

            Task.Run(async () => {
                try {//开启一个子线程，在客户端的TP加载好了后再发送数据链请求
                    await VaultUtils.WaitUntilAsync(() => LoadenTP, 50, 10000);//最多等10秒
                } catch (TaskCanceledException) {
                    VaultMod.Instance.Logger.Error("[ClientRequest_TPData_Send] The waiting for TileProcessorLoader.LoadenTP to complete has timed out.");
                } catch (Exception ex) {
                    VaultMod.Instance.Logger.Error($"[ClientRequest_TPData_Send] An exception occurred while waiting for TileProcessorLoader.LoadenTP: {ex.Message}");
                }

                try {
                    NetLoadenPercentage = 2f;
                    ModPacket modPacket = VaultMod.Instance.GetPacket();
                    modPacket.Write((byte)MessageType.SendToClient_TPData);
                    modPacket.Send();
                } catch (Exception ex) {
                    VaultMod.Instance.Logger.Error($"[ClientRequest_TPData_Send] An error occurred while executing: {ex.Message}");
                }
            });
        }

        //另一个需要注意的概念是，世界数据存储在服务端上，退出世界、进入世界的相关钩子如OnWorldLoad和OnWorldUnLoad不会在客户端上运行
        //而存档数据会在服务端关闭后自动写入存档文件，这意味着任意客户端的退出都不能主动更新自己本地的存档数据，只有等待服务端被关闭，这是存储世界数据的一个网络特点
        //所以，不需要试图在卸载世界或者玩家退出服务器时进行任何世界网络数据的同步请求，没必要也没用，服务器自身在开启和关闭时便会处理这一切

        /// <summary>
        /// 向指定客户端发送一个完整的TP数据链
        /// </summary>
        public static void SendToClient_TPData(int whoAmI) {
            if (!Main.dedServ) {
                return;
            }

            initializeWorldTickCounter = 0;
            loadTPNetworkTickCounter = 0;
            InitializeWorld = true;

            Task.Run(async () => {
                try {//开启一个子线程，在服务端的TP加载好了后再发送数据链
                    await VaultUtils.WaitUntilAsync(() => LoadenTP, 50, 10000);//最多等10秒
                } catch (TaskCanceledException) {
                    VaultMod.Instance.Logger.Error("The waiting for TileProcessorLoader.LoadenTP to complete has timed out.");
                } catch (Exception ex) {
                    VaultMod.Instance.Logger.Error($"An exception occurred while waiting for TileProcessorLoader.LoadenTP: {ex.Message}");
                }

                try {
                    SendToClient_TPDataInner(whoAmI);
                    InitializeWorld = false;
                } catch (Exception ex) {
                    VaultMod.Instance.Logger.Error($"An error occurred while executing ServerRecovery_TPDataInner: {ex.Message}");
                }
            });
        }

        private static void SendToClient_TPDataInner(int whoAmI) {
            if (!VaultUtils.isServer) {
                return;
            }
            //建立一个临时的活跃实体列表用于后续的遍历发包
            List<TileProcessor> activeTPs = TP_InWorld.ToList().FindAll(tp => tp.Active && tp.LoadenWorldSendData);
            int sendTPCount = activeTPs.Count;
            if (sendTPCount <= 0) {//如果没有可使用的TP实体就不用发送相关的数据了，虽然一般不会这样
                ResetTPDataChunkNet(whoAmI);
                return;
            }

            ModPacket fullPacket = VaultMod.Instance.GetPacket();
            //fullPacket.Write((byte)MessageType.Handle_TPData_Receive);
            //fullPacket.Write(InitializeWorld);
            long packStartPos = fullPacket.BaseStream.Position;
            SendToClient_TPDataChunkPacketStartPos(whoAmI, packStartPos);

            VaultMod.Instance.Logger.Debug($"服务器初始数据包指针位置:{packStartPos}");
            fullPacket.Write(sendTPCount);

            foreach (TileProcessor tp in activeTPs) {
                fullPacket.Write(TP_START_MARKER);
                fullPacket.Write(tp.LoadenName);
                fullPacket.WritePoint16(tp.Position);
                //宽高不太可能超过255，所以转化为byte发送节省空间，注意这里除了16所以表示的是物块格子
                fullPacket.Write((byte)(tp.Width / 16));
                fullPacket.Write((byte)(tp.Height / 16));
                // 发送TileProcessor数据
                tp.SendData(fullPacket);
            }

            using MemoryStream stream = fullPacket.BaseStream as MemoryStream;
            byte[] fullBytes = stream.ToArray();
            TPDataChunks[whoAmI] = VaultUtils.SplitBytes(fullBytes, MaxStreamSize);

            VaultMod.Instance.Logger.Debug($"开始向客户端发送数据流，得到数据流长度{fullPacket.BaseStream.Length}，转化后的fullBytes长度:{fullBytes.Length}");

            SendToClient_MaxTPDataChunkCount(whoAmI);
        }

        internal static void SendToClient_TPDataChunkPacketStartPos(int whoAmI, long pos) {
            if (!VaultUtils.isServer) {
                return;
            }
            ModPacket modPacket = VaultMod.Instance.GetPacket();
            modPacket.Write((byte)MessageType.GetSever_TPDataChunkPacketStartPos);
            modPacket.Write(pos);
            modPacket.Send(whoAmI);
        }

        //向客户端发送一次拼图总数
        internal static void SendToClient_MaxTPDataChunkCount(int whoAmI) {
            if (!VaultUtils.isServer) {
                return;
            }
            VaultMod.Instance.Logger.Debug("向客户端发送一次拼图总数");
            ModPacket modPacket = VaultMod.Instance.GetPacket();
            modPacket.Write((byte)MessageType.GetSever_MaxTPDataChunkCount);
            modPacket.Write((ushort)TPDataChunks[whoAmI].Count);
            modPacket.Send(whoAmI);
        }

        //这里需要接收客户端发来的拼图请求将对应的拼图发送过去
        internal static void SendToClient_TPDataChunk(BinaryReader reader, int whoAmI) {
            if (!VaultUtils.isServer) {
                return;
            }

            ushort index = reader.ReadUInt16();
            ModPacket modPacket = VaultMod.Instance.GetPacket();
            modPacket.Write((byte)MessageType.GetSever_TPDataChunk);
            modPacket.Write(index);
            modPacket.Write(TPDataChunks[whoAmI][index].Length);
            modPacket.Write(TPDataChunks[whoAmI][index]);
            modPacket.Send(whoAmI);

            VaultMod.Instance.Logger.Debug($"这里需要接收客户端发来的拼图请求将对应的拼图发送过去，这是第{index}块拼图");

            if (index == TPDataChunks[whoAmI].Count - 1) {
                VaultMod.Instance.Logger.Debug($"总共{index}块拼图发送完成，终止数据流");
                ResetTPDataChunkNet(whoAmI);
            }
        }

        internal static void GetSever_TPDataChunkPacketStartPos(BinaryReader reader) {
            if (!VaultUtils.isClient) {
                return;
            }

            TPDataChunkPacketStartPos = reader.ReadInt64();
        }

        //客户端接收拼图总数，为后续逐一请求拼图做准备
        internal static void GetSever_MaxTPDataChunkCount(BinaryReader reader) {
            if (!VaultUtils.isClient) {
                return;
            }

            MaxTPDataChunkCount = reader.ReadUInt16();
            NetLoadenPercentage = 10f;
            //下面将开始请求第一块拼图
            ModPacket modPacket = VaultMod.Instance.GetPacket();
            modPacket.Write((byte)MessageType.SendToClient_TPDataChunk);
            modPacket.Write((ushort)LocalTPDataChunks.Count);
            modPacket.Send();//写入当前数量，作为下一个拼图的序号发送给服务器继续请求拼图

            VaultMod.Instance.Logger.Debug($"客户端接收到拼图总数{MaxTPDataChunkCount}，开始链式请求拼图");
        }

        //客户端接收拼图
        internal static void GetSever_TPDataChunk(BinaryReader reader) {
            if (!VaultUtils.isClient) {
                return;
            }

            ushort index = reader.ReadUInt16();
            int count = reader.ReadInt32();
            byte[] data = reader.ReadBytes(count);

            if (LocalTPDataChunks_IndexToChunks.TryGetValue(index, out var bytes) && bytes?.Length > 0) {
                if (LocalTPDataChunks_IndexToChunks.Count >= MaxTPDataChunkCount) {
                    VaultMod.Instance.Logger.Debug($"客户端收集拼图完成，进行处理");
                    Handle_TPDataChunks();
                }
                return;
            }

            LocalTPDataChunks.Add(data);
            LocalTPDataChunks_IndexToChunks[index] = data;
            if (MaxTPDataChunkCount == -1) {
                VaultMod.Instance.Logger.Error("Error: The total number of puzzles was not correctly initialized.");
                ResetTPDataChunkNet();
                return;
            }

            if (LocalTPDataChunks_IndexToChunks.Count >= MaxTPDataChunkCount) {
                VaultMod.Instance.Logger.Debug($"客户端收集拼图完成，进行处理");
                Handle_TPDataChunks();
                return;
            }

            ModPacket modPacket = VaultMod.Instance.GetPacket();
            modPacket.Write((byte)MessageType.SendToClient_TPDataChunk);
            modPacket.Write((ushort)LocalTPDataChunks.Count);
            modPacket.Send();//写入当前数量，作为下一个拼图的序号发送给服务器继续请求拼图

            NetChunkIdleTime = 0;
            NetLoadenPercentage = 10f + (MaxTPDataChunkCount / (float)LocalTPDataChunks_IndexToChunks.Count) * 80f;
            VaultMod.Instance.Logger.Debug($"客户端请求下一块第{index + 1}块拼图中");
        }

        //客户端在收集完拼图后，将其进行处理
        private static void Handle_TPDataChunks() {
            if (!VaultUtils.isClient) {
                return;
            }

            InitializeWorld = true;

            Task.Run(() => {
                try {
                    using MemoryStream combinedStream = new();
                    for (ushort i = 0; i < MaxTPDataChunkCount; i++) {
                        byte[] data = LocalTPDataChunks_IndexToChunks[i];
                        combinedStream.Write(data, 0, data.Length);
                    }

                    using BinaryReader reader = new BinaryReader(combinedStream);
                    if (TPDataChunkPacketStartPos == -1) {
                        throw new InvalidOperationException("TPDataChunkPacketStartPos is not initialized. Puzzle data merge cannot proceed.");
                    }
                    reader.BaseStream.Position = TPDataChunkPacketStartPos;
                    NetLoadenPercentage = 99f;
                    VaultMod.Instance.Logger.Debug($"客户端开始合并数据流，得到数据流长度{combinedStream.Length}，数据包长度{reader.BaseStream.Length}，数据包起点:{reader.BaseStream.Position}");
                    Handle_TPData_ReceiveInner(reader);
                } catch (InvalidOperationException ex) {
                    VaultMod.Instance.Logger.Error($"The puzzle data failed to start the merging process: {ex.Message}");
                } catch (Exception ex) {
                    VaultMod.Instance.Logger.Error($"An error occurred while executing ServerRecovery_TPDataInner: {ex.Message}");
                } finally {
                    ResetTPDataChunkNet();
                }
            });
        }

        private static void ResetTPDataChunkNet(int whoAmI = -1) {
            VaultMod.Instance.Logger.Debug($"初始化网络状态");
            if (whoAmI == -1) {
                whoAmI = Main.myPlayer;
            }

            NetChunkIdleTime = 0;
            NetLoadenPercentage = 0f;
            MaxTPDataChunkCount = -1;
            TPDataChunkPacketStartPos = -1;
            InitializeWorld = false;
            LoadenTPByNetWork = true;//标记为true，表明网络加载完成
            
            TPDataChunks[whoAmI].Clear();
            TPDataChunks_IndexToChunks[whoAmI].Clear();
        }

        private static void Handle_TPData_ReceiveInner(BinaryReader reader) {
            NetLoadenPercentage = 100f;
            VaultMod.Instance.Logger.Debug($"开始读取数据，得到数据流长度{reader.BaseStream.Length}");
            int tpCount = reader.ReadInt32(); //读取 TP 数量
            if (tpCount < 0 || tpCount > MaxTPInWorldCount) {
                VaultMod.Instance.Logger.Warn($"TileProcessorLoader-ClientRequest_TPData_Receive: " +
                    $"Received invalid TP count:{tpCount}, terminating read");
                return;
            }

            for (int i = 0; i < tpCount; i++) {
                //确保是合法的标记
                if (reader.ReadUInt32() != TP_START_MARKER) {
                    VaultMod.Instance.Logger.Warn($"TileProcessorLoader-ClientRequest_TPData_Receive: " +
                        $"Invalid markID: {i}, skipping to the next node");
                    SkipToNextMarker(reader);
                    continue;
                }

                string loadenName = reader.ReadString();
                Point16 position = reader.ReadPoint16();
                byte widthByTile = reader.ReadByte();
                byte heightByTile = reader.ReadByte();

                //先检查字典中是否已有该 TileProcessor
                if (ByPositionGetTP(loadenName, position, out TileProcessor tp)) {
                    TileProcessorInstanceDoReceiveData(tp, reader, -1);
                    continue;
                }

                //通过 name 获取 TP ID
                if (!TryGetTpID(loadenName, out int tpID)) {
                    VaultMod.Instance.Logger.Warn($"TileProcessorLoader-ClientRequest_TPData_Receive: " +
                    $"No corresponding TileProcessor instance found: {loadenName}-position[{position}]，Skip");
                    SkipToNextMarker(reader);
                    continue;
                }

                //先尝试从现有的 TileProcessor 列表中查找
                if (ByPositionGetTP(tpID, position.X, position.Y, out TileProcessor existingTP)) {
                    TileProcessorInstanceDoReceiveData(existingTP, reader, -1);
                    continue;
                }

                //如果找不到，尝试新建
                if (TP_ID_To_Instance.TryGetValue(tpID, out TileProcessor template)) {
                    TileProcessor newTP = AddInWorld(template.TargetTileID, position, null);
                    if (newTP != null) {
                        //因为客户端上的物块加载不完整，所以客户端生成的TP大小很可能不正确，这里接收服务器的数据来进行覆盖矫正
                        newTP.Width = widthByTile * 16;//乘以16转化为像素宽度
                        newTP.Height = heightByTile * 16;
                        TileProcessorInstanceDoReceiveData(newTP, reader, -1);
                        continue;
                    }
                }

                //仍然失败，则记录日志并跳过
                VaultMod.Instance.Logger.Warn($"TileProcessorLoader-ClientRequest_TPData_Receive: " +
                    $"No corresponding TileProcessor instance found: {loadenName}-position[{position}]，Skip");
                SkipToNextMarker(reader);
            }
        }

        /// <summary>
        /// 网络加载状态检测器：定期检查关键网络加载状态是否卡住，如果超过指定 tick 时间未完成，则强制清除并记录错误
        /// </summary>
        internal static void UpdateNetworkStatusWatchdog() {
            //网络初始化阶段超时检测
            if (InitializeWorld && ++initializeWorldTickCounter > MaxBufferWaitingTimeMark) {
                initializeWorldTickCounter = 0;
                InitializeWorld = false;
                string timeoutMsg = WorldLoadingText.NetWaringTimeoutMsg.Value;
                Main.NewText(timeoutMsg, Color.Red);
                VaultMod.Instance.Logger.Warn(timeoutMsg);
            }

            //TP数据网络加载超时检测
            if (!LoadenTPByNetWork && ++loadTPNetworkTickCounter > MaxBufferWaitingTimeMark) {
                loadTPNetworkTickCounter = 0;
                LoadenTPByNetWork = true;
                string timeoutMsg = WorldLoadingText.NetWaringTimeoutMsg.Value;
                Main.NewText(timeoutMsg, Color.Red);
                VaultMod.Instance.Logger.Warn(timeoutMsg);
            }

            if (VaultUtils.isClient && !LoadenTPByNetWork && LocalTPDataChunks.Count < MaxTPDataChunkCount 
                && MaxTPDataChunkCount > 0 && ++NetChunkIdleTime > 120) {
                NetChunkIdleTime = 0;
                //重新申请
                VaultMod.Instance.Logger.Debug($"第{LocalTPDataChunks.Count}块拼图响应时间超时，重新申请");
                ModPacket modPacket = VaultMod.Instance.GetPacket();
                modPacket.Write((byte)MessageType.SendToClient_TPDataChunk);
                modPacket.Write((ushort)LocalTPDataChunks.Count);
                modPacket.Send();
            }
        }

        /// <summary>
        /// 跳到下一个标记节点
        /// </summary>
        private static void SkipToNextMarker(BinaryReader reader) {
            byte[] markerBytes = BitConverter.GetBytes(TP_START_MARKER);
            int matchIndex = 0;
            int safeNum = 0;

            while (reader.BaseStream.Position < reader.BaseStream.Length) {
                byte currentByte = reader.ReadByte();
                if (currentByte == markerBytes[matchIndex]) {
                    matchIndex++;
                    if (matchIndex == markerBytes.Length) {
                        //找到了完整的 marker，回退到 marker 起始处
                        reader.BaseStream.Position -= markerBytes.Length;
                        break;
                    }
                }
                else {
                    //部分匹配失败，重置匹配状态
                    matchIndex = 0;
                }
                //包的大小不可能大于MaxStreamSize，在这里进行一次额外检查防止死循环
                if (++safeNum > MaxStreamSize) {
                    break;
                }
            }
        }

        internal static void HandlePacket(MessageType type, Mod mod, BinaryReader reader, int whoAmI) {
            if (type == MessageType.Handler_PlaceInWorld) {
                Handler_PlaceInWorld(mod, reader, whoAmI);
            }
            else if (type == MessageType.Handler_TileProcessorIndsData) {
                Handler_TileProcessorIndsData(reader, whoAmI);
            }
            else if (type == MessageType.Handler_TPDeathByClient) {
                Handler_TPDeathByClient(reader);
            }
            else if (type == MessageType.SendToClient_TPData) {
                SendToClient_TPData(whoAmI);
            }
            else if (type == MessageType.SendToClient_TPDataChunk) {
                SendToClient_TPDataChunk(reader, whoAmI);
            }
            else if (type == MessageType.SendToClient_MaxTPDataChunkCount) {
                SendToClient_MaxTPDataChunkCount(whoAmI);
            }
            else if (type == MessageType.GetSever_TPDataChunk) {
                GetSever_TPDataChunk(reader);
            }
            else if (type == MessageType.GetSever_MaxTPDataChunkCount) {
                GetSever_MaxTPDataChunkCount(reader);
            }
            else if (type == MessageType.GetSever_TPDataChunkPacketStartPos) {
                GetSever_TPDataChunkPacketStartPos(reader);
            }
        }
    }
}
