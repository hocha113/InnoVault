using System;
using System.Collections.Generic;
using System.IO;
using Terraria;

namespace InnoVault.GameSystem
{
    /// <summary>
    /// 用于将世界数据加载或者保存到NBT文件中
    /// 在世界加载自动调用加载逻辑，在推出世界时调用保存逻辑
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
                return Path.GetFileNameWithoutExtension(Main.worldPathName) ?? Main.worldName + Main.worldID;
            }
        }
        /// <summary>
        /// 备份世界TP实体数据的路径，包含文件名，使用世界存档名字作为关键字
        /// </summary>
        public static string BackupTPDataPath => Path.Combine(VaultSave.RootPath, "TPDatas", "Backups", $"tp_{Path.GetFileNameWithoutExtension(WorldFullName)}.zip");
        /// <summary>
        /// 保存世界TP实体数据的路径，包含文件名，使用世界存档名字作为关键字
        /// </summary>
        public static string SaveTPDataPath => Path.Combine(VaultSave.RootPath, "TPDatas", $"tp_{Path.GetFileNameWithoutExtension(WorldFullName)}.nbt");
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
        public static int CleanupOrphanedSaves(int retentionDays = 7) {
            int moved = 0;
            try {
                string root = VaultSave.RootPath;
                string worldDatas = Path.Combine(root, "WorldDatas");
                string tpDatas = Path.Combine(root, "TPDatas");
                string orphanDir = Path.Combine(root, "Orphaned");
                string worldBackups = Path.Combine(worldDatas, "Backups");
                string tpBackups = Path.Combine(tpDatas, "Backups");

                Directory.CreateDirectory(orphanDir);

                // 收集当前仍存在的世界基名 (不带扩展)
                HashSet<string> existingWorlds = new();
                try {
                    var currentWorldDir = Path.GetDirectoryName(Main.worldPathName);
                    if (!string.IsNullOrEmpty(currentWorldDir) && Directory.Exists(currentWorldDir)) {
                        foreach (var f in Directory.GetFiles(currentWorldDir, "*.wld", SearchOption.TopDirectoryOnly)) {
                            var name = Path.GetFileNameWithoutExtension(f);
                            if (!string.IsNullOrEmpty(name)) {
                                existingWorlds.Add(name);
                            }
                        }
                    }
                } catch { /* 忽略收集错误 */ }

                // 处理 nbt 主目录
                void ScanAndMoveNBT(string baseDir, string prefix) {
                    if (!Directory.Exists(baseDir)) return;
                    foreach (var file in Directory.GetFiles(baseDir, prefix + "*.nbt", SearchOption.TopDirectoryOnly)) {
                        string fileName = Path.GetFileName(file);
                        if (string.Equals(Path.GetDirectoryName(file)?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), orphanDir, StringComparison.OrdinalIgnoreCase))
                            continue;
                        if (fileName.Length <= prefix.Length + 4) continue; // 防御
                        string worldNamePart = fileName.Substring(prefix.Length, fileName.Length - prefix.Length - 4);
                        if (existingWorlds.Contains(worldNamePart)) continue;
                        try {
                            string destPath = Path.Combine(orphanDir, fileName);
                            if (File.Exists(destPath)) {
                                string stamped = Path.GetFileNameWithoutExtension(fileName) + "_" + DateTime.UtcNow.ToString("yyyyMMddHHmmss") + ".nbt";
                                destPath = Path.Combine(orphanDir, stamped);
                            }
                            File.Move(file, destPath);
                            moved++;
                        } catch (Exception ex) {
                            VaultMod.Instance.Logger.Warn($"[CleanupOrphanedSaves] Failed to move orphan file {file}: {ex.Message}");
                        }
                    }
                }

                // 处理 Backups 下 zip（包含时间戳）
                void ScanAndMoveZip(string backupDir, string logicalPrefix) {
                    if (!Directory.Exists(backupDir)) return;
                    foreach (var file in Directory.GetFiles(backupDir, "*.zip", SearchOption.TopDirectoryOnly)) {
                        string fileName = Path.GetFileName(file);
                        int idx = fileName.IndexOf(logicalPrefix, StringComparison.OrdinalIgnoreCase);
                        if (idx < 0) continue; // 不匹配
                        string noExt = Path.GetFileNameWithoutExtension(fileName);
                        int prefixStart = noExt.IndexOf(logicalPrefix, StringComparison.OrdinalIgnoreCase);
                        if (prefixStart < 0) continue;
                        string afterPrefix = noExt.Substring(prefixStart + logicalPrefix.Length);
                        if (string.IsNullOrEmpty(afterPrefix)) continue;
                        if (existingWorlds.Contains(afterPrefix)) continue; // 仍存在对应世界
                        try {
                            string destPath = Path.Combine(orphanDir, fileName);
                            if (File.Exists(destPath)) {
                                string stamped = Path.GetFileNameWithoutExtension(fileName) + "_" + DateTime.UtcNow.ToString("yyyyMMddHHmmss") + ".zip";
                                destPath = Path.Combine(orphanDir, stamped);
                            }
                            File.Move(file, destPath);
                            moved++;
                        } catch (Exception ex) {
                            VaultMod.Instance.Logger.Warn($"[CleanupOrphanedSaves] Failed to move orphan backup {file}: {ex.Message}");
                        }
                    }
                }

                ScanAndMoveNBT(worldDatas, "world_");
                ScanAndMoveNBT(tpDatas, "tp_");
                ScanAndMoveZip(worldBackups, "world_");
                ScanAndMoveZip(tpBackups, "tp_");

                // 删除过期的孤立 nbt / zip 文件
                DateTime threshold = DateTime.UtcNow - TimeSpan.FromDays(Math.Max(0, retentionDays));
                try {
                    foreach (var file in Directory.GetFiles(orphanDir, "*.*", SearchOption.TopDirectoryOnly)) {
                        string ext = Path.GetExtension(file).ToLowerInvariant();
                        if (ext != ".nbt" && ext != ".zip") continue;
                        try {
                            DateTime last = File.GetLastWriteTimeUtc(file);
                            if (last < threshold) {
                                File.Delete(file);
                            }
                        } catch (Exception ex) {
                            VaultMod.Instance.Logger.Warn($"[CleanupOrphanedSaves] Failed to delete old orphan file {file}: {ex.Message}");
                        }
                    }
                } catch { /* 忽略整体失败 */ }
            } catch (Exception ex) {
                VaultMod.Instance.Logger.Warn($"[CleanupOrphanedSaves] Unexpected error: {ex}");
            }
            return moved;
        }
    }
}
