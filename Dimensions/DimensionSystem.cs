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
    public class DimensionSystem : ModSystem
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
        private static WorldFileData mainWorldData;

        /// <summary>
        /// 维度间传输的数据
        /// </summary>
        internal static TagCompound transferData;

        /// <summary>
        /// 是否正在复制维度数据
        /// </summary>
        private static bool copyingDimensionData;

        #endregion

        #region 加载和卸载

        /// <summary>
        /// 
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
        }

        #endregion

        #region 公共API

        /// <summary>
        /// 获取当前维度
        /// </summary>
        public static Dimension Current => currentDimension;

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
        /// </summary>
        public static string CurrentPath {
            get {
                if (currentDimension == null || mainWorldData == null)
                    return string.Empty;
                return currentDimension.GetDimensionPath(mainWorldData);
            }
        }

        #endregion

        #region 维度切换

        /// <summary>
        /// 尝试进入指定ID的维度
        /// </summary>
        public static bool Enter(string fullName) {
            if (currentDimension != cachedDimension)
                return false;

            if (dimensionsByFullName.TryGetValue(fullName, out Dimension dimension)) {
                int index = dimensionsByIndex.FirstOrDefault(kvp => kvp.Value == dimension).Key;
                BeginTransition(index);
                return true;
            }
            return false;
        }

        /// <summary>
        /// 进入指定类型的维度
        /// </summary>
        public static bool Enter<T>() where T : Dimension {
            if (currentDimension != cachedDimension)
                return false;

            if (dimensionsByType.TryGetValue(typeof(T), out Dimension dimension)) {
                int index = dimensionsByIndex.FirstOrDefault(kvp => kvp.Value == dimension).Key;
                BeginTransition(index);
                return true;
            }
            return false;
        }

        /// <summary>
        /// 离开当前维度
        /// </summary>
        public static void Exit() {
            if (currentDimension != null && currentDimension == cachedDimension) {
                BeginTransition(currentDimension.ReturnTarget);
            }
        }

        /// <summary>
        /// 开始维度过渡
        /// </summary>
        private static void BeginTransition(int targetIndex) {
            if (VaultUtils.isServer)
                return;

            //返回主菜单
            if (targetIndex == int.MinValue) {
                currentDimension = null;
                Main.gameMenu = true;
                Task.Factory.StartNew(ExitWorldCallback, null);
                return;
            }

            if (VaultUtils.isSinglePlayer) {
                //首次离开主世界时保存主世界数据
                if (currentDimension == null && targetIndex >= 0) {
                    mainWorldData = Main.ActiveWorldFileData;
                }

                currentDimension = targetIndex < 0 ? null : registeredDimensions[targetIndex];
                Main.gameMenu = true;

                Task.Factory.StartNew(ExitWorldCallback, targetIndex);
                return;
            }

            //多人模式下的处理
            //TODO: 实现多人模式支持
            //操你妈太复杂了我不想写以后再说
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
                transfer.CopyFromMainWorld();
            }
        }

        /// <summary>
        /// 读取主世界数据
        /// </summary>
        private static void ReadCopiedMainWorldData() {
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
                transfer.ReadMainWorldData();
            }
        }

        #endregion

        #region 世界回调

        /// <summary>
        /// 退出世界回调
        /// </summary>
        internal static void ExitWorldCallback(object index) {
            int netMode = Main.netMode;

            if (index != null) {
                if (netMode == 0) {
                    WorldFile.CacheSaveTime();

                    if (transferData == null) {
                        transferData = new TagCompound();
                    }

                    if (cachedDimension != null) {
                        copyingDimensionData = true;
                        cachedDimension.CopyDimensionData();
                        copyingDimensionData = false;

                        cachedDimension.OnExit();
                    }

                    if ((int)index >= 0) {
                        CopyMainWorldData();
                    }
                }
            }

            //清理入侵进度等临时UI
            Main.invasionProgress = -1;
            Main.invasionProgressDisplayLeft = 0;
            Main.invasionProgressAlpha = 0;
            Main.invasionProgressIcon = 0;

            if (currentDimension != null) {
                currentDimension.OnEnter();
            }

            Main.ActivePlayerFileData.StopPlayTimer();
            Player.SavePlayer(Main.ActivePlayerFileData);
            Player.ClearPlayerTempInfo();

            if (netMode != 1) {
                WorldFile.SaveWorld();
            }

            SystemLoader.OnWorldUnload();

            Main.fastForwardTimeToDawn = false;
            Main.fastForwardTimeToDusk = false;
            Main.UpdateTimeRate();

            if (index == null) {
                cachedDimension = null;
                Main.menuMode = 0;
                return;
            }

            WorldGen.noMapUpdate = true;

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

            if (netMode != 1) {
                LoadWorld();
            }
        }

        /// <summary>
        /// 加载世界
        /// </summary>
        private static void LoadWorld() {
            bool isDimension = currentDimension != null;
            bool cloud = mainWorldData.IsCloudSave;
            string path = isDimension ? CurrentPath : mainWorldData.Path;

            Main.rand = new UnifiedRandom((int)DateTime.Now.Ticks);

            cachedDimension?.OnUnload();

            Main.ToggleGameplayUpdates(false);

            WorldGen.gen = true;
            WorldGen.loadFailed = false;
            WorldGen.loadSuccess = false;

            if (!isDimension || currentDimension.ShouldSave) {
                if (!isDimension) {
                    Main.ActiveWorldFileData = mainWorldData;
                }

                TryLoadWorldFile(path, cloud);
            }

            if (isDimension) {
                if (!WorldGen.loadSuccess) {
                    LoadDimension(path, cloud);
                }

                currentDimension.OnLoad();
            }
            else if (!WorldGen.loadSuccess) {
                Main.menuMode = 0;
                if (VaultUtils.isServer) {
                    Netplay.Disconnect = true;
                }
                return;
            }

            WorldGen.gen = false;

            if (!VaultUtils.isServer) {
                if (Main.mapEnabled) {
                    Main.Map.Load();
                }

                Main.sectionManager.SetAllSectionsLoaded();

                while (Main.mapEnabled && Main.loadMapLock) {
                    Main.statusText = Lang.gen[68].Value + " " + (int)((float)Main.loadMapLastX / Main.maxTilesX * 100 + 1) + "%";
                    System.Threading.Thread.Sleep(0);
                }

                Main.QueueMainThreadAction(SpawnPlayer);
            }
        }

        /// <summary>
        /// 生成玩家
        /// </summary>
        private static void SpawnPlayer() {
            Main.LocalPlayer.Spawn(PlayerSpawnContext.SpawningIntoWorld);
            WorldFile.SetOngoingToTemps();
            Main.resetClouds = true;
            Main.gameMenu = false;
        }

        /// <summary>
        /// 加载维度
        /// </summary>
        private static void LoadDimension(string path, bool cloud) {
            Main.worldName = currentDimension.DisplayName.Value;
            if (VaultUtils.isServer) {
                Console.Title = Main.worldName;
            }

            WorldFileData data = new WorldFileData(path, cloud) {
                Name = Main.worldName,
                CreationTime = DateTime.Now,
                Metadata = FileMetadata.FromCurrentSettings(FileType.World),
                WorldGeneratorVersion = Main.WorldGeneratorVersion,
                UniqueId = Guid.NewGuid()
            };
            Main.ActiveWorldFileData = data;

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
            double totalWeight = 0;
            foreach (GenPass task in currentDimension.GenerationTasks) {
                totalWeight += task.Weight;
            }

            WorldGenerator.CurrentGenerationProgress = new GenerationProgress();
            WorldGenerator.CurrentGenerationProgress.TotalWeight = totalWeight;

            WorldGenConfiguration config = currentDimension.GenerationConfig;

            foreach (GenPass task in currentDimension.GenerationTasks) {
                WorldGen._genRand = new UnifiedRandom(data.Seed);
                Main.rand = new UnifiedRandom(data.Seed);

                WorldGenerator.CurrentGenerationProgress.Start(task.Weight);
                task.Apply(WorldGenerator.CurrentGenerationProgress, config?.GetPassConfiguration(task.Name));
                WorldGenerator.CurrentGenerationProgress.End();
            }

            WorldGenerator.CurrentGenerationProgress = null;

            Main.WorldFileMetadata = FileMetadata.FromCurrentSettings(FileType.World);

            if (currentDimension.ShouldSave) {
                WorldFile.SaveWorld(cloud);
            }

            SystemLoader.OnWorldLoad();
        }

        /// <summary>
        /// 尝试加载世界文件
        /// </summary>
        private static void TryLoadWorldFile(string path, bool cloud) {
            //TODO: 实现完整的文件加载逻辑,包括备份处理
            if (FileUtilities.Exists(path, cloud)) {
                try {
                    using BinaryReader reader = new BinaryReader(new MemoryStream(FileUtilities.ReadAllBytes(path, cloud)));
                    int status = currentDimension != null ? currentDimension.ReadDimensionFile(reader) : WorldFile.LoadWorld_Version2(reader);

                    if (status == 0) {
                        WorldGen.loadSuccess = true;
                        SystemLoader.OnWorldLoad();

                        if (currentDimension != null) {
                            currentDimension.PostReadFile();
                            cachedDimension?.ReadDimensionData();
                            ReadCopiedMainWorldData();
                        }
                    }
                } catch {
                    WorldGen.loadFailed = true;
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
                //TODO: 实际执行切换逻辑
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
        private void UpdateTemporaryDimensions() {
            List<int> toRemove = new();

            foreach (var kvp in dimensionLifeTimers) {
                int index = kvp.Key;
                float timeLeft = kvp.Value;

                if (!dimensionPlayerCounts.ContainsKey(index) || dimensionPlayerCounts[index] == 0) {
                    timeLeft -= 1f / 60f; //假设60FPS
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

        #endregion
    }
}
