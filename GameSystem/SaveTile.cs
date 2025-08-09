using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;

namespace InnoVault.GameSystem
{
    /// <summary>
    /// 用于序列化/反序列化 Terraria 世界中的单个物块（Tile）数据的轻量级值类型
    /// </summary>
    /// <remarks>
    /// </remarks>
    public struct TileSaveData
    {
        /// <summary>
        /// 物块的名称，该成员只在模组物块上使用，如果存储的是原版物块(即 TileType 小于 TileID.Count 的情况)，则只会存储空字符串<br/>
        /// 否则，对于模组物块，将存储其内部名，用于动态矫正<see cref="TileType"/>的值
        /// </summary>
        public string TileName;
        /// <summary>
        /// 墙壁的名称，该成员只在模组墙壁上使用，如果存储的是原版墙壁(即 WallType 小于 WallID.Count 的情况)，则只会存储空字符串<br/>
        /// 否则，对于模组墙壁，将存储其内部名，用于动态矫正<see cref="WallType"/>的值
        /// </summary>
        public string WallName;
        /// <summary>物块的世界坐标 X（保存时）</summary>
        public short X;
        /// <summary>物块的世界坐标 Y（保存时）</summary>
        public short Y;
        /// <summary>物块类型 ID（<see cref="Tile.TileType"/>）</summary>
        public ushort TileType;
        /// <summary>物块帧坐标 X（<see cref="Tile.TileFrameX"/>）</summary>
        public short FrameX;
        /// <summary>物块帧坐标 Y（<see cref="Tile.TileFrameY"/>）</summary>
        public short FrameY;
        /// <summary>墙类型 ID（<see cref="Tile.WallType"/>）</summary>
        public ushort WallType;
        /// <summary>坡度类型（<see cref="SlopeType"/> 转 byte）</summary>
        public byte Slope;
        /// <summary>液体类型（0: 水，1: 岩浆，2: 蜂蜜等）</summary>
        public byte LiquidType;
        /// <summary>液体数量（0-255）</summary>
        public byte LiquidAmount;
        /// <summary>图格颜料（0-255）</summary>
        public byte TileColor;
        /// <summary>墙壁颜料（0-255）</summary>
        public byte WallColor;
        /// <summary>是否存在物块（<see cref="Tile.HasTile"/>）</summary>
        public bool HasTile;
        /// <summary>
        /// 根据给定的世界坐标和 Tile 数据构造保存单元
        /// </summary>
        public TileSaveData(short x, short y, Tile tile) {
            X = x;
            Y = y;
            HasTile = tile.HasTile;
            TileType = tile.HasTile ? tile.TileType : (ushort)0;
            FrameX = tile.HasTile ? tile.TileFrameX : (short)0;
            FrameY = tile.HasTile ? tile.TileFrameY : (short)0;
            WallType = tile.WallType;
            Slope = (byte)tile.Slope;
            LiquidType = (byte)tile.LiquidType;
            LiquidAmount = tile.LiquidAmount;
            TileColor = tile.TileColor;
            WallColor = tile.WallColor;
            //下面是处理那些该死的模组物块的情况
            TileName = string.Empty;
            if (TileType >= TileID.Count) {
                ModTile modTile = TileLoader.GetTile(TileType);
                if (modTile != null) {
                    TileName = modTile.FullName;
                }
            }
            WallName = string.Empty;
            if (WallType >= WallID.Count) {
                ModWall modWall = WallLoader.GetWall(WallType);
                if (modWall != null) {
                    WallName = modWall.FullName;
                }
            }
        }

        /// <summary>
        /// 将本数据直接应用到保存时的原始位置（X,Y）
        /// </summary>
        public readonly void ApplyToWorld() => ApplyToWorld(X, Y);

        /// <summary>
        /// 将本数据应用到指定位置（可用于区域移动 / 粘贴）
        /// </summary>
        public readonly void ApplyToWorld(short worldX, short worldY) {
            Tile tile = Main.tile[worldX, worldY];

            ushort tileID = TileType;
            //在开始一切之前先进行模组ID校验
            if (TileName != string.Empty) {
                tileID = (ushort)VaultUtils.GetTileTypeFromFullName(TileName);
            }
            //在开始一切之前先进行模组ID校验
            ushort wallID = WallType;
            if (WallName != string.Empty) {
                wallID = (ushort)VaultUtils.GetWallTypeFromFullName(WallName);
            }

            tile.HasTile = HasTile;
            if (HasTile) {
                tile.TileType = tileID;
                tile.TileFrameX = FrameX;
                tile.TileFrameY = FrameY;
            }

            tile.WallType = wallID;
            tile.Slope = (SlopeType)Slope;
            tile.LiquidType = LiquidType;
            tile.LiquidAmount = LiquidAmount;
            tile.TileColor = TileColor;
            tile.WallColor = WallColor;
        }

        /// <summary>
        /// 序列化 IList&lt;TileSaveData&gt; 到 byte[]
        /// </summary>
        public static byte[] Serialize(IList<TileSaveData> tiles) {
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true);

            // 写入总数
            writer.Write(tiles.Count);

            foreach (var tile in tiles) {
                // 写值类型字段
                writer.Write(tile.X);
                writer.Write(tile.Y);
                writer.Write(tile.TileType);
                writer.Write(tile.FrameX);
                writer.Write(tile.FrameY);
                writer.Write(tile.WallType);
                writer.Write(tile.Slope);
                writer.Write(tile.LiquidType);
                writer.Write(tile.LiquidAmount);
                writer.Write(tile.TileColor);
                writer.Write(tile.WallColor);
                writer.Write(tile.HasTile);

                // 写字符串：先写长度(int)，再写UTF8字节，如果为空写长度0
                WriteString(writer, tile.TileName);
                WriteString(writer, tile.WallName);
            }

            writer.Flush();
            return ms.ToArray();
        }

        private static void WriteString(BinaryWriter writer, string value) {
            if (string.IsNullOrEmpty(value)) {
                writer.Write(0);
                return;
            }
            var bytes = Encoding.UTF8.GetBytes(value);
            writer.Write(bytes.Length);
            writer.Write(bytes);
        }

        /// <summary>
        /// 反序列化 byte[] 到 List&lt;TileSaveData&gt;
        /// </summary>
        public static IList<TileSaveData> Deserialize(byte[] data) {
            var result = new List<TileSaveData>();
            using var ms = new MemoryStream(data);
            using var reader = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);

            int count = reader.ReadInt32();

            for (int i = 0; i < count; i++) {
                var tile = new TileSaveData();

                tile.X = reader.ReadInt16();
                tile.Y = reader.ReadInt16();
                tile.TileType = reader.ReadUInt16();
                tile.FrameX = reader.ReadInt16();
                tile.FrameY = reader.ReadInt16();
                tile.WallType = reader.ReadUInt16();
                tile.Slope = reader.ReadByte();
                tile.LiquidType = reader.ReadByte();
                tile.LiquidAmount = reader.ReadByte();
                tile.TileColor = reader.ReadByte();
                tile.WallColor = reader.ReadByte();
                tile.HasTile = reader.ReadBoolean();

                tile.TileName = ReadString(reader);
                tile.WallName = ReadString(reader);

                result.Add(tile);
            }

            return result;
        }

        private static string ReadString(BinaryReader reader) {
            int length = reader.ReadInt32();
            if (length <= 0) {
                return string.Empty;
            }

            var bytes = reader.ReadBytes(length);
            return Encoding.UTF8.GetString(bytes);
        }
    }

    /// <summary>
    /// 关于箱子的数据存储结构
    /// </summary>
    public struct ChestSaveData
    {
        /// <summary>
        /// 物块的名称，该成员只在模组物块上使用，如果存储的是原版物块(即 TileType 小于 TileID.Count 的情况)，则只会存储空字符串<br/>
        /// 否则，对于模组物块，将存储其内部名，用于动态矫正<see cref="TileType"/>的值
        /// </summary>
        public string TileName;
        /// <summary>
        /// 相对坐标X
        /// </summary>
        public short X;
        /// <summary>
        /// 相对坐标Y
        /// </summary>
        public short Y;
        /// <summary>
        /// 箱子所在tile类型
        /// </summary>
        public ushort TileType;
        /// <summary>
        /// 帧X
        /// </summary>
        public short FrameX;
        /// <summary>
        /// 帧Y
        /// </summary>
        public short FrameY;
        /// <summary>
        /// 箱子物品列表
        /// </summary>
        public List<TagCompound> Items;
        /// <summary>
        /// 保存一个箱子的数据结构
        /// </summary>
        /// <param name="relX"></param>
        /// <param name="relY"></param>
        /// <param name="chest"></param>
        public ChestSaveData(short relX, short relY, Chest chest) {
            X = relX;
            Y = relY;
            Point16 absPos = chest.GetPoint16();

            Tile tile = Framing.GetTileSafely(absPos);
            TileType = tile.TileType;
            FrameX = tile.TileFrameX;
            FrameY = tile.TileFrameY;

            Items = new List<TagCompound>(chest.item.Length);
            for (int i = 0; i < chest.item.Length; i++) {
                Items.Add(ItemIO.Save(chest.item[i]));
            }

            //下面是处理那些该死的模组物块的情况
            TileName = string.Empty;
            if (TileType >= TileID.Count) {
                ModTile modTile = TileLoader.GetTile(TileType);
                if (modTile != null) {
                    TileName = modTile.FullName;
                }
            }
        }
        /// <summary>
        /// 将数据应用到世界，这会放置出一个箱子
        /// </summary>
        public readonly void ApplyToWorld(short targetX, short targetY) {
            ushort tileID = TileType;
            //在开始一切之前先进行模组ID校验
            if (TileName != string.Empty) {
                tileID = (ushort)VaultUtils.GetTileTypeFromFullName(TileName);
            }

            WorldGen.KillTile(targetX, targetY + 1, noItem: true);
            WorldGen.PlaceTile(targetX, targetY + 1, tileID, mute: true);
            SaveStructure.SetChestFrame(new Point16(targetX, targetY), FrameX, FrameY);

            int chestIndex = Chest.FindChest(targetX, targetY);
            if (chestIndex == -1) {
                chestIndex = Chest.CreateChest(targetX, targetY);
            }

            if (chestIndex > -1) {
                Chest chest = Main.chest[chestIndex];
                for (int i = 0; i < Items.Count; i++) {
                    chest.item[i] = ItemIO.Load(Items[i]);
                }
            }
        }
        /// <summary>
        /// 序列化
        /// </summary>
        /// <returns></returns>
        public readonly TagCompound ToTag() {
            return new TagCompound {
                ["a"] = TileName,
                ["b"] = X,
                ["c"] = Y,
                ["d"] = TileType,
                ["e"] = FrameX,
                ["f"] = FrameY,
                ["g"] = Items,
            };
        }
        /// <summary>
        /// 反序列化
        /// </summary>
        /// <returns></returns>
        public static ChestSaveData FromTag(TagCompound tag) {
            return new ChestSaveData {
                TileName = tag.GetString("a"),
                X = tag.GetShort("b"),
                Y = tag.GetShort("c"),
                TileType = tag.Get<ushort>("d"),
                FrameX = tag.GetShort("e"),
                FrameY = tag.GetShort("f"),
                Items = [.. tag.GetList<TagCompound>("g")]
            };
        }
    }

    /// <summary>
    /// 将图格的数据存储为NBT，包含一些基本的图格存取工具
    /// </summary>
    public abstract class SaveStructure : SaveContent<SaveStructure>
    {
        /// <summary>
        /// 保存世界数据的路径，包含文件名，使用世界存档名字作为关键字
        /// </summary>
        public override string SavePath => Path.Combine(VaultSave.RootPath, "Structure", Mod.Name, $"structure_{Name}.nbt");
        /// <summary>
        /// 将整个世界复制保存为TagCompound
        /// </summary>
        /// <param name="tag"></param>
        public static void CopyWorld(TagCompound tag) {
            tag["span"] = new Point16(Main.spawnTileX, Main.spawnTileY);
            tag["worldTilesCompressed"] = TileSaveData.Serialize(SaveTiles());
            tag["chests"] = (IList<TagCompound>)SaveChests();
        }
        /// <summary>
        /// 将指定的世界数据放置出来
        /// </summary>
        /// <param name="tag"></param>
        /// <param name="setProgress"></param>
        /// <param name="phaseStart"></param>
        /// <param name="phaseEnd"></param>
        public static void LoadWorld(TagCompound tag, Action<float> setProgress, float phaseStart, float phaseEnd) {
            Point16 span = tag.Get<Point16>("span");
            Main.spawnTileX = span.X;
            Main.spawnTileY = span.Y;

            var tileData = tag.GetByteArray("worldTilesCompressed");
            LoadTiles(TileSaveData.Deserialize(tileData));
            LoadChests(tag.GetList<TagCompound>("chests"));
        }
        /// <summary>
        /// 设置宝箱的样式帧
        /// </summary>
        /// <param name="point"></param>
        /// <param name="frameX"></param>
        /// <param name="frameY"></param>
        public static void SetChestFrame(Point16 point, short frameX, short frameY) {
            Framing.GetTileSafely(point.X, point.Y).TileFrameX = frameX;
            Framing.GetTileSafely(point.X, point.Y).TileFrameY = frameY;
            Framing.GetTileSafely(point.X + 1, point.Y).TileFrameX = (short)(frameX + 18);
            Framing.GetTileSafely(point.X + 1, point.Y).TileFrameY = frameY;
            Framing.GetTileSafely(point.X, point.Y + 1).TileFrameX = frameX;
            Framing.GetTileSafely(point.X, point.Y + 1).TileFrameY = (short)(frameY + 18);
            Framing.GetTileSafely(point.X + 1, point.Y + 1).TileFrameX = (short)(frameX + 18);
            Framing.GetTileSafely(point.X + 1, point.Y + 1).TileFrameY = (short)(frameY + 18);
        }
        /// <summary>
        /// 无效的图格区域
        /// </summary>
        /// <param name="tile"></param>
        /// <returns></returns>
        public static bool TileIsAir(Tile tile) => !tile.HasTile && tile.WallType == 0 && tile.LiquidAmount == 0;
        /// <summary>
        /// 保存指定矩形范围的方块数据
        /// </summary>
        /// <param name="startX">起始X坐标</param>
        /// <param name="startY">起始Y坐标</param>
        /// <param name="width">宽度</param>
        /// <param name="height">高度</param>
        public static IList<TileSaveData> SaveTiles(short startX = 0, short startY = 0, short width = 0, short height = 0) {
            var tiles = new List<TileSaveData>();
            if (width == 0) {
                width = (short)Main.maxTilesX;
            }
            if (height == 0) {
                height = (short)Main.maxTilesY;
            }

            for (short i = 0; i < width; i++) {
                for (short j = 0; j < height; j++) {
                    short worldX = (short)(startX + i);
                    short worldY = (short)(startY + j);

                    Tile tile = Main.tile[worldX, worldY];
                    if (TileIsAir(tile)) {
                        continue;
                    }

                    tiles.Add(new TileSaveData(i, j, tile)); //注意存的是相对坐标
                }
            }
            return tiles;
        }
        /// <summary>
        /// 将方块数据加载到指定位置
        /// </summary>
        /// <param name="tiles">方块数据（相对坐标）</param>
        /// <param name="targetX">放置的世界起点X</param>
        /// <param name="targetY">放置的世界起点Y</param>
        /// <param name="setProgress">进度回调</param>
        /// <param name="phaseStart">进度阶段起点</param>
        /// <param name="phaseEnd">进度阶段终点</param>
        public static void LoadTiles(IList<TileSaveData> tiles, short targetX = 0, short targetY = 0,
            Action<float> setProgress = null, float phaseStart = 0, float phaseEnd = 100) {

            int count = tiles.Count;
            for (int i = 0; i < count; i++) {
                var data = tiles[i];
                // 应用到偏移后的坐标
                data.ApplyToWorld((short)(targetX + data.X), (short)(targetY + data.Y));

                if (i % 5000 == 0) {
                    setProgress?.Invoke(phaseStart + (phaseEnd - phaseStart) * (i / (float)count));
                }
            }
        }
        /// <summary>
        /// 保存指定区域内的所有箱子
        /// </summary>
        /// <param name="startX"></param>
        /// <param name="startY"></param>
        /// <param name="width"></param>
        /// <param name="height"></param>
        /// <returns></returns>
        public static List<TagCompound> SaveChests(short startX = 0, short startY = 0, short width = 0, short height = 0) {
            var chests = new List<TagCompound>();
            if (width == 0) {
                width = (short)Main.maxTilesX;
            }
            if (height == 0) {
                height = (short)Main.maxTilesY;
            }

            for (int i = 0; i < Main.chest.Length; i++) {
                Chest chest = Main.chest[i];
                if (chest == null) {
                    continue;
                }

                Point16 pos = chest.GetPoint16();
                // 判断是否在指定范围内
                if (pos.X >= startX && pos.X < startX + width &&
                    pos.Y >= startY && pos.Y < startY + height) {

                    short relX = (short)(pos.X - startX);
                    short relY = (short)(pos.Y - startY);
                    chests.Add(new ChestSaveData(relX, relY, chest).ToTag());
                }
            }

            return chests;
        }
        /// <summary>
        /// 将给定的箱子数据在指定的位置放置出来
        /// </summary>
        /// <param name="chests"></param>
        /// <param name="targetX"></param>
        /// <param name="targetY"></param>
        /// <param name="setProgress"></param>
        /// <param name="phaseStart"></param>
        /// <param name="phaseEnd"></param>
        public static void LoadChests(IList<TagCompound> chests, short targetX = 0, short targetY = 0,
            Action<float> setProgress = null, float phaseStart = 0, float phaseEnd = 100) {
            int count = chests.Count;
            for (int i = 0; i < count; i++) {
                var chestData = ChestSaveData.FromTag(chests[i]);
                chestData.ApplyToWorld((short)(targetX + chestData.X), (short)(targetY + chestData.Y));

                if (i % 50 == 0) { // 箱子数量少，不需要太频繁
                    setProgress?.Invoke(phaseStart + (phaseEnd - phaseStart) * (i / (float)count));
                }
            }
        }
    }
}
