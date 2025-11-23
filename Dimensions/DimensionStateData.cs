using InnoVault.GameSystem;
using Terraria.ModLoader.IO;

namespace InnoVault.Dimensions
{
    /// <summary>
    /// 维度状态数据类，继承 SaveContent 以实现稳定的存档隔离
    /// </summary>
    internal class DimensionStateData : SaveWorld
    {
        /// <summary>
        /// 当前维度索引
        /// </summary>
        public int CurrentDimensionIndex { get; set; } = -1;

        /// <summary>
        /// 当前维度全名
        /// </summary>
        public string CurrentDimensionFullName { get; set; } = "MainWorld";

        /// <summary>
        /// 保存时间戳
        /// </summary>
        public string SaveTime { get; set; } = string.Empty;

        /// <summary>
        /// 重写保存前缀，使其与世界存档关联
        /// </summary>
        public override string SavePrefix => "DimensionLoader";

        /// <summary>
        /// 保存维度状态数据
        /// </summary>
        public override void SaveData(TagCompound tag) {
            tag["CurrentDimensionIndex"] = CurrentDimensionIndex;
            tag["CurrentDimensionFullName"] = CurrentDimensionFullName;
            tag["SaveTime"] = SaveTime;
        }

        /// <summary>
        /// 加载维度状态数据
        /// </summary>
        public override void LoadData(TagCompound tag) {
            CurrentDimensionIndex = tag.GetInt("CurrentDimensionIndex");
            CurrentDimensionFullName = tag.GetString("CurrentDimensionFullName");
            SaveTime = tag.GetString("SaveTime");
        }
    }
}
