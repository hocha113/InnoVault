using System.Collections.Generic;
using Terraria;
using Terraria.DataStructures;

namespace InnoVault.Storages
{
    /// <summary>
    /// 存储提供者工厂接口
    /// 用于创建特定类型的存储提供者
    /// </summary>
    public interface IStorageProviderFactory
    {
        /// <summary>
        /// 工厂的唯一标识符
        /// </summary>
        string Identifier { get; }

        /// <summary>
        /// 工厂优先级，数值越低优先级越高
        /// </summary>
        int Priority { get; }

        /// <summary>
        /// 检查此工厂是否可用(比如检查模组是否加载)
        /// </summary>
        bool IsAvailable { get; }

        /// <summary>
        /// 在指定范围内查找存储目标
        /// </summary>
        /// <param name="position">搜索中心位置(物块坐标)</param>
        /// <param name="range">搜索范围(像素)</param>
        /// <param name="item">要存储的物品，用于检查是否可以存入</param>
        /// <returns>找到的存储提供者列表</returns>
        IEnumerable<IStorageProvider> FindStorageProviders(Point16 position, int range, Item item);

        /// <summary>
        /// 获取指定位置的存储提供者
        /// </summary>
        /// <param name="position"></param>
        /// <param name="item"></param>
        /// <returns></returns>
        IStorageProvider GetStorageProviders(Point16 position, Item item);
    }
}
