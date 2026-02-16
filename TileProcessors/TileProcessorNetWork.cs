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
        internal static volatile float NetworkLoadProgress;
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
        #endregion

        void IVaultLoader.LoadData() {
            for (int i = 0; i < 255; i++) {
                TPDataChunks_IndexToChunks[i] = [];
            }
        }

        void IVaultLoader.UnLoadData() {
            NetworkLoadProgress = 0;
            NetChunkIdleTime = 0;
            MaxTPDataChunkCount = -1;
            InitializeWorld = false;
            LoadenTPByNetWork = true;
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
        /// 发送TP实体右键交互的消息
        /// </summary>
        /// <param name="i"></param>
        /// <param name="j"></param>
        /// <param name="playerIndex"></param>
        public static void SendTPRightClick(int i, int j, int playerIndex) {
            if (VaultUtils.isSinglePlayer) {
                return;
            }
            ModPacket modPacket = VaultMod.Instance.GetPacket();
            modPacket.Write((byte)MessageType.Handler_TPRightClick);
            modPacket.WritePoint16(new Point16(i, j));
            modPacket.Write(playerIndex);
            modPacket.Send();
        }
        /// <summary>
        /// 接收TP实体右键交互的消息
        /// </summary>
        /// <param name="reader"></param>
        /// <param name="whoAmI"></param>
        public static void Handler_TPRightClick(BinaryReader reader, int whoAmI) {
            Point16 point = reader.ReadPoint16();
            int playerIndex = reader.ReadInt32();
            if (ByPositionGetTP(point, out var tp)) {
                Tile tile = Framing.GetTileSafely(point.X, point.Y);
                tp.RightClick(point.X, point.Y, tile, Main.player[playerIndex]);
            }
            if (!VaultUtils.isServer) {
                return;
            }
            ModPacket modPacket = VaultMod.Instance.GetPacket();
            modPacket.Write((byte)MessageType.Handler_TPRightClick);
            modPacket.WritePoint16(point);
            modPacket.Write(playerIndex);
            modPacket.Send(-1, playerIndex);
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
            modPacket.Write(tp.FullName);
            modPacket.WritePoint16(tp.Position);
            TileProcessorInstanceDoSendData(tp, modPacket);
            modPacket.Send();
        }

        /// <summary>
        /// 该函数是对发送逻辑的二次封装，用于在让TP实体发送数据时进行通用的额外处理
        /// </summary>
        /// <param name="tileProcessor"></param>
        /// <param name="modPacket"></param>
        public static void TileProcessorInstanceDoSendData(TileProcessor tileProcessor, ModPacket modPacket) {
            try {
                tileProcessor.SendData(modPacket);
                SyncVarManager.Send(tileProcessor, modPacket);
            } catch (Exception ex) {
                tileProcessor.SendCooldownTicks = 60;
                string msg = $"TileProcessorInstanceDoSendData-Data Send Failure: {ex.Message}\n{ex.StackTrace}";
                VaultMod.LoggerError($"{tileProcessor.FullName}:NullRef@SemdData", msg);
            }
        }

        /// <summary>
        /// 该函数是对接收逻辑的二次封装，用于在让TP实体接收数据时进行通用的额外处理
        /// </summary>
        /// <param name="tileProcessor"></param>
        /// <param name="reader"></param>
        /// <param name="whoAmI"></param>
        public static void TileProcessorInstanceDoReceiveData(TileProcessor tileProcessor, BinaryReader reader, int whoAmI) {
            try {
                tileProcessor.ReceiveData(reader, whoAmI);
                SyncVarManager.Receive(tileProcessor, reader);
            } catch (Exception ex) {
                tileProcessor.SendCooldownTicks = 60;
                string msg = $"TileProcessorInstanceDoReceiveData-Data Reception Failure: {ex.Message}\n{ex.StackTrace}";
                VaultMod.LoggerError($"{tileProcessor.FullName}:NullRef@ReceiveData", msg);
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
                TileProcessorInstanceDoSendData(tileProcessor, modPacket);
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
            //因为是在客户端，LoadWorldTileProcessor并不会允许，为了避免客户端带来的静态数据污染游戏，这里也清理一次世界TP数据
            InitializeWorldTP();

            initializeWorldTickCounter = 0;
            loadTPNetworkTickCounter = 0;
            LoadenTPByNetWork = false;//标记为false，表示开始网络加载
            NetworkLoadProgress = 0f;

            Task.Run(async () => {
                try {//开启一个子线程，在客户端的TP加载好了后再发送数据链请求
                    await VaultUtils.WaitUntilAsync(() => LoadenTP, 50, 10000);//最多等10秒
                } catch (TaskCanceledException) {
                    VaultMod.Instance.Logger.Error("[ClientRequest_TPData_Send] The waiting for TileProcessorLoader.LoadenTP to complete has timed out.");
                } catch (Exception ex) {
                    VaultMod.Instance.Logger.Error($"[ClientRequest_TPData_Send] An exception occurred while waiting for TileProcessorLoader.LoadenTP: {ex.Message}");
                }

                try {
                    NetworkLoadProgress = 2f;
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

        //服务端主动推送模式：序列化TP数据后，直接将Header和所有chunk连续发送给客户端
        //客户端被动接收，无需逐块请求，大幅减少RTT等待（从 N×2×RTT 降至 1×RTT + 流式传输）
        private static void SendToClient_TPDataInner(int whoAmI) {
            if (!VaultUtils.isServer) {
                return;
            }
            //建立一个临时的活跃实体列表用于后续的遍历发包
            List<TileProcessor> activeTPs = TP_InWorld.ToList().FindAll(tp => tp.Active && tp.LoadenWorldSendData);
            int sendTPCount = activeTPs.Count;
            if (sendTPCount <= 0) {//如果没有可使用的TP实体就不用发送相关的数据了，虽然一般不会这样
                ResetTPDataChunkNet(whoAmI);
                //这里的总断是在服务器上的单方面重置，需要发包告诉客户端
                ModPacket modPacket = VaultMod.Instance.GetPacket();
                modPacket.Write((byte)MessageType.GetServer_ResetTPDataChunkNet);
                modPacket.Send(whoAmI);
                return;
            }

            //使用ModPacket作为BinaryWriter来序列化TP数据（因为TileProcessor.SendData接受ModPacket参数）
            //这个ModPacket不会被直接发送，只是借用其MemoryStream进行序列化
            ModPacket fullPacket = VaultMod.Instance.GetPacket();
            //记录ModPacket内部包头结束后的位置，这是TP有效数据的起始点
            //ModPacket创建时会预写入tModLoader的包头（包长度占位、Mod索引等），必须跳过
            long dataStartPos = fullPacket.BaseStream.Position;
            fullPacket.Write(sendTPCount);

            foreach (TileProcessor tp in activeTPs) {
                fullPacket.Write(TP_START_MARKER);
                fullPacket.Write(tp.FullName);
                fullPacket.WritePoint16(tp.Position);
                //宽高不太可能超过255，所以转化为byte发送节省空间，注意这里除了16所以表示的是物块格子
                fullPacket.Write((byte)(tp.Width / 16));
                fullPacket.Write((byte)(tp.Height / 16));
                // 发送TileProcessor数据
                tp.SendData(fullPacket);
            }

            if (fullPacket.BaseStream is not MemoryStream stream) {
                VaultMod.Instance.Logger.Error("[SendToClient_TPDataInner] ModPacket.BaseStream is not a MemoryStream, cannot serialize TP data.");
                return;
            }
            //只提取有效的TP数据部分，跳过ModPacket的内部包头
            byte[] streamBytes = stream.ToArray();
            byte[] fullBytes = new byte[streamBytes.Length - dataStartPos];
            Array.Copy(streamBytes, dataStartPos, fullBytes, 0, fullBytes.Length);
            List<byte[]> chunks = VaultUtils.SplitBytes(fullBytes, MaxStreamSize);

            //1. 发送Header：告知客户端即将接收的chunk总数
            {
                ModPacket header = VaultMod.Instance.GetPacket();
                header.Write((byte)MessageType.GetServer_MaxTPDataChunkCount);
                header.Write((ushort)chunks.Count);
                header.Send(whoAmI);
            }

            //2. 服务端主动连续推送所有chunk，每个chunk是一个独立的ModPacket（≤MaxStreamSize），无需等待客户端回复
            for (ushort i = 0; i < chunks.Count; i++) {
                ModPacket chunkPacket = VaultMod.Instance.GetPacket();
                chunkPacket.Write((byte)MessageType.GetServer_TPDataChunk);
                chunkPacket.Write(i);
                chunkPacket.Write(chunks[i].Length);
                chunkPacket.Write(chunks[i]);
                chunkPacket.Send(whoAmI);
            }
            //推送完成后，服务端不需要缓存chunks，数据随方法结束自然释放
        }

        //客户端接收拼图总数，收到后等待服务端主动推送的chunk数据
        internal static void GetServer_MaxTPDataChunkCount(BinaryReader reader) {
            if (!VaultUtils.isClient) {
                return;
            }

            MaxTPDataChunkCount = reader.ReadUInt16();
            NetworkLoadProgress = 10f;
            //服务端会主动推送所有chunk，客户端只需等待接收
        }

        //客户端被动接收服务端推送的拼图数据
        internal static void GetServer_TPDataChunk(BinaryReader reader) {
            if (!VaultUtils.isClient) {
                return;
            }

            ushort index = reader.ReadUInt16();
            int count = reader.ReadInt32();
            byte[] data = reader.ReadBytes(count);

            //去重：如果已经收到过这个index的chunk就跳过
            if (LocalTPDataChunks_IndexToChunks.ContainsKey(index)) {
                //但如果已经收齐了，尝试触发合并（防止之前因时序问题没有触发）
                if (MaxTPDataChunkCount > 0 && LocalTPDataChunks_IndexToChunks.Count >= MaxTPDataChunkCount) {
                    Handle_TPDataChunks();
                }
                return;
            }

            LocalTPDataChunks_IndexToChunks[index] = data;

            if (MaxTPDataChunkCount == -1) {
                VaultMod.Instance.Logger.Error("Error: The total number of puzzles was not correctly initialized.");
                ResetTPDataChunkNet();
                return;
            }

            NetChunkIdleTime = 0;
            NetworkLoadProgress = 10f + (LocalTPDataChunks_IndexToChunks.Count / (float)MaxTPDataChunkCount) * 80f;

            //全部收齐后触发合并处理
            if (LocalTPDataChunks_IndexToChunks.Count >= MaxTPDataChunkCount) {
                Handle_TPDataChunks();
            }
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

                    //数据由独立的MemoryStream序列化，从头开始读取即可
                    combinedStream.Position = 0;
                    using BinaryReader reader = new BinaryReader(combinedStream);
                    NetworkLoadProgress = 96f;
                    Handle_TPData_ReceiveInner(reader);
                } catch (Exception ex) {
                    VaultMod.Instance.Logger.Error($"An error occurred while executing Handle_TPDataChunks: {ex.Message}");
                } finally {
                    ResetTPDataChunkNet();
                }
            });
        }

        private static void ResetTPDataChunkNet(int whoAmI = -1) {
            if (whoAmI == -1) {
                whoAmI = Main.myPlayer;
            }

            if (whoAmI < 0 || whoAmI >= Main.maxPlayers) {
                VaultMod.Instance.Logger.Warn($"[ResetTPDataChunkNet] Invalid whoAmI: {whoAmI}");
                return;
            }

            NetChunkIdleTime = 0;
            NetworkLoadProgress = 0f;
            MaxTPDataChunkCount = -1;
            InitializeWorld = false;
            LoadenTPByNetWork = true;//标记为true，表明网络加载完成

            TPDataChunks_IndexToChunks[whoAmI].Clear();
        }

        private static string lastSuccessfulTPName;
        private static void Handle_TPData_ReceiveInner(BinaryReader reader) {
            int tpCount = reader.ReadInt32(); //读取 TP 数量
            if (tpCount < 0 || tpCount > MaxTPInWorldCount) {
                VaultMod.Instance.Logger.Warn($"TileProcessorLoader-ClientRequest_TPData_Receive: " +
                    $"Received invalid TP count:{tpCount}, terminating read");
                return;
            }

            lastSuccessfulTPName = "";//这个值用于记录上一次成功读取的TP的内部名，方便调试
            for (int i = 0; i < tpCount; i++) {
                NetworkLoadProgress = 96f + (i * 4f / tpCount);//这里的4f表示从96%到100%的进度，tpCount如果为0是不会进入循环的

                //确保是合法的标记
                uint marker = reader.ReadUInt32();
                if (marker != TP_START_MARKER) {
                    VaultMod.Instance.Logger.Warn($"TileProcessorLoader-ClientRequest_TPData_Receive: " +
                        $"Invalid markID: {i}, Marker: {marker}, last Success fulTPName: {lastSuccessfulTPName}" +
                        $", skipping to the next node");
                    SkipToNextMarker(reader);
                    continue;
                }

                string loadenName = reader.ReadString();
                Point16 position = reader.ReadPoint16();
                byte widthByTile = reader.ReadByte();
                byte heightByTile = reader.ReadByte();

                //先检查字典中是否已有该 TileProcessor
                if (ByPositionGetTP(loadenName, position, out TileProcessor tp) && tp.FullName == loadenName) {
                    TileProcessorInstanceDoReceiveData(tp, reader, -1);
                    lastSuccessfulTPName = loadenName;
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
                if (ByPositionGetTP(tpID, position.X, position.Y, out TileProcessor existingTP) && existingTP.FullName == loadenName) {
                    TileProcessorInstanceDoReceiveData(existingTP, reader, -1);
                    lastSuccessfulTPName = loadenName;
                    continue;
                }

                //如果找不到，尝试新建
                //2025.9.28: 首先我得理解到有该死的一个位置可能存在多个TP实体的情况，所以这些字典到了这一步并不完全可靠
                if (TP_ID_To_Instance.TryGetValue(tpID, out TileProcessor template) && template.FullName == loadenName) {
                    TileProcessor newTP;
                    //如果这个TP是未知TP，那么就强制生成一个未知TP
                    if (template.ID == TPUtils.GetID<UnknowTP>()) {
                        newTP = UnknowTP.Place(position, [], "unknown", "unknown");
                    }
                    else {
                        newTP = AddInWorld(template.TargetTileID, position, null);
                    }

                    if (newTP != null && newTP.FullName == loadenName) {
                        //因为客户端上的物块加载不完整，所以客户端生成的TP大小很可能不正确，这里接收服务器的数据来进行覆盖矫正
                        newTP.Width = widthByTile * 16;//乘以16转化为像素宽度
                        newTP.Height = heightByTile * 16;
                        TileProcessorInstanceDoReceiveData(newTP, reader, -1);
                        lastSuccessfulTPName = loadenName;
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

            //如果客户端长时间没有收到新的chunk数据，说明推送流可能中断，重新请求完整的TP数据链
            if (VaultUtils.isClient && !LoadenTPByNetWork && MaxTPDataChunkCount > 0
                && LocalTPDataChunks_IndexToChunks.Count < MaxTPDataChunkCount
                && ++NetChunkIdleTime > 300) {
                NetChunkIdleTime = 0;
                //重新请求完整的TP数据链
                ModPacket modPacket = VaultMod.Instance.GetPacket();
                modPacket.Write((byte)MessageType.SendToClient_TPData);
                modPacket.Send();

                VaultMod.Instance.Logger.Warn($"[UpdateNetworkStatusWatchdog]: TP data stream stalled at " +
                    $"{LocalTPDataChunks_IndexToChunks.Count}/{MaxTPDataChunkCount} chunks. " +
                    $"Re-requesting full TP data chain.");
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
                    //部分匹配失败，检查当前字节是否为新匹配的起点
                    matchIndex = currentByte == markerBytes[0] ? 1 : 0;
                }
                //包的大小不可能大于MaxStreamSize，在这里进行一次额外检查防止死循环
                if (++safeNum > MaxStreamSize) {
                    break;
                }
            }
        }

        //网络数据流的交汇点，网络钩子集中在此处
        internal static void HandlePacket(MessageType type, Mod mod, BinaryReader reader, int whoAmI) {
            switch (type) {
                case MessageType.Handler_PlaceInWorld:
                    Handler_PlaceInWorld(mod, reader, whoAmI);
                    break;
                case MessageType.Handler_TileProcessorIndsData:
                    Handler_TileProcessorIndsData(reader, whoAmI);
                    break;
                case MessageType.Handler_TPDeathByClient:
                    Handler_TPDeathByClient(reader);
                    break;
                case MessageType.SendToClient_TPData:
                    SendToClient_TPData(whoAmI);
                    break;
                case MessageType.GetServer_TPDataChunk:
                    GetServer_TPDataChunk(reader);
                    break;
                case MessageType.GetServer_MaxTPDataChunkCount:
                    GetServer_MaxTPDataChunkCount(reader);
                    break;
                case MessageType.GetServer_ResetTPDataChunkNet:
                    if (VaultUtils.isClient) {
                        ResetTPDataChunkNet();//这个消息由服务器单方面发送，只由客户端来接收处理
                    }
                    break;
                case MessageType.Handler_TPRightClick:
                    Handler_TPRightClick(reader, whoAmI);
                    break;
            }
        }
    }
}
