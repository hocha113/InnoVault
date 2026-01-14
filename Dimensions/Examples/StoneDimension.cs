using System.Collections.Generic;
using Terraria;
using Terraria.ID;
using Terraria.IO;
using Terraria.WorldBuilding;

namespace InnoVault.Dimensions.Examples
{
    /// <summary>
    /// 石头维度 - 一个用于测试的简单维度示例
    /// <br/>整个世界由石头填充，中间有一个空洞作为出生点
    /// </summary>
    public class StoneDimension : Dimension
    {
        /// <inheritdoc/>
        public override int Width => 400;

        /// <inheritdoc/>
        public override int Height => 300;

        /// <inheritdoc/>
        public override List<GenPass> Tasks => [
            new StoneWorldGenPass("填充石头", 1f),
            new SpawnAreaGenPass("创建出生区域", 0.5f)
        ];

        /// <inheritdoc/>
        public override bool ShouldSave => false;

        /// <inheritdoc/>
        public override void OnEnter() {
            VaultUtils.Text("[石头维度] 欢迎来到石头世界！", Microsoft.Xna.Framework.Color.Gray);
        }

        /// <inheritdoc/>
        public override void OnExit() {
            VaultUtils.Text("[石头维度] 正在返回主世界...", Microsoft.Xna.Framework.Color.Gray);
        }

        /// <inheritdoc/>
        public override void OnLoad() {
            //设置出生点为世界中心
            Main.spawnTileX = Width / 2;
            Main.spawnTileY = Height / 2;
        }
    }

    /// <summary>
    /// 石头世界生成任务 - 用石头填充整个世界
    /// </summary>
    public class StoneWorldGenPass : GenPass
    {
        public StoneWorldGenPass(string name, float weight) : base(name, weight) { }

        protected override void ApplyPass(GenerationProgress progress, GameConfiguration configuration) {
            progress.Message = "正在生成石头世界...";

            for (int x = 0; x < Main.maxTilesX; x++) {
                for (int y = 0; y < Main.maxTilesY; y++) {
                    //设置进度
                    progress.Set((x * Main.maxTilesY + y) / (float)(Main.maxTilesX * Main.maxTilesY));

                    Tile tile = Main.tile[x, y];
                    tile.HasTile = true;
                    tile.TileType = TileID.Stone;
                    tile.WallType = WallID.Stone;

                    //顶部区域不放墙
                    if (y < 50) {
                        tile.WallType = WallID.None;
                    }
                }
            }
        }
    }

    /// <summary>
    /// 出生区域生成任务 - 在世界中心创建一个空洞
    /// </summary>
    public class SpawnAreaGenPass : GenPass
    {
        public SpawnAreaGenPass(string name, float weight) : base(name, weight) { }

        protected override void ApplyPass(GenerationProgress progress, GameConfiguration configuration) {
            progress.Message = "正在创建出生区域...";

            int centerX = Main.maxTilesX / 2;
            int centerY = Main.maxTilesY / 2;
            int roomWidth = 30;
            int roomHeight = 20;

            //清空中心区域作为出生房间
            for (int x = centerX - roomWidth / 2; x < centerX + roomWidth / 2; x++) {
                for (int y = centerY - roomHeight / 2; y < centerY + roomHeight / 2; y++) {
                    progress.Set((x - (centerX - roomWidth / 2)) / (float)roomWidth);

                    if (x >= 0 && x < Main.maxTilesX && y >= 0 && y < Main.maxTilesY) {
                        Tile tile = Main.tile[x, y];
                        tile.HasTile = false;
                        tile.WallType = WallID.Stone;
                        tile.LiquidAmount = 0;
                    }
                }
            }

            //放置地板
            int floorY = centerY + roomHeight / 2 - 1;
            for (int x = centerX - roomWidth / 2; x < centerX + roomWidth / 2; x++) {
                if (x >= 0 && x < Main.maxTilesX && floorY >= 0 && floorY < Main.maxTilesY) {
                    Tile tile = Main.tile[x, floorY];
                    tile.HasTile = true;
                    tile.TileType = TileID.Platforms;
                }
            }

            //放置火把提供光源
            int torchY = centerY - 2;
            if (centerX - 5 >= 0 && torchY >= 0) {
                WorldGen.PlaceTile(centerX - 5, torchY, TileID.Torches);
            }
            if (centerX + 5 < Main.maxTilesX && torchY >= 0) {
                WorldGen.PlaceTile(centerX + 5, torchY, TileID.Torches);
            }

            //设置出生点
            Main.spawnTileX = centerX;
            Main.spawnTileY = floorY - 1;
        }
    }
}
