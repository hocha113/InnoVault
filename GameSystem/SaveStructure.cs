using InnoVault.TileProcessors;
using Microsoft.Xna.Framework;
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
    /// 用于序列化/反序列化世界中的单个物块（Tile）数据的轻量级值类型<br/>
    /// 如果需要保存箱子等储存物，配合<see cref="ChestSaveData"/>使用
    /// </summary>
    public struct TileSaveData
    {
        /// <summary>
        /// 控制在加载存档时是否强制放置物块<br/>
        /// 如果为 <see langword="true"/> 则即使目标位置当前没有物块，也会恢复存档中的物块<br/>
        /// 每次调用 SaveStructure.LoadTiles 后会自动重置为默认值 <see langword="false"/>
        /// </summary>
        public static bool ForcePlaceTiles { get; set; } = false;
        /// <summary>
        /// 控制在加载存档时是否强制放置墙壁<br/>
        /// 如果为 <see langword="true"/> 则即使目标位置当前没有墙壁，也会恢复存档中的墙壁<br/>
        /// 每次调用 SaveStructure.LoadTiles 后会自动重置为默认值 <see langword="false"/>
        /// </summary>
        public static bool ForcePlaceWalls { get; set; } = false;
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
        /// <summary>是否存在促动器（<see cref="Tile.HasActuator"/>）</summary>
        public bool HasActuator;
        /// <summary>是否处于被促动状态（<see cref="Tile.IsActuated"/>）</summary>
        public bool IsActuated;
        /// <summary>是否为半砖（<see cref="Tile.IsHalfBlock"/>）</summary>
        public bool IsHalfBlock;
        /// <summary>是否有红色电线（<see cref="Tile.RedWire"/>）</summary>
        public bool RedWire;
        /// <summary>是否有绿色电线（<see cref="Tile.GreenWire"/>）</summary>
        public bool GreenWire;
        /// <summary>是否有蓝色电线（<see cref="Tile.BlueWire"/>）</summary>
        public bool BlueWire;
        /// <summary>是否有黄色电线（<see cref="Tile.YellowWire"/>）</summary>
        public bool YellowWire;
        /// <summary>物块是否不可见（<see cref="Tile.IsTileInvisible"/>）</summary>
        public bool IsTileInvisible;
        /// <summary>墙壁是否不可见（<see cref="Tile.IsWallInvisible"/>）</summary>
        public bool IsWallInvisible;
        /// <summary>物块是否全亮（忽略光照，<see cref="Tile.IsTileFullbright"/>）</summary>
        public bool IsTileFullbright;
        /// <summary>墙壁是否全亮（忽略光照，<see cref="Tile.IsWallFullbright"/>）</summary>
        public bool IsWallFullbright;
        /// <summary>
        /// 根据给定的世界坐标和 Tile 数据构造保存单元
        /// </summary>
        public TileSaveData(short x, short y, Tile tile) {
            X = x;
            Y = y;

            HasTile = tile.HasTile;
            HasActuator = tile.HasActuator;
            IsActuated = tile.IsActuated;
            IsHalfBlock = tile.IsHalfBlock;
            RedWire = tile.RedWire;
            GreenWire = tile.GreenWire;
            BlueWire = tile.BlueWire;
            YellowWire = tile.YellowWire;
            IsTileInvisible = tile.IsTileInvisible;
            IsWallInvisible = tile.IsWallInvisible;
            IsTileFullbright = tile.IsTileFullbright;
            IsWallFullbright = tile.IsWallFullbright;

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
            if (!WorldGen.InWorld(worldX, worldY)) {
                return;
            }

            Tile tile = Main.tile[worldX, worldY];

            ushort tileID = TileType;
            //在开始一切之前先进行模组ID校验
            if (!string.IsNullOrEmpty(TileName)) {
                tileID = (ushort)VaultUtils.GetTileTypeFromFullName(TileName);
            }
            //在开始一切之前先进行模组ID校验
            ushort wallID = WallType;
            if (!string.IsNullOrEmpty(WallName)) {
                wallID = (ushort)VaultUtils.GetWallTypeFromFullName(WallName);
            }

            tile.HasTile = HasTile;
            tile.HasActuator = HasActuator;
            tile.IsActuated = IsActuated;
            tile.IsHalfBlock = IsHalfBlock;
            tile.RedWire = RedWire;
            tile.GreenWire = GreenWire;
            tile.BlueWire = BlueWire;
            tile.YellowWire = YellowWire;
            tile.IsTileInvisible = IsTileInvisible;
            tile.IsWallInvisible = IsWallInvisible;
            tile.IsTileFullbright = IsTileFullbright;
            tile.IsWallFullbright = IsWallFullbright;

            if (HasTile || ForcePlaceTiles) {
                tile.TileType = tileID;
                tile.TileFrameX = FrameX;
                tile.TileFrameY = FrameY;
            }

            if (wallID > WallID.None || ForcePlaceWalls) {
                tile.WallType = wallID;
            }

            tile.Slope = (SlopeType)Slope;
            tile.LiquidType = LiquidType;
            tile.LiquidAmount = LiquidAmount;
            tile.TileColor = TileColor;
            tile.WallColor = WallColor;
        }
        /// <summary>
        /// 序列化为 TagCompound
        /// </summary>
        public readonly TagCompound ToTag() {
            BitsByte flags1 = new();
            flags1[0] = HasTile;
            flags1[1] = HasActuator;
            flags1[2] = IsActuated;
            flags1[3] = IsHalfBlock;

            BitsByte flags2 = new();
            flags2[0] = RedWire;
            flags2[1] = GreenWire;
            flags2[2] = BlueWire;
            flags2[3] = YellowWire;
            flags2[4] = IsTileInvisible;
            flags2[5] = IsWallInvisible;
            flags2[6] = IsTileFullbright;
            flags2[7] = IsWallFullbright;

            return new TagCompound {
                ["X"] = X,
                ["Y"] = Y,
                ["Flags1"] = (byte)flags1,
                ["Flags2"] = (byte)flags2,
                ["TileType"] = TileType,
                ["FrameX"] = FrameX,
                ["FrameY"] = FrameY,
                ["WallType"] = WallType,
                ["Slope"] = Slope,
                ["LiquidType"] = LiquidType,
                ["LiquidAmount"] = LiquidAmount,
                ["TileColor"] = TileColor,
                ["WallColor"] = WallColor,
                ["TileName"] = TileName ?? string.Empty,
                ["WallName"] = WallName ?? string.Empty
            };
        }

        /// <summary>
        /// 从 TagCompound 反序列化
        /// </summary>
        public static TileSaveData FromTag(TagCompound tag) {
            BitsByte flags1 = (BitsByte)tag.GetByte("Flags1");
            BitsByte flags2 = (BitsByte)tag.GetByte("Flags2");

            return new TileSaveData {
                X = tag.GetShort("X"),
                Y = tag.GetShort("Y"),

                HasTile = flags1[0],
                HasActuator = flags1[1],
                IsActuated = flags1[2],
                IsHalfBlock = flags1[3],

                RedWire = flags2[0],
                GreenWire = flags2[1],
                BlueWire = flags2[2],
                YellowWire = flags2[3],
                IsTileInvisible = flags2[4],
                IsWallInvisible = flags2[5],
                IsTileFullbright = flags2[6],
                IsWallFullbright = flags2[7],

                TileType = tag.GetUShort("TileType"),
                FrameX = tag.GetShort("FrameX"),
                FrameY = tag.GetShort("FrameY"),
                WallType = tag.GetUShort("WallType"),
                Slope = tag.GetByte("Slope"),
                LiquidType = tag.GetByte("LiquidType"),
                LiquidAmount = tag.GetByte("LiquidAmount"),
                TileColor = tag.GetByte("TileColor"),
                WallColor = tag.GetByte("WallColor"),
                TileName = tag.GetString("TileName"),
                WallName = tag.GetString("WallName")
            };
        }
        /// <summary>
        /// 序列化 IList&lt;TileSaveData&gt; 到 byte[]
        /// </summary>
        public static byte[] Serialize(IList<TileSaveData> tiles) {
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true);

            writer.Write(tiles.Count);

            foreach (var tile in tiles) {
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

                // 位压缩
                BitsByte flags1 = new BitsByte();
                flags1[0] = tile.HasTile;
                flags1[1] = tile.HasActuator;
                flags1[2] = tile.IsActuated;
                flags1[3] = tile.IsHalfBlock;
                writer.Write((byte)flags1);

                BitsByte flags2 = new BitsByte();
                flags2[0] = tile.RedWire;
                flags2[1] = tile.GreenWire;
                flags2[2] = tile.BlueWire;
                flags2[3] = tile.YellowWire;
                flags2[4] = tile.IsTileInvisible;
                flags2[5] = tile.IsWallInvisible;
                flags2[6] = tile.IsTileFullbright;
                flags2[7] = tile.IsWallFullbright;
                writer.Write((byte)flags2);

                WriteString(writer, tile.TileName);
                WriteString(writer, tile.WallName);
            }

            writer.Flush();
            return ms.ToArray();
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

                BitsByte flags1 = reader.ReadByte();
                tile.HasTile = flags1[0];
                tile.HasActuator = flags1[1];
                tile.IsActuated = flags1[2];
                tile.IsHalfBlock = flags1[3];

                BitsByte flags2 = reader.ReadByte();
                tile.RedWire = flags2[0];
                tile.GreenWire = flags2[1];
                tile.BlueWire = flags2[2];
                tile.YellowWire = flags2[3];
                tile.IsTileInvisible = flags2[4];
                tile.IsWallInvisible = flags2[5];
                tile.IsTileFullbright = flags2[6];
                tile.IsWallFullbright = flags2[7];

                tile.TileName = ReadString(reader);
                tile.WallName = ReadString(reader);

                result.Add(tile);
            }

            return result;
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
    /// 用于序列化/反序列化世界中的单个箱子内容（Chest）数据的轻量级值类型<br/>
    /// 注意，该结构并不维护物块数据，只存储箱子内容，一般需要配合<see cref="TileSaveData"/>使用
    /// </summary>
    public struct ChestSaveData
    {
        /// <summary>
        /// 相对坐标X
        /// </summary>
        public short X;
        /// <summary>
        /// 相对坐标Y
        /// </summary>
        public short Y;
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
            Items = new List<TagCompound>(chest.item.Length);
            for (int i = 0; i < chest.item.Length; i++) {
                Items.Add(ItemIO.Save(chest.item[i]));
            }
        }
        /// <summary>
        /// 将数据应用到世界，这会试图创建出一个箱子存储对象
        /// </summary>
        public readonly void ApplyToWorld(short targetX, short targetY) {
            if (!WorldGen.InWorld(targetX, targetY)) {
                return;
            }

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
        /// 序列化 TagCompound
        /// </summary>
        /// <returns></returns>
        public readonly TagCompound ToTag() {
            return new TagCompound {
                ["a"] = X,
                ["b"] = Y,
                ["c"] = Items,
            };
        }
        /// <summary>
        /// 反序列化 TagCompound
        /// </summary>
        /// <returns></returns>
        public static ChestSaveData FromTag(TagCompound tag) {
            return new ChestSaveData {
                X = tag.GetShort("a"),
                Y = tag.GetShort("b"),
                Items = [.. tag.GetList<TagCompound>("c")]
            };
        }
        /// <summary>
        /// 序列化 IList&lt;ChestSaveData&gt; 到 byte[]
        /// </summary>
        public static byte[] Serialize(IList<ChestSaveData> chests) {
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true);

            //写入总数
            writer.Write(chests.Count);

            foreach (var chest in chests) {
                writer.Write(chest.X);
                writer.Write(chest.Y);

                //写入物品数量
                writer.Write(chest.Items.Count);

                //逐个物品序列化成 TagCompound -> byte[]
                foreach (var itemTag in chest.Items) {
                    using MemoryStream itemStream = new MemoryStream();
                    TagIO.ToStream(itemTag, itemStream);
                    //转成二进制数据
                    byte[] itemBytes = itemStream.ToArray();
                    writer.Write(itemBytes.Length);
                    writer.Write(itemBytes);
                    itemStream.Flush();
                }
            }

            writer.Flush();
            return ms.ToArray();
        }
        /// <summary>
        /// 反序列化 byte[] 到 IList&lt;ChestSaveData&gt;
        /// </summary>
        public static IList<ChestSaveData> Deserialize(byte[] data) {
            var result = new List<ChestSaveData>();

            using var ms = new MemoryStream(data);
            using var reader = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);

            int count = reader.ReadInt32();
            for (int i = 0; i < count; i++) {
                var chest = new ChestSaveData {
                    X = reader.ReadInt16(),
                    Y = reader.ReadInt16(),
                    Items = []
                };

                int itemCount = reader.ReadInt32();
                for (int j = 0; j < itemCount; j++) {
                    int length = reader.ReadInt32();
                    byte[] itemBytes = reader.ReadBytes(length);

                    using var itemStream = new MemoryStream(itemBytes);
                    var itemTag = TagIO.FromStream(itemStream);
                    chest.Items.Add(itemTag);
                }

                result.Add(chest);
            }

            return result;
        }
    }

    /// <summary>
    /// 用于序列化/反序列化世界中的单个TP实体（TileProcessor）数据的轻量级值类型<br/>
    /// 注意，该结构并不维护物块数据，只存储TP实体内容，一般需要配合<see cref="TileSaveData"/>使用
    /// </summary>
    public struct TPSaveData
    {
        /// <summary>
        /// 相对坐标X
        /// </summary>
        public short X;
        /// <summary>
        /// 相对坐标Y
        /// </summary>
        public short Y;
        /// <summary>
        /// 实体内部数据
        /// </summary>
        public TagCompound Data;
        /// <summary>
        /// 保存一个TP实体的数据结构
        /// </summary>
        /// <param name="relX"></param>
        /// <param name="relY"></param>
        /// <param name="tileProcessor"></param>
        public TPSaveData(short relX, short relY, TileProcessor tileProcessor) {
            X = relX;
            Y = relY;
            Data = [];
            try {
                tileProcessor.SaveData(Data);
            } catch (Exception ex) {
                Data = [];
                VaultMod.LoggerError($"@TPSaveData-{tileProcessor.ID}", $"TPSaveData: " +
                    $"An error occurred while trying to save the TP {tileProcessor}: {ex.Message}");
            }
        }
        /// <summary>
        /// 将数据应用到世界，这会放置出一个TP实体
        /// </summary>
        public readonly void ApplyToWorld(short targetX, short targetY) {
            if (!WorldGen.InWorld(targetX, targetY)) {
                return;
            }

            Tile tile = Main.tile[targetX, targetY];
            TileProcessor tileProcessor = TileProcessorLoader.AddInWorld(tile.TileType, new Point16(targetX, targetY), null);
            if (tileProcessor != null && Data.Count > 0) {
                if (VaultUtils.isClient) {
                    TileProcessorNetWork.PlaceInWorldNetSend(VaultMod.Instance, tile.TileType, new Point16(targetX, targetY));
                }

                try {
                    tileProcessor.LoadData(Data);
                } catch (Exception ex) {
                    VaultMod.LoggerError($"@TPSaveData.ApplyToWorld-{tileProcessor.ID}", $"TPSaveData.ApplyToWorld: " +
                        $"An error occurred while trying to load the TP {tileProcessor}: {ex.Message}");
                }
            }
        }
        /// <summary>
        /// 序列化 TagCompound
        /// </summary>
        public readonly TagCompound ToTag() {
            return new TagCompound {
                ["a"] = X,
                ["b"] = Y,
                ["c"] = Data
            };
        }
        /// <summary>
        /// 反序列化 TagCompound
        /// </summary>
        public static TPSaveData FromTag(TagCompound tag) {
            return new TPSaveData {
                X = tag.GetShort("a"),
                Y = tag.GetShort("b"),
                Data = tag.GetCompound("c")
            };
        }
    }

    /// <summary>
    /// 用于封装一个区域内所有保存数据的结构<br/>
    /// 包含压缩后的物块数据以及对应的箱子与TP实体数据<br/>
    /// 提供TagCompound序列化与反序列化支持
    /// </summary>
    public struct RegionSaveData
    {
        /// <summary>
        /// 区域大小，描述该结构区域的宽度与高度
        /// </summary>
        public Point16 Size;
        /// <summary>
        /// 压缩后的物块数据
        /// 存储由TileSaveData序列化后的结果
        /// </summary>
        public byte[] Tiles;
        /// <summary>
        /// 箱子数据
        /// 存储区域内所有ChestSaveData的TagCompound表示
        /// </summary>
        public IList<TagCompound> Chests;
        /// <summary>
        /// TP数据
        /// 存储区域内所有TPSaveData的TagCompound表示
        /// </summary>
        public IList<TagCompound> TileProcessors;
        /// <summary>
        /// 构造函数
        /// 初始化一个包含物块、箱子和TP实体数据的结构
        /// </summary>
        /// <param name="size">区域大小</param>
        /// <param name="tiles">压缩后的物块数据</param>
        /// <param name="chests">箱子数据集合</param>
        /// <param name="tps">TP实体数据集合</param>
        public RegionSaveData(Point16 size, byte[] tiles, IList<TagCompound> chests, IList<TagCompound> tps) {
            Size = size;//区域大小
            Tiles = tiles;//物块数据
            Chests = chests;//箱子数据
            TileProcessors = tps;//TP数据
        }
        /// <summary>
        /// 将保存的区域数据应用到世界<br/>
        /// 会在指定目标位置放置物块、箱子和TP实体
        /// </summary>
        /// <param name="targetX">目标区域左上角的X坐标</param>
        /// <param name="targetY">目标区域左上角的Y坐标</param>
        /// <param name="clampToWorldBounds">是否自动根据<see cref="Size"/>调整目标位置，防止区域超出世界边界</param>
        public readonly void ApplyToWorld(short targetX, short targetY, bool clampToWorldBounds = true) {
            if (clampToWorldBounds) {
                int maxX = Main.maxTilesX - Size.X;
                int maxY = Main.maxTilesY - Size.Y;
                targetX = (short)Math.Clamp(targetX, 0, maxX);
                targetY = (short)Math.Clamp(targetY, 0, maxY);
            }

            //反序列化物块并放置
            var tilesList = TileSaveData.Deserialize(Tiles);
            SaveStructure.LoadTiles(tilesList, targetX, targetY);

            //反序列化并放置箱子
            foreach (var chestTag in Chests) {
                var chestData = ChestSaveData.FromTag(chestTag);
                chestData.ApplyToWorld((short)(targetX + chestData.X), (short)(targetY + chestData.Y));
            }

            //反序列化并放置TP实体
            foreach (var tpTag in TileProcessors) {
                var tpData = TPSaveData.FromTag(tpTag);
                tpData.ApplyToWorld((short)(targetX + tpData.X), (short)(targetY + tpData.Y));
            }
        }
        /// <summary>
        /// 将当前数据结构序列化为TagCompound
        /// 用于保存到文件或其他存储介质
        /// </summary>
        /// <returns>序列化后的TagCompound对象</returns>
        public readonly TagCompound ToTag() {
            return new TagCompound {
                ["size"] = Size, //区域大小
                ["tiles"] = Tiles, //物块数据
                ["chests"] = Chests, //箱子数据
                ["tps"] = TileProcessors //TP数据
            };
        }
        /// <summary>
        /// 从TagCompound反序列化为RegionSaveData
        /// </summary>
        /// <param name="tag">包含区域数据的TagCompound对象</param>
        /// <returns>反序列化得到的RegionSaveData</returns>
        public static RegionSaveData FromTag(TagCompound tag) {
            return new RegionSaveData(
                tag.GetPoint16("size"),//区域大小
                tag.GetByteArray("tiles"),//物块数据
                [.. tag.GetList<TagCompound>("chests")],//箱子数据
                [.. tag.GetList<TagCompound>("tps")]//TP数据
            );
        }
    }

    /// <summary>
    /// 将图格的数据存储为NBT，包含一些基本的图格存取工具
    /// </summary>
    public abstract class SaveStructure : SaveContent<SaveStructure>
    {
        /// <summary>
        /// 保存结构数据的基本路径，指引到对应模组文件夹
        /// </summary>
        public string StructurePath => Path.Combine(VaultSave.RootPath, "Structure", Mod.Name);
        /// <summary>
        /// 保存结构数据的路径
        /// </summary>
        public override string SavePath => Path.Combine(StructurePath, $"{Name}.nbt");
        /// <summary>
        /// 将整个世界复制保存为TagCompound
        /// </summary>
        /// <param name="tag"></param>
        public static void CopyWorld(TagCompound tag) {
            tag["span"] = new Point16(Main.spawnTileX, Main.spawnTileY);
            tag["tiles"] = TileSaveData.Serialize(SaveTiles());
            tag["chests"] = SaveChestsByTag();
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
            LoadTiles(TileSaveData.Deserialize(tag.GetByteArray("tiles")));
            LoadChestsByTag(tag.GetList<TagCompound>("chests"));
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
        /// <param name="tag">TagCompound数据</param>
        /// <param name="key">对应的key</param>
        /// <param name="targetX">放置的世界起点X</param>
        /// <param name="targetY">放置的世界起点Y</param>
        /// <param name="setProgress">进度回调</param>
        /// <param name="phaseStart">进度阶段起点</param>
        /// <param name="phaseEnd">进度阶段终点</param>
        public static void LoadTiles(TagCompound tag, string key, short targetX = 0, short targetY = 0,
            Action<float> setProgress = null, float phaseStart = 0, float phaseEnd = 100) =>
            LoadTiles(TileSaveData.Deserialize(tag.GetByteArray(key)), targetX, targetY, setProgress, phaseStart, phaseEnd);
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
                //应用到偏移后的坐标
                data.ApplyToWorld((short)(targetX + data.X), (short)(targetY + data.Y));

                if (i % 5000 == 0) {
                    setProgress?.Invoke(phaseStart + (phaseEnd - phaseStart) * (i / (float)count));
                }
            }

            TileSaveData.ForcePlaceTiles = false;
            TileSaveData.ForcePlaceWalls = false;
        }
        /// <summary>
        /// 保存指定区域内的所有箱子
        /// </summary>
        /// <param name="startX"></param>
        /// <param name="startY"></param>
        /// <param name="width"></param>
        /// <param name="height"></param>
        /// <returns></returns>
        public static IList<ChestSaveData> SaveChests(short startX = 0, short startY = 0, short width = 0, short height = 0) {
            var chests = new List<ChestSaveData>();
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
                //判断是否在指定范围内
                if (pos.X >= startX && pos.X < startX + width &&
                    pos.Y >= startY && pos.Y < startY + height) {

                    short relX = (short)(pos.X - startX);
                    short relY = (short)(pos.Y - startY);
                    chests.Add(new ChestSaveData(relX, relY, chest));
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
        public static void LoadChests(IList<ChestSaveData> chests, short targetX = 0, short targetY = 0,
            Action<float> setProgress = null, float phaseStart = 0, float phaseEnd = 100) {
            int count = chests.Count;
            for (int i = 0; i < count; i++) {
                var chestData = chests[i];
                chestData.ApplyToWorld((short)(targetX + chestData.X), (short)(targetY + chestData.Y));

                if (i % 50 == 0) { //箱子数量少，不需要太频繁
                    setProgress?.Invoke(phaseStart + (phaseEnd - phaseStart) * (i / (float)count));
                }
            }
        }
        /// <summary>
        /// 将给定的箱子数据在指定的位置放置出来
        /// </summary>
        /// <param name="tag">TagCompound数据</param>
        /// <param name="key">对应的key</param>
        /// <param name="targetX"></param>
        /// <param name="targetY"></param>
        /// <param name="setProgress"></param>
        /// <param name="phaseStart"></param>
        /// <param name="phaseEnd"></param>
        public static void LoadChestsByTag(TagCompound tag, string key, short targetX = 0, short targetY = 0,
            Action<float> setProgress = null, float phaseStart = 0, float phaseEnd = 100) =>
            LoadChestsByTag(tag.GetList<TagCompound>(key), targetX, targetY, setProgress, phaseStart, phaseEnd);
        /// <summary>
        /// 将给定的箱子数据在指定的位置放置出来
        /// </summary>
        /// <param name="chests"></param>
        /// <param name="targetX"></param>
        /// <param name="targetY"></param>
        /// <param name="setProgress"></param>
        /// <param name="phaseStart"></param>
        /// <param name="phaseEnd"></param>
        public static void LoadChestsByTag(IList<TagCompound> chests, short targetX = 0, short targetY = 0,
            Action<float> setProgress = null, float phaseStart = 0, float phaseEnd = 100) {
            int count = chests.Count;
            for (int i = 0; i < count; i++) {
                var chestData = ChestSaveData.FromTag(chests[i]);
                chestData.ApplyToWorld((short)(targetX + chestData.X), (short)(targetY + chestData.Y));

                if (i % 50 == 0) { //箱子数量少，不需要太频繁
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
        public static IList<TagCompound> SaveChestsByTag(short startX = 0, short startY = 0, short width = 0, short height = 0) {
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
                //判断是否在指定范围内
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
        /// 将给定的TP实体数据在指定的位置放置出来
        /// </summary>
        /// <param name="tag">TagCompound数据</param>
        /// <param name="key">对应的key</param>
        /// <param name="targetX"></param>
        /// <param name="targetY"></param>
        /// <param name="setProgress"></param>
        /// <param name="phaseStart"></param>
        /// <param name="phaseEnd"></param>
        public static void LoadTileProcessor(TagCompound tag, string key, short targetX = 0, short targetY = 0,
            Action<float> setProgress = null, float phaseStart = 0, float phaseEnd = 100) =>
            LoadTileProcessor(tag.GetList<TagCompound>(key), targetX, targetY, setProgress, phaseStart, phaseEnd);
        /// <summary>
        /// 将给定的TP实体数据在指定的位置放置出来
        /// </summary>
        /// <param name="tileProcessors"></param>
        /// <param name="targetX"></param>
        /// <param name="targetY"></param>
        /// <param name="setProgress"></param>
        /// <param name="phaseStart"></param>
        /// <param name="phaseEnd"></param>
        public static void LoadTileProcessor(IList<TagCompound> tileProcessors, short targetX = 0, short targetY = 0,
            Action<float> setProgress = null, float phaseStart = 0, float phaseEnd = 100) {
            int count = tileProcessors.Count;
            for (int i = 0; i < count; i++) {
                var tpData = TPSaveData.FromTag(tileProcessors[i]);
                tpData.ApplyToWorld((short)(targetX + tpData.X), (short)(targetY + tpData.Y));

                if (i % 50 == 0) { //箱子数量少，不需要太频繁
                    setProgress?.Invoke(phaseStart + (phaseEnd - phaseStart) * (i / (float)count));
                }
            }
        }
        /// <summary>
        /// 保存指定区域内的所有TP实体
        /// </summary>
        /// <param name="startX"></param>
        /// <param name="startY"></param>
        /// <param name="width"></param>
        /// <param name="height"></param>
        /// <returns></returns>
        public static IList<TagCompound> SaveTileProcessor(short startX = 0, short startY = 0, short width = 0, short height = 0) {
            var tileProcessors = new List<TagCompound>();
            if (width == 0) {
                width = (short)Main.maxTilesX;
            }
            if (height == 0) {
                height = (short)Main.maxTilesY;
            }

            for (int i = 0; i < TileProcessorLoader.TP_InWorld.Count; i++) {
                TileProcessor tileProcessor = TileProcessorLoader.TP_InWorld[i];
                if (tileProcessor == null) {
                    continue;
                }

                Point16 pos = tileProcessor.Position;
                //判断是否在指定范围内
                if (pos.X >= startX && pos.X < startX + width &&
                    pos.Y >= startY && pos.Y < startY + height) {

                    short relX = (short)(pos.X - startX);
                    short relY = (short)(pos.Y - startY);
                    tileProcessors.Add(new TPSaveData(relX, relY, tileProcessor).ToTag());
                }
            }

            return tileProcessors;
        }

        /// <summary>
        /// 将给定的区域数据在指定的位置放置出来
        /// </summary>
        /// <param name="tag">TagCompound数据</param>
        /// <param name="origin">起始坐标 左上角的世界物块位置</param>
        /// <param name="key">数据键，默认为 region</param>
        /// <param name="clampToWorldBounds">是否自动根据<see cref="RegionSaveData.Size"/>调整目标位置，防止区域超出世界边界</param>
        public static RegionSaveData LoadRegion(TagCompound tag, Point16 origin, string key = "region", bool clampToWorldBounds = true)
            => LoadRegion(tag.GetRegionSaveData(key), origin, clampToWorldBounds);

        /// <summary>
        /// 将给定的区域数据在指定的位置放置出来
        /// </summary>
        /// <param name="region">区域数据</param>
        /// <param name="origin">起始坐标 左上角的世界物块位置</param>
        /// <param name="clampToWorldBounds">是否自动根据<see cref="RegionSaveData.Size"/>调整目标位置，防止区域超出世界边界</param>
        public static RegionSaveData LoadRegion(RegionSaveData region, Point16 origin, bool clampToWorldBounds = true) {
            region.ApplyToWorld(origin.X, origin.Y, clampToWorldBounds);
            return region;
        }

        /// <summary>
        /// 将指定区域内的物块、箱子、TP实体数据进行序列化保存<br/>
        /// 返回一个包含三类数据的 <see cref="RegionSaveData"/> 结构
        /// </summary>
        /// <param name="tag">TagCompound数据</param>
        /// <param name="origin">起始坐标 左上角的世界物块位置</param>
        /// <param name="width">区域宽度 单位为Tile</param>
        /// <param name="height">区域高度 单位为Tile</param>
        /// <returns>包含序列化后数据的RegionSaveData结构</returns>
        public static RegionSaveData SaveRegion(TagCompound tag, Point16 origin, short width, short height) {
            var size = new Point16(width, height);
            var tiles = TileSaveData.Serialize(SaveTiles(origin.X, origin.Y, width, height));//序列化物块数据
            var chests = SaveChestsByTag(origin.X, origin.Y, width, height);//序列化箱子数据
            var tps = SaveTileProcessor(origin.X, origin.Y, width, height);//序列化TP实体数据
            RegionSaveData regionSaveData = new RegionSaveData(size, tiles, chests, tps);
            tag["region"] = regionSaveData.ToTag();
            return regionSaveData;//返回区域数据结构
        }

        /// <summary>
        /// 保存正方形区域数据
        /// </summary>
        /// <param name="tag">TagCompound数据</param>
        /// <param name="origin">起始坐标 左上角的世界位置</param>
        /// <param name="size">正方形边长 单位为Tile</param>
        public static RegionSaveData SaveRegion(TagCompound tag, Point16 origin, short size)
            => SaveRegion(tag, origin, size, size); //保存正方形区域

        /// <summary>
        /// 根据矩形区域保存数据
        /// </summary>
        /// <param name="tag">TagCompound数据</param>
        /// <param name="area">矩形区域 定义了左上角坐标和尺寸</param>
        public static RegionSaveData SaveRegion(TagCompound tag, Rectangle area)
            => SaveRegion(tag, new Point16(area.X, area.Y), (short)area.Width, (short)area.Height);//保存矩形区域

        /// <summary>
        /// 在指定范围内寻找一个安全的方块放置区域
        /// </summary>
        /// <param name="structureSize">结构尺寸（宽高，单位：方块）</param>
        /// <param name="startX">初始搜索中心X</param>
        /// <param name="startY">初始搜索中心Y</param>
        /// <param name="maxAttempts">最大尝试次数</param>
        /// <param name="horizontalRange">X方向最大偏移范围</param>
        /// <param name="verticalRange">Y方向最大偏移范围</param>
        /// <param name="isTileSafe">判定物块是否安全，默认判定模组方块为危险方块</param>
        /// <returns>安全放置点左上角坐标</returns>
        public static Point16 FindSafePlacement(Point16 structureSize, int startX, int startY, int maxAttempts = 300
            , int horizontalRange = 200, int verticalRange = 200, Func<Tile, bool> isTileSafe = null) {
            int attempts = 0;
            isTileSafe ??= (Tile tile) => tile.TileType < TileID.Count;
            //先判断一次，如果正常就直接返回
            if (IsAreaSafe(startX, startY, structureSize, isTileSafe)) {
                return new Point16(startX, startY);
            }

            while (attempts < maxAttempts) {
                //在范围内随机一个候选点
                int candidateX = startX + WorldGen.genRand.Next(-horizontalRange, horizontalRange + 1);
                int candidateY = startY + WorldGen.genRand.Next(-verticalRange, verticalRange + 1);

                //边界检查
                candidateX = Math.Clamp(candidateX, 0, Main.maxTilesX - structureSize.X);
                candidateY = Math.Clamp(candidateY, 0, Main.maxTilesY - structureSize.Y);

                if (IsAreaSafe(candidateX, candidateY, structureSize, isTileSafe)) {
                    return new Point16(candidateX, candidateY);
                }

                attempts++;
            }

            //如果找不到，兜底位置
            int fallbackX = Math.Clamp(startX, 0, Main.maxTilesX - structureSize.X);
            int fallbackY = Math.Clamp(startY - 50, 0, Main.maxTilesY - structureSize.Y);
            return new Point16(fallbackX, fallbackY);
        }

        /// <summary>
        /// 在指定范围内寻找一个安全的方块放置区域
        /// </summary>
        /// <param name="structureSize">结构尺寸（宽高，单位：方块）</param>
        /// <param name="startPoint">初始搜索中心</param>
        /// <param name="maxAttempts">最大尝试次数</param>
        /// <param name="horizontalRange">X方向最大偏移范围</param>
        /// <param name="verticalRange">Y方向最大偏移范围</param>
        /// <param name="isTileSafe">判定物块是否安全，默认判定模组方块为危险方块</param>
        /// <returns>安全放置点左上角坐标</returns>
        public static Point16 FindSafePlacement(Point16 structureSize, Point16 startPoint, int maxAttempts = 300
            , int horizontalRange = 200, int verticalRange = 200, Func<Tile, bool> isTileSafe = null)
            => FindSafePlacement(structureSize, startPoint.X, startPoint.Y, maxAttempts, horizontalRange, verticalRange, isTileSafe);

        /// <summary>
        /// 检查某个矩形区域是否安全
        /// </summary>
        private static bool IsAreaSafe(int startX, int startY, Point16 size, Func<Tile, bool> isTileSafe) {
            for (int i = 0; i < size.X; i++) {
                for (int j = 0; j < size.Y; j++) {
                    Tile tile = Framing.GetTileSafely(startX + i, startY + j);
                    if (isTileSafe.Invoke(tile)) {
                        continue;
                    }
                    return false;
                }
            }
            return true;
        }
    }
}
