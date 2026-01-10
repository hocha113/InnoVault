using Microsoft.Xna.Framework;
using System.Collections.Generic;
using Terraria;
using Terraria.DataStructures;

namespace InnoVault.Storages
{
    /// <summary>
    /// 原版箱子存储提供者工厂
    /// </summary>
    public class ChestStorageProviderFactory : IStorageProviderFactory
    {
        /// <inheritdoc/>
        public string Identifier => "Vanilla.Chest";
        /// <inheritdoc/>
        public int Priority => 0;
        /// <inheritdoc/>
        public bool IsAvailable => true;

        /// <inheritdoc/>
        public IEnumerable<IStorageProvider> FindStorageProviders(Point16 position, int range, Item item) {
            Vector2 worldPos = position.ToWorldCoordinates();
            float rangeSquared = range * range;

            //遍历所有箱子并按距离排序返回
            List<(Chest chest, int index, float distSq)> validChests = [];

            for (int i = 0; i < Main.maxChests; i++) {
                Chest chest = Main.chest[i];
                if (chest == null) {
                    continue;
                }

                //跳过锁定的箱子
                if (Chest.IsLocked(chest.x, chest.y)) {
                    continue;
                }

                Vector2 chestCenter = new Vector2(chest.x * 16 + 16, chest.y * 16 + 16);
                float distSq = Vector2.DistanceSquared(worldPos, chestCenter);

                if (distSq > rangeSquared) {
                    continue;
                }

                //如果指定了物品，检查是否可以存入
                if (item != null && item.Alives() && !chest.CanItemBeAddedToChest(item)) {
                    continue;
                }

                validChests.Add((chest, i, distSq));
            }

            //按距离排序
            validChests.Sort((a, b) => a.distSq.CompareTo(b.distSq));

            foreach (var (chest, index, _) in validChests) {
                yield return new ChestStorageProvider(chest, index);
            }
        }

        /// <inheritdoc/>
        public IStorageProvider GetStorageProviders(Point16 position, Item item) {
            //先尝试用原版方法精确查找
            int index = Chest.FindChest(position.X, position.Y);
            if (index >= 0 && index < Main.maxChests) {
                Chest chest = Main.chest[index];
                if (chest != null && !Chest.IsLocked(chest.x, chest.y)) {
                    return new ChestStorageProvider(chest, index);
                }
            }

            //如果精确查找失败，尝试查找该位置所属的多格箱子
            if (VaultUtils.SafeGetTopLeft(position.X, position.Y, out Point16 topLeft)) {
                index = Chest.FindChest(topLeft.X, topLeft.Y);
                if (index >= 0 && index < Main.maxChests) {
                    Chest chest = Main.chest[index];
                    if (chest != null && !Chest.IsLocked(chest.x, chest.y)) {
                        return new ChestStorageProvider(chest, index);
                    }
                }
            }

            return null;
        }
    }

    /// <summary>
    /// 原版箱子的存储提供者实现
    /// </summary>
    public class ChestStorageProvider : IStorageProvider
    {
        private readonly Chest _chest;
        private readonly int _chestIndex;

        /// <summary>
        /// 创建箱子存储提供者
        /// </summary>
        /// <param name="chest">箱子实例</param>
        /// <param name="chestIndex">箱子在Main.chest中的索引</param>
        public ChestStorageProvider(Chest chest, int chestIndex) {
            _chest = chest;
            _chestIndex = chestIndex;
            Position = new Point16(chest.x, chest.y);
        }

        /// <summary>
        /// 创建箱子存储提供者
        /// </summary>
        /// <param name="chestIndex">箱子在Main.chest中的索引</param>
        public ChestStorageProvider(int chestIndex) {
            var chest = Main.chest[chestIndex];
            _chest = chest;
            _chestIndex = chestIndex;
            Position = new Point16(chest.x, chest.y);
        }

        /// <inheritdoc/>
        public string Identifier => "Vanilla.Chest";
        /// <inheritdoc/>
        public Point16 Position { get; }
        /// <inheritdoc/>
        public Vector2 WorldCenter => new Vector2(Position.X * 16 + 16, Position.Y * 16 + 16);
        /// <inheritdoc/>
        public Rectangle HitBox => new Rectangle(Position.X * 16, Position.Y * 16, 32, 32);

        /// <inheritdoc/>
        public bool IsValid {
            get {
                if (_chestIndex < 0 || _chestIndex >= Main.maxChests) {
                    return false;
                }
                Chest current = Main.chest[_chestIndex];
                return current != null && current.x == Position.X && current.y == Position.Y;
            }
        }

        /// <inheritdoc/>
        public bool HasSpace => IsValid && _chest.CanItemBeAddedToChest();

        /// <summary>
        /// 检查箱子是否被锁定
        /// </summary>
        public bool IsLocked => Chest.IsLocked(Position.X, Position.Y);

        /// <summary>
        /// 获取箱子索引
        /// </summary>
        public int ChestIndex => _chestIndex;

        /// <summary>
        /// 获取原始箱子实例
        /// </summary>
        public Chest Chest => _chest;

        /// <summary>
        /// 从箱子索引创建存储提供者
        /// </summary>
        /// <param name="chestIndex">箱子索引</param>
        /// <returns>存储提供者，如果索引无效返回null</returns>
        public static ChestStorageProvider FromIndex(int chestIndex) {
            if (chestIndex < 0 || chestIndex >= Main.maxChests) {
                return null;
            }
            Chest chest = Main.chest[chestIndex];
            return chest != null ? new ChestStorageProvider(chest, chestIndex) : null;
        }

        /// <summary>
        /// 从世界坐标查找箱子并创建存储提供者
        /// </summary>
        /// <param name="position">物块坐标</param>
        /// <returns>存储提供者，如果未找到返回null</returns>
        public static ChestStorageProvider FromPosition(Point16 position) {
            int index = Chest.FindChest(position.X, position.Y);
            return index >= 0 ? FromIndex(index) : null;
        }

        /// <summary>
        /// 从世界坐标查找箱子并创建存储提供者(自动处理多格物块)
        /// </summary>
        /// <param name="x">物块X坐标</param>
        /// <param name="y">物块Y坐标</param>
        /// <returns>存储提供者，如果未找到返回null</returns>
        public static ChestStorageProvider FromPosition(int x, int y) {
            //先尝试直接查找
            int index = Chest.FindChest(x, y);
            if (index >= 0) {
                return FromIndex(index);
            }

            //尝试获取左上角后再查找
            if (VaultUtils.SafeGetTopLeft(x, y, out Point16 topLeft)) {
                index = Chest.FindChest(topLeft.X, topLeft.Y);
                if (index >= 0) {
                    return FromIndex(index);
                }
            }

            return null;
        }

        /// <inheritdoc/>
        public bool CanAcceptItem(Item item) {
            if (!IsValid || IsLocked) {
                return false;
            }
            if (item == null || item.IsAir || item.stack <= 0) {
                return false;
            }
            return _chest.CanItemBeAddedToChest(item);
        }

        /// <inheritdoc/>
        public bool DepositItem(Item item) {
            if (!CanAcceptItem(item)) {
                return false;
            }

            _chest.AddItem(item, true);
            return true;
        }

        /// <inheritdoc/>
        public Item WithdrawItem(int itemType, int count) {
            if (!IsValid || IsLocked || count <= 0) {
                return new Item();
            }

            int remaining = count;
            int totalWithdrawn = 0;

            foreach (Item slotItem in _chest.item) {
                if (slotItem == null || slotItem.IsAir || slotItem.type != itemType) {
                    continue;
                }

                int take = System.Math.Min(remaining, slotItem.stack);
                slotItem.stack -= take;
                totalWithdrawn += take;
                remaining -= take;

                if (slotItem.stack <= 0) {
                    slotItem.TurnToAir();
                }

                if (remaining <= 0) {
                    break;
                }
            }

            if (totalWithdrawn <= 0) {
                return new Item();
            }

            return new Item(itemType, totalWithdrawn);
        }

        /// <inheritdoc/>
        public IEnumerable<Item> GetStoredItems() {
            if (!IsValid) {
                yield break;
            }
            foreach (Item item in _chest.item) {
                if (item != null && !item.IsAir) {
                    yield return item;
                }
            }
        }

        /// <inheritdoc/>
        public long GetItemCount(int itemType) {
            if (!IsValid) {
                return 0;
            }
            long count = 0;
            foreach (Item item in _chest.item) {
                if (item != null && !item.IsAir && item.type == itemType) {
                    count += item.stack;
                }
            }
            return count;
        }

        /// <inheritdoc/>
        public void PlayDepositAnimation() {
            if (IsValid) {
                _chest.eatingAnimationTime = 20;
            }
        }
    }
}
