using InnoVault.GameSystem;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using Terraria;
using Terraria.DataStructures;
using Terraria.ModLoader;
using static InnoVault.TileProcessors.TileProcessorLoader;

namespace InnoVault.TileProcessors
{
    /// <summary>
    /// 负责关于TP实体的网络工作<br/>
    /// 加入世界时的批量同步采用"会话化 + 主线程序列化 + 后台压缩切块 + 主线程分帧推送/应用"的流水线，
    /// 以保证线程安全、抗卡顿与可恢复性
    /// </summary>
    public class TileProcessorNetWork : IVaultLoader
    {
        #region Data
        /// <summary>
        /// 属于TP实体网络工作的独特魔术标签，更加节省空间
        /// </summary>
        public const uint TP_START_MARKER = 0xDEADCAFE;
        /// <summary>
        /// 单次发布的最大TP实体容量<br/>
        /// 该常量自批量同步改为分块流式传输后已不再使用，仅为二进制兼容而保留
        /// </summary>
        [Obsolete("Unused since the TP join-sync moved to chunked streaming; retained for compatibility.")]
        public const int MaxTPSendPackCount = 400;
        /// <summary>
        /// 单个包的最大文件流长度
        /// </summary>
        public const int MaxStreamSize = short.MaxValue;
        /// <summary>
        /// 服务端单帧向单个客户端推送的最大数据块数量，用于分帧限流避免冲垮发送缓冲
        /// </summary>
        public const int MaxChunksPerTickPerClient = 16;
        /// <summary>
        /// 客户端单帧应用的最大TP实体数量，用于分帧避免在主线程一次性应用造成卡顿
        /// </summary>
        public const int MaxApplyPerTick = 512;
        /// <summary>
        /// 服务端单个玩家每秒允许的最大放置请求数，用于轻量限流
        /// </summary>
        public const int MaxPlacementsPerSecond = 60;
        /// <summary>
        /// 当前是否正在进行初始化世界的网络工作<br/>
        /// 该状态现已由 <see cref="VaultLoadingProgress"/> 统一管理，此处仅作兼容转发
        /// </summary>
        public static bool InitializeWorld => VaultLoadingProgress.NetworkInitializing;
        /// <summary>
        /// 是否已经完成了TP实体的网络加载<br/>
        /// 该状态现已由 <see cref="VaultLoadingProgress"/> 统一管理，此处仅作兼容转发
        /// </summary>
        public static bool LoadenTPByNetWork => VaultLoadingProgress.NetworkTPLoaded;
        /// <summary>
        /// 网络缓冲加载的最大等待时间刻
        /// </summary>
        public const int MaxBufferWaitingTimeMark = 900;
        /// <summary>
        /// 是否对TP实体的运行期增量同步启用 SyncVar 增量(delta)传输<br/>
        /// 默认关闭以保持与旧版本完全一致的行为；启用后运行期同步只发送发生变化的字段，
        /// 加入世界时的全量同步不受影响。该开关必须在客户端与服务端上保持一致（通常在 Mod.Load 中统一设置）
        /// </summary>
        public static bool EnableSyncVarDeltaSync = false;
        #endregion

        #region 客户端入站会话状态（仅主线程访问）
        //协议头标志位：是否压缩
        private const byte FLAG_COMPRESSED = 0x01;
        //当前入站会话编号，服务端每次推送分配一个新的编号，客户端据此丢弃过期/异源会话的数据块
        private static byte _inboundSessionId;
        //是否已收到本会话的协议头并开始收集数据块
        private static bool _inboundActive;
        //本会话的数据是否经过压缩
        private static bool _inboundCompressed;
        //本会话预期的数据块总数，-1 表示尚未开始
        private static int _inboundExpectedChunks = -1;
        //已收齐的数据块是否已被移交给后台解析，一次性闸门防止重复触发
        private static bool _inboundCompleted;
        //本次进图的网络加载是否已彻底完成，用于丢弃完成后才迟到的旧会话头，避免重复触发整图重载
        private static bool _networkLoadFinished;
        //入站数据块缓存：块索引 → 数据，仅主线程访问
        private static readonly Dictionary<int, byte[]> _inboundChunks = [];
        //后台解析完成后投递给主线程应用的任务队列
        private static readonly ConcurrentQueue<List<TPRecord>> _applyJobs = new();
        //当前正在分帧应用的任务，仅主线程访问
        private static ApplyState _activeApply;
        #endregion

        #region 服务端出站会话状态（仅主线程访问，压缩切块在后台）
        //服务端会话编号滚动计数器
        private static byte _nextSessionId;
        //等待本地TP加载完成后再序列化的客户端请求
        private static readonly List<PendingRequest> _pendingRequests = [];
        //已压缩切块、等待推送的传输任务（后台写入，主线程读取）
        private static readonly ConcurrentQueue<OutboundTransfer> _readyTransfers = new();
        //正在分帧推送中的传输任务，仅主线程访问
        private static readonly List<OutboundTransfer> _activeOutbound = [];
        //服务端按玩家统计的每秒放置次数，用于限流
        private static int[] _placeCountThisSecond;
        #endregion

        private sealed class PendingRequest
        {
            public int Who;
            public int WaitedTicks;
        }

        private sealed class OutboundTransfer
        {
            public int Who;
            public byte SessionId;
            public bool Compressed;
            public List<byte[]> Chunks;
            public int NextIndex;
            public bool HeaderSent;
        }

        private sealed class ApplyState
        {
            public List<TPRecord> Records;
            public int Index;
        }

        //单个TP在批量流中的解析结果，承载各自独立的负载切片以彻底隔离逐实体的反序列化
        private readonly struct TPRecord(string name, Point16 pos, byte w, byte h, byte[] payload)
        {
            public readonly string Name = name;
            public readonly Point16 Pos = pos;
            public readonly byte W = w;
            public readonly byte H = h;
            public readonly byte[] Payload = payload;
        }

        void IVaultLoader.LoadData() {
            _placeCountThisSecond = new int[256];
        }

        void IVaultLoader.UnLoadData() {
            VaultLoadingProgress.ReportNetwork(0f);
            VaultLoadingProgress.ResetChunkIdleTime();
            VaultLoadingProgress.NetworkInitializing = false;
            VaultLoadingProgress.NetworkTPLoaded = true;

            _pendingRequests.Clear();
            _activeOutbound.Clear();
            while (_readyTransfers.TryDequeue(out _)) { }
            while (_applyJobs.TryDequeue(out _)) { }
            _activeApply = null;
            _inboundChunks.Clear();
            _inboundActive = false;
            _inboundCompleted = false;
            _inboundExpectedChunks = -1;
            _networkLoadFinished = false;
            _placeCountThisSecond = null;
            _nextSessionId = 0;
        }

        #region InWorldNet
        /// <summary>
        /// 发送放置一个TP实体到世界中的消息
        /// </summary>
        public static void PlaceInWorldNetSend(Mod mod, int type, Point16 point) {
            //客户端发送同步请求到服务器
            ModPacket packet = mod.GetPacket();
            packet.Write((byte)MessageType.Handler_PlaceInWorld);
            packet.Write(type);
            packet.WritePoint16(point);
            packet.Send(); //发送到服务器
        }

        /// <summary>
        /// 接收放置一个TP实体到世界中的消息
        /// </summary>
        internal static void Handler_PlaceInWorld(Mod mod, BinaryReader reader, int whoAmI) {
            //读取放置方块的数据
            int tileType = reader.ReadInt32();
            Point16 point = reader.ReadPoint16();

            //坐标边界校验，拒绝越界坐标，防止异常输入污染世界
            if (point.X < 0 || point.X >= Main.maxTilesX || point.Y < 0 || point.Y >= Main.maxTilesY) {
                return;
            }

            //服务端按玩家进行每秒放置限流，缓解恶意刷包
            if (VaultUtils.isServer && whoAmI >= 0 && _placeCountThisSecond != null && whoAmI < _placeCountThisSecond.Length) {
                if (_placeCountThisSecond[whoAmI] >= MaxPlacementsPerSecond) {
                    return;
                }
                _placeCountThisSecond[whoAmI]++;
            }

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
        public static void Handler_TPRightClick(BinaryReader reader, int whoAmI) {
            Point16 point = reader.ReadPoint16();
            int playerIndex = reader.ReadInt32();

            //服务端以连接身份(whoAmI)作为权威交互者，避免客户端伪造他人身份触发交互
            if (VaultUtils.isServer) {
                playerIndex = whoAmI;
            }
            if (playerIndex < 0 || playerIndex >= Main.maxPlayers) {
                return;
            }

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
            modPacket.Write(playerIndex);//此处为服务端裁定后的权威玩家索引
            modPacket.Send(-1, playerIndex);
        }

        /// <summary>
        /// 发送TP网络数据
        /// </summary>
        public static void TileProcessorSendData(TileProcessor tp) {
            if (VaultUtils.isSinglePlayer) {
                return;
            }
            ModPacket modPacket = VaultMod.Instance.GetPacket();
            modPacket.Write((byte)MessageType.Handler_TileProcessorIndsData);
            modPacket.Write(tp.FullName);
            modPacket.WritePoint16(tp.Position);
            if (EnableSyncVarDeltaSync) {
                WriteEntityBlobDelta(tp, modPacket);
            }
            else {
                TileProcessorInstanceDoSendData(tp, modPacket);
            }
            modPacket.Send();
        }

        /// <summary>
        /// 该函数是对发送逻辑的二次封装，用于在让TP实体发送数据时进行通用的额外处理<br/>
        /// 写入 <see cref="TileProcessor.SendData(ModPacket)"/> 与全量 SyncVar 数据，与
        /// <see cref="TileProcessorInstanceDoReceiveData"/> 严格对应
        /// </summary>
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
        /// 该函数是对接收逻辑的二次封装，用于在让TP实体接收数据时进行通用的额外处理<br/>
        /// 读取 <see cref="TileProcessor.ReceiveData(BinaryReader, int)"/> 与全量 SyncVar 数据，与
        /// <see cref="TileProcessorInstanceDoSendData"/> 严格对应
        /// </summary>
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
            string loadenName = reader.ReadString();
            Point16 position = reader.ReadPoint16();

            //增量模式：负载以长度前缀封装，服务端可原样转发，接收使用独立子读取器彻底隔离
            if (EnableSyncVarDeltaSync) {
                int blobLen = reader.ReadInt32();
                byte[] blob = blobLen > 0 ? reader.ReadBytes(blobLen) : [];
                ApplyEntityBlobDelta(loadenName, position, blob, whoAmI);
                if (VaultUtils.isServer) {
                    ModPacket modPacket = VaultMod.Instance.GetPacket();
                    modPacket.Write((byte)MessageType.Handler_TileProcessorIndsData);
                    modPacket.Write(loadenName);
                    modPacket.WritePoint16(position);
                    modPacket.Write(blobLen);
                    modPacket.Write(blob);
                    modPacket.Send(-1, whoAmI);
                }
                return;
            }

            //全量模式（默认）：与旧行为完全一致
            TileProcessor tileProcessor;
            if (ByPositionGetTP(loadenName, position, out var tp)) {//使用字典查询节省性能
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

        //增量模式下逐实体写入：以长度前缀封装 SendData + SyncVar 增量，便于服务端原样转发与接收隔离
        private static void WriteEntityBlobDelta(TileProcessor tp, ModPacket modPacket) {
            if (modPacket.BaseStream is not MemoryStream stream) {
                TileProcessorInstanceDoSendData(tp, modPacket);//极端兜底，理论上ModPacket始终基于MemoryStream
                return;
            }

            long lenPos = stream.Position;
            modPacket.Write(0);//长度占位
            long start = stream.Position;
            try {
                tp.SendData(modPacket);
                SyncVarManager.WriteState(tp, modPacket, forceFull: false);
            } catch (Exception ex) {
                tp.SendCooldownTicks = 60;
                VaultMod.LoggerError($"{tp.FullName}:NullRef@SendDataDelta", $"{ex.Message}\n{ex.StackTrace}");
            }
            long end = stream.Position;
            int blobLen = (int)(end - start);
            stream.Position = lenPos;
            modPacket.Write(blobLen);//回填长度
            stream.Position = end;
        }

        //增量模式下逐实体应用：在独立子读取器上读取，避免任何错位影响外层数据流
        private static void ApplyEntityBlobDelta(string loadenName, Point16 position, byte[] blob, int whoAmI) {
            TileProcessor tileProcessor;
            if (ByPositionGetTP(loadenName, position, out var tp)) {
                tileProcessor = tp;
            }
            else {
                if (!TP_FullName_To_ID.TryGetValue(loadenName, out var tpID)) {
                    VaultMod.Instance.Logger.Error($"TileProcessorLoader-ReceiveDelta: Unknown TileProcessor Type: {loadenName}");
                    return;
                }
                tileProcessor = AddInWorld(tpID, position, null);
                if (tileProcessor == null) {
                    VaultMod.Instance.Logger.Error($"TileProcessorLoader-ReceiveDelta: Re-Establishment Failed: {loadenName}-Position [{position}]");
                    return;
                }
            }

            using MemoryStream ms = new(blob);
            using BinaryReader br = new(ms);
            try {
                tileProcessor.ReceiveData(br, whoAmI);
                //WriteState 在无字段变化时不会写入任何掩码，因此仅当负载仍有剩余字节时才读取增量
                //负载经长度前缀封装且各自独立，此判断安全且不会影响其他实体
                if (ms.Position < ms.Length) {
                    SyncVarManager.ReadState(tileProcessor, br);
                }
            } catch (Exception ex) {
                tileProcessor.SendCooldownTicks = 60;
                VaultMod.LoggerError($"{tileProcessor.FullName}:NullRef@ReceiveDataDelta", $"{ex.Message}\n{ex.StackTrace}");
            }
        }
        #endregion

        //我们需要先明白，在多人模式中地图加载是不完全的，对于客户端，完整的图格加载往往只在玩家生成点周围
        //所以如果不进行处理，客户端的TP实体就会在进入地图后消失，但服务端的不受影响
        //为了解决这个问题，我禁止了客户端自行杀死TP的更新，转而让服务端广播TP的死亡消息
        //目前来讲，这个问题被很好的解决了。但是要小心由这种解决方法所诞生出来的不安全字段，比如TP的Tile字段将有可能为默认值

        /// <summary>
        /// 由服务端向所有客户端广播这个TP实体死亡的消息
        /// </summary>
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

        #region 加入世界批量同步：客户端请求
        /// <summary>
        /// 让客户端向服务器发出数据请求，请求一个完整的TP数据链
        /// </summary>
        public static void ClientRequest_TPData_Send() {
            if (!VaultUtils.isClient) {
                return;
            }

            //标记开始网络加载：归零进度、清除完成标志、重置计时器
            //注意：本地TP的清理不在此处进行，而是延后到主线程应用阶段开始时，
            //以避免与后台的世界加载任务(LoadWorldTileProcessor)产生竞争
            _networkLoadFinished = false;
            //清理上一次进图可能残留的入站会话与待应用任务，确保每次进图都从干净状态开始
            //（例如上一世界在收齐数据后、应用前断开，留下的陈旧任务不应被带入新世界）
            ResetClientInboundState();
            VaultLoadingProgress.BeginNetworkLoad();

            Task.Run(async () => {
                try {//开启一个子线程，在客户端的TP加载好了后再发送数据链请求
                    await VaultUtils.WaitUntilAsync(() => LoadenTP, 50, 10000);//最多等10秒
                } catch (TaskCanceledException) {
                    VaultMod.Instance.Logger.Error("[ClientRequest_TPData_Send] The waiting for TileProcessorLoader.LoadenTP to complete has timed out.");
                } catch (Exception ex) {
                    VaultMod.Instance.Logger.Error($"[ClientRequest_TPData_Send] An exception occurred while waiting for TileProcessorLoader.LoadenTP: {ex.Message}");
                }

                try {
                    VaultLoadingProgress.ReportNetwork(VaultLoadingProgress.NetworkRequestSent);
                    ModPacket modPacket = VaultMod.Instance.GetPacket();
                    modPacket.Write((byte)MessageType.SendToClient_TPData);
                    modPacket.Send();
                } catch (Exception ex) {
                    VaultMod.Instance.Logger.Error($"[ClientRequest_TPData_Send] An error occurred while executing: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// 当前客户端的拼图数据流是否尚未接收完整<br/>
        /// 供 <see cref="VaultLoadingProgress.Tick"/> 中的看门狗判断是否需要重新请求数据链
        /// </summary>
        internal static bool IsChunkStreamIncomplete => _inboundActive && !_inboundCompleted
            && _inboundExpectedChunks > 0 && _inboundChunks.Count < _inboundExpectedChunks;

        /// <summary>
        /// 重新向服务器请求一条完整的TP数据链，用于推送流中断后的卡顿恢复<br/>
        /// 服务端将以一个全新的会话编号回应，客户端据此自动丢弃旧会话的残留数据块
        /// </summary>
        internal static void ResendTPDataChainRequest() {
            ModPacket modPacket = VaultMod.Instance.GetPacket();
            modPacket.Write((byte)MessageType.SendToClient_TPData);
            modPacket.Send();

            VaultMod.Instance.Logger.Warn($"[TP-Net] TP data stream stalled at " +
                $"{_inboundChunks.Count}/{_inboundExpectedChunks} chunks. Re-requesting full TP data chain.");
        }
        #endregion

        #region 加入世界批量同步：服务端出站
        //另一个需要注意的概念是，世界数据存储在服务端上，退出世界、进入世界的相关钩子如OnWorldLoad和OnWorldUnLoad不会在客户端上运行
        //而存档数据会在服务端关闭后自动写入存档文件，这意味着任意客户端的退出都不能主动更新自己本地的存档数据
        //所以，不需要试图在卸载世界或者玩家退出服务器时进行任何世界网络数据的同步请求，服务器自身在开启和关闭时便会处理这一切

        /// <summary>
        /// 接收客户端的TP数据链请求<br/>
        /// 仅登记一个待处理请求，真正的序列化推送由服务端每帧的 <see cref="NetworkPump"/> 在本地TP加载完成后驱动，
        /// 避免在网络线程上阻塞等待，也避免在后台线程序列化"活着的"世界数据
        /// </summary>
        public static void SendToClient_TPData(int whoAmI) {
            if (!VaultUtils.isServer) {
                return;
            }
            _pendingRequests.Add(new PendingRequest { Who = whoAmI, WaitedTicks = 0 });
        }

        //服务端每帧出站泵：处理待发请求 -> 提升已就绪的传输 -> 分帧限流推送
        private static void PumpServerOutbound() {
            //每秒重置放置限流计数
            if (Main.GameUpdateCount % 60 == 0 && _placeCountThisSecond != null) {
                Array.Clear(_placeCountThisSecond, 0, _placeCountThisSecond.Length);
            }

            //1. 本地TP加载完成后，在主线程上序列化对应客户端的快照
            for (int i = _pendingRequests.Count - 1; i >= 0; i--) {
                PendingRequest req = _pendingRequests[i];
                if (LoadenTP) {
                    _pendingRequests.RemoveAt(i);
                    DispatchClientSnapshot(req.Who);
                }
                else if (++req.WaitedTicks > MaxBufferWaitingTimeMark) {
                    _pendingRequests.RemoveAt(i);
                    VaultMod.Instance.Logger.Warn($"[TP-Net] Client {req.Who} TP request timed out waiting for local TP load.");
                    SendResetToClient(req.Who);
                }
            }

            //2. 将后台压缩切块完成的传输提升为活跃推送任务
            while (_readyTransfers.TryDequeue(out OutboundTransfer rt)) {
                _activeOutbound.Add(rt);
            }

            //3. 分帧限流推送
            for (int i = _activeOutbound.Count - 1; i >= 0; i--) {
                OutboundTransfer t = _activeOutbound[i];
                if (!t.HeaderSent) {
                    SendChunkHeader(t);
                    t.HeaderSent = true;
                }

                int budget = MaxChunksPerTickPerClient;
                while (budget-- > 0 && t.NextIndex < t.Chunks.Count) {
                    SendChunk(t, t.NextIndex);
                    t.NextIndex++;
                }

                if (t.NextIndex >= t.Chunks.Count) {
                    _activeOutbound.RemoveAt(i);
                }
            }
        }

        //在主线程上序列化某个客户端的TP快照，随后将压缩与切块交给后台线程（纯CPU、只处理不可变字节）
        private static void DispatchClientSnapshot(int whoAmI) {
            VaultLoadingProgress.ResetNetworkTimers();

            byte[] fullBytes;
            int tpCount;
            VaultLoadingProgress.NetworkInitializing = true;
            try {
                fullBytes = SerializeActiveTPs(out tpCount);
            } finally {
                VaultLoadingProgress.NetworkInitializing = false;
            }

            if (tpCount <= 0 || fullBytes.Length == 0) {
                SendResetToClient(whoAmI);
                return;
            }

            byte sessionId = _nextSessionId++;
            Task.Run(() => {
                try {
                    bool compressed = false;
                    byte[] payload = fullBytes;
                    byte[] comp = Compress(fullBytes);
                    if (comp.Length < fullBytes.Length) {//仅在确实更小时才采用压缩结果
                        payload = comp;
                        compressed = true;
                    }
                    List<byte[]> chunks = VaultUtils.SplitBytes(payload, MaxStreamSize);
                    _readyTransfers.Enqueue(new OutboundTransfer {
                        Who = whoAmI,
                        SessionId = sessionId,
                        Compressed = compressed,
                        Chunks = chunks
                    });
                } catch (Exception ex) {
                    VaultMod.Instance.Logger.Error($"[TP-Net] Failed to compress/split TP data for client {whoAmI}: {ex.Message}");
                }
            });
        }

        //在主线程上将所有可同步的活跃TP序列化为带帧长前缀的字节流（不含ModPacket内部包头）
        private static byte[] SerializeActiveTPs(out int tpCount) {
            List<TileProcessor> activeTPs = TP_InWorld.Where(tp => tp != null && tp.Active && tp.LoadenWorldSendData).ToList();
            tpCount = activeTPs.Count;
            if (tpCount <= 0) {
                return [];
            }

            //借用ModPacket作为序列化缓冲（因为TileProcessor.SendData接受ModPacket参数）
            ModPacket fullPacket = VaultMod.Instance.GetPacket();
            if (fullPacket.BaseStream is not MemoryStream stream) {
                VaultMod.Instance.Logger.Error("[SerializeActiveTPs] ModPacket.BaseStream is not a MemoryStream, cannot serialize TP data.");
                tpCount = 0;
                return [];
            }
            //ModPacket创建时会预写入tModLoader的包头，必须跳过，这里记录有效数据起点
            long dataStartPos = stream.Position;
            fullPacket.Write(tpCount);

            foreach (TileProcessor tp in activeTPs) {
                fullPacket.Write(TP_START_MARKER);
                //写入一个块长度占位，稍后回填；块长度覆盖从此处之后到本TP数据末尾的全部字节
                long lenPos = stream.Position;
                fullPacket.Write(0);
                long contentStart = stream.Position;

                fullPacket.Write(tp.FullName);
                fullPacket.WritePoint16(tp.Position);
                //宽高不太可能超过255，转化为byte（物块格子数）节省空间
                fullPacket.Write((byte)(tp.Width / 16));
                fullPacket.Write((byte)(tp.Height / 16));
                //与接收侧严格对称：SendData + 全量 SyncVar，且自带逐实体异常隔离
                TileProcessorInstanceDoSendData(tp, fullPacket);

                long contentEnd = stream.Position;
                int blockLength = (int)(contentEnd - contentStart);
                stream.Position = lenPos;
                fullPacket.Write(blockLength);
                stream.Position = contentEnd;
            }

            byte[] streamBytes = stream.ToArray();
            int len = (int)(streamBytes.Length - dataStartPos);
            byte[] fullBytes = new byte[len];
            Array.Copy(streamBytes, dataStartPos, fullBytes, 0, len);
            return fullBytes;
        }

        private static void SendResetToClient(int whoAmI) {
            ModPacket modPacket = VaultMod.Instance.GetPacket();
            modPacket.Write((byte)MessageType.GetServer_ResetTPDataChunkNet);
            modPacket.Send(whoAmI);
        }

        private static void SendChunkHeader(OutboundTransfer t) {
            ModPacket header = VaultMod.Instance.GetPacket();
            header.Write((byte)MessageType.GetServer_MaxTPDataChunkCount);
            header.Write(t.SessionId);
            header.Write((byte)(t.Compressed ? FLAG_COMPRESSED : 0));
            header.Write(t.Chunks.Count);//int，避免旧实现 ushort 截断溢出
            header.Send(t.Who);
        }

        private static void SendChunk(OutboundTransfer t, int index) {
            byte[] chunk = t.Chunks[index];
            ModPacket chunkPacket = VaultMod.Instance.GetPacket();
            chunkPacket.Write((byte)MessageType.GetServer_TPDataChunk);
            chunkPacket.Write(t.SessionId);
            chunkPacket.Write(index);
            chunkPacket.Write(chunk.Length);
            chunkPacket.Write(chunk);
            chunkPacket.Send(t.Who);
        }
        #endregion

        #region 加入世界批量同步：客户端入站
        //客户端接收协议头：会话编号、压缩标志、数据块总数，并据此开启新的入站会话
        internal static void GetServer_MaxTPDataChunkCount(BinaryReader reader) {
            if (!VaultUtils.isClient) {
                return;
            }

            byte sessionId = reader.ReadByte();
            bool compressed = (reader.ReadByte() & FLAG_COMPRESSED) != 0;
            int chunkCount = reader.ReadInt32();
            BeginInboundSession(sessionId, compressed, chunkCount);
        }

        private static void BeginInboundSession(byte sessionId, bool compressed, int chunkCount) {
            //若本次进图的网络加载已完成，则忽略迟到的旧会话头（卡顿重传与原会话同时返回的罕见竞态）
            //避免在游戏过程中被一个过期会话触发整图重载
            if (_networkLoadFinished) {
                return;
            }

            //新会话开始：清理旧的残留数据块（重传/旧会话），从干净状态收集
            _inboundChunks.Clear();
            _inboundSessionId = sessionId;
            _inboundCompressed = compressed;
            _inboundExpectedChunks = chunkCount;
            _inboundActive = true;
            _inboundCompleted = false;

            VaultLoadingProgress.EnterPhase(LoadingPhase.ReceivingChunks);
            VaultLoadingProgress.ReportNetwork(VaultLoadingProgress.NetworkHeaderReceived);
            VaultLoadingProgress.ResetChunkIdleTime();
            VaultLoadingProgress.ResetNetworkTimers();

            if (chunkCount <= 0) {//理论上空世界会走Reset分支，这里仅作防御
                OnInboundComplete();
            }
        }

        //客户端被动接收服务端推送的拼图数据
        internal static void GetServer_TPDataChunk(BinaryReader reader) {
            if (!VaultUtils.isClient) {
                return;
            }

            byte sessionId = reader.ReadByte();
            int index = reader.ReadInt32();
            int length = reader.ReadInt32();
            byte[] data = reader.ReadBytes(length);

            //丢弃过期/异源会话以及已完成会话的数据块
            if (!_inboundActive || sessionId != _inboundSessionId || _inboundCompleted) {
                return;
            }
            if (_inboundExpectedChunks < 0) {
                VaultMod.Instance.Logger.Error("[TP-Net] Received chunk before header; ignoring.");
                return;
            }
            if (_inboundChunks.ContainsKey(index)) {//去重
                return;
            }

            _inboundChunks[index] = data;

            //有进展即重置网络计时器，使硬超时退化为"长时间无任何进展"的兜底，而非"总耗时上限"
            VaultLoadingProgress.ResetChunkIdleTime();
            VaultLoadingProgress.ResetNetworkTimers();
            VaultLoadingProgress.ReportChunks(_inboundChunks.Count, _inboundExpectedChunks);

            if (_inboundChunks.Count >= _inboundExpectedChunks) {
                OnInboundComplete();
            }
        }

        //数据块收齐：在主线程上快照并清空共享字典，随后把不可变缓冲交给后台线程解压与解析
        private static void OnInboundComplete() {
            if (_inboundCompleted) {
                return;
            }
            _inboundCompleted = true;
            _inboundActive = false;

            int expected = _inboundExpectedChunks;
            bool compressed = _inboundCompressed;
            byte[] combined = CombineChunks(expected);
            _inboundChunks.Clear();

            VaultLoadingProgress.NetworkInitializing = true;
            VaultLoadingProgress.EnterPhase(LoadingPhase.ApplyingNetworkData);
            VaultLoadingProgress.ReportNetwork(VaultLoadingProgress.NetworkCombining);

            if (combined.Length == 0) {//空数据，投递空任务让应用泵以统一路径收尾
                _applyJobs.Enqueue([]);
                return;
            }

            Task.Run(() => {
                try {
                    byte[] full = compressed ? Decompress(combined) : combined;
                    List<TPRecord> records = ParseInboundBuffer(full);
                    _applyJobs.Enqueue(records);
                } catch (Exception ex) {
                    VaultMod.Instance.Logger.Error($"[TP-Net] Failed to parse inbound TP data: {ex.Message}");
                    _applyJobs.Enqueue([]);//即便失败也要让客户端解除阻塞
                }
            });
        }

        private static byte[] CombineChunks(int expected) {
            using MemoryStream ms = new();
            for (int i = 0; i < expected; i++) {
                if (_inboundChunks.TryGetValue(i, out byte[] data)) {
                    ms.Write(data, 0, data.Length);
                }
                else {
                    VaultMod.Instance.Logger.Warn($"[TP-Net] Missing chunk #{i}/{expected} during combine.");
                }
            }
            return ms.ToArray();
        }

        //后台线程：把批量字节流解析为一组带独立负载切片的记录，全程不触碰世界状态
        private static List<TPRecord> ParseInboundBuffer(byte[] full) {
            List<TPRecord> records = [];
            using MemoryStream ms = new(full);
            using BinaryReader reader = new(ms);

            int tpCount = reader.ReadInt32();
            if (tpCount < 0 || tpCount > MaxTPInWorldCount) {
                VaultMod.Instance.Logger.Warn($"[TP-NetParse] Received invalid TP count: {tpCount}, aborting parse.");
                return records;
            }

            for (int i = 0; i < tpCount; i++) {
                if (ms.Position + 4 > ms.Length) {
                    break;
                }

                uint marker = reader.ReadUInt32();
                if (marker != TP_START_MARKER) {
                    VaultMod.Instance.Logger.Warn($"[TP-NetParse] Invalid marker at #{i}: {marker}, resyncing to next marker.");
                    if (!SkipToNextMarker(reader)) {
                        break;
                    }
                    continue;//下一轮迭代读取定位到的marker
                }

                if (ms.Position + 4 > ms.Length) {
                    break;
                }
                int blockLength = reader.ReadInt32();
                long contentStart = ms.Position;
                long nextBlock = contentStart + blockLength;
                if (blockLength < 0 || nextBlock > ms.Length) {
                    VaultMod.Instance.Logger.Warn($"[TP-NetParse] Bad block length {blockLength} at #{i}, aborting.");
                    break;
                }

                try {
                    string name = reader.ReadString();
                    Point16 pos = reader.ReadPoint16();
                    byte w = reader.ReadByte();
                    byte h = reader.ReadByte();
                    int payloadLen = (int)(nextBlock - ms.Position);
                    byte[] payload = payloadLen > 0 ? reader.ReadBytes(payloadLen) : [];
                    records.Add(new TPRecord(name, pos, w, h, payload));
                } catch (Exception ex) {
                    VaultMod.Instance.Logger.Warn($"[TP-NetParse] Failed to parse TP block #{i}: {ex.Message}");
                } finally {
                    ms.Position = nextBlock;//无论如何都对齐到下一个块，保证后续实体不受影响
                }
            }

            return records;
        }

        //客户端每帧应用泵：在主线程上分帧应用解析出的记录，避免一次性应用造成卡顿，也避免后台改写世界
        private static void PumpClientApply() {
            if (_activeApply == null) {
                if (!_applyJobs.TryDequeue(out List<TPRecord> records)) {
                    return;
                }
                //本次进图若已完成（来自卡顿重传等冗余会话的任务），直接丢弃，避免整图被重复清空与重载
                //应用是严格串行的：上一份任务完成时会先置位 _networkLoadFinished，因此这里能可靠拦截后到的冗余任务
                if (_networkLoadFinished) {
                    return;
                }
                //在主线程、本地加载已完成之后，于应用前清理本地扫描出的TP，避免与世界加载任务竞争
                InitializeWorldTP();
                _activeApply = new ApplyState { Records = records, Index = 0 };
                VaultLoadingProgress.NetworkInitializing = true;
                VaultLoadingProgress.EnterPhase(LoadingPhase.ApplyingNetworkData);
            }

            int budget = MaxApplyPerTick;
            while (budget-- > 0 && _activeApply.Index < _activeApply.Records.Count) {
                ApplyRecord(_activeApply.Records[_activeApply.Index]);
                _activeApply.Index++;
            }

            VaultLoadingProgress.ResetNetworkTimers();//应用有进展，刷新计时器
            VaultLoadingProgress.ReportApply(_activeApply.Index, _activeApply.Records.Count);

            if (_activeApply.Index >= _activeApply.Records.Count) {
                _activeApply = null;
                CompleteClientInbound();
            }
        }

        //在主线程上应用单条记录：定位或新建TP，并在其独立负载切片上反序列化
        private static void ApplyRecord(TPRecord record) {
            string loadenName = record.Name;
            Point16 position = record.Pos;

            using MemoryStream ms = new(record.Payload);
            using BinaryReader pr = new(ms);

            //先检查字典中是否已有该 TileProcessor
            if (ByPositionGetTP(loadenName, position, out TileProcessor tp) && tp.FullName == loadenName) {
                TileProcessorInstanceDoReceiveData(tp, pr, -1);
                return;
            }

            if (!TryGetTpID(loadenName, out int tpID)) {
                VaultMod.Instance.Logger.Warn($"[TP-NetApply] No corresponding TileProcessor type: {loadenName}-position[{position}], skip.");
                return;
            }

            if (ByPositionGetTP(tpID, position.X, position.Y, out TileProcessor existingTP) && existingTP.FullName == loadenName) {
                TileProcessorInstanceDoReceiveData(existingTP, pr, -1);
                return;
            }

            //如果找不到，尝试新建（注意一个位置可能存在多个TP实体，这些字典到此并不完全可靠）
            if (TP_ID_To_Instance.TryGetValue(tpID, out TileProcessor template) && template.FullName == loadenName) {
                TileProcessor newTP;
                if (template.ID == TPUtils.GetID<UnknowTP>()) {//未知TP强制生成未知TP
                    newTP = UnknowTP.Place(position, [], "unknown", "unknown");
                }
                else {
                    newTP = AddInWorld(template.TargetTileID, position, null);
                }

                if (newTP != null && newTP.FullName == loadenName) {
                    //客户端物块加载不完整，生成的TP尺寸可能不正确，用服务端数据覆盖矫正
                    newTP.Width = record.W * 16;
                    newTP.Height = record.H * 16;
                    TileProcessorInstanceDoReceiveData(newTP, pr, -1);
                    return;
                }
            }

            VaultMod.Instance.Logger.Warn($"[TP-NetApply] No corresponding TileProcessor instance for {loadenName}-position[{position}], skip.");
        }

        //清理客户端入站会话与待应用任务，使每次进图（或服务端重置）都从干净状态开始
        private static void ResetClientInboundState() {
            _inboundActive = false;
            _inboundCompleted = false;
            _inboundExpectedChunks = -1;
            _inboundChunks.Clear();
            _activeApply = null;
            while (_applyJobs.TryDequeue(out _)) { }//清空可能残留的旧任务
        }

        //客户端收到服务端的空数据重置：清理本地TP并直接结束网络加载
        private static void HandleServerReset() {
            ResetClientInboundState();
            InitializeWorldTP();
            CompleteClientInbound();
        }

        private static void CompleteClientInbound() {
            VaultLoadingProgress.ReportNetwork(1f);
            VaultLoadingProgress.NetworkInitializing = false;
            VaultLoadingProgress.NetworkTPLoaded = true;//标记为true，表明网络加载完成
            VaultLoadingProgress.EnterPhase(LoadingPhase.Complete);
            VaultLoadingProgress.ResetChunkIdleTime();
            _inboundExpectedChunks = -1;
            _inboundCompleted = false;
            _inboundActive = false;
            _networkLoadFinished = true;//本次进图网络加载完成，后续迟到的旧会话头将被忽略
        }

        /// <summary>
        /// 跳到下一个标记节点，定位成功后流位置停在该marker起始处<br/>
        /// 仅作为帧长前缀机制之外的兜底重同步手段
        /// </summary>
        private static bool SkipToNextMarker(BinaryReader reader) {
            byte[] markerBytes = BitConverter.GetBytes(TP_START_MARKER);
            int matchIndex = 0;
            Stream stream = reader.BaseStream;

            while (stream.Position < stream.Length) {
                byte currentByte = reader.ReadByte();
                if (currentByte == markerBytes[matchIndex]) {
                    matchIndex++;
                    if (matchIndex == markerBytes.Length) {
                        stream.Position -= markerBytes.Length;//回退到marker起始处
                        return true;
                    }
                }
                else {
                    //部分匹配失败，检查当前字节是否为新匹配的起点
                    matchIndex = currentByte == markerBytes[0] ? 1 : 0;
                }
            }

            return false;
        }
        #endregion

        #region 压缩
        private static byte[] Compress(byte[] data) {
            using MemoryStream output = new();
            using (DeflateStream deflate = new(output, CompressionLevel.Optimal, true)) {
                deflate.Write(data, 0, data.Length);
            }
            return output.ToArray();
        }

        private static byte[] Decompress(byte[] data) {
            using MemoryStream input = new(data);
            using DeflateStream deflate = new(input, CompressionMode.Decompress);
            using MemoryStream output = new();
            deflate.CopyTo(output);
            return output.ToArray();
        }
        #endregion

        /// <summary>
        /// TP网络系统的每帧驱动入口，由 <see cref="TileProcessorSystem.PostUpdateEverything"/> 在
        /// CanRunByWorld 门控之前调用<br/>
        /// 服务端在此分帧推送出站数据，客户端在此分帧应用入站数据
        /// </summary>
        internal static void NetworkPump() {
            if (VaultUtils.isSinglePlayer) {
                return;
            }
            if (VaultUtils.isServer) {
                PumpServerOutbound();
            }
            else if (VaultUtils.isClient) {
                PumpClientApply();
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
                        HandleServerReset();//这个消息由服务器单方面发送，只由客户端来接收处理
                    }
                    break;
                case MessageType.Handler_TPRightClick:
                    Handler_TPRightClick(reader, whoAmI);
                    break;
            }
        }
    }
}
