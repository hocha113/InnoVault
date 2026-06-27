using InnoVault.TileProcessors;
using System;
using System.Collections.Generic;
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
        /// 是否正在加载好了世界相关<br/>
        /// 该状态现已由 <see cref="VaultLoadingProgress"/> 统一管理，此处仅作兼容转发
        /// </summary>
        public static bool LoadenWorld {
            get => VaultLoadingProgress.WorldDataLoaded;
            set => VaultLoadingProgress.WorldDataLoaded = value;
        }
        /// <summary>
        /// 是否正在保存好了世界相关<br/>
        /// 该状态现已由 <see cref="VaultLoadingProgress"/> 统一管理，此处仅作兼容转发
        /// </summary>
        public static bool SavedWorld {
            get => VaultLoadingProgress.WorldSaved;
            set => VaultLoadingProgress.WorldSaved = value;
        }
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
        //用于串行化世界保存任务的锁，避免多个后台保存任务并发写入同一批文件造成损坏
        private static readonly object saveLock = new();
        //指向最近一次排队的保存任务，世界 / 模组卸载时可等待其完成，确保后台写盘不被中断
        private static Task saveTask = Task.CompletedTask;
        //首先我们要明白一点，在多人模式的情况下，只有服务器会加载这两个钩子，其他客户端并不会正常运行
        //所以，如果想数据正常加载，就需要发一个巨大的数据包来让其他的端同步，Save的时候要保证世界数据同步，而Load的时候要保证其他端也被加载
        //这个钩子的调用顺序先与LoadData，可以错开加载压力，同时不会因为没有存储数据就不会调用
        /// <inheritdoc/>
        public override void OnWorldLoad() {
            if (VaultUtils.isClient) {
                //在多人模式中，客户端不需要加载世界存档数据，因为存档文件只被服务器管理
                //所以允许客户端直接把世界加载标签设置为true
                VaultLoadingProgress.WorldDataLoaded = true;
            }
        }
        /// <inheritdoc/>
        public override void OnWorldUnload() {
            VaultLoadingProgress.WorldDataLoaded = false;
            VaultLoadingProgress.ResetSession();
            //世界卸载前等待挂起的保存任务完成，避免后台写盘被打断导致存档不完整
            WaitForPendingSave();
        }
        /// <inheritdoc/>
        public override void SaveWorldData(TagCompound tag) {
            tag["root:worldData"] = "";
            //将本次保存串联到上一个保存任务之后执行，确保任意时刻只有一个后台写盘在进行，避免并发写文件
            lock (saveLock) {
                saveTask = saveTask.ContinueWith(_ => {
                    VaultLoadingProgress.EnterPhase(LoadingPhase.Saving);
                    VaultLoadingProgress.WorldSaved = false;
                    try {
                        DoSaveWorld();
                        SaveWorldEvent?.Invoke();
                    } catch (Exception ex) {
                        VaultMod.Instance.Logger.Error($"An error occurred while saving the world:{ex.Message}");
                    } finally {
                        VaultLoadingProgress.WorldSaved = true;
                    }
                }, TaskScheduler.Default);
            }
        }
        /// <inheritdoc/>
        public override void LoadWorldData(TagCompound tag) {
            tag.TryGet("root:worldData", out string _);
            Task.Run(() => {
                VaultLoadingProgress.WorldDataLoaded = false;
                DoLoadWorld();
                LoadWorldEvent?.Invoke();
                VaultLoadingProgress.WorldDataLoaded = true;
            }
            );
        }
        /// <inheritdoc/>
        public override void Unload() {
            //卸载前等待挂起的保存任务完成，避免后台任务在类型卸载后继续访问已失效的内容
            WaitForPendingSave();
            LoadWorldEvent = null;
            SaveWorldEvent = null;
        }
        //等待挂起的世界保存任务完成（带超时上限），用于世界卸载 / 模组卸载等关键节点
        private static void WaitForPendingSave() {
            Task pending;
            lock (saveLock) {
                pending = saveTask;
            }
            if (pending == null || pending.IsCompleted) {
                return;
            }
            try {
                if (!pending.Wait(TimeSpan.FromSeconds(30))) {
                    VaultMod.Instance.Logger.Warn("[VaultSave] Timed out waiting for the pending world save to finish.");
                }
            } catch (Exception ex) {
                VaultMod.Instance.Logger.Warn($"[VaultSave] Waiting for the pending world save failed: {ex}");
            }
        }
        //统一保存世界相关数据
        private static void DoSaveWorld() {
            TryDo(() => {
                SaveWorld.DoSave();
                //仅在确有世界数据时维护 zip 备份，避免空数据世界反复生成空备份
                if (TagCache.TryGet(SaveWorld.GetInstance<SaveWorld>().SavePath, out var tag) && tag.Count > 0) {
                    SaveMod.SaveTagToZip(tag, SaveWorld.BackupPath, true);
                    SaveMod.PruneBackups(SaveWorld.BackupPath, 7);
                }
            }, "[SaveWorld] Failed to save world data");
            TryDo(SaveTileProcessors, "[SaveWorld] Failed to save TileProcessor data");
        }
        //统一加载世界相关数据
        private static void DoLoadWorld() {
            //加载前先检查主文件是否可用，必要时用备份 zip 恢复，避免主文件损坏直接导致整份世界数据回退默认值
            TryDo(() => RepairFromBackupIfNeeded(SaveWorld.GetInstance<SaveWorld>().SavePath, SaveWorld.BackupPath)
                , "[LoadWorld] Failed to repair world data from backup");
            TryDo(() => RepairFromBackupIfNeeded(SaveWorld.SaveTPDataPath, SaveWorld.BackupTPDataPath)
                , "[LoadWorld] Failed to repair TileProcessor data from backup");
            TryDo(() => SaveWorld.DoLoad(), "[LoadWorld] Failed to load world data");
            TryDo(LoadTPDataByNBT, "[LoadWorld] Failed to load TileProcessor data");
        }
        //主文件（含 .bak）缺失或损坏时，尝试用最新的备份 zip 恢复并写回主文件，使后续正常加载流程可以读到
        private static void RepairFromBackupIfNeeded(string savePath, string backupBaseZipPath) {
            if (SaveMod.TryLoadRootTag(savePath, out _, forceReload: true)) {
                return;//主文件或 .bak 可正常读取，无需恢复
            }
            if (SaveMod.TryRestoreFromBackupZips(backupBaseZipPath, out var tag)) {
                SaveMod.SaveTagToFile(tag, savePath);
                VaultMod.Instance.Logger.Warn($"[VaultSave] '{savePath}' was missing or corrupt; restored from backup zip.");
            }
        }
        //将TP数据保存进对应的NBT文件中
        private static void SaveTileProcessors() {
            //TP 尚未加载完成时不要保存，避免用不完整 / 空的运行时状态覆盖磁盘上的有效数据
            //注意：不能再以"没有活跃 TP"为由跳过保存，否则当世界内 TP 全部被移除时旧文件不会更新，下次加载会复活幽灵数据
            if (!TileProcessorLoader.LoadenTP) {
                return;
            }

            bool hasActive = TileProcessorLoader.TP_InWorld.Any(tp => tp?.Active == true);

            //从未有过 TP 且当前也没有时，无需创建空文件（与 SaveContent.DoSave 处理一致）；
            //若主文件或其 .bak 已存在（说明曾经有 TP），即使现在为空也要写入以清除残留，防止下次加载复活幽灵数据
            if (!hasActive
                && !File.Exists(SaveWorld.SaveTPDataPath)
                && !File.Exists(SaveWorld.SaveTPDataPath + ".bak")) {
                return;
            }

            //existingTag 可能是 TagCache 中的活引用；后台保存任务若就地 mutate 它，主线程并发读同一对象会脏读/枚举异常
            //SaveWorldData 只替换顶层 key（不改嵌套对象），故浅拷贝出新对象再写入即可隔离写入，SaveTagToFile 随后会把新对象重新入缓存
            TagCompound tpTag = [];
            if (SaveMod.TryLoadRootTag(SaveWorld.SaveTPDataPath, out var existingTag)) {
                foreach (var pair in existingTag) {
                    tpTag[pair.Key] = pair.Value;
                }
            }
            TileProcessorLoader.SaveWorldData(tpTag);
            SaveMod.SaveTagToFile(tpTag, SaveWorld.SaveTPDataPath);

            //仅在确有 TP 数据时维护 zip 备份，避免空世界反复生成空备份
            if (hasActive) {
                SaveMod.SaveTagToZip(tpTag, SaveWorld.BackupTPDataPath, true);
                SaveMod.PruneBackups(SaveWorld.BackupTPDataPath, 7);
            }
        }
        //读取TP实体存储的NBT数据，并将其赋值给ActiveWorldTagData用于后续加载读取
        //TP实体真正加载数据在步骤WorldGen.Hooks.OnWorldLoad中，运行在该加载钩子之后
        private static void LoadTPDataByNBT() {
            if (SaveMod.TryLoadRootTag(SaveWorld.SaveTPDataPath, out var tag)) {
                //处理数据清洗，将已卸载模组的数据转换为占位符格式
                if (tag.TryGet("TPData_TagList", out List<TagCompound> list)) {
                    UnknowTP.CheckAndArchive(list);
                }
                TileProcessorLoader.ActiveWorldTagData = tag;
            }
            //当 NBT 文件不存在时，不要把 ActiveWorldTagData 置空：
            //旧存档迁移路径（TileProcessorSystem.LoadWorldData）会在此之前把老世界标签填入它，
            //这里置空会先于 LoadWorldTileProcessor 消费而抹掉迁移数据。跨世界残留已由 TileProcessorSystem.OnWorldUnload 负责清理
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