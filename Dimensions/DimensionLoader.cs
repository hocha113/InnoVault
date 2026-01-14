using InnoVault.GameSystem;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Terraria;
using Terraria.IO;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using Terraria.Utilities;
using Terraria.WorldBuilding;

namespace InnoVault.Dimensions
{
    /// <summary>
    /// 维度系统管理器,负责维度的切换、加载和数据管理
    /// </summary>
    public class DimensionLoader : ModSystem
    {
        #region 静态字段

        /// <summary>
        /// 所有已注册的维度
        /// </summary>
        internal readonly static List<Dimension> registeredDimensions = [];

        /// <summary>
        /// FullName到维度的映射字典,用于快速查找
        /// </summary>
        internal readonly static Dictionary<string, Dimension> dimensionsByFullName = new();

        /// <summary>
        /// Type到维度的映射字典,用于快速查找
        /// </summary>
        internal readonly static Dictionary<Type, Dimension> dimensionsByType = new();

        /// <summary>
        /// Mod到维度列表的映射字典,用于快速查找
        /// </summary>
        internal readonly static Dictionary<Mod, List<Dimension>> dimensionsByMod = new();

        /// <summary>
        /// 层级到维度列表的映射字典,用于快速查找
        /// </summary>
        internal readonly static Dictionary<DimensionLayerEnum, List<Dimension>> dimensionsByLayer = new();

        /// <summary>
        /// 维度索引到维度的映射字典,用于快速查找
        /// </summary>
        internal readonly static Dictionary<int, Dimension> dimensionsByIndex = new();

        /// <summary>
        /// 网络包类型常量
        /// </summary>
        internal const byte PACKET_DIMENSION_SWITCH = 1;

        /// <summary>
        /// 维度切换队列
        /// </summary>
        private readonly static Queue<(int dimensionIndex, int playerWhoAmI)> switchQueue = new();

        /// <summary>
        /// 维度生存计时器(用于临时维度)
        /// </summary>
        private readonly static Dictionary<int, float> dimensionLifeTimers = new();

        /// <summary>
        /// 维度中的玩家数量
        /// </summary>
        private readonly static Dictionary<int, int> dimensionPlayerCounts = new();

        /// <summary>
        /// 当前活跃的维度
        /// </summary>
        internal static Dimension currentDimension;

        /// <summary>
        /// 缓存的维度(用于过渡期)
        /// </summary>
        internal static Dimension cachedDimension;

        /// <summary>
        /// 主世界数据
        /// </summary>
        internal static WorldFileData mainWorldData;

        /// <summary>
        /// 维度间传输的数据
        /// </summary>
        internal static TagCompound transferData;

        /// <summary>
        /// 是否正在复制维度数据
        /// </summary>
        internal static bool copyingDimensionData;

        /// <summary>
        /// 待恢复的维度索引（用于世界加载时恢复维度状态）
        /// </summary>
        internal static int? pendingDimensionRestore = null;

        /// <summary>
        /// 是否正在进行维度切换（用于区分世界加载/卸载和维度切换）
        /// </summary>
        internal static bool isTransitioning = false;

        #endregion

        #region 加载和卸载

        /// <summary>
        /// 
        /// </summary>
        public override void Load() {
            On_NPC.UpdateNPC_UpdateGravity += UpdateDimensionNPCGravity;
        }

        /// <summary>
        /// 世界加载时恢复维度状态
        /// </summary>
        public override void OnWorldLoad() {
            try {
                //只有在非维度切换状态下才恢复维度状态
                //因为维度切换会触发 OnWorldLoad，但此时不应该恢复状态
                if (!isTransitioning) {
                    VaultMod.Instance.Logger.Info("World loading (not transitioning), loading dimension state...");

                    //加载维度状态
                    DimensionStateData.LoadDimensionState();

                    //如果有待恢复的维度，则在世界完全加载后切换
                    if (pendingDimensionRestore.HasValue && pendingDimensionRestore.Value >= 0) {
                        int targetIndex = pendingDimensionRestore.Value;
                        VaultMod.Instance.Logger.Info($"Restoring dimension state to index: {targetIndex}");

                        if (dimensionsByIndex.ContainsKey(targetIndex)) {
                            DontTaskRun = true;
                            BeginTransition(targetIndex);
                        }
                        else {
                            VaultMod.Instance.Logger.Warn($"Cannot restore dimension: index {targetIndex} not found");
                            pendingDimensionRestore = null;
                        }
                    }
                    else {
                        VaultMod.Instance.Logger.Info("No dimension state to restore, starting in main world.");
                    }
                }
                else {
                    VaultMod.Instance.Logger.Info("World loading during dimension transition, skipping dimension state restore.");
                }
            } catch (Exception ex) {
                VaultMod.Instance.Logger.Error($"Error in OnWorldLoad: {ex}");
                pendingDimensionRestore = null;
            }
        }

        /// <summary>
        /// 世界卸载时清理维度状态并保存当前维度
        /// </summary>
        public override void OnWorldUnload() {
            try {
                //只有在非维度切换状态下才保存维度状态
                //因为维度切换会触发 OnWorldUnload，但此时不应该保存状态
                if (!isTransitioning) {
                    VaultMod.Instance.Logger.Info("World unloading (not transitioning), saving dimension state...");
                    DimensionStateData.SaveDimensionState();

                    //清理当前维度状态
                    if (currentDimension != null) {
                        try {
                            currentDimension.OnExit();
                            currentDimension.OnUnload();
                        } catch (Exception ex) {
                            VaultMod.Instance.Logger.Error($"Error during dimension cleanup on world unload: {ex}");
                        }
                    }

                    //重置所有维度相关的状态
                    currentDimension = null;
                    cachedDimension = null;
                    mainWorldData = null;
                    transferData = null;

                    //清理维度切换队列
                    switchQueue?.Clear();

                    //清理维度计时器和玩家计数
                    dimensionLifeTimers?.Clear();
                    dimensionPlayerCounts?.Clear();

                    //清理待恢复标记
                    pendingDimensionRestore = null;

                    VaultMod.Instance.Logger.Info("Dimension states cleared and saved on world unload.");
                } else {
                    VaultMod.Instance.Logger.Info("World unloading during dimension transition, skipping dimension state save.");
                }
            } catch (Exception ex) {
                VaultMod.Instance.Logger.Error($"Error in OnWorldUnload: {ex}");
            }
        }

        /// <summary>
        /// 模组卸载时的最终清理
        /// </summary>
        public override void Unload() {
            registeredDimensions?.Clear();
            dimensionsByFullName?.Clear();
            dimensionsByType?.Clear();
            dimensionsByMod?.Clear();
            dimensionsByLayer?.Clear();
            dimensionsByIndex?.Clear();
            switchQueue?.Clear();
            dimensionLifeTimers?.Clear();
            dimensionPlayerCounts?.Clear();

            currentDimension = null;
            cachedDimension = null;
            mainWorldData = null;
            transferData = null;
            pendingDimensionRestore = null;
            isTransitioning = false;

            On_NPC.UpdateNPC_UpdateGravity -= UpdateDimensionNPCGravity;
        }

        #endregion

        #region 公共API

        /// <summary>
        /// 获取当前维度
        /// </summary>
        public static Dimension Current => currentDimension;

        /// <summary>
        /// 获取主世界数据（无论当前是否在维度中）
        /// </summary>
        public static WorldFileData MainWorldData => mainWorldData ?? Main.ActiveWorldFileData;

        /// <summary>
        /// 检查指定ID的维度是否激活
        /// </summary>
        public static bool IsActive(string fullName) => currentDimension?.FullName == fullName;

        /// <summary>
        /// 检查指定类型的维度是否激活
        /// </summary>
        public static bool IsActive<T>() where T : Dimension => currentDimension?.GetType() == typeof(T);

        /// <summary>
        /// 检查是否在任何维度中(不在主世界)
        /// </summary>
        public static bool AnyActive() => currentDimension != null;

        /// <summary>
        /// 检查当前维度是否来自指定模组
        /// </summary>
        public static bool AnyActive(Mod mod) => currentDimension?.Mod == mod;

        /// <summary>
        /// 检查当前维度是否来自指定模组
        /// </summary>
        public static bool AnyActive<T>() where T : Mod => currentDimension?.Mod == ModContent.GetInstance<T>();

        /// <summary>
        /// 获取当前维度的文件路径
        /// <para>路径格式: Worlds/Dimensions/[主世界名称]/[维度全名].wld</para>
        /// </summary>
        public static string CurrentPath {
            get {
                //如果没有维度，或者是主世界，返回标准路径
                if (currentDimension == null) {
                    return mainWorldData?.Path ?? Main.ActiveWorldFileData?.Path ?? string.Empty;
                }

                //获取主世界数据作为"父级"依据
                var baseData = mainWorldData ?? Main.ActiveWorldFileData;
                if (baseData == null) {
                    VaultMod.Instance.Logger.Error("Cannot get dimension path: no world data available");
                    return string.Empty;
                }

                //获取并清理主世界名称 (防止非法文件名字符)
                string cleanWorldName = baseData.Name;
                foreach (char c in Path.GetInvalidFileNameChars()) {
                    cleanWorldName = cleanWorldName.Replace(c, '_');
                }

                //构建隔离的文件夹路径: Worlds/Dimensions/{主世界名}/
                //使用 Main.WorldPath 确保存档依然在玩家的存档目录下，方便云同步和管理
                string dimensionRoot = Path.Combine(Main.WorldPath, "Dimensions", cleanWorldName);

                //确保目录存在 (如果不存在则创建)
                if (!Directory.Exists(dimensionRoot)) {
                    Directory.CreateDirectory(dimensionRoot);
                }

                string fileName = $"{currentDimension.FullName}.wld"; 
                return Path.Combine(dimensionRoot, fileName);
            }
        }

        #endregion

        #region 维度切换

        /// <summary>
        /// 尝试进入指定ID的维度
        /// </summary>
        public static bool Enter(string fullName) {
            //防止连续切换
            if (currentDimension != null && cachedDimension != null && currentDimension != cachedDimension)
                return false;

            if (dimensionsByFullName.TryGetValue(fullName, out Dimension dimension)) {
                int index = dimensionsByIndex.FirstOrDefault(kvp => kvp.Value == dimension).Key;
                BeginTransition(index);
                return true;
            }

            VaultMod.Instance.Logger.Warn($"Failed to enter dimension: {fullName} not found");
            return false;
        }

        /// <summary>
        /// 进入指定类型的维度
        /// </summary>
        public static bool Enter<T>() where T : Dimension {
            //防止连续切换
            if (currentDimension != null && cachedDimension != null && currentDimension != cachedDimension)
                return false;

            if (dimensionsByType.TryGetValue(typeof(T), out Dimension dimension)) {
                int index = dimensionsByIndex.FirstOrDefault(kvp => kvp.Value == dimension).Key;
                BeginTransition(index);
                return true;
            }

            VaultMod.Instance.Logger.Warn($"Failed to enter dimension: {typeof(T).Name} not found");
            return false;
        }

        /// <summary>
        /// 离开当前维度
        /// </summary>
        public static void Exit() {
            if (currentDimension != null) {
                BeginTransition(currentDimension.ReturnTarget);
            }
        }

        /// <summary>
        /// 改变当前维度
        /// </summary>
        public static void Change(int targetIndex) {
            BeginTransition(targetIndex);
        }

        private static bool DontTaskRun = false;
        /// <summary>
        /// 开始维度过渡
        /// </summary>
        private static void BeginTransition(int targetIndex) {
            try {
                bool isTransitioning2 = isTransitioning;
                //设置过渡标志，防止在切换维度时触发保存/加载维度状态
                isTransitioning = true;
                VaultMod.Instance.Logger.Info($"Beginning dimension transition to index: {targetIndex}");

                //服务器端处理
                if (VaultUtils.isServer) {
                    SendDimensionSwitchPacket(targetIndex);
                    PerformServerSwitch(targetIndex);
                    return;
                }

                //客户端处理
                cachedDimension = currentDimension;

                //返回主菜单
                if (targetIndex == int.MinValue) {
                    currentDimension = null;
                    Main.gameMenu = true;
                    Task.Factory.StartNew(ExitWorldCallback, null);
                    return;
                }

                //首次离开主世界时保存主世界引用
                if (currentDimension == null && targetIndex >= 0) {
                    mainWorldData = Main.ActiveWorldFileData;
                }

                //设定新的当前维度
                currentDimension = targetIndex < 0 ? null : registeredDimensions[targetIndex];

                //显示加载界面
                Main.gameMenu = true;
                Main.menuMode = 10; //加载世界界面

                if (DontTaskRun) {
                    DontTaskRun = false;
                    ExitWorldCallback(targetIndex);
                }
                else {
                    //异步执行世界保存和加载
                    Task.Factory.StartNew(ExitWorldCallback, targetIndex);
                }              
            } catch (Exception ex) {
                VaultMod.Instance.Logger.Error($"Error in BeginTransition: {ex}");
                isTransitioning = false; //出错时清除标志
            }
        }

        /// <summary>
        /// 发送维度切换网络包
        /// </summary>
        private static void SendDimensionSwitchPacket(int targetIndex) {
            try {
                ModPacket packet = VaultMod.Instance.GetPacket();
                packet.Write(PACKET_DIMENSION_SWITCH);
                packet.Write(targetIndex);
                packet.Send();
            } catch (Exception ex) {
                VaultMod.Instance.Logger.Error($"Error sending dimension switch packet: {ex}");
            }
        }

        /// <summary>
        /// 服务器执行维度切换
        /// </summary>
        private static void PerformServerSwitch(int targetIndex) {
            try {
                VaultMod.Instance.Logger.Info($"Server switching to dimension index: {targetIndex}");

                //保存当前世界
                WorldFile.SaveWorld();

                //准备数据切换
                cachedDimension = currentDimension;
                if (currentDimension == null && targetIndex >= 0) {
                    mainWorldData = Main.ActiveWorldFileData;
                }

                currentDimension = targetIndex < 0 ? null : registeredDimensions[targetIndex];

                //执行核心切换逻辑
                ExitWorldCallback(targetIndex);
            } catch (Exception ex) {
                VaultMod.Instance.Logger.Error($"Error in server dimension switch: {ex}");
                isTransitioning = false; //出错时清除标志
            }
        }

        /// <summary>
        /// 接收维度切换网络包
        /// </summary>
        public static void ReceiveDimensionSwitch(BinaryReader reader) {
            try {
                int targetIndex = reader.ReadInt32();
                BeginTransition(targetIndex);
            } catch (Exception ex) {
                VaultMod.Instance.Logger.Error($"Error receiving dimension switch: {ex}");
            }
        }

        /// <summary>
        /// 获取维度索引
        /// </summary>
        public static int GetIndex(string fullName) {
            if (dimensionsByFullName.TryGetValue(fullName, out Dimension dimension)) {
                return dimensionsByIndex.FirstOrDefault(kvp => kvp.Value == dimension).Key;
            }
            return int.MinValue;
        }

        /// <summary>
        /// 获取维度索引
        /// </summary>
        public static int GetIndex<T>() where T : Dimension {
            if (dimensionsByType.TryGetValue(typeof(T), out Dimension dimension)) {
                return dimensionsByIndex.FirstOrDefault(kvp => kvp.Value == dimension).Key;
            }
            return int.MinValue;
        }

        #endregion

        #region 数据传输

        /// <summary>
        /// 复制数据到传输容器
        /// <para>仅在CopyFromMainWorld或CopyDimensionData中调用</para>
        /// </summary>
        public static void CopyData(string key, object data) {
            if (data != null && (copyingDimensionData || !transferData.ContainsKey(key))) {
                transferData[key] = data;
            }
        }

        /// <summary>
        /// 从传输容器读取数据
        /// <para>仅在ReadMainWorldData或ReadDimensionData中调用</para>
        /// </summary>
        public static T ReadData<T>(string key) => transferData.Get<T>(key);

        /// <summary>
        /// 从主世界复制通用数据
        /// </summary>
        private static void CopyMainWorldData() {
            try {
                transferData["mainId"] = Main.ActiveWorldFileData.UniqueId.ToByteArray();
                transferData["seed"] = Main.ActiveWorldFileData.SeedText;
                transferData["gameMode"] = Main.ActiveWorldFileData.GameMode;
                transferData["hardMode"] = Main.hardMode;
                transferData["time"] = Main.time;
                transferData["dayTime"] = Main.dayTime;

                //世界种子选项
                transferData[nameof(Main.drunkWorld)] = Main.drunkWorld;
                transferData[nameof(Main.getGoodWorld)] = Main.getGoodWorld;
                transferData[nameof(Main.tenthAnniversaryWorld)] = Main.tenthAnniversaryWorld;
                transferData[nameof(Main.dontStarveWorld)] = Main.dontStarveWorld;
                transferData[nameof(Main.notTheBeesWorld)] = Main.notTheBeesWorld;
                transferData[nameof(Main.remixWorld)] = Main.remixWorld;
                transferData[nameof(Main.noTrapsWorld)] = Main.noTrapsWorld;
                transferData[nameof(Main.zenithWorld)] = Main.zenithWorld;

                //调用所有IDimensionDataTransfer实现
                foreach (IDimensionDataTransfer transfer in VaultUtils.GetDerivedInstances<IDimensionDataTransfer>()) {
                    try {
                        transfer.CopyFromMainWorld();
                    } catch (Exception ex) {
                        VaultMod.Instance.Logger.Error($"Error in CopyFromMainWorld for {transfer.GetType().Name}: {ex}");
                    }
                }
            } catch (Exception ex) {
                VaultMod.Instance.Logger.Error($"Error in CopyMainWorldData: {ex}");
            }
        }

        /// <summary>
        /// 读取主世界数据
        /// </summary>
        private static void ReadCopiedMainWorldData() {
            try {
                mainWorldData.UniqueId = new Guid(transferData.Get<byte[]>("mainId"));
                Main.ActiveWorldFileData.SetSeed(transferData.Get<string>("seed"));
                Main.GameMode = transferData.Get<int>("gameMode");
                Main.hardMode = transferData.Get<bool>("hardMode");

                //根据维度设置决定是否应用时间
                if (currentDimension != null && currentDimension.EnableTimeOfDay) {
                    Main.time = transferData.Get<double>("time");
                    Main.dayTime = transferData.Get<bool>("dayTime");
                }

                Main.drunkWorld = transferData.Get<bool>(nameof(Main.drunkWorld));
                Main.getGoodWorld = transferData.Get<bool>(nameof(Main.getGoodWorld));
                Main.tenthAnniversaryWorld = transferData.Get<bool>(nameof(Main.tenthAnniversaryWorld));
                Main.dontStarveWorld = transferData.Get<bool>(nameof(Main.dontStarveWorld));
                Main.notTheBeesWorld = transferData.Get<bool>(nameof(Main.notTheBeesWorld));
                Main.remixWorld = transferData.Get<bool>(nameof(Main.remixWorld));
                Main.noTrapsWorld = transferData.Get<bool>(nameof(Main.noTrapsWorld));
                Main.zenithWorld = transferData.Get<bool>(nameof(Main.zenithWorld));

                foreach (IDimensionDataTransfer transfer in VaultUtils.GetDerivedInstances<IDimensionDataTransfer>()) {
                    try {
                        transfer.ReadMainWorldData();
                    } catch (Exception ex) {
                        VaultMod.Instance.Logger.Error($"Error in ReadMainWorldData for {transfer.GetType().Name}: {ex}");
                    }
                }
            } catch (Exception ex) {
                VaultMod.Instance.Logger.Error($"Error in ReadCopiedMainWorldData: {ex}");
            }
        }

        #endregion

        #region 世界回调

        /// <summary>
        /// 退出世界回调
        /// </summary>
        internal static void ExitWorldCallback(object index) {
            try {
                int netMode = Main.netMode;

                if (index != null) {
                    if (netMode == 0 || netMode == 2) {
                        WorldFile.CacheSaveTime();

                        transferData ??= [];

                        //保存旧维度数据
                        if (cachedDimension != null) {
                            copyingDimensionData = true;
                            try {
                                cachedDimension.CopyDimensionData();
                            } catch (Exception ex) {
                                VaultMod.Instance.Logger.Error($"Error copying dimension data: {ex}");
                            }
                            copyingDimensionData = false;

                            cachedDimension.OnExit();
                        }

                        //从主世界离开时保存主世界数据
                        if (cachedDimension == null && (int)index >= 0) {
                            CopyMainWorldData();
                        }
                    }
                }

                //清理游戏状态
                Main.invasionProgress = -1;
                Main.invasionProgressDisplayLeft = 0;
                Main.invasionProgressAlpha = 0;
                Main.invasionProgressIcon = 0;

                //重置地图
                Main.Map?.Clear();

                //触发新维度进入钩子
                currentDimension?.OnEnter();

                //保存玩家数据
                if (Main.ActivePlayerFileData != null) {
                    Main.ActivePlayerFileData.StopPlayTimer();
                    Player.SavePlayer(Main.ActivePlayerFileData);
                    Player.ClearPlayerTempInfo();
                }

                //保存世界
                if (netMode != 1) {
                    WorldFile.SaveWorld();
                }

                SystemLoader.OnWorldUnload();

                Main.fastForwardTimeToDawn = false;
                Main.fastForwardTimeToDusk = false;
                Main.UpdateTimeRate();

                //返回主菜单时清除过渡标志
                if (index == null) {
                    cachedDimension = null;
                    isTransitioning = false;
                    VaultMod.Instance.Logger.Info("Returning to main menu, clearing transition flag.");
                    Main.menuMode = 0;
                    return;
                }

                WorldGen.noMapUpdate = true;

                //重置玩家（单人模式）
                if (netMode == 0) {
                    if (cachedDimension != null && cachedDimension.ResetPlayerOnExit) {
                        PlayerFileData playerData = Player.GetFileData(Main.ActivePlayerFileData.Path, Main.ActivePlayerFileData.IsCloudSave);
                        if (playerData != null) {
                            playerData.Player.whoAmI = Main.myPlayer;
                            playerData.SetAsActive();
                        }
                    }

                    for (int i = 0; i < 255; i++) {
                        if (i != Main.myPlayer) {
                            Main.player[i].active = false;
                        }
                    }
                }

                //加载新世界
                if (netMode != 1) {
                    LoadWorld();
                }
            } catch (Exception ex) {
                VaultMod.Instance.Logger.Error($"Error in ExitWorldCallback: {ex}");
                isTransitioning = false; //出错时清除标志
                Main.menuMode = 0;
            }
        }

        /// <summary>
        /// 加载世界
        /// </summary>
        private static void LoadWorld() {
            try {
                bool isDimension = currentDimension != null;

                //确保主世界数据存在
                if (!isDimension && mainWorldData == null) {
                    mainWorldData = Main.ActiveWorldFileData;
                }

                bool cloud = mainWorldData.IsCloudSave;
                string path = isDimension ? CurrentPath : mainWorldData.Path;

                Main.rand = new UnifiedRandom((int)DateTime.Now.Ticks);

                cachedDimension?.OnUnload();

                Main.ToggleGameplayUpdates(false);

                WorldGen.gen = true;
                WorldGen.loadFailed = false;
                WorldGen.loadSuccess = false;

                //设置WorldFileData
                SetupWorldFileData(isDimension, path, cloud);

                //尝试加载世界文件
                TryLoadWorldFile(path, cloud);

                //处理维度
                if (isDimension) {
                    if (!WorldGen.loadSuccess) {
                        LoadDimensionGen(path, cloud);
                    }
                    currentDimension.OnLoad();
                }
                else if (!WorldGen.loadSuccess) {
                    Main.menuMode = 0;
                    isTransitioning = false; //失败时清除标志
                    if (VaultUtils.isServer) {
                        Netplay.Disconnect = true;
                    }
                    return;
                }

                WorldGen.gen = false;

                //客户端地图加载
                if (!VaultUtils.isServer) {
                    LoadClientMap();
                }

                //维度切换完成，清除过渡标志
                isTransitioning = false;
                VaultMod.Instance.Logger.Info("Dimension transition complete, clearing transition flag.");
            } catch (Exception ex) {
                VaultMod.Instance.Logger.Error($"Error loading world: {ex}");
                isTransitioning = false; //出错时清除标志
                Main.menuMode = 0;
                if (VaultUtils.isServer) {
                    Netplay.Disconnect = true;
                }
            }
        }

        /// <summary>
        /// 设置世界文件数据
        /// </summary>
        private static void SetupWorldFileData(bool isDimension, string path, bool cloud) {
            if (isDimension) {
                WorldFileData dimData = new WorldFileData(path, cloud) {
                    Name = currentDimension.DisplayName.Value,
                    CreationTime = DateTime.Now,
                    Metadata = FileMetadata.FromCurrentSettings(FileType.World),
                    WorldGeneratorVersion = Main.WorldGeneratorVersion,
                    UniqueId = Guid.NewGuid(),
                    GameMode = Main.GameMode
                };
                dimData.SetSeed(mainWorldData.SeedText);
                Main.ActiveWorldFileData = dimData;
            }
            else {
                Main.ActiveWorldFileData = mainWorldData;
            }
        }

        /// <summary>
        /// 加载客户端地图
        /// </summary>
        private static void LoadClientMap() {
            if (Main.mapEnabled) {
                Main.Map.Load();
            }

            Main.sectionManager.SetAllSectionsLoaded();

            while (Main.mapEnabled && Main.loadMapLock) {
                Main.statusText = Terraria.Localization.Language.GetTextValue("LegacyWorldGen.68") + " " + (int)((float)Main.loadMapLastX / Main.maxTilesX * 100 + 1) + "%";
                System.Threading.Thread.Sleep(0);
            }

            Main.QueueMainThreadAction(SpawnPlayer);
        }

        /// <summary>
        /// 生成玩家
        /// </summary>
        private static void SpawnPlayer() {
            try {
                Main.LocalPlayer.Spawn(PlayerSpawnContext.SpawningIntoWorld);

                //维度自定义生成点
                if (currentDimension != null) {
                    //可以让维度自定义生成位置
                    //Main.LocalPlayer.position = currentDimension.GetSpawnPosition(Main.LocalPlayer);
                }

                WorldFile.SetOngoingToTemps();
                Main.resetClouds = true;
                Main.gameMenu = false;
            } catch (Exception ex) {
                VaultMod.Instance.Logger.Error($"Error spawning player: {ex}");
            }
        }

        /// <summary>
        /// 生成维度（全新生成）
        /// </summary>
        private static void LoadDimensionGen(string path, bool cloud) {
            try {
                Main.worldName = currentDimension.DisplayName.Value;
                if (VaultUtils.isServer) {
                    Console.Title = Main.worldName;
                }

                Main.maxTilesX = currentDimension.Width;
                Main.maxTilesY = currentDimension.Height;
                Main.spawnTileX = Main.maxTilesX / 2;
                Main.spawnTileY = Main.maxTilesY / 2;

                WorldGen.setWorldSize();
                WorldGen.clearWorld();
                Main.worldSurface = Main.maxTilesY * 0.3;
                Main.rockLayer = Main.maxTilesY * 0.5;
                GenVars.waterLine = Main.maxTilesY;
                Main.weatherCounter = 18000;

                ReadCopiedMainWorldData();

                //执行世界生成
                double totalWeight = currentDimension.GenerationTasks.Sum(t => t.Weight);
                WorldGenerator.CurrentGenerationProgress = new GenerationProgress();
                WorldGenerator.CurrentGenerationProgress.TotalWeight = totalWeight;

                WorldGenConfiguration config = currentDimension.GenerationConfig;

                foreach (GenPass task in currentDimension.GenerationTasks) {
                    WorldGen._genRand = new UnifiedRandom(Main.ActiveWorldFileData.Seed);
                    Main.rand = new UnifiedRandom(Main.ActiveWorldFileData.Seed);

                    WorldGenerator.CurrentGenerationProgress.Start(task.Weight);
                    try {
                        task.Apply(WorldGenerator.CurrentGenerationProgress, config?.GetPassConfiguration(task.Name));
                    } catch (Exception ex) {
                        VaultMod.Instance.Logger.Error($"Error in dimension gen pass {task.Name}: {ex}");
                    }
                    WorldGenerator.CurrentGenerationProgress.End();
                }

                WorldGenerator.CurrentGenerationProgress = null;
                Main.WorldFileMetadata = FileMetadata.FromCurrentSettings(FileType.World);

                //保存新生成的维度
                if (currentDimension.ShouldSave) {
                    WorldFile.SaveWorld(cloud);
                }

                WorldGen.loadSuccess = true;
                SystemLoader.OnWorldLoad();
            } catch (Exception ex) {
                VaultMod.Instance.Logger.Error($"Error generating dimension: {ex}");
                WorldGen.loadFailed = true;
            }
        }

        /// <summary>
        /// 尝试加载世界文件
        /// </summary>
        private static void TryLoadWorldFile(string path, bool cloud) {
            if (!FileUtilities.Exists(path, cloud)) {
                return;
            }

            try {
                using BinaryReader reader = new BinaryReader(new MemoryStream(FileUtilities.ReadAllBytes(path, cloud)));

                int status = 0;
                if (currentDimension != null) {
                    //自定义维度读取
                    status = currentDimension.ReadDimensionFile(reader);
                }
                else {
                    //主世界读取 - 使用Terraria标准方法
                    WorldFile.LoadWorld(cloud);
                    status = 0;
                }

                if (status == 0) {
                    WorldGen.loadSuccess = true;
                    SystemLoader.OnWorldLoad();

                    if (currentDimension != null) {
                        currentDimension.PostReadFile();
                        cachedDimension?.ReadDimensionData();
                        ReadCopiedMainWorldData();
                    }
                }
            } catch (Exception ex) {
                VaultMod.Instance.Logger.Error($"Failed to load world file: {ex}");
                WorldGen.loadFailed = true;

                //尝试加载备份
                if (currentDimension == null) {
                    string backupPath = path + ".bak";
                    if (FileUtilities.Exists(backupPath, cloud)) {
                        try {
                            FileUtilities.Move(backupPath, path, cloud, true);
                            TryLoadWorldFile(path, cloud);
                        } catch (Exception backupEx) {
                            VaultMod.Instance.Logger.Error($"Failed to load backup: {backupEx}");
                        }
                    }
                }
            }
        }

        #endregion

        #region 更新逻辑

        /// <summary>
        /// 
        /// </summary>
        public override void PostUpdateEverything() {
            //处理切换队列
            while (switchQueue.Count > 0) {
                var (dimensionIndex, playerWhoAmI) = switchQueue.Dequeue();
                //TODO: 实现多人玩家切换逻辑
            }

            //更新临时维度计时器
            UpdateTemporaryDimensions();

            //应用时间流速
            if (currentDimension != null && currentDimension.TimeScale != 1.0f) {
                Main.time += Main.dayRate * (currentDimension.TimeScale - 1.0f);
            }
        }

        /// <summary>
        /// 更新临时维度
        /// </summary>
        private static void UpdateTemporaryDimensions() {
            List<int> toRemove = new();

            foreach (var kvp in dimensionLifeTimers) {
                int index = kvp.Key;
                float timeLeft = kvp.Value;

                if (!dimensionPlayerCounts.ContainsKey(index) || dimensionPlayerCounts[index] == 0) {
                    timeLeft -= 1f / 60f;
                    dimensionLifeTimers[index] = timeLeft;

                    if (timeLeft <= 0) {
                        toRemove.Add(index);
                    }
                }
            }

            foreach (int index in toRemove) {
                //TODO: 清理维度
                dimensionLifeTimers.Remove(index);
            }
        }

        private void UpdateDimensionNPCGravity(On_NPC.orig_UpdateNPC_UpdateGravity orig, NPC self) {
            orig.Invoke(self);
            if (currentDimension != null)
                self.GravityMultiplier *= currentDimension.GetGravityMultiplier(self);
        }

        internal static void UpdateDimensionPlayerGravity(Player player) {
            if (currentDimension != null)
                player.gravity *= currentDimension.GetGravityMultiplier(player);
        }

        #endregion
    }
}
