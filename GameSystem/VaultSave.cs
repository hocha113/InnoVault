using InnoVault.TileProcessors;
using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.Linq;
using Terraria;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;

namespace InnoVault.GameSystem
{
    /// <summary>
    /// 管理文件加载与读取数据相关的内容
    /// </summary>
    public sealed class VaultSave : ModSystem
    {
        /// <summary>
        /// 存档的根节点文件名
        /// </summary>
        public const string RootFilesName = nameof(VaultSave);
        /// <summary>
        /// 存档的根路径
        /// </summary>
        public static string RootPath => Path.Combine(Main.SavePath, RootFilesName);
        /// <summary>
        /// 获取世界的内部文件名
        /// </summary>
        public static string WorldFullName => Path.GetFileNameWithoutExtension(Main.worldPathName) ?? Main.worldName + Main.worldID;
        /// <summary>
        /// 保存世界数据的路径，包含文件名，使用世界存档名字作为关键字
        /// </summary>
        public static string SaveWorldPath => Path.Combine(RootPath, "WorldDatas", $"world_{WorldFullName}.nbt");
        /// <summary>
        /// 保存世界TP实体数据的路径，包含文件名，使用世界存档名字作为关键字
        /// </summary>
        public static string SaveTPDataPath => Path.Combine(RootPath, "TPDatas", $"tp_{Path.GetFileNameWithoutExtension(WorldFullName)}.nbt");
        /// <summary>
        /// 获取保存模组数据的路径，包含文件名，使用模组名字作为关键字
        /// </summary>
        public static string SaveModDataPath(Mod mod) => Path.Combine(RootPath, "ModDatas", $"mod_{mod.Name}.nbt");
        /// <inheritdoc/>
        public override void PostSetupContent() {
            LoadModDataByNBT();
        }
        /// <inheritdoc/>
        public override void OnModUnload() {
            SaveModDataByNBT();
        }
        /// <summary>
        /// 保存所有模组的全局数据
        /// </summary>
        internal static void SaveModDataByNBT() {
            if (SaveMod.ModToSaves.Count == 0) {
                return;
            }

            TagCompound rootTag;
            foreach (var (mod, saves) in SaveMod.ModToSaves) {
                rootTag = [];
                foreach (var save in saves) {
                    TagCompound saveTag = new();
                    save.SaveData(saveTag);
                    rootTag[$"SaveMod:{save.Name}"] = saveTag;
                }
                SaveTagToFile(rootTag, SaveModDataPath(mod));
            }
        }
        /// <summary>
        /// 保存指定模组的全局数据
        /// </summary>
        public static void SaveModDataByNBT(Mod targetMod) {
            if (!SaveMod.ModToSaves.TryGetValue(targetMod, out var values)) {
                return;
            }

            TagCompound rootTag = [];
            foreach (var save in values) {
                TagCompound saveTag = new();
                save.SaveData(saveTag);
                rootTag[$"SaveMod:{save.Name}"] = saveTag;
            }
            SaveTagToFile(rootTag, SaveModDataPath(targetMod));
        }
        /// <summary>
        /// 加载所有模组的全局数据
        /// </summary>
        internal static void LoadModDataByNBT() {
            if (SaveMod.ModToSaves.Count == 0) {
                return;
            }

            foreach (var saves in SaveMod.ModToSaves) {
                if (!TryLoadRootTag(SaveModDataPath(saves.Key), out var rootTag)) {
                    continue;
                }
                foreach (var save in saves.Value) {
                    if (!rootTag.TryGet($"SaveMod:{save.Name}", out TagCompound saveTag)) {
                        continue;
                    }
                    save.LoadData(saveTag);
                }
            }
        }
        /// <summary>
        /// 加载指定模组的全局数据
        /// </summary>
        public static void LoadModDataByNBT(Mod targetMod) {
            if (!SaveMod.ModToSaves.TryGetValue(targetMod, out var values)) {
                return;
            }
            if (!TryLoadRootTag(SaveModDataPath(targetMod), out var rootTag)) {
                return;
            }
            foreach (var save in values) {
                if (!rootTag.TryGet($"SaveMod:{save.Name}", out TagCompound saveTag)) {
                    continue;
                }
                save.LoadData(saveTag);
            }
        }
        //首先我们要明白一点，在多人模式的情况下，只有服务器会加载这两个钩子，其他客户端并不会正常运行
        //所以，如果想数据正常加载，就需要发一个巨大的数据包来让其他的端同步，Save的时候要保证世界数据同步，而Load的时候要保证其他端也被加载
        /// <inheritdoc/>
        public override void ClearWorld() => DoSaveWorld();
        //这个钩子的调用顺序先与LoadData，可以错开加载压力，同时不会因为没有存储数据就不会调用，ClearWorld的使用理由类似
        /// <inheritdoc/>
        public override void OnWorldLoad() => DoLoadWorld();
        //统一保存世界相关数据
        private static void DoSaveWorld() {
            TryDo(SaveModWorlds, "[SaveWorld] Failed to save world data");
            TryDo(SaveTileProcessors, "[SaveWorld] Failed to save TileProcessor data");
        }
        //统一加载世界相关数据
        private static void DoLoadWorld() {
            TryDo(LoadModWorlds, "[LoadWorld] Failed to load world data");
            TryDo(LoadTPDataByNBT, "[LoadWorld] Failed to load TileProcessor data");
        }
        //调用SaveWorld的保存钩子
        private static void SaveModWorlds() {
            if (SaveWorld.ModToSaves.Count == 0) {
                return;
            }

            TagCompound rootTag = new();

            foreach (var (mod, saves) in SaveWorld.ModToSaves) {
                TagCompound modTag = new();

                foreach (var save in saves) {
                    TagCompound saveTag = new();
                    save.SaveData(saveTag);
                    modTag[$"SaveWorld:{save.Name}"] = saveTag;
                }

                rootTag[$"mod:{mod.Name}"] = modTag;
            }

            SaveTagToFile(rootTag, SaveWorldPath);
        }
        //将TP数据保存进对应的NBT文件中
        private static void SaveTileProcessors() {
            if (!TileProcessorLoader.TP_InWorld.Any(tp => tp?.Active == true)) {
                return;
            }

            TagCompound tpTag = TryLoadRootTag(SaveTPDataPath, out var existingTag) ? existingTag : new();
            TileProcessorLoader.SaveWorldData(tpTag);
            SaveTagToFile(tpTag, SaveTPDataPath);
        }
        //加载SaveWorld，模组继承该结构后用于加载世界数据
        private static void LoadModWorlds() {
            if (SaveWorld.ModToSaves.Count == 0 || !TryLoadRootTag(SaveWorldPath, out var rootTag)) {
                return;
            }

            foreach (var (mod, saves) in SaveWorld.ModToSaves) {
                if (!rootTag.TryGet($"mod:{mod.Name}", out TagCompound modTag)) {
                    continue;
                }

                foreach (var save in saves) {
                    if (modTag.TryGet($"SaveWorld:{save.Name}", out TagCompound saveTag)) {
                        save.LoadData(saveTag);
                    }
                }
            }
        }
        //读取TP实体存储的NBT数据，并将其赋值给ActiveWorldTagData用于后续加载读取
        //TP实体真正加载数据在步骤WorldGen.Hooks.OnWorldLoad中，运行在该加载钩子之后
        private static void LoadTPDataByNBT() {
            if (TryLoadRootTag(SaveTPDataPath, out var tag)) {
                TileProcessorLoader.ActiveWorldTagData = tag;
            }
        }
        //快速处理异常所使用的套壳函数
        private static void TryDo(Action action, string errorMessage) {
            try {
                action();
            } catch (Exception ex) {
                VaultMod.Instance.Logger.Warn($"{errorMessage}: {ex}");
            }
        }
        /// <summary>
        /// 将传入的 <see cref="TagCompound"/> 数据写入指定路径的 NBT 文件
        /// 如果路径所处的目录不存在则自动创建
        /// 使用 <see cref="TagIO.ToStream"/> 进行序列化并压缩写入
        /// 在写入过程中如出现异常会记录错误日志
        /// </summary>
        public static void SaveTagToFile(TagCompound tag, string path) {
            try {
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) {
                    Directory.CreateDirectory(dir);
                }

                using FileStream stream = File.Create(path);
                TagIO.ToStream(tag, stream);
            } catch (Exception ex) {
                VaultMod.Instance.Logger.Error($"[SaveTagToFile] Failed to save NBT: {ex}");
            }
        }

        /// <summary>
        /// 尝试从指定路径读取并反序列化出 <see cref="TagCompound"/> 数据
        /// 如果文件不存在则返回 <see langword="false"/> 并输出 <see langword="null"/>
        /// 若读取或反序列化失败则返回 <see langword="false"/> 并记录警告日志
        /// 使用 <see cref="TagIO.FromStream"/> 从压缩 NBT 文件中加载
        /// </summary>
        public static bool TryLoadRootTag(string path, out TagCompound tag) {
            tag = null!;
            if (!File.Exists(path)) {
                return false;
            }

            try {
                using FileStream stream = File.OpenRead(path);
                tag = TagIO.FromStream(stream);
                return true;
            } catch (Exception ex) {
                VaultMod.Instance.Logger.Warn($"[TryLoadRootTag] Failed to load NBT file at {path}: {ex}");
                return false;
            }
        }
    }
}
