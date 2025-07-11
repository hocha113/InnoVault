using System.IO;
using Terraria.ModLoader;

namespace InnoVault.GameSystem
{
    /// <summary>
    /// 管理模组全局数据的基类，加载读取范围从游戏加载末期开始覆盖全程
    /// 一般用于进行标题界面或者菜单界面等游戏世界外的数据存档
    /// 该基类并不会自动调用保存或者读取逻辑，需要自行在何时的时机中去调用DoLoad或者DoSave存取需要的数据
    /// </summary>
    public class SaveMod : SaveContent<SaveMod>
    {
        /// <summary>
        /// 获取保存模组数据的路径，包含文件名，使用模组名字作为关键字
        /// </summary>
        public override string SavePath => Path.Combine(VaultSave.RootPath, "ModDatas", $"mod_{Mod.Name}.nbt");
    }
}
