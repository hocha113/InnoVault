using InnoVault.GameSystem;
using System;
using System.Linq;
using Terraria.ModLoader.IO;
using static InnoVault.Dimensions.DimensionLoader;

namespace InnoVault.Dimensions
{
    /// <summary>
    /// 维度状态数据类，继承 SaveContent 以实现稳定的存档隔离
    /// </summary>
    internal class DimensionStateData : SaveContent<DimensionStateData>
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

        public override string SavePath => SaveWorld.GetInstance<SaveWorld>().SavePath;

        public override bool PreSaveData(TagCompound tag, int style) {
            return style == 1;
        }

        public override bool PreLoadData(TagCompound tag, int style) {
            return style == 1;
        }

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

        /// <summary>
        /// 保存当前维度状态到世界存档
        /// </summary>
        internal static void SaveDimensionState() {
            try {
                //获取维度状态数据实例（SaveContent 单例）
                DimensionStateData stateData = GetInstance<DimensionStateData>();

                //保存当前维度索引（如果在维度中）
                if (currentDimension != null) {
                    int currentIndex = dimensionsByIndex.FirstOrDefault(kvp => kvp.Value == currentDimension).Key;
                    stateData.CurrentDimensionIndex = currentIndex;
                    stateData.CurrentDimensionFullName = currentDimension.FullName;
                    VaultMod.Instance.Logger.Info($"Saving dimension state: {currentDimension.FullName} (Index: {currentIndex})");
                }
                else {
                    stateData.CurrentDimensionIndex = -1;
                    stateData.CurrentDimensionFullName = "MainWorld";
                    VaultMod.Instance.Logger.Info("Saving dimension state: MainWorld");
                }

                //保存时间戳
                stateData.SaveTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

                //使用 SaveContent 系统保存维度状态，自动实现存档隔离
                DoSave<DimensionStateData>();
            } catch (Exception ex) {
                VaultMod.Instance.Logger.Error($"Error saving dimension state: {ex}");
            }
        }

        /// <summary>
        /// 从世界存档加载维度状态
        /// </summary>
        internal static void LoadDimensionState() {
            try {
                //使用 SaveContent 系统加载维度状态
                DoLoad<DimensionStateData>();

                //获取加载的数据实例
                DimensionStateData stateData = GetInstance<DimensionStateData>();

                //检查是否成功加载了数据
                if (stateData != null && !string.IsNullOrEmpty(stateData.CurrentDimensionFullName)) {
                    int dimensionIndex = stateData.CurrentDimensionIndex;
                    string dimensionName = stateData.CurrentDimensionFullName;
                    string saveTime = stateData.SaveTime;

                    VaultMod.Instance.Logger.Info($"Loading dimension state: {dimensionName} (Index: {dimensionIndex}, Saved: {saveTime})");

                    //如果保存的是维度状态（不是主世界）
                    if (dimensionIndex >= 0 && !string.IsNullOrEmpty(dimensionName) && dimensionName != "MainWorld") {
                        //验证维度是否仍然存在
                        if (dimensionsByIndex.ContainsKey(dimensionIndex)) {
                            pendingDimensionRestore = dimensionIndex;
                            VaultMod.Instance.Logger.Info($"Dimension state loaded successfully, will restore to: {dimensionName}");
                        }
                        else {
                            VaultMod.Instance.Logger.Warn($"Saved dimension {dimensionName} (Index: {dimensionIndex}) no longer exists");
                            pendingDimensionRestore = null;
                        }
                    }
                    else {
                        //主世界状态，无需恢复
                        pendingDimensionRestore = null;
                        VaultMod.Instance.Logger.Info("Main world state loaded, no dimension to restore.");
                    }
                }
                else {
                    //没有保存的维度状态
                    pendingDimensionRestore = null;
                    VaultMod.Instance.Logger.Info("No saved dimension state found, starting in main world.");
                }

            } catch (Exception ex) {
                VaultMod.Instance.Logger.Error($"Error loading dimension state: {ex}");
                pendingDimensionRestore = null;
            }
        }
    }
}
