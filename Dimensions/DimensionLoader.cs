using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
    #region 维度过渡状态
    /// <summary>
    /// 维度过渡阶段枚举，描述玩家在维度间移动时的状态
    /// </summary>
    public enum DimensionPhase
    {
        /// <summary>空闲状态，未进行任何过渡</summary>
        Idle,
        /// <summary>准备离开当前维度</summary>
        PreparingExit,
        /// <summary>正在保存当前维度数据</summary>
        SavingData,
        /// <summary>正在卸载当前维度</summary>
        Unloading,
        /// <summary>正在加载目标维度</summary>
        Loading,
        /// <summary>正在生成目标维度</summary>
        Generating,
        /// <summary>正在恢复数据到目标维度</summary>
        RestoringData,
        /// <summary>正在完成进入流程</summary>
        Finalizing
    }

    /// <summary>
    /// 维度过渡事件参数
    /// </summary>
    public sealed class DimensionTransitionArgs
    {
        /// <summary>源维度，null表示主世界</summary>
        public Dimension SourceDimension { get; init; }
        /// <summary>目标维度，null表示主世界</summary>
        public Dimension TargetDimension { get; init; }
        /// <summary>当前过渡阶段</summary>
        public DimensionPhase Phase { get; init; }
        /// <summary>过渡进度(0到1)</summary>
        public float Progress { get; init; }
        /// <summary>是否可以取消本次过渡</summary>
        public bool Cancellable { get; init; }
        /// <summary>设为true可取消过渡(仅在Cancellable为true时有效)</summary>
        public bool Cancel { get; set; }
    }
    #endregion

    #region 数据复制器接口
    /// <summary>
    /// 维度数据复制器接口，实现此接口可自定义数据在维度间的传递方式
    /// </summary>
    public interface IDimensionDataCopier
    {
        /// <summary>复制器优先级，数值越小越先执行</summary>
        int Priority => 0;
        /// <summary>将数据序列化到流中</summary>
        void Serialize(BinaryWriter writer);
        /// <summary>从流中反序列化数据</summary>
        void Deserialize(BinaryReader reader);
    }
    #endregion

    #region 维度上下文
    /// <summary>
    /// 维度运行时上下文，封装维度系统的所有状态信息
    /// </summary>
    public sealed class DimensionContext
    {
        /// <summary>当前活跃的维度实例</summary>
        public Dimension ActiveDimension { get; internal set; }
        /// <summary>上一个维度的缓存引用</summary>
        public Dimension CachedDimension { get; internal set; }
        /// <summary>主世界文件数据</summary>
        public WorldFileData OriginWorld { get; internal set; }
        /// <summary>当前过渡阶段</summary>
        public DimensionPhase CurrentPhase { get; internal set; } = DimensionPhase.Idle;
        /// <summary>维度间传递的数据包</summary>
        public TagCompound TransferData { get; internal set; }
        /// <summary>是否处于状态恢复流程中</summary>
        public bool IsRestoring { get; internal set; }
        /// <summary>是否正在执行维度数据复制</summary>
        public bool IsCopyingData { get; internal set; }

        /// <summary>检查是否在任意维度中</summary>
        public bool InDimension => ActiveDimension != null;
        /// <summary>检查是否正在进行维度过渡</summary>
        public bool InTransition => CurrentPhase != DimensionPhase.Idle;

        /// <summary>获取当前维度的世界文件路径</summary>
        public string GetCurrentWorldPath() {
            if (OriginWorld == null || ActiveDimension == null) {
                return string.Empty;
            }
            string dimFolder = Path.Combine(Main.WorldPath, OriginWorld.UniqueId.ToString());
            return Path.Combine(dimFolder, ActiveDimension.FileName + ".wld");
        }

        /// <summary>重置上下文到初始状态</summary>
        public void Reset() {
            ActiveDimension = null;
            CachedDimension = null;
            OriginWorld = null;
            CurrentPhase = DimensionPhase.Idle;
            TransferData = null;
            IsRestoring = false;
            IsCopyingData = false;
        }
    }
    #endregion

    /// <summary>
    /// 维度系统核心管理器，采用上下文驱动的状态机架构
    /// 负责维度的生命周期管理、数据同步和过渡控制
    /// </summary>
    public class DimensionLoader : ModSystem
    {
        #region 单例与上下文
        /// <summary>维度运行时上下文实例</summary>
        internal static readonly DimensionContext Context = new();

        /// <summary>状态持久化文件的格式版本</summary>
        private const int StateFileVersion = 2;

        /// <summary>注册的数据复制器列表</summary>
        private static readonly List<IDimensionDataCopier> _dataCopiers = [];

        /// <summary>维度过渡事件，在过渡的各个阶段触发</summary>
        public static event Action<DimensionTransitionArgs> OnTransition;
        #endregion

        #region 公开查询接口
        /// <summary>隐藏返回按钮的标记</summary>
        public static bool NoReturn { get; set; }

        /// <summary>隐藏地狱背景的标记</summary>
        public static bool HideUnderworld { get; set; }

        /// <summary>获取当前活跃的维度</summary>
        public static Dimension Current => Context.ActiveDimension;

        /// <summary>获取当前维度的世界文件路径</summary>
        public static string CurrentPath => Context.GetCurrentWorldPath();

        /// <summary>检查是否在任意维度中</summary>
        public static bool AnyActive() => Context.InDimension;

        /// <summary>检查指定ID的维度是否处于活跃状态</summary>
        public static bool IsActive(string dimensionId) {
            return Context.ActiveDimension?.FullName == dimensionId;
        }

        /// <summary>检查指定类型的维度是否处于活跃状态</summary>
        public static bool IsActive<TDim>() where TDim : Dimension {
            return Context.ActiveDimension?.GetType() == typeof(TDim);
        }

        /// <summary>检查当前维度是否属于指定模组</summary>
        public static bool AnyActive(Mod mod) => Context.ActiveDimension?.Mod == mod;

        /// <summary>检查当前维度是否属于指定模组类型</summary>
        public static bool AnyActive<TMod>() where TMod : Mod {
            return Context.ActiveDimension?.Mod == ModContent.GetInstance<TMod>();
        }

        /// <summary>获取指定类型的维度实例</summary>
        public static TDim GetDimension<TDim>() where TDim : Dimension {
            return Dimension.Dimensions.OfType<TDim>().FirstOrDefault();
        }

        /// <summary>尝试获取指定类型的维度实例</summary>
        public static bool TryGetDimension<TDim>(out TDim dimension) where TDim : Dimension {
            dimension = GetDimension<TDim>();
            return dimension != null;
        }

        /// <summary>通过完整名称获取维度实例</summary>
        public static Dimension GetDimension(string fullName) {
            return Dimension.Dimensions.FirstOrDefault(d => d.FullName == fullName);
        }

        /// <summary>获取指定主世界的维度存储目录</summary>
        public static string GetDimensionDirectory(Guid worldId) {
            return Path.Combine(Main.WorldPath, worldId.ToString());
        }
        #endregion

        #region 维度进入与退出
        /// <summary>
        /// 请求进入指定ID的维度
        /// </summary>
        /// <param name="dimensionId">维度的完整ID(格式: ModName/DimensionName)</param>
        /// <returns>请求是否成功发起</returns>
        public static bool Enter(string dimensionId) {
            if (Context.InTransition || Context.ActiveDimension != Context.CachedDimension) {
                return false;
            }
            int targetIndex = FindDimensionIndex(dimensionId);
            if (targetIndex < 0) {
                return false;
            }
            InitiateTransition(targetIndex);
            return true;
        }

        /// <summary>
        /// 请求进入指定类型的维度
        /// </summary>
        public static bool Enter<TDim>() where TDim : Dimension {
            if (Context.InTransition || Context.ActiveDimension != Context.CachedDimension) {
                return false;
            }
            int targetIndex = FindDimensionIndex<TDim>();
            if (targetIndex < 0) {
                return false;
            }
            InitiateTransition(targetIndex);
            return true;
        }

        /// <summary>
        /// 请求离开当前维度，返回到ReturnDestination指定的位置
        /// </summary>
        public static void Exit() {
            if (Context.ActiveDimension == null || Context.ActiveDimension != Context.CachedDimension) {
                return;
            }
            InitiateTransition(Context.ActiveDimension.ReturnDestination);
        }

        private static int FindDimensionIndex(string fullName) {
            for (int i = 0; i < Dimension.Dimensions.Count; i++) {
                if (Dimension.Dimensions[i].FullName == fullName) {
                    return i;
                }
            }
            return -1;
        }

        private static int FindDimensionIndex<TDim>() where TDim : Dimension {
            for (int i = 0; i < Dimension.Dimensions.Count; i++) {
                if (Dimension.Dimensions[i] is TDim) {
                    return i;
                }
            }
            return -1;
        }

        private static void InitiateTransition(int targetIndex) {
            if (Main.netMode == NetmodeID.Server) {
                return;
            }

            //返回主菜单的特殊处理
            if (targetIndex == int.MinValue) {
                Context.ActiveDimension = null;
                Main.gameMenu = true;
                Task.Factory.StartNew(ExecuteWorldTransition, null);
                return;
            }

            if (Main.netMode == NetmodeID.SinglePlayer) {
                //首次离开主世界时记录主世界信息
                if (Context.ActiveDimension == null && targetIndex >= 0) {
                    Context.OriginWorld = Main.ActiveWorldFileData;
                }
                Context.ActiveDimension = targetIndex < 0 ? null : Dimension.Dimensions[targetIndex];
                Main.gameMenu = true;
                Task.Factory.StartNew(ExecuteWorldTransition, targetIndex);
                return;
            }
        }
        #endregion

        #region 数据传递系统
        /// <summary>
        /// 注册一个数据复制器
        /// </summary>
        public static void RegisterDataCopier(IDimensionDataCopier copier) {
            _dataCopiers.Add(copier);
            _dataCopiers.Sort((a, b) => a.Priority.CompareTo(b.Priority));
        }

        /// <summary>
        /// 存储数据到维度传递包中
        /// </summary>
        /// <param name="key">数据键名，建议使用nameof</param>
        /// <param name="value">要存储的数据</param>
        public static void CopyWorldData(string key, object value) {
            if (value == null || Context.TransferData == null) {
                return;
            }
            if (Context.IsCopyingData || !Context.TransferData.ContainsKey(key)) {
                Context.TransferData[key] = value;
            }
        }

        /// <summary>
        /// 从维度传递包中读取数据
        /// </summary>
        public static T ReadCopiedWorldData<T>(string key) {
            if (Context.TransferData == null) {
                return default;
            }
            return Context.TransferData.Get<T>(key);
        }

        /// <summary>
        /// 收集主世界的核心数据到传递包
        /// </summary>
        internal static void CollectMainWorldData() {
            var data = Context.TransferData;

            //基础世界信息
            data["_originId"] = Main.ActiveWorldFileData.UniqueId.ToByteArray();
            data["_seed"] = Main.ActiveWorldFileData.SeedText;
            data["_gameMode"] = Main.ActiveWorldFileData.GameMode;
            data["_hardMode"] = Main.hardMode;

            //世界特殊种子标记
            PackWorldFlags(data);

            //图鉴进度
            PackBestiaryProgress(data);

            //创意模式能力状态
            PackCreativePowers(data);

            //Boss击杀进度
            PackBossProgress(data);

            //调用模组扩展的数据复制
            foreach (var copier in ModContent.GetContent<ICopyDimensionData>()) {
                copier.CopyMainWorldData();
            }
        }

        /// <summary>
        /// 将传递包中的数据恢复到当前世界
        /// </summary>
        internal static void RestoreMainWorldData() {
            var data = Context.TransferData;
            if (data == null) return;

            //恢复基础信息
            Context.OriginWorld.UniqueId = new Guid(data.Get<byte[]>("_originId"));
            Main.ActiveWorldFileData.SetSeed(data.Get<string>("_seed"));
            Main.GameMode = data.Get<int>("_gameMode");
            Main.hardMode = data.Get<bool>("_hardMode");

            //恢复世界标记
            UnpackWorldFlags(data);

            //恢复图鉴
            UnpackBestiaryProgress(data);

            //恢复创意能力
            UnpackCreativePowers(data);

            //恢复Boss进度
            UnpackBossProgress(data);

            //调用模组扩展的数据读取
            foreach (var copier in ModContent.GetContent<ICopyDimensionData>()) {
                copier.ReadCopiedMainWorldData();
            }
        }

        #region 数据打包与解包
        private static void PackWorldFlags(TagCompound data) {
            //使用位掩码压缩布尔值以减少存储空间
            int flags = 0;
            if (Main.drunkWorld) flags |= 1;
            if (Main.getGoodWorld) flags |= 2;
            if (Main.tenthAnniversaryWorld) flags |= 4;
            if (Main.dontStarveWorld) flags |= 8;
            if (Main.notTheBeesWorld) flags |= 16;
            if (Main.remixWorld) flags |= 32;
            if (Main.noTrapsWorld) flags |= 64;
            if (Main.zenithWorld) flags |= 128;
            data["_worldFlags"] = flags;
        }

        private static void UnpackWorldFlags(TagCompound data) {
            int flags = data.Get<int>("_worldFlags");
            Main.drunkWorld = (flags & 1) != 0;
            Main.getGoodWorld = (flags & 2) != 0;
            Main.tenthAnniversaryWorld = (flags & 4) != 0;
            Main.dontStarveWorld = (flags & 8) != 0;
            Main.notTheBeesWorld = (flags & 16) != 0;
            Main.remixWorld = (flags & 32) != 0;
            Main.noTrapsWorld = (flags & 64) != 0;
            Main.zenithWorld = (flags & 128) != 0;
        }

        private static void PackBestiaryProgress(TagCompound data) {
            using var ms = new MemoryStream();
            using var bw = new BinaryWriter(ms);

            //击杀记录
            var killsField = typeof(NPCKillsTracker).GetField("_killCountsByNpcId", BindingFlags.NonPublic | BindingFlags.Instance);
            var kills = (Dictionary<string, int>)killsField.GetValue(Main.BestiaryTracker.Kills);
            bw.Write(kills.Count);
            foreach (var kv in kills) {
                bw.Write(kv.Key);
                bw.Write(kv.Value);
            }

            //目击记录
            var sightsField = typeof(NPCWasNearPlayerTracker).GetField("_wasNearPlayer", BindingFlags.NonPublic | BindingFlags.Instance);
            var sights = (HashSet<string>)sightsField.GetValue(Main.BestiaryTracker.Sights);
            bw.Write(sights.Count);
            foreach (var s in sights) {
                bw.Write(s);
            }

            //对话记录
            var chatsField = typeof(NPCWasChatWithTracker).GetField("_chattedWithPlayer", BindingFlags.NonPublic | BindingFlags.Instance);
            var chats = (HashSet<string>)chatsField.GetValue(Main.BestiaryTracker.Chats);
            bw.Write(chats.Count);
            foreach (var c in chats) {
                bw.Write(c);
            }

            data["_bestiary"] = ms.ToArray();
        }

        private static void UnpackBestiaryProgress(TagCompound data) {
            var bytes = data.Get<byte[]>("_bestiary");
            if (bytes == null) return;

            using var ms = new MemoryStream(bytes);
            using var br = new BinaryReader(ms);

            var killsField = typeof(NPCKillsTracker).GetField("_killCountsByNpcId", BindingFlags.NonPublic | BindingFlags.Instance);
            var kills = (Dictionary<string, int>)killsField.GetValue(Main.BestiaryTracker.Kills);
            int killCount = br.ReadInt32();
            for (int i = 0; i < killCount; i++) {
                kills[br.ReadString()] = br.ReadInt32();
            }

            var sightsField = typeof(NPCWasNearPlayerTracker).GetField("_wasNearPlayer", BindingFlags.NonPublic | BindingFlags.Instance);
            var sights = (HashSet<string>)sightsField.GetValue(Main.BestiaryTracker.Sights);
            int sightCount = br.ReadInt32();
            for (int i = 0; i < sightCount; i++) {
                sights.Add(br.ReadString());
            }

            var chatsField = typeof(NPCWasChatWithTracker).GetField("_chattedWithPlayer", BindingFlags.NonPublic | BindingFlags.Instance);
            var chats = (HashSet<string>)chatsField.GetValue(Main.BestiaryTracker.Chats);
            int chatCount = br.ReadInt32();
            for (int i = 0; i < chatCount; i++) {
                chats.Add(br.ReadString());
            }
        }

        private static void PackCreativePowers(TagCompound data) {
            using var ms = new MemoryStream();
            using var bw = new BinaryWriter(ms);

            var powersField = typeof(CreativePowerManager).GetField("_powersById", BindingFlags.NonPublic | BindingFlags.Instance);
            var powers = (Dictionary<ushort, ICreativePower>)powersField.GetValue(CreativePowerManager.Instance);

            foreach (var kv in powers) {
                if (kv.Value is IPersistentPerWorldContent persistent) {
                    bw.Write((ushort)(kv.Key + 1));
                    persistent.Save(bw);
                }
            }
            bw.Write((ushort)0);

            data["_powers"] = ms.ToArray();
        }

        private static void UnpackCreativePowers(TagCompound data) {
            var bytes = data.Get<byte[]>("_powers");
            if (bytes == null) return;

            using var ms = new MemoryStream(bytes);
            using var br = new BinaryReader(ms);

            var powersField = typeof(CreativePowerManager).GetField("_powersById", BindingFlags.NonPublic | BindingFlags.Instance);
            var powers = (Dictionary<ushort, ICreativePower>)powersField.GetValue(CreativePowerManager.Instance);

            ushort id;
            while ((id = br.ReadUInt16()) > 0) {
                if (powers.TryGetValue((ushort)(id - 1), out var power) && power is IPersistentPerWorldContent persistent) {
                    persistent.Load(br, 0);
                }
            }
        }

        private static void PackBossProgress(TagCompound data) {
            //使用多个int位掩码存储大量布尔值
            int flags1 = 0, flags2 = 0;

            if (NPC.downedSlimeKing) flags1 |= 1 << 0;
            if (NPC.downedBoss1) flags1 |= 1 << 1;
            if (NPC.downedBoss2) flags1 |= 1 << 2;
            if (NPC.downedBoss3) flags1 |= 1 << 3;
            if (NPC.downedQueenBee) flags1 |= 1 << 4;
            if (NPC.downedDeerclops) flags1 |= 1 << 5;
            if (NPC.downedQueenSlime) flags1 |= 1 << 6;
            if (NPC.downedMechBoss1) flags1 |= 1 << 7;
            if (NPC.downedMechBoss2) flags1 |= 1 << 8;
            if (NPC.downedMechBoss3) flags1 |= 1 << 9;
            if (NPC.downedMechBossAny) flags1 |= 1 << 10;
            if (NPC.downedPlantBoss) flags1 |= 1 << 11;
            if (NPC.downedGolemBoss) flags1 |= 1 << 12;
            if (NPC.downedFishron) flags1 |= 1 << 13;
            if (NPC.downedEmpressOfLight) flags1 |= 1 << 14;
            if (NPC.downedAncientCultist) flags1 |= 1 << 15;
            if (NPC.downedTowerSolar) flags1 |= 1 << 16;
            if (NPC.downedTowerVortex) flags1 |= 1 << 17;
            if (NPC.downedTowerNebula) flags1 |= 1 << 18;
            if (NPC.downedTowerStardust) flags1 |= 1 << 19;
            if (NPC.downedMoonlord) flags1 |= 1 << 20;

            if (NPC.downedGoblins) flags2 |= 1 << 0;
            if (NPC.downedClown) flags2 |= 1 << 1;
            if (NPC.downedFrost) flags2 |= 1 << 2;
            if (NPC.downedPirates) flags2 |= 1 << 3;
            if (NPC.downedMartians) flags2 |= 1 << 4;
            if (NPC.downedHalloweenTree) flags2 |= 1 << 5;
            if (NPC.downedHalloweenKing) flags2 |= 1 << 6;
            if (NPC.downedChristmasTree) flags2 |= 1 << 7;
            if (NPC.downedChristmasSantank) flags2 |= 1 << 8;
            if (NPC.downedChristmasIceQueen) flags2 |= 1 << 9;
            if (DD2Event.DownedInvasionT1) flags2 |= 1 << 10;
            if (DD2Event.DownedInvasionT2) flags2 |= 1 << 11;
            if (DD2Event.DownedInvasionT3) flags2 |= 1 << 12;

            data["_bossFlags1"] = flags1;
            data["_bossFlags2"] = flags2;
        }

        private static void UnpackBossProgress(TagCompound data) {
            int flags1 = data.Get<int>("_bossFlags1");
            int flags2 = data.Get<int>("_bossFlags2");

            NPC.downedSlimeKing = (flags1 & (1 << 0)) != 0;
            NPC.downedBoss1 = (flags1 & (1 << 1)) != 0;
            NPC.downedBoss2 = (flags1 & (1 << 2)) != 0;
            NPC.downedBoss3 = (flags1 & (1 << 3)) != 0;
            NPC.downedQueenBee = (flags1 & (1 << 4)) != 0;
            NPC.downedDeerclops = (flags1 & (1 << 5)) != 0;
            NPC.downedQueenSlime = (flags1 & (1 << 6)) != 0;
            NPC.downedMechBoss1 = (flags1 & (1 << 7)) != 0;
            NPC.downedMechBoss2 = (flags1 & (1 << 8)) != 0;
            NPC.downedMechBoss3 = (flags1 & (1 << 9)) != 0;
            NPC.downedMechBossAny = (flags1 & (1 << 10)) != 0;
            NPC.downedPlantBoss = (flags1 & (1 << 11)) != 0;
            NPC.downedGolemBoss = (flags1 & (1 << 12)) != 0;
            NPC.downedFishron = (flags1 & (1 << 13)) != 0;
            NPC.downedEmpressOfLight = (flags1 & (1 << 14)) != 0;
            NPC.downedAncientCultist = (flags1 & (1 << 15)) != 0;
            NPC.downedTowerSolar = (flags1 & (1 << 16)) != 0;
            NPC.downedTowerVortex = (flags1 & (1 << 17)) != 0;
            NPC.downedTowerNebula = (flags1 & (1 << 18)) != 0;
            NPC.downedTowerStardust = (flags1 & (1 << 19)) != 0;
            NPC.downedMoonlord = (flags1 & (1 << 20)) != 0;

            NPC.downedGoblins = (flags2 & (1 << 0)) != 0;
            NPC.downedClown = (flags2 & (1 << 1)) != 0;
            NPC.downedFrost = (flags2 & (1 << 2)) != 0;
            NPC.downedPirates = (flags2 & (1 << 3)) != 0;
            NPC.downedMartians = (flags2 & (1 << 4)) != 0;
            NPC.downedHalloweenTree = (flags2 & (1 << 5)) != 0;
            NPC.downedHalloweenKing = (flags2 & (1 << 6)) != 0;
            NPC.downedChristmasTree = (flags2 & (1 << 7)) != 0;
            NPC.downedChristmasSantank = (flags2 & (1 << 8)) != 0;
            NPC.downedChristmasIceQueen = (flags2 & (1 << 9)) != 0;
            DD2Event.DownedInvasionT1 = (flags2 & (1 << 10)) != 0;
            DD2Event.DownedInvasionT2 = (flags2 & (1 << 11)) != 0;
            DD2Event.DownedInvasionT3 = (flags2 & (1 << 12)) != 0;
        }
        #endregion
        #endregion

        #region 状态持久化
        private static string GetStateFilePath(Guid worldId) {
            return Path.Combine(Main.WorldPath, worldId.ToString(), "dim_session.bin");
        }

        private static void PersistCurrentState(Guid worldId, int dimIndex) {
            try {
                string filePath = GetStateFilePath(worldId);
                string dir = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) {
                    Directory.CreateDirectory(dir);
                }

                using var fs = new FileStream(filePath, FileMode.Create);
                using var bw = new BinaryWriter(fs);

                bw.Write(StateFileVersion);
                bw.Write(dimIndex);
                if (dimIndex >= 0 && dimIndex < Dimension.Dimensions.Count) {
                    bw.Write(Dimension.Dimensions[dimIndex].FullName);
                }
                else {
                    bw.Write(string.Empty);
                }

                VaultMod.Instance.Logger.Info($"[Dimension] 状态已持久化: worldId={worldId}, dimIndex={dimIndex}");
            }
            catch (Exception ex) {
                VaultMod.Instance.Logger.Warn($"[Dimension] 状态持久化失败: {ex.Message}");
            }
        }

        private static int LoadPersistedState(Guid worldId) {
            try {
                string filePath = GetStateFilePath(worldId);
                if (!File.Exists(filePath)) {
                    return -1;
                }

                using var fs = new FileStream(filePath, FileMode.Open);
                using var br = new BinaryReader(fs);

                int version = br.ReadInt32();
                if (version != StateFileVersion) {
                    VaultMod.Instance.Logger.Warn($"[Dimension] 状态文件版本不匹配: 期望{StateFileVersion}, 实际{version}");
                    return -1;
                }

                int dimIndex = br.ReadInt32();
                string dimName = br.ReadString();

                if (dimIndex >= 0 && !string.IsNullOrEmpty(dimName)) {
                    for (int i = 0; i < Dimension.Dimensions.Count; i++) {
                        if (Dimension.Dimensions[i].FullName == dimName) {
                            VaultMod.Instance.Logger.Info($"[Dimension] 恢复状态: worldId={worldId}, dim={dimName}");
                            return i;
                        }
                    }
                    VaultMod.Instance.Logger.Warn($"[Dimension] 维度'{dimName}'已不存在，将返回主世界");
                    return -1;
                }

                return dimIndex;
            }
            catch (Exception ex) {
                VaultMod.Instance.Logger.Warn($"[Dimension] 加载持久化状态失败: {ex.Message}");
                return -1;
            }
        }

        private static void ClearPersistedState(Guid worldId) {
            PersistCurrentState(worldId, -1);
        }
        #endregion

        #region 世界过渡执行
        /// <summary>
        /// 执行世界过渡的核心流程(后台线程)
        /// </summary>
        internal static void ExecuteWorldTransition(object targetIndexObj) {
            int netMode = Main.netMode;
            var ctx = Context;

            //阶段1: 准备退出
            ctx.CurrentPhase = DimensionPhase.PreparingExit;
            FireTransitionEvent(ctx.CachedDimension, ctx.ActiveDimension, DimensionPhase.PreparingExit, 0f);

            if (targetIndexObj != null) {
                int targetIndex = (int)targetIndexObj;

                if (netMode == NetmodeID.SinglePlayer) {
                    WorldFile.CacheSaveTime();

                    //阶段2: 保存数据
                    ctx.CurrentPhase = DimensionPhase.SavingData;
                    FireTransitionEvent(ctx.CachedDimension, ctx.ActiveDimension, DimensionPhase.SavingData, 0.1f);

                    ctx.TransferData ??= [];

                    if (ctx.CachedDimension != null) {
                        ctx.IsCopyingData = true;
                        ctx.CachedDimension.CopyDimensionData();
                        ctx.IsCopyingData = false;
                        ctx.CachedDimension.OnExit();
                    }

                    if (targetIndex >= 0) {
                        CollectMainWorldData();
                        if (ctx.OriginWorld != null) {
                            PersistCurrentState(ctx.OriginWorld.UniqueId, targetIndex);
                        }
                    }
                    else if (ctx.OriginWorld != null) {
                        ClearPersistedState(ctx.OriginWorld.UniqueId);
                    }
                }
                else {
                    Netplay.Connection.State = 3;
                    ctx.CachedDimension?.OnExit();
                }
            }
            else {
                //直接退出到主菜单时保存当前维度状态
                if (ctx.OriginWorld != null && ctx.ActiveDimension != null) {
                    int currentIdx = Dimension.Dimensions.IndexOf(ctx.ActiveDimension);
                    if (currentIdx >= 0) {
                        PersistCurrentState(ctx.OriginWorld.UniqueId, currentIdx);
                    }
                }
            }

            //阶段3: 卸载当前世界
            ctx.CurrentPhase = DimensionPhase.Unloading;
            FireTransitionEvent(ctx.CachedDimension, ctx.ActiveDimension, DimensionPhase.Unloading, 0.2f);

            ResetInvasionState();
            NoReturn = false;

            if (ctx.ActiveDimension != null) {
                HideUnderworld = true;
                ctx.ActiveDimension.OnEnter();
            }
            else {
                HideUnderworld = false;
            }

            CleanupCurrentWorld();
            SystemLoader.OnWorldUnload();

            Main.fastForwardTimeToDawn = false;
            Main.fastForwardTimeToDusk = false;
            Main.UpdateTimeRate();

            if (targetIndexObj == null) {
                ctx.Reset();
                Main.menuMode = 0;
                return;
            }

            //阶段4/5: 加载或生成目标世界
            WorldGen.noMapUpdate = true;
            HandlePlayerDataOnTransition();
            DeactivateOtherPlayers();

            if (netMode != NetmodeID.MultiplayerClient) {
                PerformWorldLoad();
            }

            ctx.CurrentPhase = DimensionPhase.Idle;
        }

        private static void FireTransitionEvent(Dimension source, Dimension target, DimensionPhase phase, float progress) {
            OnTransition?.Invoke(new DimensionTransitionArgs {
                SourceDimension = source,
                TargetDimension = target,
                Phase = phase,
                Progress = progress,
                Cancellable = phase == DimensionPhase.PreparingExit
            });
        }

        private static void ResetInvasionState() {
            Main.invasionProgress = -1;
            Main.invasionProgressDisplayLeft = 0;
            Main.invasionProgressAlpha = 0;
            Main.invasionProgressIcon = 0;
        }

        private static void CleanupCurrentWorld() {
            SoundEngine.StopTrackedSounds();
            CaptureInterface.ResetFocus();

            Main.ActivePlayerFileData.StopPlayTimer();
            Player.SavePlayer(Main.ActivePlayerFileData);
            Player.ClearPlayerTempInfo();

            Rain.ClearRain();

            if (Main.netMode != NetmodeID.MultiplayerClient) {
                WorldFile.SaveWorld();
            }
            else {
                Netplay.Disconnect = true;
                Main.netMode = NetmodeID.SinglePlayer;
            }
        }

        private static void HandlePlayerDataOnTransition() {
            var ctx = Context;
            if (ctx.CachedDimension != null && ctx.CachedDimension.NoPlayerSaving) {
                var playerData = Player.GetFileData(Main.ActivePlayerFileData.Path, Main.ActivePlayerFileData.IsCloudSave);
                if (playerData != null) {
                    playerData.Player.whoAmI = Main.myPlayer;
                    playerData.SetAsActive();
                }
            }
        }

        private static void DeactivateOtherPlayers() {
            for (int i = 0; i < 255; i++) {
                if (i != Main.myPlayer) {
                    Main.player[i].active = false;
                }
            }
        }
        #endregion

        #region 世界加载
        private static void PerformWorldLoad() {
            var ctx = Context;
            bool loadingDimension = ctx.ActiveDimension != null;
            bool isCloud = ctx.OriginWorld.IsCloudSave;
            string worldPath = loadingDimension ? ctx.GetCurrentWorldPath() : ctx.OriginWorld.Path;

            Main.rand = new UnifiedRandom((int)DateTime.Now.Ticks);

            ctx.CurrentPhase = DimensionPhase.Loading;
            ctx.CachedDimension?.OnUnload();

            Main.ToggleGameplayUpdates(false);
            WorldGen.gen = true;
            WorldGen.loadFailed = false;
            WorldGen.loadSuccess = false;

            if (!loadingDimension || ctx.ActiveDimension.ShouldSave) {
                if (!loadingDimension) {
                    Main.ActiveWorldFileData = ctx.OriginWorld;
                }
                AttemptWorldFileLoad(worldPath, isCloud, 0);
            }

            if (loadingDimension) {
                if (WorldGen.loadFailed) {
                    VaultMod.Instance.Logger.Warn($"加载维度\"{Main.worldName}\"失败" + (WorldGen.worldBackup ? "(已尝试备份)" : "(无备份)"));
                }

                if (!WorldGen.loadSuccess) {
                    ctx.CurrentPhase = DimensionPhase.Generating;
                    GenerateDimensionWorld(worldPath, isCloud);
                }

                ctx.CurrentPhase = DimensionPhase.RestoringData;
                ctx.ActiveDimension.OnLoad();
            }
            else if (!WorldGen.loadSuccess) {
                VaultMod.Instance.Logger.Error($"加载世界\"{ctx.OriginWorld.Name}\"失败" + (WorldGen.worldBackup ? "(已尝试备份)" : "(无备份)"));
                Main.menuMode = 0;
                if (Main.netMode == NetmodeID.Server) {
                    Netplay.Disconnect = true;
                }
                return;
            }

            WorldGen.gen = false;

            if (Main.netMode != NetmodeID.Server) {
                FinalizeWorldLoad();
            }

            ctx.CurrentPhase = DimensionPhase.Finalizing;
        }

        private static void AttemptWorldFileLoad(string path, bool cloud, int attempt) {
            LoadWorldFileInternal(path, cloud);

            if (!WorldGen.loadFailed) return;

            if (attempt == 1) {
                if (FileUtilities.Exists(path + ".bak", cloud)) {
                    WorldGen.worldBackup = true;
                    SwapWithBackup(path, cloud, false);
                }
                else {
                    WorldGen.worldBackup = false;
                    return;
                }
            }
            else if (attempt == 3) {
                SwapWithBackup(path, cloud, true);
                return;
            }

            AttemptWorldFileLoad(path, cloud, attempt + 1);
        }

        private static void SwapWithBackup(string path, bool cloud, bool restore) {
            if (restore) {
                FileUtilities.Copy(path, path + ".bak", cloud);
                FileUtilities.Copy(path + ".bad", path, cloud);
                FileUtilities.Delete(path + ".bad", cloud);
            }
            else {
                FileUtilities.Copy(path, path + ".bad", cloud);
                FileUtilities.Copy(path + ".bak", path, cloud);
                FileUtilities.Delete(path + ".bak", cloud);
            }

            string tMLPath = Path.ChangeExtension(path, ".twld");
            if (FileUtilities.Exists(tMLPath, cloud)) {
                FileUtilities.Copy(tMLPath, restore ? tMLPath + ".bak" : tMLPath + ".bad", cloud);
            }
            if (FileUtilities.Exists(restore ? tMLPath + ".bad" : tMLPath + ".bak", cloud)) {
                FileUtilities.Copy(restore ? tMLPath + ".bad" : tMLPath + ".bak", tMLPath, cloud);
                FileUtilities.Delete(restore ? tMLPath + ".bad" : tMLPath + ".bak", cloud);
            }
        }

        private static void LoadWorldFileInternal(string path, bool cloud) {
            bool useCloud = cloud && SocialAPI.Cloud != null;
            if (!FileUtilities.Exists(path, useCloud)) {
                return;
            }

            var ctx = Context;
            if (ctx.ActiveDimension != null) {
                Main.ActiveWorldFileData = new WorldFileData(path, cloud);
            }

            try {
                int status;
                using (var reader = new BinaryReader(new MemoryStream(FileUtilities.ReadAllBytes(path, useCloud)))) {
                    status = ctx.ActiveDimension != null
                        ? ctx.ActiveDimension.ReadFile(reader)
                        : WorldFile.LoadWorld_Version2(reader);
                }

                if (Main.netMode == NetmodeID.Server) {
                    Console.Title = Main.worldName;
                }

                SystemLoader.OnWorldLoad();

                //调用tModLoader的世界加载
                typeof(ModLoader).Assembly
                    .GetType("Terraria.ModLoader.IO.WorldIO")
                    .GetMethod("Load", BindingFlags.NonPublic | BindingFlags.Static)
                    .Invoke(null, [path, useCloud]);

                if (status != 0) {
                    WorldGen.loadFailed = true;
                    WorldGen.loadSuccess = false;
                    return;
                }

                WorldGen.loadSuccess = true;
                WorldGen.loadFailed = false;

                if (ctx.ActiveDimension != null) {
                    ctx.ActiveDimension.PostReadFile();
                    ctx.CachedDimension?.ReadCopiedDimensionData();
                    RestoreMainWorldData();
                }
                else {
                    FinalizeWorldFileLoad();
                    ctx.CachedDimension?.ReadCopiedDimensionData();
                    ctx.TransferData = null;
                }
            }
            catch {
                WorldGen.loadFailed = true;
                WorldGen.loadSuccess = false;
            }
        }

        private static void GenerateDimensionWorld(string path, bool cloud) {
            var dim = Context.ActiveDimension;

            Main.worldName = dim.DisplayName.Value;
            if (Main.netMode == NetmodeID.Server) {
                Console.Title = Main.worldName;
            }

            var data = new WorldFileData(path, cloud) {
                Name = Main.worldName,
                CreationTime = DateTime.Now,
                Metadata = FileMetadata.FromCurrentSettings(FileType.World),
                WorldGeneratorVersion = Main.WorldGeneratorVersion,
                UniqueId = Guid.NewGuid()
            };
            Main.ActiveWorldFileData = data;

            //设置世界尺寸
            Main.maxTilesX = dim.Width;
            Main.maxTilesY = dim.Height;
            Main.spawnTileX = Main.maxTilesX / 2;
            Main.spawnTileY = Main.maxTilesY / 2;

            WorldGen.setWorldSize();
            WorldGen.clearWorld();

            Main.worldSurface = Main.maxTilesY * 0.3;
            Main.rockLayer = Main.maxTilesY * 0.5;
            GenVars.waterLine = Main.maxTilesY;
            Main.weatherCounter = 18000;
            Cloud.resetClouds();

            RestoreMainWorldData();

            //执行世界生成任务
            double totalWeight = dim.Tasks.Sum(t => t.Weight);
            WorldGenerator.CurrentGenerationProgress = new GenerationProgress { TotalWeight = totalWeight };

            var config = dim.Config;
            foreach (var task in dim.Tasks) {
                WorldGen._genRand = new UnifiedRandom(data.Seed);
                Main.rand = new UnifiedRandom(data.Seed);

                WorldGenerator.CurrentGenerationProgress.Start(task.Weight);
                task.Apply(WorldGenerator.CurrentGenerationProgress, config?.GetPassConfiguration(task.Name));
                WorldGenerator.CurrentGenerationProgress.End();
            }

            WorldGenerator.CurrentGenerationProgress = null;
            Main.WorldFileMetadata = FileMetadata.FromCurrentSettings(FileType.World);

            if (dim.ShouldSave) {
                WorldFile.SaveWorld(cloud);
            }

            SystemLoader.OnWorldLoad();
        }

        private static void FinalizeWorldLoad() {
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

            Main.QueueMainThreadAction(CompletePlayerSpawn);
        }

        private static void CompletePlayerSpawn() {
            Main.LocalPlayer.Spawn(PlayerSpawnContext.SpawningIntoWorld);
            WorldFile.SetOngoingToTemps();
            Main.resetClouds = true;
            Main.gameMenu = false;
        }

        /// <summary>
        /// 世界文件加载后的液体处理等后续工作
        /// </summary>
        internal static void FinalizeWorldFileLoad() {
            GenVars.waterLine = Main.maxTilesY;
            Liquid.QuickWater(2);
            WorldGen.WaterCheck();

            Liquid.quickSettle = true;
            int iterations = 0;
            int liquidTotal = Liquid.numLiquid + LiquidBuffer.numLiquidBuffer;
            float lastProgress = 0f;

            while (Liquid.numLiquid > 0 && iterations < 100000) {
                iterations++;

                float progress = (liquidTotal - Liquid.numLiquid + LiquidBuffer.numLiquidBuffer) / (float)liquidTotal;
                if (Liquid.numLiquid + LiquidBuffer.numLiquidBuffer > liquidTotal) {
                    liquidTotal = Liquid.numLiquid + LiquidBuffer.numLiquidBuffer;
                }

                progress = Math.Max(progress, lastProgress);
                lastProgress = progress;

                Main.statusText = Lang.gen[27].Value + " " + (int)(progress * 50 + 50) + "%";
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

        //为了向后兼容保留的别名
        internal static void PostLoadWorldFile() => FinalizeWorldFileLoad();
        #endregion

        #region 生命周期钩子
        internal static void HandleEnterWorld(Player player) {
            var ctx = Context;

            if (Main.netMode == NetmodeID.MultiplayerClient) {
                ctx.CachedDimension?.OnUnload();
                ctx.ActiveDimension?.OnLoad();
            }

            ctx.CachedDimension = ctx.ActiveDimension;

            //仅在主世界且非恢复中时检查是否需要恢复维度
            if (ctx.ActiveDimension == null && !ctx.IsRestoring && Main.netMode == NetmodeID.SinglePlayer) {
                TryRestoreFromPersistence();
            }
        }

        private static void TryRestoreFromPersistence() {
            if (Main.ActiveWorldFileData == null) return;

            Guid worldId = Main.ActiveWorldFileData.UniqueId;
            int savedIndex = LoadPersistedState(worldId);

            if (savedIndex >= 0 && savedIndex < Dimension.Dimensions.Count) {
                Context.IsRestoring = true;

                Main.QueueMainThreadAction(() => {
                    Context.OriginWorld = Main.ActiveWorldFileData;
                    Context.ActiveDimension = Dimension.Dimensions[savedIndex];
                    Main.gameMenu = true;

                    Task.Factory.StartNew(ExecuteWorldTransition, savedIndex);
                    Context.IsRestoring = false;
                });
            }
        }

        internal static void HandleDisconnect() {
            if (Context.ActiveDimension != null || Context.CachedDimension != null) {
                Main.menuMode = 14;
            }
            Context.Reset();
            NoReturn = false;
            HideUnderworld = false;
        }

        /// <inheritdoc/>
        public override void Load() {
            Player.Hooks.OnEnterWorld += HandleEnterWorld;
            Netplay.OnDisconnect += HandleDisconnect;
        }

        /// <inheritdoc/>
        public override void PreSaveAndQuit() {
            var ctx = Context;
            if (ctx.ActiveDimension != null && ctx.OriginWorld != null) {
                int idx = Dimension.Dimensions.IndexOf(ctx.ActiveDimension);
                if (idx >= 0) {
                    PersistCurrentState(ctx.OriginWorld.UniqueId, idx);
                }
            }

            ctx.Reset();
            NoReturn = false;
            HideUnderworld = false;
        }

        /// <inheritdoc/>
        public override void Unload() {
            Player.Hooks.OnEnterWorld -= HandleEnterWorld;
            Netplay.OnDisconnect -= HandleDisconnect;

            Context.Reset();
            _dataCopiers.Clear();
            OnTransition = null;
        }
        #endregion
    }
}
