using InnoVault.GameContent;
using InnoVault.TileProcessors;
using Microsoft.Xna.Framework;
using Terraria;

namespace InnoVault.GameSystem
{
    /// <summary>
    /// 描述世界加载/保存流程当前所处的阶段
    /// </summary>
    public enum LoadingPhase
    {
        /// <summary>
        /// 无加载活动
        /// </summary>
        Inactive,
        /// <summary>
        /// （单机/服务端）等待世界存档数据加载完成
        /// </summary>
        WaitingWorldData,
        /// <summary>
        /// 正在扫描世界物块以收集TP
        /// </summary>
        ScanningWorld,
        /// <summary>
        /// 正在向世界中放置TP实体
        /// </summary>
        PlacingProcessors,
        /// <summary>
        /// 正在应用TP的存档数据
        /// </summary>
        LoadingWorldData,
        /// <summary>
        /// （客户端）已向服务器请求TP数据链
        /// </summary>
        RequestingNetwork,
        /// <summary>
        /// （客户端）正在接收TP数据块流
        /// </summary>
        ReceivingChunks,
        /// <summary>
        /// （客户端）正在合并并应用网络数据
        /// </summary>
        ApplyingNetworkData,
        /// <summary>
        /// 正在保存世界
        /// </summary>
        Saving,
        /// <summary>
        /// 加载完成
        /// </summary>
        Complete
    }

    /// <summary>
    /// 集中管理世界加载/保存流程的状态与进度，作为整套加载过程的唯一事实来源<br/>
    /// 取代了过去散落在 <see cref="TileProcessorLoader"/>、<see cref="TileProcessorNetWork"/>、
    /// <see cref="VaultSave"/> 中的 volatile float 进度字段、布尔状态标志与网络看门狗计时器<br/>
    /// 进度相关字段均为线程安全，可被后台加载线程写入、主线程读取
    /// </summary>
    public static class VaultLoadingProgress
    {
        #region 命名进度分段（0~1）
        //这些常量取代了过去硬编码在加载流程里的进度魔法数字，数值与旧实现一一对应（0~1 对应旧的 0~100）

        /// <summary>
        /// 本地加载：等待世界数据阶段的进度上限（旧 WorldLoadProgress = 10）
        /// </summary>
        public const float LocalWaitingWorldDataEnd = 0.10f;
        /// <summary>
        /// 本地加载：物块扫描阶段的进度上限（旧 WorldLoadProgress = 15）
        /// </summary>
        public const float LocalScanningEnd = 0.15f;
        /// <summary>
        /// 本地加载：TP放置阶段的进度上限（旧 WorldLoadProgress = 15 + i*75/count 的终值 90）
        /// </summary>
        public const float LocalPlacingEnd = 0.90f;
        /// <summary>
        /// 网络加载：数据请求已发出时的进度（旧 NetworkLoadProgress = 2）
        /// </summary>
        public const float NetworkRequestSent = 0.02f;
        /// <summary>
        /// 网络加载：收到数据块总数(Header)时的进度（旧 NetworkLoadProgress = 10）
        /// </summary>
        public const float NetworkHeaderReceived = 0.10f;
        /// <summary>
        /// 网络加载：所有数据块接收完成时的进度（旧 NetworkLoadProgress = 10 + recv/total*80 的终值 90）
        /// </summary>
        public const float NetworkChunksEnd = 0.90f;
        /// <summary>
        /// 网络加载：数据块合并完成、开始应用时的进度（旧 NetworkLoadProgress = 96）
        /// </summary>
        public const float NetworkCombining = 0.96f;
        /// <summary>
        /// 客户端总进度合成中本地加载所占权重
        /// </summary>
        public const float LocalWeight = 0.4f;
        /// <summary>
        /// 客户端总进度合成中网络加载所占权重
        /// </summary>
        public const float NetworkWeight = 0.6f;
        //客户端拼图数据流卡顿后触发重传的等待帧数
        private const int ChunkStallResendTicks = 300;
        #endregion

        #region 权威状态（线程安全）
        private static volatile int phase = (int)LoadingPhase.Inactive;
        private static volatile float localProgress;
        private static volatile float networkProgress;
        private static volatile bool worldDataLoaded;          //旧 VaultSave.LoadenWorld
        private static volatile bool worldSaved = true;        //旧 VaultSave.SavedWorld
        private static volatile bool localTPLoaded;            //旧 TileProcessorLoader.LoadenTP
        private static volatile bool networkTPLoaded = true;   //旧 TileProcessorNetWork.LoadenTPByNetWork
        private static volatile bool networkInitializing;      //旧 TileProcessorNetWork.InitializeWorld

        //网络看门狗计时器，仅在主线程（包处理与逻辑帧）中访问
        private static int initializeWorldTickCounter;
        private static int loadTPNetworkTickCounter;
        private static int netChunkIdleTime;

        /// <summary>
        /// 当前加载阶段
        /// </summary>
        public static LoadingPhase Phase {
            get => (LoadingPhase)phase;
            internal set => phase = (int)value;
        }
        /// <summary>
        /// 本地世界TP加载进度，范围 0~1
        /// </summary>
        public static float LocalProgress {
            get => localProgress;
            internal set => localProgress = value;
        }
        /// <summary>
        /// 网络TP加载进度，范围 0~1
        /// </summary>
        public static float NetworkProgress {
            get => networkProgress;
            internal set => networkProgress = value;
        }
        /// <summary>
        /// 世界存档数据是否已加载完成
        /// </summary>
        public static bool WorldDataLoaded {
            get => worldDataLoaded;
            internal set => worldDataLoaded = value;
        }
        /// <summary>
        /// 世界是否已保存完成
        /// </summary>
        public static bool WorldSaved {
            get => worldSaved;
            internal set => worldSaved = value;
        }
        /// <summary>
        /// 本地TP实体是否已加载进世界
        /// </summary>
        public static bool LocalTPLoaded {
            get => localTPLoaded;
            internal set => localTPLoaded = value;
        }
        /// <summary>
        /// 客户端是否已完成TP的网络加载
        /// </summary>
        public static bool NetworkTPLoaded {
            get => networkTPLoaded;
            internal set => networkTPLoaded = value;
        }
        /// <summary>
        /// 当前是否正在进行世界初始化的网络工作<br/>
        /// 服务端推送TP数据、客户端合并应用收到的数据期间均为 <see langword="true"/>
        /// </summary>
        public static bool NetworkInitializing {
            get => networkInitializing;
            internal set => networkInitializing = value;
        }
        #endregion

        #region 派生只读接口（供UI读取）
        /// <summary>
        /// 当前是否正在进行世界加载，用于UI判断是否显示加载界面<br/>
        /// 客户端关注网络加载是否完成，单机/服务端关注本地TP是否加载完成
        /// </summary>
        public static bool IsLoading => VaultUtils.isClient ? !NetworkTPLoaded : !LocalTPLoaded;
        /// <summary>
        /// 当前是否正在保存世界
        /// </summary>
        public static bool IsSaving => !WorldSaved;
        /// <summary>
        /// 当前是否有加载或保存活动
        /// </summary>
        public static bool IsActive => IsLoading || IsSaving;
        /// <summary>
        /// 网络加载是否已明显滞后，用于UI显示等待提示（例如感叹号）<br/>
        /// 当网络加载等待超过最大缓冲时间的三分之二时判定为滞后
        /// </summary>
        public static bool IsStalled => !VaultUtils.isSinglePlayer
            && loadTPNetworkTickCounter >= TileProcessorNetWork.MaxBufferWaitingTimeMark * 2 / 3;
        /// <summary>
        /// 当前加载总进度，范围 0~1<br/>
        /// 客户端按权重合成本地与网络进度，单机/服务端仅取本地进度
        /// </summary>
        public static float Overall {
            get {
                float value = VaultUtils.isClient
                    ? localProgress * LocalWeight + networkProgress * NetworkWeight
                    : localProgress;
                return MathHelper.Clamp(value, 0f, 1f);
            }
        }
        #endregion

        #region 上报接口（供 loader / network / save 调用）
        /// <summary>
        /// 进入指定加载阶段
        /// </summary>
        internal static void EnterPhase(LoadingPhase newPhase) => Phase = newPhase;

        /// <summary>
        /// 直接上报本地加载进度，自动裁剪到 0~1
        /// </summary>
        internal static void ReportLocal(float fraction) => localProgress = MathHelper.Clamp(fraction, 0f, 1f);

        /// <summary>
        /// 直接上报网络加载进度，自动裁剪到 0~1
        /// </summary>
        internal static void ReportNetwork(float fraction) => networkProgress = MathHelper.Clamp(fraction, 0f, 1f);

        /// <summary>
        /// 上报TP放置进度，自动映射到 [<see cref="LocalScanningEnd"/>, <see cref="LocalPlacingEnd"/>] 区间
        /// </summary>
        internal static void ReportPlacement(int index, int count) {
            if (count <= 0) {
                return;
            }
            localProgress = MathHelper.Lerp(LocalScanningEnd, LocalPlacingEnd, index / (float)count);
        }

        /// <summary>
        /// 上报网络数据块接收进度，自动映射到 [<see cref="NetworkHeaderReceived"/>, <see cref="NetworkChunksEnd"/>] 区间
        /// </summary>
        internal static void ReportChunks(int received, int total) {
            if (total <= 0) {
                return;
            }
            networkProgress = MathHelper.Lerp(NetworkHeaderReceived, NetworkChunksEnd, received / (float)total);
        }

        /// <summary>
        /// 上报网络数据应用进度，自动映射到 [<see cref="NetworkCombining"/>, 1] 区间
        /// </summary>
        internal static void ReportApply(int index, int count) {
            if (count <= 0) {
                return;
            }
            networkProgress = MathHelper.Lerp(NetworkCombining, 1f, index / (float)count);
        }

        /// <summary>
        /// 在世界本地加载开始时调用：进入等待阶段、归零本地进度<br/>
        /// 本地加载完成标志由后台加载线程在真正开始加载时清除，以与旧实现的时序保持一致
        /// </summary>
        internal static void BeginLocalLoad() {
            EnterPhase(LoadingPhase.WaitingWorldData);
            localProgress = 0f;
        }

        /// <summary>
        /// 在客户端发起TP网络加载时调用：进入请求阶段、归零网络进度、清除网络加载完成标志、重置计时器
        /// </summary>
        internal static void BeginNetworkLoad() {
            EnterPhase(LoadingPhase.RequestingNetwork);
            networkProgress = 0f;
            networkTPLoaded = false;
            ResetNetworkTimers();
        }

        /// <summary>
        /// 重置加载会话的阶段与进度，在世界卸载时调用，不影响布尔状态标志
        /// </summary>
        internal static void ResetSession() {
            EnterPhase(LoadingPhase.Inactive);
            localProgress = 0f;
            networkProgress = 0f;
        }

        /// <summary>
        /// 重置网络初始化与TP加载超时计时器
        /// </summary>
        internal static void ResetNetworkTimers() {
            initializeWorldTickCounter = 0;
            loadTPNetworkTickCounter = 0;
        }

        /// <summary>
        /// 重置拼图数据流卡顿计时器
        /// </summary>
        internal static void ResetChunkIdleTime() => netChunkIdleTime = 0;
        #endregion

        #region 每帧驱动与网络看门狗
        /// <summary>
        /// 加载系统的每帧驱动入口，由 <see cref="TileProcessorSystem"/> 调用<br/>
        /// 必须在任何 CanRunByWorld 门控之前调用，以保证客户端网络加载期间看门狗依旧工作
        /// </summary>
        internal static void Tick() {
            if (VaultUtils.isSinglePlayer) {
                return;
            }

            //网络初始化阶段超时检测
            if (NetworkInitializing && ++initializeWorldTickCounter > TileProcessorNetWork.MaxBufferWaitingTimeMark) {
                initializeWorldTickCounter = 0;
                NetworkInitializing = false;
                EmitTimeoutWarning();
            }

            //TP数据网络加载超时检测
            if (!NetworkTPLoaded && ++loadTPNetworkTickCounter > TileProcessorNetWork.MaxBufferWaitingTimeMark) {
                loadTPNetworkTickCounter = 0;
                NetworkTPLoaded = true;
                EmitTimeoutWarning();
            }

            //如果客户端长时间没有收到新的chunk数据，说明推送流可能中断，重新请求完整的TP数据链
            if (VaultUtils.isClient && !NetworkTPLoaded
                && TileProcessorNetWork.IsChunkStreamIncomplete
                && ++netChunkIdleTime > ChunkStallResendTicks) {
                netChunkIdleTime = 0;
                TileProcessorNetWork.ResendTPDataChainRequest();
            }
        }

        private static void EmitTimeoutWarning() {
            string timeoutMsg = WorldLoadingText.NetWaringTimeoutMsg.Value;
            Main.NewText(timeoutMsg, Color.Red);
            VaultMod.Instance.Logger.Warn(timeoutMsg);
        }
        #endregion
    }
}
