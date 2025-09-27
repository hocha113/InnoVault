using InnoVault.TileProcessors;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
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
        /// 是否正在加载好了世界相关
        /// </summary>
        public static bool LoadenWorld { get; set; } = false;
        /// <summary>
        /// 是否正在保存好了世界相关
        /// </summary>
        public static bool SavedWorld { get; set; } = true;
        /// <summary>
        /// 存档的根节点文件名
        /// </summary>
        public const string RootFilesName = nameof(VaultSave);
        /// <summary>
        /// 存档的根路径
        /// </summary>
        public static string RootPath => Path.Combine(Main.SavePath, RootFilesName);
        /// <summary>
        /// 在加载世界时被调用，运行在TP实体加载完成后
        /// </summary>
        public static event Action LoadWorldEvent;
        /// <summary>
        /// 在保存世界时被调用，运行在主要NBT数据保存完成后
        /// </summary>
        public static event Action SaveWorldEvent;
        //首先我们要明白一点，在多人模式的情况下，只有服务器会加载这两个钩子，其他客户端并不会正常运行
        //所以，如果想数据正常加载，就需要发一个巨大的数据包来让其他的端同步，Save的时候要保证世界数据同步，而Load的时候要保证其他端也被加载
        //这个钩子的调用顺序先与LoadData，可以错开加载压力，同时不会因为没有存储数据就不会调用
        /// <inheritdoc/>
        public override void OnWorldLoad() {
            if (VaultUtils.isClient) {
                //在多人模式中，客户端不需要加载世界存档数据，因为存档文件只被服务器管理
                //所以允许客户端直接把世界加载标签设置为true
                LoadenWorld = true;
            }
        }
        /// <inheritdoc/>
        public override void OnWorldUnload() {
            LoadenWorld = false;
        }
        /// <inheritdoc/>
        public override void SaveWorldData(TagCompound tag) {
            tag["root:worldData"] = "";
            Task.Run(() => {
                SavedWorld = false;
                try {
                    DoSaveWorld();
                    SaveWorldEvent?.Invoke();
                } catch (Exception ex) {
                    VaultMod.Instance.Logger.Error($"An error occurred while saving the world:{ex.Message}");
                } finally {
                    SavedWorld = true;
                }
            });
        }
        /// <inheritdoc/>
        public override void LoadWorldData(TagCompound tag) {
            tag.TryGet("root:worldData", out string _);
            Task.Run(() => {
                LoadenWorld = false;
                DoLoadWorld();
                LoadWorldEvent?.Invoke();
                LoadenWorld = true;
            }
            );
        }
        /// <inheritdoc/>
        public override void Unload() {
            LoadWorldEvent = null;
            SaveWorldEvent = null;
        }
        //统一保存世界相关数据
        private static void DoSaveWorld() {
            TryDo(() => {
                SaveWorld.DoSave();
                if (TagCache.TryGet(SaveWorld.GetInstance<SaveWorld>().SavePath, out var tag)) {
                    SaveWorld.SaveTagToZip(tag, SaveWorld.BackupPath, true);
                }
            }, "[SaveWorld] Failed to save world data");
            TryDo(SaveTileProcessors, "[SaveWorld] Failed to save TileProcessor data");
        }
        //统一加载世界相关数据
        private static void DoLoadWorld() {
            TryDo(() => SaveWorld.DoLoad(), "[LoadWorld] Failed to load world data");
            TryDo(LoadTPDataByNBT, "[LoadWorld] Failed to load TileProcessor data");
        }
        //将TP数据保存进对应的NBT文件中
        private static void SaveTileProcessors() {
            if (!TileProcessorLoader.TP_InWorld.Any(tp => tp?.Active == true)) {
                return;
            }

            TagCompound tpTag = SaveMod.TryLoadRootTag(SaveWorld.SaveTPDataPath, out var existingTag) ? existingTag : [];
            TileProcessorLoader.SaveWorldData(tpTag);
            SaveMod.SaveTagToFile(tpTag, SaveWorld.SaveTPDataPath);
            SaveMod.SaveTagToZip(tpTag, SaveWorld.BackupTPDataPath, true);
        }
        //读取TP实体存储的NBT数据，并将其赋值给ActiveWorldTagData用于后续加载读取
        //TP实体真正加载数据在步骤WorldGen.Hooks.OnWorldLoad中，运行在该加载钩子之后
        private static void LoadTPDataByNBT() {
            if (SaveMod.TryLoadRootTag(SaveWorld.SaveTPDataPath, out var tag)) {
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
    }
}
