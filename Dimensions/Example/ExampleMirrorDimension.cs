using Microsoft.Xna.Framework;
using System.Collections.Generic;
using Terraria;
using Terraria.GameContent.Generation;
using Terraria.IO;
using Terraria.Utilities;
using Terraria.WorldBuilding;

namespace InnoVault.Dimensions.Example
{
    /// <summary>
    /// 示例维度，先瞎几把搓一个，能跑就行，镜像维度
    /// <para>一个时间倒流、重力反转的平行世界</para>
    /// </summary>
    public class ExampleMirrorDimension : Dimension
    {
        #region 基础属性

        public override int Width => 4200; //中等大小世界

        public override int Height => 1200;

        public override DimensionLayerEnum Layer => DimensionLayerEnum.Parallel;

        public override bool LoadLocalized => false;//别加载本地化

        #endregion

        #region 环境设置

        private DimensionEnvironment _environment;
        public override DimensionEnvironment Environment {
            get {
                if (_environment == null) {
                    _environment = new DimensionEnvironment {
                        ColorTint = new Color(0.8f, 0.8f, 1.0f), //淡蓝色调
                        FogDensity = 0.01f,
                        FogColor = new Color(100, 100, 150, 128),
                        AmbientParticles = new List<int> { 15, 16, 68 }, //闪光粒子
                        ParticleSpawnRate = 0.02f,
                        ShowStars = true,
                        ShowCelestialBodies = true
                    };
                }
                return _environment;
            }
        }

        public override bool HideUnderworldBackground => true;

        #endregion

        #region 时间和重力

        public override float TimeScale => -0.5f; //时间倒流,速度为正常的一半

        public override bool EnableTimeOfDay => true;

        public override bool EnableWeather => false; //禁用天气

        public override float GetGravityMultiplier(Entity entity) {
            return 1.0f;
        }

        #endregion

        #region 世界生成

        public override List<GenPass> GenerationTasks {
            get {
                List<GenPass> tasks = new List<GenPass>();

                //基础地形生成
                tasks.Add(new PassLegacy("镜像地形", GenerateTerrain));

                //装饰
                tasks.Add(new PassLegacy("镜像装饰", GenerateDecoration));

                return tasks;
            }
        }

        /// <summary>
        /// 生成镜像地形
        /// </summary>
        private void GenerateTerrain(GenerationProgress progress, GameConfiguration configuration) {
            progress.Message = "创建镜像世界...";

            //生成平坦地面
            for (int x = 0; x < Width; x++) {
                for (int y = Height / 2; y < Height; y++) {
                    WorldGen.PlaceTile(x, y, 1, true, true); //石头
                }

                progress.Set((float)x / Width);
            }
        }

        /// <summary>
        /// 生成装饰
        /// </summary>
        private void GenerateDecoration(GenerationProgress progress, GameConfiguration configuration) {
            progress.Message = "添加镜像装饰...";

            //添加一些发光的晶体
            for (int i = 0; i < Width * 2; i++) {
                int x = WorldGen.genRand.Next(0, Width);
                int y = WorldGen.genRand.Next(Height / 2, Height);

                if (Main.tile[x, y].HasTile) {
                    WorldGen.PlaceTile(x, y - 1, 63, true, true); //蓝宝石
                }

                progress.Set((float)i / (Width * 2));
            }
        }

        #endregion

        #region 维度规则

        public override List<DimensionRule> SpecialRules {
            get {
                return new List<DimensionRule>
                {
                    new MirrorEffectRule()
                };
            }
        }

        /// <summary>
        /// 镜像效果规则
        /// </summary>
        private class MirrorEffectRule : DimensionRule
        {
            public override string Name => "镜像效果";

            public override void Apply() {
                //应用镜像效果(例如翻转某些UI元素)
            }

            public override void Remove() {
                //移除镜像效果
            }
        }

        #endregion

        #region 生命周期

        public override void OnEnter() {
            Main.NewText("你进入了镜像维度,时间在这里倒流...", Color.Cyan);
        }

        public override void OnExit() {
            Main.NewText("你离开了镜像维度", Color.Gray);
        }

        public override void OnLoad() {
            //设置出生点在世界中央偏上
            Main.spawnTileX = Width / 2;
            Main.spawnTileY = Height / 2 - 10;
        }

        public override void Update() {
            //每隔一段时间生成环境粒子
            if (Main.GameUpdateCount % 30 == 0) {
                DimensionUtils.SpawnAmbientParticles(this);
            }
        }

        public override void PostUpdate() {
            //检查玩家是否触底(由于重力反转,玩家可能飞向天空)
            Player player = Main.LocalPlayer;
            if (player.position.Y < 0) {
                player.position.Y = 0;
                player.velocity.Y = 0;
            }
        }

        #endregion

        #region 玩家交互

        public override void OnPlayerEnter(Player player) {
            //给予玩家适应时间的buff
            player.AddBuff(11, 600); //发光buff 10秒
        }

        public override void OnPlayerLeave(Player player) {
            //移除重力效果
            player.gravity = Player.defaultGravity;
        }

        public override bool OnPlayerDeath(Player player) {
            //玩家死亡时自动返回主世界
            Main.NewText("镜像维度排斥了你的灵魂...", Color.Red);
            DimensionLoader.Exit();
            return true; //自定义死亡处理
        }

        public override bool CanEnter(Player player) {
            //检查玩家是否有特定物品
            //return player.HasItem(ItemID.SuspiciousLookingEye);
            return true; //示例中允许所有玩家进入
        }

        #endregion

        #region 数据传输

        public override void CopyFromMainWorld() {
            //从主世界复制某些数据
            DimensionLoader.CopyData("ExampleBossDefeated", NPC.downedBoss1);
        }

        public override void ReadMainWorldData() {
            //读取从主世界复制的数据
            bool bossDefeated = DimensionLoader.ReadData<bool>("ExampleBossDefeated");

            if (bossDefeated) {
                //基于主世界进度调整维度
            }
        }

        public override void CopyDimensionData() {
            //复制维度数据传回主世界
            DimensionLoader.CopyData("MirrorExplorationProgress", 0.5f);
        }

        public override void ReadDimensionData() {
            //读取维度数据
            float progress = DimensionLoader.ReadData<float>("MirrorExplorationProgress");
        }

        #endregion

        #region 自定义光照

        public override bool CustomLighting(Tile tile, int x, int y, ref FastRandom rand, ref Vector3 color) {
            //在镜像维度中,所有方块都有微弱的蓝色光晕
            if (tile.HasTile) {
                color.X += 0.1f;
                color.Y += 0.1f;
                color.Z += 0.2f;
                return true; //使用自定义光照
            }

            return false; //使用默认光照
        }

        #endregion

        #region 音频

        public override bool CustomAudioControl => true;

        public override int GetMusicID() {
            //返回自定义音乐ID(需要先注册音乐)
            //return MusicLoader.GetMusicSlot(Mod, "Sounds/Music/MirrorDimension");
            return -1;
        }

        #endregion
    }
}
