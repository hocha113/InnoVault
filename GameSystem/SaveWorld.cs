using System.IO;
using Terraria;

namespace InnoVault.GameSystem
{
    /// <summary>
    /// 用于将世界数据加载或者保存到NBT文件中
    /// </summary>
    public abstract class SaveWorld : SaveContent<SaveWorld>
    {
        /// <summary>
        /// 获取世界的内部文件名
        /// </summary>
        public static string WorldFullName => Path.GetFileNameWithoutExtension(Main.worldPathName) ?? Main.worldName + Main.worldID;
        /// <summary>
        /// 保存世界TP实体数据的路径，包含文件名，使用世界存档名字作为关键字
        /// </summary>
        public static string SaveTPDataPath => Path.Combine(VaultSave.RootPath, "TPDatas", $"tp_{Path.GetFileNameWithoutExtension(WorldFullName)}.nbt");
        /// <summary>
        /// 保存世界数据的路径，包含文件名，使用世界存档名字作为关键字
        /// </summary>
        public override string SavePath => Path.Combine(VaultSave.RootPath, "WorldDatas", $"world_{WorldFullName}.nbt");
    }
}
