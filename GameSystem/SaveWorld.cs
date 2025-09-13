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
        public static string WorldFullName => Path.GetFileNameWithoutExtension(Main.worldPathName) ?? Main.worldName + Main.worldID;
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
    }
}
