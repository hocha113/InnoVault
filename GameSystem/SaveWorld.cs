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
        public static string BackupTPDataPath => Path.Combine(VaultSave.RootPath, "TPDatas", "Backups", $"tp_{WorldFullName}.zip");
        /// <summary>
        /// 保存世界TP实体数据的路径，包含文件名，使用世界存档名字作为关键字
        /// </summary>
        public static string SaveTPDataPath => Path.Combine(VaultSave.RootPath, "TPDatas", $"tp_{WorldFullName}.nbt");
        /// <summary>
        /// 备份世界数据的路径，包含文件名，使用世界存档名字作为关键字
        /// </summary>
        public static string BackupPath => Path.Combine(VaultSave.RootPath, "WorldDatas", "Backups", $"world_{WorldFullName}.zip");
        /// <summary>
        /// 保存世界数据的路径，包含文件名，使用世界存档名字作为关键字
        /// </summary>
        public override string SavePath => Path.Combine(VaultSave.RootPath, "WorldDatas", $"world_{WorldFullName}.nbt");

        /// <summary>
        /// 扫描 VaultSave 根目录下 WorldDatas / TPDatas（含其 Backups 子目录）中失去对应原版 .wld 世界文件的存档：
        /// 1. world_*.nbt / tp_*.nbt
        /// 2. world_*.zip / tp_*.zip（含时间戳前缀：yyyy-MM-dd-world_... / yyyy-MM-dd-tp_...）
        /// 将它们移动到 RootPath/Orphaned 目录，并删除该目录下超过保留天数 (默认7天) 未修改的 .nbt/.zip 文件。
        /// </summary>
        /// <param name="retentionDays">孤立文件保留天数，默认 7 天</param>
        /// <returns>被移动到 Orphaned 的文件数量</returns>
        [Obsolete("此方法尚未实现，并且已经弃用")]
        public static int CleanupOrphanedSaves(int retentionDays = 7) {
            return 0;
        }
    }
}
