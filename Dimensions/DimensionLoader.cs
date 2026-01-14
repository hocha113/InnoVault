using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.GameContent.Bestiary;
using Terraria.GameContent.Creative;
using Terraria.GameContent.Events;
using Terraria.Graphics.Capture;
using Terraria.ID;
using Terraria.IO;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using Terraria.Social;
using Terraria.Utilities;
using Terraria.WorldBuilding;

namespace InnoVault.Dimensions
{
    /// <summary>
    /// 维度系统核心管理类，负责维度的进入、退出、更新和数据同步
    /// </summary>
    public class DimensionLoader : ModSystem
    {
        #region 静态状态
        /// <summary>
        /// 当前所在的维度，null表示在主世界
        /// </summary>
        internal static Dimension current;

        /// <summary>
        /// 缓存的上一个维度，用于数据传递
        /// </summary>
        internal static Dimension cache;

        /// <summary>
        /// 主世界的文件数据
        /// </summary>
        private static WorldFileData mainWorld;

        /// <summary>
        /// 是否正在复制维度数据
        /// </summary>
        private static bool copyingDimensionData;

        /// <summary>
        /// 用于世界间数据传递的标签
        /// </summary>
        internal static TagCompound copiedData;

        /// <summary>
        /// 是否正在从维度状态恢复（避免重复加载）
        /// </summary>
        private static bool isRestoringFromState;

        /// <summary>
        /// 维度状态文件版本号
        /// </summary>
        private const int DimensionStateVersion = 1;
        #endregion

        #region 维度状态持久化
        /// <summary>
        /// 获取指定主世界的维度状态文件路径
        /// </summary>
        /// <param name="worldUniqueId">主世界的唯一ID</param>
        /// <returns>维度状态文件的完整路径</returns>
        private static string GetDimensionStatePath(Guid worldUniqueId) {
            return Path.Combine(Main.WorldPath, worldUniqueId.ToString(), "dimension_state.dat");
        }

        /// <summary>
        /// 获取指定主世界的维度目录路径
        /// </summary>
        /// <param name="worldUniqueId">主世界的唯一ID</param>
        /// <returns>维度目录的完整路径</returns>
        public static string GetDimensionDirectory(Guid worldUniqueId) {
            return Path.Combine(Main.WorldPath, worldUniqueId.ToString());
        }

        /// <summary>
        /// 保存当前维度状态到文件
        /// </summary>
        /// <param name="worldUniqueId">主世界的唯一ID</param>
        /// <param name="dimensionIndex">当前维度索引，-1 表示在主世界</param>
        private static void SaveDimensionState(Guid worldUniqueId, int dimensionIndex) {
            try {
                string statePath = GetDimensionStatePath(worldUniqueId);
                string directory = Path.GetDirectoryName(statePath);

                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory)) {
                    Directory.CreateDirectory(directory);
                }

                using FileStream fs = new(statePath, FileMode.Create, FileAccess.Write);
                using BinaryWriter writer = new(fs);

                writer.Write(DimensionStateVersion);
                writer.Write(dimensionIndex);

                //保存维度的完整名称以便跨模组加载时验证
                if (dimensionIndex >= 0 && dimensionIndex < Dimension.Dimensions.Count) {
                    writer.Write(Dimension.Dimensions[dimensionIndex].FullName);
                }
                else {
                    writer.Write(string.Empty);
                }

                VaultMod.Instance.Logger.Info($"[DimensionLoader] Saved dimension state: world={worldUniqueId}, dimensionIndex={dimensionIndex}");
            } catch (Exception ex) {
                VaultMod.Instance.Logger.Warn($"[DimensionLoader] Failed to save dimension state: {ex.Message}");
            }
        }

        /// <summary>
        /// 加载指定主世界的维度状态
        /// </summary>
        /// <param name="worldUniqueId">主世界的唯一ID</param>
        /// <returns>维度索引，-1 表示在主世界或无状态</returns>
        private static int LoadDimensionState(Guid worldUniqueId) {
            try {
                string statePath = GetDimensionStatePath(worldUniqueId);

                if (!File.Exists(statePath)) {
                    return -1;
                }

                using FileStream fs = new(statePath, FileMode.Open, FileAccess.Read);
                using BinaryReader reader = new(fs);

                int version = reader.ReadInt32();
                if (version != DimensionStateVersion) {
                    VaultMod.Instance.Logger.Warn($"[DimensionLoader] Dimension state file version mismatch: expected={DimensionStateVersion}, actual={version}");
                    return -1;
                }

                int dimensionIndex = reader.ReadInt32();
                string dimensionFullName = reader.ReadString();

                //验证维度是否仍然存在
                if (dimensionIndex >= 0 && !string.IsNullOrEmpty(dimensionFullName)) {
                    //通过完整名称查找维度（模组重新加载后索引可能变化）
                    for (int i = 0; i < Dimension.Dimensions.Count; i++) {
                        if (Dimension.Dimensions[i].FullName == dimensionFullName) {
                            VaultMod.Instance.Logger.Info($"[DimensionLoader] Loaded dimension state: world={worldUniqueId}, dimension={dimensionFullName}");
                            return i;
                        }
                    }
                    VaultMod.Instance.Logger.Warn($"[DimensionLoader] Dimension '{dimensionFullName}' no longer exists, returning to main world");
                    return -1;
                }

                return dimensionIndex;
            } catch (Exception ex) {
                VaultMod.Instance.Logger.Warn($"[DimensionLoader] Failed to load dimension state: {ex.Message}");
                return -1;
            }
        }

        /// <summary>
        /// 清除指定主世界的维度状态（用于正常退出维度时）
        /// </summary>
        /// <param name="worldUniqueId">主世界的唯一ID</param>
        private static void ClearDimensionState(Guid worldUniqueId) {
            SaveDimensionState(worldUniqueId, -1);
        }
        #endregion

        #region 公开属性
        /// <summary>
        /// 隐藏返回按钮
        /// <br/>在 <see cref="Dimension.OnEnter"/> 调用前重置，在 <see cref="Dimension.OnExit"/> 调用后重置
        /// </summary>
        public static bool NoReturn { get; set; }

        /// <summary>
        /// 隐藏地狱背景
        /// <br/>在 <see cref="Dimension.OnEnter"/> 调用前重置，在 <see cref="Dimension.OnExit"/> 调用后重置
        /// </summary>
        public static bool HideUnderworld { get; set; }

        /// <summary>
        /// 当前维度
        /// </summary>
        public static Dimension Current => current;

        /// <summary>
        /// 检查当前维度ID是否与指定ID匹配
        /// <code>DimensionSystem.IsActive("MyMod/MyDimension")</code>
        /// </summary>
        public static bool IsActive(string id) => current?.FullName == id;

        /// <summary>
        /// 检查指定维度是否激活
        /// </summary>
        public static bool IsActive<T>() where T : Dimension => current?.GetType() == typeof(T);

        /// <summary>
        /// 检查是否不在主世界中
        /// </summary>
        public static bool AnyActive() => current != null;

        /// <summary>
        /// 检查当前维度是否来自指定模组
        /// </summary>
        public static bool AnyActive(Mod mod) => current?.Mod == mod;

        /// <summary>
        /// 检查当前维度是否来自指定模组
        /// </summary>
        public static bool AnyActive<T>() where T : Mod => current?.Mod == ModContent.GetInstance<T>();

        /// <summary>
        /// 当前维度的文件路径
        /// </summary>
        public static string CurrentPath => mainWorld != null && current != null
            ? Path.Combine(Main.WorldPath, mainWorld.UniqueId.ToString(), current.FileName + ".wld")
            : string.Empty;
        #endregion

        #region 进入/退出维度
        /// <summary>
        /// 尝试进入指定ID的维度
        /// <code>DimensionSystem.Enter("MyMod/MyDimension")</code>
        /// </summary>
        public static bool Enter(string id) {
            if (current != cache) {
                return false;
            }

            for (int i = 0; i < Dimension.Dimensions.Count; i++) {
                if (Dimension.Dimensions[i].FullName == id) {
                    BeginEntering(i);
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 进入指定维度
        /// </summary>
        public static bool Enter<T>() where T : Dimension {
            if (current != cache) {
                return false;
            }

            for (int i = 0; i < Dimension.Dimensions.Count; i++) {
                if (Dimension.Dimensions[i].GetType() == typeof(T)) {
                    BeginEntering(i);
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 退出当前维度
        /// </summary>
        public static void Exit() {
            if (current != null && current == cache) {
                BeginEntering(current.ReturnDestination);
            }
        }

        /// <summary>
        /// 开始进入维度的流程
        /// </summary>
        private static void BeginEntering(int index) {
            if (Main.netMode == NetmodeID.Server) {
                return;
            }

            if (index == int.MinValue) {
                //返回主菜单
                current = null;
                Main.gameMenu = true;
                Task.Factory.StartNew(ExitWorldCallBack, null);
                return;
            }

            if (Main.netMode == NetmodeID.SinglePlayer) {
                if (current == null && index >= 0) {
                    mainWorld = Main.ActiveWorldFileData;
                }

                current = index < 0 ? null : Dimension.Dimensions[index];
                Main.gameMenu = true;

                Task.Factory.StartNew(ExitWorldCallBack, index);
                return;
            }

            //多人模式：发送网络包请求进入维度
            DimensionNetwork.SendEnterDimensionPacket(index);
        }
        #endregion

        #region 世界数据复制
        /// <summary>
        /// 在 <see cref="Dimension.CopyMainWorldData"/> 或 <see cref="Dimension.OnExit"/> 中调用
        /// <br/>将数据存储到指定键下，用于世界间传递
        /// <code>DimensionSystem.CopyWorldData(nameof(DownedSystem.downedBoss), DownedSystem.downedBoss);</code>
        /// </summary>
        public static void CopyWorldData(string key, object data) {
            if (data != null && (copyingDimensionData || !copiedData.ContainsKey(key))) {
                copiedData[key] = data;
            }
        }

        /// <summary>
        /// 在 <see cref="Dimension.ReadCopiedMainWorldData"/> 或 <see cref="Dimension.ReadCopiedDimensionData"/> 中调用
        /// <br/>读取从其他世界复制的数据
        /// <code>DownedSystem.downedBoss = DimensionSystem.ReadCopiedWorldData&lt;bool&gt;(nameof(DownedSystem.downedBoss));</code>
        /// </summary>
        public static T ReadCopiedWorldData<T>(string key) => copiedData.Get<T>(key);

        /// <summary>
        /// 复制主世界的核心数据
        /// </summary>
        internal static void CopyMainWorldData() {
            copiedData["mainId"] = Main.ActiveWorldFileData.UniqueId.ToByteArray();
            copiedData["seed"] = Main.ActiveWorldFileData.SeedText;
            copiedData["gameMode"] = Main.ActiveWorldFileData.GameMode;
            copiedData["hardMode"] = Main.hardMode;

            //复制图鉴数据
            CopyBestiaryData();

            //复制创意模式能力数据
            CopyCreativePowersData();

            //复制世界特殊标记
            copiedData[nameof(Main.drunkWorld)] = Main.drunkWorld;
            copiedData[nameof(Main.getGoodWorld)] = Main.getGoodWorld;
            copiedData[nameof(Main.tenthAnniversaryWorld)] = Main.tenthAnniversaryWorld;
            copiedData[nameof(Main.dontStarveWorld)] = Main.dontStarveWorld;
            copiedData[nameof(Main.notTheBeesWorld)] = Main.notTheBeesWorld;
            copiedData[nameof(Main.remixWorld)] = Main.remixWorld;
            copiedData[nameof(Main.noTrapsWorld)] = Main.noTrapsWorld;
            copiedData[nameof(Main.zenithWorld)] = Main.zenithWorld;

            //复制Boss击杀状态
            CopyDownedData();

            //调用所有 ICopyDimensionData 的复制方法
            foreach (ICopyDimensionData data in ModContent.GetContent<ICopyDimensionData>()) {
                data.CopyMainWorldData();
            }
        }

        /// <summary>
        /// 读取复制的主世界数据
        /// </summary>
        internal static void ReadCopiedMainWorldData() {
            mainWorld.UniqueId = new Guid(copiedData.Get<byte[]>("mainId"));
            Main.ActiveWorldFileData.SetSeed(copiedData.Get<string>("seed"));
            Main.GameMode = copiedData.Get<int>("gameMode");
            Main.hardMode = copiedData.Get<bool>("hardMode");

            //读取图鉴数据
            ReadBestiaryData();

            //读取创意模式能力数据
            ReadCreativePowersData();

            //读取世界特殊标记
            Main.drunkWorld = copiedData.Get<bool>(nameof(Main.drunkWorld));
            Main.getGoodWorld = copiedData.Get<bool>(nameof(Main.getGoodWorld));
            Main.tenthAnniversaryWorld = copiedData.Get<bool>(nameof(Main.tenthAnniversaryWorld));
            Main.dontStarveWorld = copiedData.Get<bool>(nameof(Main.dontStarveWorld));
            Main.notTheBeesWorld = copiedData.Get<bool>(nameof(Main.notTheBeesWorld));
            Main.remixWorld = copiedData.Get<bool>(nameof(Main.remixWorld));
            Main.noTrapsWorld = copiedData.Get<bool>(nameof(Main.noTrapsWorld));
            Main.zenithWorld = copiedData.Get<bool>(nameof(Main.zenithWorld));

            //读取Boss击杀状态
            ReadDownedData();

            //调用所有 ICopyDimensionData 的读取方法
            foreach (ICopyDimensionData data in ModContent.GetContent<ICopyDimensionData>()) {
                data.ReadCopiedMainWorldData();
            }
        }
        #endregion

        #region 图鉴数据复制
        private static void CopyBestiaryData() {
            using MemoryStream stream = new();
            using BinaryWriter writer = new(stream);

            FieldInfo field = typeof(NPCKillsTracker).GetField("_killCountsByNpcId", BindingFlags.NonPublic | BindingFlags.Instance);
            Dictionary<string, int> kills = (Dictionary<string, int>)field.GetValue(Main.BestiaryTracker.Kills);

            writer.Write(kills.Count);
            foreach (KeyValuePair<string, int> item in kills) {
                writer.Write(item.Key);
                writer.Write(item.Value);
            }

            field = typeof(NPCWasNearPlayerTracker).GetField("_wasNearPlayer", BindingFlags.NonPublic | BindingFlags.Instance);
            HashSet<string> sights = (HashSet<string>)field.GetValue(Main.BestiaryTracker.Sights);

            writer.Write(sights.Count);
            foreach (string item in sights) {
                writer.Write(item);
            }

            field = typeof(NPCWasChatWithTracker).GetField("_chattedWithPlayer", BindingFlags.NonPublic | BindingFlags.Instance);
            HashSet<string> chats = (HashSet<string>)field.GetValue(Main.BestiaryTracker.Chats);

            writer.Write(chats.Count);
            foreach (string item in chats) {
                writer.Write(item);
            }

            copiedData["bestiary"] = stream.ToArray();
        }

        private static void ReadBestiaryData() {
            using MemoryStream stream = new(copiedData.Get<byte[]>("bestiary"));
            using BinaryReader reader = new(stream);

            FieldInfo field = typeof(NPCKillsTracker).GetField("_killCountsByNpcId", BindingFlags.NonPublic | BindingFlags.Instance);
            Dictionary<string, int> kills = (Dictionary<string, int>)field.GetValue(Main.BestiaryTracker.Kills);

            int count = reader.ReadInt32();
            for (int i = 0; i < count; i++) {
                kills[reader.ReadString()] = reader.ReadInt32();
            }

            field = typeof(NPCWasNearPlayerTracker).GetField("_wasNearPlayer", BindingFlags.NonPublic | BindingFlags.Instance);
            HashSet<string> sights = (HashSet<string>)field.GetValue(Main.BestiaryTracker.Sights);

            count = reader.ReadInt32();
            for (int i = 0; i < count; i++) {
                sights.Add(reader.ReadString());
            }

            field = typeof(NPCWasChatWithTracker).GetField("_chattedWithPlayer", BindingFlags.NonPublic | BindingFlags.Instance);
            HashSet<string> chats = (HashSet<string>)field.GetValue(Main.BestiaryTracker.Chats);

            count = reader.ReadInt32();
            for (int i = 0; i < count; i++) {
                chats.Add(reader.ReadString());
            }
        }
        #endregion

        #region 创意模式能力数据复制
        private static void CopyCreativePowersData() {
            using MemoryStream stream = new();
            using BinaryWriter writer = new(stream);

            FieldInfo field = typeof(CreativePowerManager).GetField("_powersById", BindingFlags.NonPublic | BindingFlags.Instance);
            foreach (KeyValuePair<ushort, ICreativePower> item in (Dictionary<ushort, ICreativePower>)field.GetValue(CreativePowerManager.Instance)) {
                if (item.Value is IPersistentPerWorldContent power) {
                    writer.Write((ushort)(item.Key + 1));
                    power.Save(writer);
                }
            }
            writer.Write((ushort)0);

            copiedData["powers"] = stream.ToArray();
        }

        private static void ReadCreativePowersData() {
            using MemoryStream stream = new(copiedData.Get<byte[]>("powers"));
            using BinaryReader reader = new(stream);

            FieldInfo field = typeof(CreativePowerManager).GetField("_powersById", BindingFlags.NonPublic | BindingFlags.Instance);
            Dictionary<ushort, ICreativePower> powers = (Dictionary<ushort, ICreativePower>)field.GetValue(CreativePowerManager.Instance);

            ushort id;
            while ((id = reader.ReadUInt16()) > 0) {
                ((IPersistentPerWorldContent)powers[(ushort)(id - 1)]).Load(reader, 0);
            }
        }
        #endregion

        #region Boss击杀状态复制
        private static void CopyDownedData() {
            copiedData[nameof(NPC.downedSlimeKing)] = NPC.downedSlimeKing;
            copiedData[nameof(NPC.downedBoss1)] = NPC.downedBoss1;
            copiedData[nameof(NPC.downedBoss2)] = NPC.downedBoss2;
            copiedData[nameof(NPC.downedBoss3)] = NPC.downedBoss3;
            copiedData[nameof(NPC.downedQueenBee)] = NPC.downedQueenBee;
            copiedData[nameof(NPC.downedDeerclops)] = NPC.downedDeerclops;
            copiedData[nameof(NPC.downedQueenSlime)] = NPC.downedQueenSlime;
            copiedData[nameof(NPC.downedMechBoss1)] = NPC.downedMechBoss1;
            copiedData[nameof(NPC.downedMechBoss2)] = NPC.downedMechBoss2;
            copiedData[nameof(NPC.downedMechBoss3)] = NPC.downedMechBoss3;
            copiedData[nameof(NPC.downedMechBossAny)] = NPC.downedMechBossAny;
            copiedData[nameof(NPC.downedPlantBoss)] = NPC.downedPlantBoss;
            copiedData[nameof(NPC.downedGolemBoss)] = NPC.downedGolemBoss;
            copiedData[nameof(NPC.downedFishron)] = NPC.downedFishron;
            copiedData[nameof(NPC.downedEmpressOfLight)] = NPC.downedEmpressOfLight;
            copiedData[nameof(NPC.downedAncientCultist)] = NPC.downedAncientCultist;
            copiedData[nameof(NPC.downedTowerSolar)] = NPC.downedTowerSolar;
            copiedData[nameof(NPC.downedTowerVortex)] = NPC.downedTowerVortex;
            copiedData[nameof(NPC.downedTowerNebula)] = NPC.downedTowerNebula;
            copiedData[nameof(NPC.downedTowerStardust)] = NPC.downedTowerStardust;
            copiedData[nameof(NPC.downedMoonlord)] = NPC.downedMoonlord;
            copiedData[nameof(NPC.downedGoblins)] = NPC.downedGoblins;
            copiedData[nameof(NPC.downedClown)] = NPC.downedClown;
            copiedData[nameof(NPC.downedFrost)] = NPC.downedFrost;
            copiedData[nameof(NPC.downedPirates)] = NPC.downedPirates;
            copiedData[nameof(NPC.downedMartians)] = NPC.downedMartians;
            copiedData[nameof(NPC.downedHalloweenTree)] = NPC.downedHalloweenTree;
            copiedData[nameof(NPC.downedHalloweenKing)] = NPC.downedHalloweenKing;
            copiedData[nameof(NPC.downedChristmasTree)] = NPC.downedChristmasTree;
            copiedData[nameof(NPC.downedChristmasSantank)] = NPC.downedChristmasSantank;
            copiedData[nameof(NPC.downedChristmasIceQueen)] = NPC.downedChristmasIceQueen;
            copiedData[nameof(DD2Event.DownedInvasionT1)] = DD2Event.DownedInvasionT1;
            copiedData[nameof(DD2Event.DownedInvasionT2)] = DD2Event.DownedInvasionT2;
            copiedData[nameof(DD2Event.DownedInvasionT3)] = DD2Event.DownedInvasionT3;
        }

        private static void ReadDownedData() {
            NPC.downedSlimeKing = copiedData.Get<bool>(nameof(NPC.downedSlimeKing));
            NPC.downedBoss1 = copiedData.Get<bool>(nameof(NPC.downedBoss1));
            NPC.downedBoss2 = copiedData.Get<bool>(nameof(NPC.downedBoss2));
            NPC.downedBoss3 = copiedData.Get<bool>(nameof(NPC.downedBoss3));
            NPC.downedQueenBee = copiedData.Get<bool>(nameof(NPC.downedQueenBee));
            NPC.downedDeerclops = copiedData.Get<bool>(nameof(NPC.downedDeerclops));
            NPC.downedQueenSlime = copiedData.Get<bool>(nameof(NPC.downedQueenSlime));
            NPC.downedMechBoss1 = copiedData.Get<bool>(nameof(NPC.downedMechBoss1));
            NPC.downedMechBoss2 = copiedData.Get<bool>(nameof(NPC.downedMechBoss2));
            NPC.downedMechBoss3 = copiedData.Get<bool>(nameof(NPC.downedMechBoss3));
            NPC.downedMechBossAny = copiedData.Get<bool>(nameof(NPC.downedMechBossAny));
            NPC.downedPlantBoss = copiedData.Get<bool>(nameof(NPC.downedPlantBoss));
            NPC.downedGolemBoss = copiedData.Get<bool>(nameof(NPC.downedGolemBoss));
            NPC.downedFishron = copiedData.Get<bool>(nameof(NPC.downedFishron));
            NPC.downedEmpressOfLight = copiedData.Get<bool>(nameof(NPC.downedEmpressOfLight));
            NPC.downedAncientCultist = copiedData.Get<bool>(nameof(NPC.downedAncientCultist));
            NPC.downedTowerSolar = copiedData.Get<bool>(nameof(NPC.downedTowerSolar));
            NPC.downedTowerVortex = copiedData.Get<bool>(nameof(NPC.downedTowerVortex));
            NPC.downedTowerNebula = copiedData.Get<bool>(nameof(NPC.downedTowerNebula));
            NPC.downedTowerStardust = copiedData.Get<bool>(nameof(NPC.downedTowerStardust));
            NPC.downedMoonlord = copiedData.Get<bool>(nameof(NPC.downedMoonlord));
            NPC.downedGoblins = copiedData.Get<bool>(nameof(NPC.downedGoblins));
            NPC.downedClown = copiedData.Get<bool>(nameof(NPC.downedClown));
            NPC.downedFrost = copiedData.Get<bool>(nameof(NPC.downedFrost));
            NPC.downedPirates = copiedData.Get<bool>(nameof(NPC.downedPirates));
            NPC.downedMartians = copiedData.Get<bool>(nameof(NPC.downedMartians));
            NPC.downedHalloweenTree = copiedData.Get<bool>(nameof(NPC.downedHalloweenTree));
            NPC.downedHalloweenKing = copiedData.Get<bool>(nameof(NPC.downedHalloweenKing));
            NPC.downedChristmasTree = copiedData.Get<bool>(nameof(NPC.downedChristmasTree));
            NPC.downedChristmasSantank = copiedData.Get<bool>(nameof(NPC.downedChristmasSantank));
            NPC.downedChristmasIceQueen = copiedData.Get<bool>(nameof(NPC.downedChristmasIceQueen));
            DD2Event.DownedInvasionT1 = copiedData.Get<bool>(nameof(DD2Event.DownedInvasionT1));
            DD2Event.DownedInvasionT2 = copiedData.Get<bool>(nameof(DD2Event.DownedInvasionT2));
            DD2Event.DownedInvasionT3 = copiedData.Get<bool>(nameof(DD2Event.DownedInvasionT3));
        }
        #endregion

        #region 世界加载/卸载回调
        /// <summary>
        /// 退出世界的回调，运行在后台线程
        /// </summary>
        internal static void ExitWorldCallBack(object index) {
            int netMode = Main.netMode;

            if (index != null) {
                if (netMode == NetmodeID.SinglePlayer) {
                    WorldFile.CacheSaveTime();

                    if (copiedData == null) {
                        copiedData = [];
                    }
                    if (cache != null) {
                        copyingDimensionData = true;
                        cache.CopyDimensionData();
                        copyingDimensionData = false;

                        cache.OnExit();
                    }
                    if ((int)index >= 0) {
                        CopyMainWorldData();

                        //保存维度状态：玩家正在进入维度
                        if (mainWorld != null) {
                            SaveDimensionState(mainWorld.UniqueId, (int)index);
                        }
                    }
                    else if (mainWorld != null) {
                        //玩家正在返回主世界，清除维度状态
                        ClearDimensionState(mainWorld.UniqueId);
                    }
                }
                else {
                    Netplay.Connection.State = 3;
                    cache?.OnExit();
                }
            }
            else {
                //玩家直接退出游戏（index == null）
                //如果当前在维度中，保存维度状态以便下次恢复
                if (mainWorld != null && current != null) {
                    int currentIndex = -1;
                    for (int i = 0; i < Dimension.Dimensions.Count; i++) {
                        if (Dimension.Dimensions[i] == current) {
                            currentIndex = i;
                            break;
                        }
                    }
                    if (currentIndex >= 0) {
                        SaveDimensionState(mainWorld.UniqueId, currentIndex);
                    }
                }
            }

            Main.invasionProgress = -1;
            Main.invasionProgressDisplayLeft = 0;
            Main.invasionProgressAlpha = 0;
            Main.invasionProgressIcon = 0;

            NoReturn = false;

            if (current != null) {
                HideUnderworld = true;
                current.OnEnter();
            }
            else {
                HideUnderworld = false;
            }

            SoundEngine.StopTrackedSounds();
            CaptureInterface.ResetFocus();

            Main.ActivePlayerFileData.StopPlayTimer();
            Player.SavePlayer(Main.ActivePlayerFileData);
            Player.ClearPlayerTempInfo();

            Rain.ClearRain();

            if (netMode != NetmodeID.MultiplayerClient) {
                WorldFile.SaveWorld();
            }
            else if (index == null) {
                Netplay.Disconnect = true;
                Main.netMode = NetmodeID.SinglePlayer;
            }
            SystemLoader.OnWorldUnload();

            Main.fastForwardTimeToDawn = false;
            Main.fastForwardTimeToDusk = false;
            Main.UpdateTimeRate();

            if (index == null) {
                //玩家直接退出到主菜单
                //重置所有维度状态，避免污染下次进入的世界
                ResetAllDimensionState();
                Main.menuMode = 0;
                return;
            }

            WorldGen.noMapUpdate = true;
            if (cache != null && cache.NoPlayerSaving) {
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

            if (netMode != NetmodeID.MultiplayerClient) {
                LoadWorld();
            }
        }

        /// <summary>
        /// 加载世界（维度或主世界）
        /// </summary>
        private static void LoadWorld() {
            bool isDimension = current != null;
            bool cloud = mainWorld.IsCloudSave;
            string path = isDimension ? CurrentPath : mainWorld.Path;

            Main.rand = new UnifiedRandom((int)DateTime.Now.Ticks);

            cache?.OnUnload();

            Main.ToggleGameplayUpdates(false);

            WorldGen.gen = true;
            WorldGen.loadFailed = false;
            WorldGen.loadSuccess = false;

            if (!isDimension || current.ShouldSave) {
                if (!isDimension) {
                    Main.ActiveWorldFileData = mainWorld;
                }

                TryLoadWorldFile(path, cloud, 0);
            }

            if (isDimension) {
                if (WorldGen.loadFailed) {
                    VaultMod.Instance.Logger.Warn("Failed to load \"" + Main.worldName + (WorldGen.worldBackup ? "\" from file" : "\" from file, no backup"));
                }

                if (!WorldGen.loadSuccess) {
                    LoadDimension(path, cloud);
                }

                current.OnLoad();
            }
            else if (!WorldGen.loadSuccess) {
                VaultMod.Instance.Logger.Error("Failed to load \"" + mainWorld.Name + (WorldGen.worldBackup ? "\" from file" : "\" from file, no backup"));
                Main.menuMode = 0;
                if (Main.netMode == NetmodeID.Server) {
                    Netplay.Disconnect = true;
                }
                return;
            }

            WorldGen.gen = false;

            if (Main.netMode != NetmodeID.Server) {
                if (Main.mapEnabled) {
                    Main.Map.Load();
                }
                Main.sectionManager.SetAllSectionsLoaded();
                while (Main.mapEnabled && Main.loadMapLock) {
                    Main.statusText = Lang.gen[68].Value + " " + (int)((float)Main.loadMapLastX / Main.maxTilesX * 100 + 1) + "%";
                    System.Threading.Thread.Sleep(0);
                }

                if (Main.anglerWhoFinishedToday.Contains(Main.LocalPlayer.name)) {
                    Main.anglerQuestFinished = true;
                }

                Main.QueueMainThreadAction(SpawnPlayer);
            }
        }

        private static void SpawnPlayer() {
            Main.LocalPlayer.Spawn(PlayerSpawnContext.SpawningIntoWorld);
            WorldFile.SetOngoingToTemps();
            Main.resetClouds = true;
            Main.gameMenu = false;
        }

        /// <summary>
        /// 加载维度（生成新世界或从文件加载）
        /// </summary>
        private static void LoadDimension(string path, bool cloud) {
            Main.worldName = current.DisplayName.Value;
            if (Main.netMode == NetmodeID.Server) {
                Console.Title = Main.worldName;
            }

            WorldFileData data = new(path, cloud) {
                Name = Main.worldName,
                CreationTime = DateTime.Now,
                Metadata = FileMetadata.FromCurrentSettings(FileType.World),
                WorldGeneratorVersion = Main.WorldGeneratorVersion,
                UniqueId = Guid.NewGuid()
            };
            Main.ActiveWorldFileData = data;

            Main.maxTilesX = current.Width;
            Main.maxTilesY = current.Height;
            Main.spawnTileX = Main.maxTilesX / 2;
            Main.spawnTileY = Main.maxTilesY / 2;
            WorldGen.setWorldSize();
            WorldGen.clearWorld();
            Main.worldSurface = Main.maxTilesY * 0.3;
            Main.rockLayer = Main.maxTilesY * 0.5;
            GenVars.waterLine = Main.maxTilesY;
            Main.weatherCounter = 18000;
            Cloud.resetClouds();

            ReadCopiedMainWorldData();

            double weight = 0;
            for (int i = 0; i < current.Tasks.Count; i++) {
                weight += current.Tasks[i].Weight;
            }
            WorldGenerator.CurrentGenerationProgress = new GenerationProgress {
                TotalWeight = weight
            };

            WorldGenConfiguration config = current.Config;

            for (int i = 0; i < current.Tasks.Count; i++) {
                WorldGen._genRand = new UnifiedRandom(data.Seed);
                Main.rand = new UnifiedRandom(data.Seed);

                GenPass task = current.Tasks[i];

                WorldGenerator.CurrentGenerationProgress.Start(task.Weight);
                task.Apply(WorldGenerator.CurrentGenerationProgress, config?.GetPassConfiguration(task.Name));
                WorldGenerator.CurrentGenerationProgress.End();
            }
            WorldGenerator.CurrentGenerationProgress = null;

            Main.WorldFileMetadata = FileMetadata.FromCurrentSettings(FileType.World);

            if (current.ShouldSave) {
                WorldFile.SaveWorld(cloud);
            }

            SystemLoader.OnWorldLoad();
        }

        /// <summary>
        /// 尝试加载世界文件
        /// </summary>
        private static void TryLoadWorldFile(string path, bool cloud, int tries) {
            LoadWorldFile(path, cloud);
            if (WorldGen.loadFailed) {
                if (tries == 1) {
                    if (FileUtilities.Exists(path + ".bak", cloud)) {
                        WorldGen.worldBackup = true;

                        FileUtilities.Copy(path, path + ".bad", cloud);
                        FileUtilities.Copy(path + ".bak", path, cloud);
                        FileUtilities.Delete(path + ".bak", cloud);

                        string tMLPath = Path.ChangeExtension(path, ".twld");
                        if (FileUtilities.Exists(tMLPath, cloud)) {
                            FileUtilities.Copy(tMLPath, tMLPath + ".bad", cloud);
                        }
                        if (FileUtilities.Exists(tMLPath + ".bak", cloud)) {
                            FileUtilities.Copy(tMLPath + ".bak", tMLPath, cloud);
                            FileUtilities.Delete(tMLPath + ".bak", cloud);
                        }
                    }
                    else {
                        WorldGen.worldBackup = false;
                        return;
                    }
                }
                else if (tries == 3) {
                    FileUtilities.Copy(path, path + ".bak", cloud);
                    FileUtilities.Copy(path + ".bad", path, cloud);
                    FileUtilities.Delete(path + ".bad", cloud);

                    string tMLPath = Path.ChangeExtension(path, ".twld");
                    if (FileUtilities.Exists(tMLPath, cloud)) {
                        FileUtilities.Copy(tMLPath, tMLPath + ".bak", cloud);
                    }
                    if (FileUtilities.Exists(tMLPath + ".bad", cloud)) {
                        FileUtilities.Copy(tMLPath + ".bad", tMLPath, cloud);
                        FileUtilities.Delete(tMLPath + ".bad", cloud);
                    }

                    return;
                }
                TryLoadWorldFile(path, cloud, tries++);
            }
        }

        /// <summary>
        /// 加载世界文件
        /// </summary>
        private static void LoadWorldFile(string path, bool cloud) {
            bool flag = cloud && SocialAPI.Cloud != null;
            if (!FileUtilities.Exists(path, flag)) {
                return;
            }

            if (current != null) {
                Main.ActiveWorldFileData = new WorldFileData(path, cloud);
            }

            try {
                int status;
                using (BinaryReader reader = new(new MemoryStream(FileUtilities.ReadAllBytes(path, flag)))) {
                    status = current != null ? current.ReadFile(reader) : WorldFile.LoadWorld_Version2(reader);
                }
                if (Main.netMode == NetmodeID.Server) {
                    Console.Title = Main.worldName;
                }
                SystemLoader.OnWorldLoad();

                //调用 tModLoader 的世界加载
                typeof(ModLoader).Assembly.GetType("Terraria.ModLoader.IO.WorldIO")
                    .GetMethod("Load", BindingFlags.NonPublic | BindingFlags.Static)
                    .Invoke(null, [path, flag]);

                if (status != 0) {
                    WorldGen.loadFailed = true;
                    WorldGen.loadSuccess = false;
                    return;
                }
                WorldGen.loadSuccess = true;
                WorldGen.loadFailed = false;

                if (current != null) {
                    current.PostReadFile();
                    cache?.ReadCopiedDimensionData();
                    ReadCopiedMainWorldData();
                }
                else {
                    PostLoadWorldFile();
                    cache.ReadCopiedDimensionData();
                    copiedData = null;
                }
            } catch {
                WorldGen.loadFailed = true;
                WorldGen.loadSuccess = false;
            }
        }

        /// <summary>
        /// 世界文件加载后的处理
        /// </summary>
        internal static void PostLoadWorldFile() {
            GenVars.waterLine = Main.maxTilesY;
            Liquid.QuickWater(2);
            WorldGen.WaterCheck();
            Liquid.quickSettle = true;
            int updates = 0;
            int amount = Liquid.numLiquid + LiquidBuffer.numLiquidBuffer;
            float num = 0;
            while (Liquid.numLiquid > 0 && updates < 100000) {
                updates++;
                float progress = (amount - Liquid.numLiquid + LiquidBuffer.numLiquidBuffer) / (float)amount;
                if (Liquid.numLiquid + LiquidBuffer.numLiquidBuffer > amount) {
                    amount = Liquid.numLiquid + LiquidBuffer.numLiquidBuffer;
                }
                if (progress > num) {
                    num = progress;
                }
                else {
                    progress = num;
                }
                Main.statusText = Lang.gen[27].Value + " " + (int)(progress * 100 / 2 + 50) + "%";
                Liquid.UpdateLiquid();
            }
            Liquid.quickSettle = false;
            Main.weatherCounter = WorldGen.genRand.Next(3600, 18000);
            Cloud.resetClouds();
            WorldGen.WaterCheck();
            NPC.setFireFlyChance();
            if (Main.slimeRainTime > 0) {
                Main.StartSlimeRain(false);
            }
            NPC.SetWorldSpecificMonstersByWorldID();
        }

        /// <summary>
        /// 获取指定类型维度的实例
        /// </summary>
        /// <typeparam name="T">维度类型</typeparam>
        /// <returns>维度实例，如果不存在则返回 null</returns>
        public static T GetDimension<T>() where T : Dimension {
            foreach (var dimension in Dimension.Dimensions) {
                if (dimension is T typedDimension) {
                    return typedDimension;
                }
            }
            return null;
        }

        /// <summary>
        /// 尝试获取指定类型的维度实例
        /// </summary>
        /// <typeparam name="T">维度类型</typeparam>
        /// <param name="dimension">输出的维度实例</param>
        /// <returns>是否成功获取</returns>
        public static bool TryGetDimension<T>(out T dimension) where T : Dimension {
            dimension = GetDimension<T>();
            return dimension != null;
        }

        /// <summary>
        /// 通过完整名称获取维度实例
        /// </summary>
        /// <param name="fullName">完整名称（格式：ModName/DimensionName）</param>
        /// <returns>维度实例，如果不存在则返回 null</returns>
        public static Dimension GetDimension(string fullName) {
            foreach (var dimension in Dimension.Dimensions) {
                if (dimension.FullName == fullName) {
                    return dimension;
                }
            }
            return null;
        }
        #endregion

        #region ModSystem 生命周期
        /// <summary>
        /// 玩家进入世界时的事件处理
        /// </summary>
        internal static void OnEnterWorld(Player player) {
            if (Main.netMode == NetmodeID.MultiplayerClient) {
                cache?.OnUnload();
                current?.OnLoad();
            }
            cache = current;

            //仅在主世界时检查是否需要恢复维度状态
            if (current == null && !isRestoringFromState && Main.netMode == NetmodeID.SinglePlayer) {
                TryRestoreDimensionState();
            }
        }

        /// <summary>
        /// 尝试从保存的状态恢复维度
        /// </summary>
        private static void TryRestoreDimensionState() {
            if (Main.ActiveWorldFileData == null) {
                return;
            }

            Guid worldId = Main.ActiveWorldFileData.UniqueId;
            int savedDimensionIndex = LoadDimensionState(worldId);

            if (savedDimensionIndex >= 0 && savedDimensionIndex < Dimension.Dimensions.Count) {
                isRestoringFromState = true;

                //延迟进入维度，确保世界完全加载
                Main.QueueMainThreadAction(() => {
                    //设置主世界引用
                    mainWorld = Main.ActiveWorldFileData;

                    //进入保存的维度
                    current = Dimension.Dimensions[savedDimensionIndex];
                    Main.gameMenu = true;

                    Task.Factory.StartNew(ExitWorldCallBack, savedDimensionIndex);

                    isRestoringFromState = false;
                });
            }
        }

        /// <summary>
        /// 断开连接时的事件处理
        /// </summary>
        internal static void OnDisconnect() {
            if (current != null || cache != null) {
                Main.menuMode = 14;
            }

            //重置所有维度相关状态，避免污染下次进入的世界
            ResetAllDimensionState();
        }

        /// <inheritdoc/>
        public override void Load() {
            Player.Hooks.OnEnterWorld += OnEnterWorld;
            Netplay.OnDisconnect += OnDisconnect;
        }

        /// <inheritdoc/>
        public override void PreSaveAndQuit() {
            //当玩家通过"保存并退出"按钮退出时调用
            //如果当前在维度中，保存维度状态
            if (current != null && mainWorld != null) {
                int currentIndex = -1;
                for (int i = 0; i < Dimension.Dimensions.Count; i++) {
                    if (Dimension.Dimensions[i] == current) {
                        currentIndex = i;
                        break;
                    }
                }
                if (currentIndex >= 0) {
                    SaveDimensionState(mainWorld.UniqueId, currentIndex);
                }
            }

            //重置所有维度状态，避免污染下次进入的世界
            ResetAllDimensionState();
        }

        /// <summary>
        /// 重置所有维度相关的内存状态
        /// </summary>
        private static void ResetAllDimensionState() {
            current = null;
            cache = null;
            mainWorld = null;
            copiedData = null;
            isRestoringFromState = false;
            NoReturn = false;
            HideUnderworld = false;
        }

        /// <inheritdoc/>
        public override void OnWorldUnload() {
            //当世界卸载时调用
            //注意：维度切换时也会触发，需要判断是否是真正的退出
        }

        /// <inheritdoc/>
        public override void Unload() {
            Player.Hooks.OnEnterWorld -= OnEnterWorld;
            Netplay.OnDisconnect -= OnDisconnect;

            ResetAllDimensionState();
        }
        #endregion
    }
}
