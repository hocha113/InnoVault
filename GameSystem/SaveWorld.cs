using System;
using System.IO;
using Terraria;

namespace InnoVault.GameSystem
{
    /// <summary>
    /// 用于将世界数据加载或者保存到NBT文件中
    /// 在世界加载自动调用加载逻辑，在退出世界时调用保存逻辑
    /// </summary>
    public class SaveWorld : SaveContent<SaveWorld>
    {
        /// <summary>
        /// 获取世界的内部文件名
        /// </summary>
        public static string WorldFullName {
            get {
                if (!VaultLoad.LoadenContent) {
                    return string.Empty;
                }
                //在主世界中，使用当前世界数据
                return Path.GetFileNameWithoutExtension(Main.worldPathName) ?? Main.worldName + Main.worldID;
            }
        }
        /// <summary>
        /// 备份世界TP实体数据的路径，包含文件名，使用世界存档名字作为关键字
        /// </summary>
        public static string BackupTPDataPath => GetBackupTPDataPath(WorldFullName);
        /// <summary>
        /// 保存世界TP实体数据的路径，包含文件名，使用世界存档名字作为关键字
        /// </summary>
        public static string SaveTPDataPath => GetTPDataPath(WorldFullName);
        /// <summary>
        /// 备份世界数据的路径，包含文件名，使用世界存档名字作为关键字
        /// </summary>
        public static string BackupPath => GetBackupPath(WorldFullName);
        /// <summary>
        /// 保存世界数据的路径，包含文件名，使用世界存档名字作为关键字
        /// </summary>
        public override string SavePath => GetSavePath(WorldFullName);

        internal static string GetSavePath(string worldFullName) => Path.Combine(VaultSave.RootPath, "WorldDatas", $"world_{worldFullName}.nbt");
        internal static string GetBackupPath(string worldFullName) => Path.Combine(VaultSave.RootPath, "WorldDatas", "Backups", $"world_{worldFullName}.zip");
        internal static string GetTPDataPath(string worldFullName) => Path.Combine(VaultSave.RootPath, "TPDatas", $"tp_{worldFullName}.nbt");
        internal static string GetBackupTPDataPath(string worldFullName) => Path.Combine(VaultSave.RootPath, "TPDatas", "Backups", $"tp_{worldFullName}.zip");

        //删除某个世界文件名对应的全部 InnoVault 世界数据（主文件、.bak、备份 zip）并清掉缓存，
        //用于会被重新生成的场景世界：否则新世界读档时会读回旧数据，或在主文件缺失时从备份 zip 复活旧数据
        internal static void DeleteWorldData(string worldFullName) {
            foreach (string path in new[] { GetSavePath(worldFullName), GetTPDataPath(worldFullName) }) {
                TagCache.Invalidate(path);
                TryDelete(path);
                TryDelete(path + ".bak");
            }
            DeleteBackups(GetBackupPath(worldFullName));
            DeleteBackups(GetBackupTPDataPath(worldFullName));
        }

        private static void TryDelete(string path) {
            try {
                if (File.Exists(path)) {
                    File.Delete(path);
                }
            } catch (Exception ex) {
                VaultMod.Instance.Logger.Warn($"[SaveWorld] Failed to delete {path}: {ex.Message}");
            }
        }

        /// <summary>
        /// 扫描 VaultSave 根目录下 WorldDatas / TPDatas（含其 Backups 子目录）中失去对应原版 .wld 世界文件的存档：
        /// 1. world_*.nbt / tp_*.nbt
        /// 2. world_*.zip / tp_*.zip（含时间戳前缀：yyyy-MM-dd-world_... / yyyy-MM-dd-tp_...）
        /// 将它们移动到 RootPath/Orphaned 目录，并删除该目录下超过保留天数 (默认7天) 未修改的 .nbt/.zip 文件
        /// </summary>
        /// <param name="retentionDays">孤立文件保留天数，默认 7 天</param>
        /// <returns>被移动到 Orphaned 的文件数量</returns>
        [Obsolete("此方法尚未实现，并且已经弃用")]
        public static int CleanupOrphanedSaves(int retentionDays = 7) {
            return 0;
        }
    }
}
