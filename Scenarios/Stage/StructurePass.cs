using InnoVault.GameSystem;
using Microsoft.Xna.Framework;
using System;
using System.IO;
using Terraria;
using Terraria.DataStructures;
using Terraria.IO;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using Terraria.WorldBuilding;

namespace InnoVault.Scenarios
{
    /// <summary>
    /// 现成的生成步骤：把一份 SaveStructure 区域数据放到舞台的指定位置，不写 <see cref="GenPass"/> 也能做出一块舞台<br/>
    /// 区域数据可以用 <see cref="Load(Mod, string, string)"/> 从模组文件读取（<c>SaveStructure.SaveRegion</c> 存下的 TagCompound）
    /// </summary>
    public class StructurePass : GenPass
    {
        private readonly Func<RegionSaveData> _source;

        /// <summary>放置一份已经读好的区域数据</summary>
        /// <param name="structure">区域数据</param>
        /// <param name="origin">放置的左上角（物块坐标）</param>
        /// <param name="name">生成步骤名</param>
        /// <param name="weight">进度权重</param>
        public StructurePass(RegionSaveData structure, Point origin, string name = "InnoVault: Scenario Structure", double weight = 1.0)
            : this(() => structure, origin, name, weight) { }

        /// <summary>放置一份生成时才读取的区域数据（只在需要生成时读文件）</summary>
        /// <param name="source">读取区域数据</param>
        /// <param name="origin">放置的左上角（物块坐标）</param>
        /// <param name="name">生成步骤名</param>
        /// <param name="weight">进度权重</param>
        public StructurePass(Func<RegionSaveData> source, Point origin, string name = "InnoVault: Scenario Structure", double weight = 1.0)
            : base(name, weight) {
            _source = source ?? throw new ArgumentNullException(nameof(source));
            Origin = origin;
        }

        /// <summary>放置的左上角（物块坐标）；结构超出舞台时向内挪</summary>
        public Point Origin { get; }

        /// <summary>放置后写入 <c>Main.spawnTileX/Y</c> 的舞台出生点（物块坐标），为空表示不改</summary>
        public Point? Spawn { get; init; }

        /// <summary>放置完成后调用，参数是结构实际占据的范围（物块坐标），可以在这里写锚点</summary>
        public Action<Rectangle> Placed { get; init; }

        /// <summary>生成进度界面上的说明文字，为空时用步骤名</summary>
        public string Message { get; init; }

        /// <summary>
        /// 从模组文件读取区域数据：文件是 <see cref="TagIO"/> 存下的 TagCompound，<paramref name="key"/> 下是区域数据；
        /// 没有这个键时把根标签本身当作区域数据
        /// </summary>
        /// <param name="mod">文件所在的模组</param>
        /// <param name="path">模组内的文件路径</param>
        /// <param name="key">区域数据的键，缺省 <c>region</c>（与 <c>SaveStructure.SaveRegion</c> 一致）</param>
        public static RegionSaveData Load(Mod mod, string path, string key = "region") {
            ArgumentNullException.ThrowIfNull(mod);
            using Stream stream = mod.GetFileStream(path) ?? throw new FileNotFoundException($"{mod.Name} has no file {path}");
            TagCompound root = TagIO.FromStream(stream);
            return RegionSaveData.FromTag(root.ContainsKey(key) ? root.GetCompound(key) : root);
        }

        /// <inheritdoc/>
        protected override void ApplyPass(GenerationProgress progress, GameConfiguration configuration) {
            progress.Message = Message ?? Name;
            RegionSaveData structure = _source();
            int x = Math.Clamp(Origin.X, 0, Math.Max(0, Main.maxTilesX - structure.Size.X));
            int y = Math.Clamp(Origin.Y, 0, Math.Max(0, Main.maxTilesY - structure.Size.Y));
            SaveStructure.LoadRegion(structure, new Point16(x, y), clampToWorldBounds: false);
            progress.Set(1.0);
            if (Spawn is Point spawn) {
                Main.spawnTileX = spawn.X;
                Main.spawnTileY = spawn.Y;
            }
            Placed?.Invoke(new Rectangle(x, y, structure.Size.X, structure.Size.Y));
        }
    }
}
