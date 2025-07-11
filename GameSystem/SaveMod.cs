using System.Collections.Generic;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;

namespace InnoVault.GameSystem
{
    /// <summary>
    /// 管理模组全局数据的基类，加载读取范围从游戏加载末期开始覆盖全程
    /// 一般用于进行标题界面或者菜单界面等游戏世界外的数据存档
    /// </summary>
    public abstract class SaveMod : VaultType
    {
        /// <summary>
        /// 所有实例以单例形式存储于此
        /// </summary>
        public static List<SaveMod> SaveMods { get; private set; } = [];
        /// <summary>
        /// 从模组映射到对应的实例列表
        /// </summary>
        public static Dictionary<Mod, List<SaveMod>> ModToSaves { get; private set; } = [];
        /// <inheritdoc/>
        protected override void Register() {
            if (!CanLoad()) {
                return;
            }
            SaveMods.Add(this);
        }
        /// <inheritdoc/>
        public override void SetupContent() {
            if (!CanLoad()) {
                return;
            }
            ModToSaves.TryAdd(Mod, []);
            ModToSaves[Mod].Add(this);
            SetStaticDefaults();
        }
        /// <summary>
        /// 保存模组全局数据
        /// </summary>
        /// <param name="tag"></param>
        public virtual void SaveData(TagCompound tag) {

        }
        /// <summary>
        /// 加载模组全局数据
        /// </summary>
        /// <param name="tag"></param>
        public virtual void LoadData(TagCompound tag) {

        }
    }
}
