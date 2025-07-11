using System.Collections.Generic;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;

namespace InnoVault.GameSystem
{
    /// <summary>
    /// 用于将世界数据加载或者保存到NBT文件中
    /// </summary>
    public abstract class SaveWorld : VaultType
    {
        /// <summary>
        /// 所有实例以单例形式存储于此
        /// </summary>
        public static List<SaveWorld> SaveWorlds { get; private set; } = [];
        /// <summary>
        /// 从模组映射到对应的实例列表
        /// </summary>
        public static Dictionary<Mod, List<SaveWorld>> ModToSaves { get; private set; } = [];
        /// <inheritdoc/>
        protected override void Register() {
            if (!CanLoad()) {
                return;
            }
            SaveWorlds.Add(this);
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
        /// 保存世界数据
        /// </summary>
        /// <param name="tag"></param>
        public virtual void SaveData(TagCompound tag) {

        }
        /// <summary>
        /// 加载世界数据
        /// </summary>
        /// <param name="tag"></param>
        public virtual void LoadData(TagCompound tag) {

        }
    }
}
