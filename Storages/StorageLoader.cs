using System.Collections.Generic;
using System.Linq;
using Terraria;
using Terraria.DataStructures;

namespace InnoVault.Storages
{
    /// <summary>
    /// 存储系统
    /// 提供统一的存储查找和操作入口
    /// </summary>
    public sealed class StorageLoader : IVaultLoader
    {
        private static readonly List<IStorageProviderFactory> _factories = [];
        private static bool _initialized = false;

        void IVaultLoader.SetupData() => Initialize();

        void IVaultLoader.UnLoadData() => Reset();

        /// <summary>
        /// 初始化注册表，注册内置的存储提供者工厂
        /// </summary>
        public static void Initialize() {
            if (_initialized) {
                return;
            }
            _factories.Clear();

            //注册内置工厂
            foreach (var storageProviderFactory in VaultUtils.GetDerivedInstances<IStorageProviderFactory>()) {
                Register(storageProviderFactory);
            }

            _initialized = true;
        }

        /// <summary>
        /// 重置注册表
        /// </summary>
        public static void Reset() {
            _factories.Clear();
            _initialized = false;
        }

        /// <summary>
        /// 注册一个存储提供者工厂
        /// </summary>
        public static void Register(IStorageProviderFactory factory) {
            if (factory == null) {
                return;
            }

            //检查是否已存在同标识符的工厂
            for (int i = 0; i < _factories.Count; i++) {
                if (_factories[i].Identifier == factory.Identifier) {
                    _factories[i] = factory;
                    SortFactories();
                    return;
                }
            }

            _factories.Add(factory);
            SortFactories();
        }

        /// <summary>
        /// 注销一个存储提供者工厂
        /// </summary>
        public static void Unregister(string identifier) {
            _factories.RemoveAll(f => f.Identifier == identifier);
        }

        /// <summary>
        /// 获取所有可用的存储提供者工厂
        /// </summary>
        public static IEnumerable<IStorageProviderFactory> GetAvailableFactories() {
            foreach (var factory in _factories) {
                if (factory.IsAvailable) {
                    yield return factory;
                }
            }
        }

        private static void SortFactories() {
            _factories.Sort((a, b) => a.Priority.CompareTo(b.Priority));
        }

        /// <summary>
        /// 查找可以存放指定物品的最近存储对象
        /// </summary>
        /// <param name="position">搜索中心位置(物块坐标)</param>
        /// <param name="range">搜索范围(像素)</param>
        /// <param name="item">要存储的物品</param>
        /// <returns>找到的存储提供者，如果没找到返回null</returns>
        public static IStorageProvider FindStorageTarget(Point16 position, int range, Item item) {
            if (!item.Alives()) {
                return null;
            }

            //确保注册表已初始化
            Initialize();

            //按优先级遍历所有工厂
            foreach (var factory in GetAvailableFactories()) {
                foreach (var provider in factory.FindStorageProviders(position, range, item)) {
                    if (provider != null && provider.IsValid && provider.CanAcceptItem(item)) {
                        return provider;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// 查找可以存放指定物品的最近存储对象
        /// </summary>
        /// <param name="position">搜索中心位置(物块坐标)</param>
        /// <param name="range">搜索范围(像素)</param>
        /// <returns>找到的存储提供者，如果没找到返回null</returns>
        public static IStorageProvider FindStorageTarget(Point16 position, int range) {
            //确保注册表已初始化
            Initialize();

            //按优先级遍历所有工厂
            foreach (var factory in GetAvailableFactories()) {
                foreach (var provider in factory.FindStorageProviders(position, range, new Item())) {
                    if (provider != null && provider.IsValid) {
                        return provider;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// 获取指定位置的存储对象
        /// </summary>
        /// <param name="position">目标物块坐标</param>
        /// <returns>找到的存储提供者，如果没找到返回null</returns>
        public static IStorageProvider GetStorageTargetByPoint(Point16 position) {
            //确保注册表已初始化
            Initialize();

            //按优先级遍历所有工厂
            foreach (var factory in GetAvailableFactories()) {
                var provider = factory.GetStorageProviders(position, new Item());
                if (provider != null && provider.IsValid) {
                    return provider;
                }
            }

            return null;
        }

        /// <summary>
        /// 获取指定位置的存储对象
        /// </summary>
        /// <param name="position">目标物块坐标</param>
        /// <param name="item">用于检查兼容性的物品</param>
        /// <returns>找到的存储提供者，如果没找到返回null</returns>
        public static IStorageProvider GetStorageTargetByPoint(Point16 position, Item item) {
            //确保注册表已初始化
            Initialize();

            //按优先级遍历所有工厂
            foreach (var factory in GetAvailableFactories()) {
                var provider = factory.GetStorageProviders(position, item ?? new Item());
                if (provider != null && provider.IsValid) {
                    return provider;
                }
            }

            return null;
        }

        /// <summary>
        /// 尝试获取指定位置的存储对象
        /// </summary>
        /// <param name="position">目标物块坐标</param>
        /// <param name="provider">输出的存储提供者</param>
        /// <returns>是否成功找到存储对象</returns>
        public static bool TryGetStorageTargetByPoint(Point16 position, out IStorageProvider provider) {
            provider = GetStorageTargetByPoint(position);
            return provider != null;
        }

        /// <summary>
        /// 尝试获取指定位置的存储对象
        /// </summary>
        /// <param name="position">目标物块坐标</param>
        /// <param name="item">用于检查兼容性的物品</param>
        /// <param name="provider">输出的存储提供者</param>
        /// <returns>是否成功找到存储对象</returns>
        public static bool TryGetStorageTargetByPoint(Point16 position, Item item, out IStorageProvider provider) {
            provider = GetStorageTargetByPoint(position, item);
            return provider != null;
        }

        /// <summary>
        /// 查找指定范围内所有可用的存储对象
        /// </summary>
        /// <param name="position">搜索中心位置(物块坐标)</param>
        /// <param name="range">搜索范围(像素)</param>
        /// <param name="item">用于检查兼容性的物品，可为null</param>
        /// <returns>找到的所有存储提供者</returns>
        public static IEnumerable<IStorageProvider> FindAllStorageTargets(Point16 position, int range, Item item = null) {
            //确保注册表已初始化
            Initialize();

            foreach (var factory in GetAvailableFactories()) {
                foreach (var provider in factory.FindStorageProviders(position, range, item ?? new Item())) {
                    if (provider != null && provider.IsValid) {
                        yield return provider;
                    }
                }
            }
        }

        /// <summary>
        /// 查找指定范围内所有可以接受指定物品的存储对象
        /// </summary>
        /// <param name="position">搜索中心位置(物块坐标)</param>
        /// <param name="range">搜索范围(像素)</param>
        /// <param name="item">要存储的物品</param>
        /// <returns>找到的所有可接受该物品的存储提供者</returns>
        public static IEnumerable<IStorageProvider> FindAllAcceptableStorageTargets(Point16 position, int range, Item item) {
            foreach (var provider in FindAllStorageTargets(position, range, item)) {
                if (provider.CanAcceptItem(item)) {
                    yield return provider;
                }
            }
        }

        /// <summary>
        /// 尝试将物品存入指定范围内的存储对象
        /// </summary>
        /// <param name="position">搜索中心位置(物块坐标)</param>
        /// <param name="range">搜索范围(像素)</param>
        /// <param name="item">要存储的物品</param>
        /// <returns>存储是否成功</returns>
        public static bool TryDepositItem(Point16 position, int range, Item item) {
            var storage = FindStorageTarget(position, range, item);
            if (storage == null) {
                return false;
            }

            bool success = storage.DepositItem(item);
            if (success) {
                storage.PlayDepositAnimation();
            }
            return success;
        }

        /// <summary>
        /// 尝试将物品存入指定的存储对象
        /// </summary>
        /// <param name="provider">目标存储提供者</param>
        /// <param name="item">要存储的物品</param>
        /// <param name="playAnimation">是否播放存入动画</param>
        /// <returns>存储是否成功</returns>
        public static bool TryDepositItem(IStorageProvider provider, Item item, bool playAnimation = true) {
            if (provider == null || !provider.IsValid || !provider.CanAcceptItem(item)) {
                return false;
            }

            bool success = provider.DepositItem(item);
            if (success && playAnimation) {
                provider.PlayDepositAnimation();
            }
            return success;
        }

        /// <summary>
        /// 批量存入物品到指定范围内的存储对象
        /// </summary>
        /// <param name="position">搜索中心位置(物块坐标)</param>
        /// <param name="range">搜索范围(像素)</param>
        /// <param name="items">要存储的物品列表</param>
        /// <returns>成功存入的物品数量</returns>
        public static int TryDepositItems(Point16 position, int range, IEnumerable<Item> items) {
            int depositedCount = 0;
            foreach (var item in items) {
                if (item == null || !item.Alives()) {
                    continue;
                }
                if (TryDepositItem(position, range, item)) {
                    depositedCount++;
                }
            }
            return depositedCount;
        }

        /// <summary>
        /// 尝试从指定范围内的存储中取出物品
        /// </summary>
        /// <param name="position">搜索中心位置(物块坐标)</param>
        /// <param name="range">搜索范围(像素)</param>
        /// <param name="itemType">物品类型ID</param>
        /// <param name="count">要取出的数量</param>
        /// <returns>取出的物品，如果无法取出返回空物品</returns>
        public static Item TryWithdrawItem(Point16 position, int range, int itemType, int count) {
            foreach (var provider in FindAllStorageTargets(position, range)) {
                long available = provider.GetItemCount(itemType);
                if (available > 0) {
                    int withdrawCount = (int)System.Math.Min(available, count);
                    Item withdrawn = provider.WithdrawItem(itemType, withdrawCount);
                    if (withdrawn != null && withdrawn.Alives()) {
                        return withdrawn;
                    }
                }
            }
            return new Item();
        }

        /// <summary>
        /// 尝试从指定范围内的存储中取出指定数量的物品，会遍历多个存储直到满足需求
        /// </summary>
        /// <param name="position">搜索中心位置(物块坐标)</param>
        /// <param name="range">搜索范围(像素)</param>
        /// <param name="itemType">物品类型ID</param>
        /// <param name="count">要取出的数量</param>
        /// <param name="withdrawnItems">取出的物品列表</param>
        /// <returns>实际取出的总数量</returns>
        public static int TryWithdrawItems(Point16 position, int range, int itemType, int count, out List<Item> withdrawnItems) {
            withdrawnItems = [];
            int remaining = count;

            foreach (var provider in FindAllStorageTargets(position, range)) {
                if (remaining <= 0) {
                    break;
                }

                long available = provider.GetItemCount(itemType);
                if (available > 0) {
                    int withdrawCount = (int)System.Math.Min(available, remaining);
                    Item withdrawn = provider.WithdrawItem(itemType, withdrawCount);
                    if (withdrawn != null && withdrawn.Alives()) {
                        withdrawnItems.Add(withdrawn);
                        remaining -= withdrawn.stack;
                    }
                }
            }

            return count - remaining;
        }

        /// <summary>
        /// 检查指定范围内的存储是否包含足够数量的指定物品
        /// </summary>
        /// <param name="position">搜索中心位置(物块坐标)</param>
        /// <param name="range">搜索范围(像素)</param>
        /// <param name="itemType">物品类型ID</param>
        /// <param name="requiredCount">需要的数量</param>
        /// <returns>是否包含足够数量</returns>
        public static bool HasEnoughItems(Point16 position, int range, int itemType, int requiredCount) {
            return GetTotalItemCount(position, range, itemType) >= requiredCount;
        }

        /// <summary>
        /// 在指定范围内的所有存储中搜索指定类型物品的总数量
        /// </summary>
        /// <param name="position">搜索中心位置(物块坐标)</param>
        /// <param name="range">搜索范围(像素)</param>
        /// <param name="itemType">物品类型ID</param>
        /// <returns>物品总数量</returns>
        public static long GetTotalItemCount(Point16 position, int range, int itemType) {
            long total = 0;
            foreach (var provider in FindAllStorageTargets(position, range)) {
                total += provider.GetItemCount(itemType);
            }
            return total;
        }

        /// <summary>
        /// 获取指定范围内所有存储中指定物品类型的物品
        /// </summary>
        /// <param name="position">搜索中心位置(物块坐标)</param>
        /// <param name="range">搜索范围(像素)</param>
        /// <param name="itemType">物品类型ID</param>
        /// <returns>符合条件的物品枚举</returns>
        public static IEnumerable<Item> GetStoredItemsByType(Point16 position, int range, int itemType) {
            foreach (var provider in FindAllStorageTargets(position, range)) {
                foreach (var item in provider.GetStoredItems()) {
                    if (item.type == itemType) {
                        yield return item;
                    }
                }
            }
        }

        /// <summary>
        /// 获取指定范围内所有存储中的物品
        /// </summary>
        /// <param name="position">搜索中心位置(物块坐标)</param>
        /// <param name="range">搜索范围(像素)</param>
        /// <returns>所有物品的枚举</returns>
        public static IEnumerable<Item> GetAllStoredItems(Point16 position, int range) {
            foreach (var provider in FindAllStorageTargets(position, range)) {
                foreach (var item in provider.GetStoredItems()) {
                    yield return item;
                }
            }
        }

        /// <summary>
        /// 获取指定范围内所有存储中满足条件的物品
        /// </summary>
        /// <param name="position">搜索中心位置(物块坐标)</param>
        /// <param name="range">搜索范围(像素)</param>
        /// <param name="predicate">物品过滤条件</param>
        /// <returns>满足条件的物品枚举</returns>
        public static IEnumerable<Item> GetStoredItemsWhere(Point16 position, int range, System.Func<Item, bool> predicate) {
            if (predicate == null) {
                yield break;
            }

            foreach (var item in GetAllStoredItems(position, range)) {
                if (predicate(item)) {
                    yield return item;
                }
            }
        }

        /// <summary>
        /// 获取指定范围内所有有剩余空间的存储对象
        /// </summary>
        /// <param name="position">搜索中心位置(物块坐标)</param>
        /// <param name="range">搜索范围(像素)</param>
        /// <returns>有剩余空间的存储提供者枚举</returns>
        public static IEnumerable<IStorageProvider> FindStoragesWithSpace(Point16 position, int range) {
            foreach (var provider in FindAllStorageTargets(position, range)) {
                if (provider.HasSpace) {
                    yield return provider;
                }
            }
        }

        /// <summary>
        /// 获取指定范围内存储对象的总数量
        /// </summary>
        /// <param name="position">搜索中心位置(物块坐标)</param>
        /// <param name="range">搜索范围(像素)</param>
        /// <returns>存储对象数量</returns>
        public static int GetStorageCount(Point16 position, int range)
            => FindAllStorageTargets(position, range).Count();
    }
}