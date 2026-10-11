using InnoVault.GameSystem;
using InnoVault.TileProcessors;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;

namespace InnoVault.Scenarios
{
    //区域还原的执行者。快照是一个 TagCompound，内存里与还原日志里是同一份：
    //物块数据按列整段拷贝每种已注册的 ITileData（tML 的物块数据每种一个数组，下标 y + x * Height，同一列连续），
    //模组物块与墙另记一张 ID 到全名的表，读回时按全名重新解析；箱子、TP 沿用 SaveStructure 的格式，告示牌与物块实体自己记
    internal sealed class TmlRestoreBackend : IScenarioRestoreBackend
    {
        public void Capture(ScenarioRestore restore, ScenarioTransition transition) {
            Rectangle rect = ClipToWorld(restore.ResolveBounds());
            if (rect.Width <= 0 || rect.Height <= 0) {
                ScenarioPlatform.Error($"Scenario.Restore.{restore.Scenario?.Key}", $"[Scenarios] {restore.Scenario?.Key}: restore bounds {restore.ResolveBounds()} are outside the stage; nothing recorded");
                return;
            }
            TagCompound snapshot;
            try {
                snapshot = ScenarioRestoreSnapshot.Capture(rect);
            } catch (Exception ex) {
                ScenarioPlatform.Error($"Scenario.Restore.{restore.Scenario?.Key}", $"[Scenarios] {restore.Scenario?.Key}: recording the restore area failed; it will not be restored: {ex}");
                return;
            }
            restore.Snapshot = snapshot;
            ScenarioStage stage = ScenarioDirector.Stage?.Stage;
            bool transientStage = stage is { BindsRootWorld: false, Persistence: StagePersistence.Transient };
            if (restore.Journal && !transientStage) {
                restore.JournalPath = ScenarioRestoreJournal.Write(restore.Scenario?.Key, snapshot);
            }
            if (restore.Structure is RegionSaveData structure) {
                Point origin = restore.StructureOrigin ?? rect.Location;
                using (ScenarioProtection.Bypass()) {
                    SaveStructure.LoadRegion(structure, new Point16(origin.X, origin.Y), clampToWorldBounds: false);
                    ScenarioRestoreSnapshot.RefreshEdges(new Rectangle(origin.X, origin.Y, structure.Size.X, structure.Size.Y));
                }
            }
        }

        public void Restore(ScenarioRestore restore, ScenarioTransition transition) {
            if (restore.Snapshot is not TagCompound snapshot) {
                return;
            }
            restore.Snapshot = null;
            //舞台已经存过盘：内存里写回不会进存档，日志留给下次读入这块舞台时补还原
            if (transition.StageAlreadySaved || Main.gameMenu) {
                return;
            }
            try {
                ScenarioRestoreSnapshot.Apply(snapshot);
            } catch (Exception ex) {
                ScenarioPlatform.Error($"Scenario.Restore.{restore.Scenario?.Key}", $"[Scenarios] {restore.Scenario?.Key}: writing the restore area back failed; the journal is kept for the next load: {ex}");
                return;
            }
            ScenarioRestoreJournal.MarkRestored(restore.JournalPath);
            restore.JournalPath = null;
        }

        //读档线程：各模组 LoadWorldData 之后、舞台场景激活之前。按记录的逆序写回，最早的那份（真正的原貌）最后落地
        internal static void RecoverJournals() {
            foreach (string path in ScenarioRestoreJournal.Pending(ScenarioRestoreJournal.CurrentWorldId())) {
                try {
                    TagCompound snapshot = TagIO.FromFile(path);
                    ScenarioRestoreSnapshot.Apply(snapshot);
                    ScenarioPlatform.Info($"[Scenarios] Restored an area from the journal {Path.GetFileName(path)}");
                } catch (Exception ex) {
                    ScenarioPlatform.Error("Scenario.Restore.Recover", $"[Scenarios] Recovering {path} failed; the journal is discarded: {ex}");
                }
                ScenarioRestoreJournal.TryDelete(path);
            }
        }

        //舞台存盘时（原版物块已经写完）：已经写回内存的区域进了存档，它们的日志不再需要
        internal static void OnStageSaved() => ScenarioRestoreJournal.DeleteRestored(ScenarioRestoreJournal.CurrentWorldId());

        private static Rectangle ClipToWorld(Rectangle rect) => Rectangle.Intersect(rect, new Rectangle(0, 0, Main.maxTilesX, Main.maxTilesY));
    }

    //还原日志：VaultSave/Scenarios/Restore/<世界 GUID>/<记录时刻>_<场景>.restore；写回后改名为 .restored，等下次存盘再删除
    internal static class ScenarioRestoreJournal
    {
        private const string ActiveExtension = ".restore";
        private const string RestoredExtension = ".restored";

        private static string Root => Path.Combine(VaultSave.RootPath, "Scenarios", "Restore");

        //世界 GUID；没有 GUID 的老世界按路径派生，与换舞台后端认根世界的方式一致
        internal static Guid CurrentWorldId() {
            Terraria.IO.WorldFileData data = Main.ActiveWorldFileData;
            if (data == null) {
                return Guid.Empty;
            }
            return data.UniqueId != Guid.Empty
                ? data.UniqueId
                : new Guid(MD5.HashData(Encoding.UTF8.GetBytes(data.Path ?? data.Name ?? string.Empty)));
        }

        internal static string Write(string key, TagCompound snapshot) {
            try {
                string directory = Path.Combine(Root, CurrentWorldId().ToString("N"));
                Directory.CreateDirectory(directory);
                string path = Path.Combine(directory, $"{DateTime.UtcNow.Ticks:D19}_{TmlStageBackend.SafeFileName(key ?? "scenario")}{ActiveExtension}");
                TagIO.ToFile(snapshot, path);
                return path;
            } catch (Exception ex) {
                ScenarioPlatform.Error("Scenario.Restore.Journal", $"[Scenarios] Writing the restore journal failed; a crash during the scenario would keep its changes: {ex.Message}");
                return null;
            }
        }

        internal static void MarkRestored(string path) {
            if (path == null || !File.Exists(path)) {
                return;
            }
            try {
                File.Move(path, Path.ChangeExtension(path, RestoredExtension), overwrite: true);
            } catch (Exception ex) {
                ScenarioPlatform.Error("Scenario.Restore.Journal", $"[Scenarios] Marking {path} as restored failed: {ex.Message}");
            }
        }

        //按记录时刻从新到旧
        internal static IEnumerable<string> Pending(Guid worldId) {
            string directory = Path.Combine(Root, worldId.ToString("N"));
            if (!Directory.Exists(directory)) {
                return [];
            }
            return Directory.GetFiles(directory)
                .Where(static p => p.EndsWith(ActiveExtension, StringComparison.OrdinalIgnoreCase) || p.EndsWith(RestoredExtension, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(static p => Path.GetFileName(p), StringComparer.Ordinal)
                .ToArray();
        }

        internal static void DeleteRestored(Guid worldId) {
            string directory = Path.Combine(Root, worldId.ToString("N"));
            if (!Directory.Exists(directory)) {
                return;
            }
            foreach (string path in Directory.GetFiles(directory, "*" + RestoredExtension)) {
                TryDelete(path);
            }
        }

        internal static void DeleteFor(Guid worldId) {
            string directory = Path.Combine(Root, worldId.ToString("N"));
            try {
                if (Directory.Exists(directory)) {
                    Directory.Delete(directory, recursive: true);
                }
            } catch (Exception ex) {
                ScenarioPlatform.Error("Scenario.Restore.Journal", $"[Scenarios] Deleting {directory} failed: {ex.Message}");
            }
        }

        internal static void TryDelete(string path) {
            try {
                if (File.Exists(path)) {
                    File.Delete(path);
                }
            } catch (Exception ex) {
                ScenarioPlatform.Error($"Scenario.Delete.{path}", $"[Scenarios] Failed to delete {path}: {ex.Message}");
            }
        }
    }

    internal static class ScenarioRestoreSnapshot
    {
        internal static TagCompound Capture(Rectangle rect) {
            List<TagCompound> data = [];
            foreach (ITileDataColumns columns in TileDataRegistry.All()) {
                data.Add(new TagCompound {
                    ["type"] = columns.TypeName,
                    ["size"] = columns.ElementSize,
                    ["bytes"] = columns.Capture(rect),
                });
            }
            short x = (short)rect.X, y = (short)rect.Y, w = (short)rect.Width, h = (short)rect.Height;
            return new TagCompound {
                ["x"] = rect.X,
                ["y"] = rect.Y,
                ["w"] = rect.Width,
                ["h"] = rect.Height,
                ["data"] = data,
                ["tileNames"] = ModTypeNames(rect, walls: false),
                ["wallNames"] = ModTypeNames(rect, walls: true),
                ["chests"] = SaveStructure.SaveChestsByTag(x, y, w, h).ToList(),
                ["signs"] = SaveStructure.SaveSignsByTag(x, y, w, h).ToList(),
                ["entities"] = SaveStructure.SaveTileEntitiesByTag(x, y, w, h).ToList(),
                ["tps"] = SaveStructure.SaveTileProcessor(x, y, w, h).ToList(),
            };
        }

        internal static void Apply(TagCompound snapshot) {
            Rectangle rect = new(snapshot.GetInt("x"), snapshot.GetInt("y"), snapshot.GetInt("w"), snapshot.GetInt("h"));
            if (rect.Width <= 0 || rect.Height <= 0 || rect.Left < 0 || rect.Top < 0 || rect.Right > Main.maxTilesX || rect.Bottom > Main.maxTilesY) {
                throw new InvalidDataException($"restore area {rect} does not fit the current stage ({Main.maxTilesX}x{Main.maxTilesY})");
            }
            using (ScenarioProtection.Bypass()) {
                RemoveChests(rect);
                RemoveSigns(rect);
                RemoveTileEntities(rect);
                RemoveTileProcessors(rect);
                foreach (TagCompound entry in snapshot.GetList<TagCompound>("data")) {
                    string type = entry.GetString("type");
                    ITileDataColumns columns = TileDataRegistry.Find(type);
                    if (columns == null || columns.ElementSize != entry.GetInt("size")) {
                        ScenarioPlatform.Info($"[Scenarios] Restore: tile data {type} is not loaded or changed size; that layer is left as is");
                        continue;
                    }
                    columns.Restore(rect, entry.GetByteArray("bytes"));
                }
                RemapModTypes(rect, snapshot.GetList<TagCompound>("tileNames"), snapshot.GetList<TagCompound>("wallNames"));
                SaveStructure.LoadChestsByTag(snapshot.GetList<TagCompound>("chests"), (short)rect.X, (short)rect.Y);
                SaveStructure.LoadSignsByTag(snapshot.GetList<TagCompound>("signs"), (short)rect.X, (short)rect.Y);
                SaveStructure.LoadTileEntitiesByTag(snapshot.GetList<TagCompound>("entities"), (short)rect.X, (short)rect.Y);
                SaveStructure.LoadTileProcessor(snapshot.GetList<TagCompound>("tps"), (short)rect.X, (short)rect.Y);
                RefreshEdges(rect);
            }
            if (!Main.gameMenu && !Main.dedServ) {
                RefreshMap(rect);
                Unstick(Main.LocalPlayer, rect);
            }
        }

        //重新取帧：区域边缘一圈连同外侧邻格（它们曾经按改建后的样子取过帧），以及有液体的边缘格重新进入液体结算
        internal static void RefreshEdges(Rectangle rect) {
            for (int x = rect.Left; x < rect.Right; x++) {
                RefreshCell(x, rect.Top);
                RefreshCell(x, rect.Bottom - 1);
            }
            for (int y = rect.Top + 1; y < rect.Bottom - 1; y++) {
                RefreshCell(rect.Left, y);
                RefreshCell(rect.Right - 1, y);
            }
        }

        private static void RefreshCell(int x, int y) {
            if (!WorldGen.InWorld(x, y, 1)) {
                return;
            }
            WorldGen.SquareTileFrame(x, y, resetFrame: false);
            WorldGen.SquareWallFrame(x, y, resetFrame: false);
            if (Main.tile[x, y].LiquidAmount > 0) {
                Liquid.AddWater(x, y);
            }
        }

        private static void RefreshMap(Rectangle rect) {
            if (Main.Map == null) {
                return;
            }
            for (int x = rect.Left; x < rect.Right; x++) {
                for (int y = rect.Top; y < rect.Bottom; y++) {
                    Main.Map.UpdateType(x, y);
                }
            }
            Main.refreshMap = true;
        }

        //写回后玩家被埋在物块里时往上挪到第一个空位
        private static void Unstick(Player player, Rectangle rect) {
            if (player == null || !player.active) {
                return;
            }
            Rectangle covered = new((int)(player.position.X / 16f), (int)(player.position.Y / 16f), player.width / 16 + 2, player.height / 16 + 2);
            if (!rect.Intersects(covered)) {
                return;
            }
            Vector2 position = player.position;
            for (int step = 0; step < 200 && Collision.SolidCollision(position, player.width, player.height); step++) {
                position.Y -= 16f;
            }
            if (position != player.position) {
                ScenarioPlacement.PlaceAt(player, position);
            }
        }

        private static List<TagCompound> ModTypeNames(Rectangle rect, bool walls) {
            Dictionary<int, string> names = [];
            for (int x = rect.Left; x < rect.Right; x++) {
                for (int y = rect.Top; y < rect.Bottom; y++) {
                    Tile tile = Main.tile[x, y];
                    if (walls) {
                        int wall = tile.WallType;
                        if (wall >= WallID.Count && !names.ContainsKey(wall) && WallLoader.GetWall(wall) is ModWall modWall) {
                            names[wall] = modWall.FullName;
                        }
                    }
                    else if (tile.HasTile) {
                        int type = tile.TileType;
                        if (type >= TileID.Count && !names.ContainsKey(type) && TileLoader.GetTile(type) is ModTile modTile) {
                            names[type] = modTile.FullName;
                        }
                    }
                }
            }
            return names.Select(static pair => new TagCompound { ["id"] = pair.Key, ["name"] = pair.Value }).ToList();
        }

        //记录时的模组类型 ID 按全名重新解析（日志可能来自上一次启动，模组列表可能变了）；找不到的那几格置空
        private static void RemapModTypes(Rectangle rect, IList<TagCompound> tileNames, IList<TagCompound> wallNames) {
            Dictionary<int, int> tiles = BuildRemap(tileNames, static name => ModContent.TryFind(name, out ModTile tile) ? tile.Type : -1);
            Dictionary<int, int> walls = BuildRemap(wallNames, static name => ModContent.TryFind(name, out ModWall wall) ? wall.Type : -1);
            if (tiles == null && walls == null) {
                return;
            }
            int cleared = 0;
            for (int x = rect.Left; x < rect.Right; x++) {
                for (int y = rect.Top; y < rect.Bottom; y++) {
                    Tile tile = Main.tile[x, y];
                    if (tiles != null && tile.HasTile && tiles.TryGetValue(tile.TileType, out int tileType)) {
                        if (tileType < 0) {
                            tile.HasTile = false;
                            cleared++;
                        }
                        else {
                            tile.TileType = (ushort)tileType;
                        }
                    }
                    if (walls != null && walls.TryGetValue(tile.WallType, out int wallType)) {
                        tile.WallType = (ushort)Math.Max(wallType, 0);
                    }
                }
            }
            if (cleared > 0) {
                ScenarioPlatform.Info($"[Scenarios] Restore: {cleared} tiles of mods that are no longer loaded were cleared");
            }
        }

        //全部映射到自己时返回空，省掉逐格遍历
        private static Dictionary<int, int> BuildRemap(IList<TagCompound> names, Func<string, int> resolve) {
            Dictionary<int, int> remap = null;
            foreach (TagCompound entry in names) {
                int id = entry.GetInt("id");
                int current = resolve(entry.GetString("name"));
                if (current != id) {
                    (remap ??= [])[id] = current;
                }
            }
            return remap;
        }

        private static void RemoveChests(Rectangle rect) {
            for (int i = 0; i < Main.chest.Length; i++) {
                Chest chest = Main.chest[i];
                if (chest != null && rect.Contains(chest.x, chest.y)) {
                    Chest.DestroyChestDirect(chest.x, chest.y, i);
                }
            }
        }

        private static void RemoveSigns(Rectangle rect) {
            for (int i = 0; i < Main.sign.Length; i++) {
                if (Main.sign[i] is Sign sign && rect.Contains(sign.x, sign.y)) {
                    Main.sign[i] = null;
                }
            }
        }

        //只从表里移除，不调用 OnKill：这是把时间倒回去，不是把东西拆掉
        private static void RemoveTileEntities(Rectangle rect) {
            List<Point16> positions = [];
            foreach (Point16 position in TileEntity.ByPosition.Keys) {
                if (rect.Contains(position.X, position.Y)) {
                    positions.Add(position);
                }
            }
            foreach (Point16 position in positions) {
                if (TileEntity.ByPosition.Remove(position, out TileEntity entity)) {
                    TileEntity.ByID.Remove(entity.ID);
                }
            }
        }

        private static void RemoveTileProcessors(Rectangle rect) {
            List<TileProcessor> doomed = [];
            foreach (TileProcessor processor in TileProcessorLoader.TP_InWorld) {
                if (processor != null && processor.Active && rect.Contains(processor.Position.X, processor.Position.Y)) {
                    doomed.Add(processor);
                }
            }
            foreach (TileProcessor processor in doomed) {
                processor.Kill();
            }
        }
    }

    //一种 ITileData 的按列拷贝。Tile.Get<T>() 是公开接口；只有"有哪些已注册的种类"要靠反射 tML 内部的 TileData
    internal interface ITileDataColumns
    {
        string TypeName { get; }

        int ElementSize { get; }

        byte[] Capture(Rectangle rect);

        void Restore(Rectangle rect, byte[] bytes);
    }

    internal sealed class TileDataColumns<T> : ITileDataColumns where T : unmanaged, ITileData
    {
        public string TypeName => typeof(T).FullName;

        public int ElementSize => Unsafe.SizeOf<T>();

        public byte[] Capture(Rectangle rect) {
            T[] buffer = new T[rect.Width * rect.Height];
            int k = 0;
            for (int x = rect.Left; x < rect.Right; x++) {
                for (int y = rect.Top; y < rect.Bottom; y++) {
                    buffer[k++] = Main.tile[x, y].Get<T>();
                }
            }
            return MemoryMarshal.AsBytes(buffer.AsSpan()).ToArray();
        }

        public void Restore(Rectangle rect, byte[] bytes) {
            ReadOnlySpan<T> values = MemoryMarshal.Cast<byte, T>(bytes);
            if (values.Length != rect.Width * rect.Height) {
                throw new InvalidDataException($"{TypeName}: {values.Length} cells recorded for a {rect.Width}x{rect.Height} area");
            }
            int k = 0;
            for (int x = rect.Left; x < rect.Right; x++) {
                for (int y = rect.Top; y < rect.Bottom; y++) {
                    Main.tile[x, y].Get<T>() = values[k++];
                }
            }
        }
    }

    internal static class TileDataRegistry
    {
        private static readonly FieldInfo _onCopySingle = typeof(Main).Assembly.GetType("Terraria.TileData")?
            .GetField("OnCopySingle", BindingFlags.NonPublic | BindingFlags.Static);
        private static readonly Type[] _vanilla = [
            typeof(TileTypeData), typeof(WallTypeData), typeof(LiquidData), typeof(TileWallWireStateData), typeof(TileWallBrightnessInvisibilityData),
        ];
        private static readonly Dictionary<Type, ITileDataColumns> _columns = [];
        private static bool _fallbackLogged;

        internal static bool CanEnumerate => _onCopySingle != null;

        //已注册的种类：TileData<T> 的静态构造把自己的 CopySingle 挂到 TileData.OnCopySingle 上，委托列表就是全部种类
        internal static IReadOnlyList<ITileDataColumns> All() {
            List<ITileDataColumns> all = [];
            foreach (Type type in RegisteredTypes()) {
                if (Get(type) is ITileDataColumns columns) {
                    all.Add(columns);
                }
            }
            return all;
        }

        internal static ITileDataColumns Find(string typeName) {
            foreach (ITileDataColumns columns in All()) {
                if (columns.TypeName == typeName) {
                    return columns;
                }
            }
            //读档时补还原：模组的格数据种类要等第一次被访问才注册（TileData<T> 的静态构造），这时可能还没人碰过，按全名找到类型直接用
            Type type = typeof(Main).Assembly.GetType(typeName);
            if (type == null) {
                foreach (Mod mod in ModLoader.Mods) {
                    type = mod.Code?.GetType(typeName);
                    if (type != null) {
                        break;
                    }
                }
            }
            return type is { IsValueType: true } && typeof(ITileData).IsAssignableFrom(type) ? Get(type) : null;
        }

        internal static void Clear() => _columns.Clear();

        private static ITileDataColumns Get(Type type) {
            lock (_columns) {
                if (!_columns.TryGetValue(type, out ITileDataColumns columns)) {
                    try {
                        columns = (ITileDataColumns)Activator.CreateInstance(typeof(TileDataColumns<>).MakeGenericType(type));
                    } catch (Exception ex) {
                        ScenarioPlatform.Error($"Scenario.Restore.TileData.{type.FullName}", $"[Scenarios] Restore: tile data {type.FullName} cannot be copied: {ex.Message}");
                        columns = null;
                    }
                    _columns[type] = columns;
                }
                return columns;
            }
        }

        private static IEnumerable<Type> RegisteredTypes() {
            List<Type> types = [];
            try {
                if (_onCopySingle?.GetValue(null) is Delegate chain) {
                    foreach (Delegate entry in chain.GetInvocationList()) {
                        Type owner = entry.Method.DeclaringType;
                        if (owner is { IsGenericType: true } && owner.GetGenericArguments() is [Type data] && typeof(ITileData).IsAssignableFrom(data)) {
                            types.Add(data);
                        }
                    }
                }
            } catch (Exception ex) {
                types.Clear();
                if (!_fallbackLogged) {
                    _fallbackLogged = true;
                    VaultMod.Instance?.Logger.Warn($"[Scenarios] Restore: enumerating tile data types failed ({ex.Message}); only vanilla tile data is recorded");
                }
            }
            if (types.Count == 0) {
                types.AddRange(_vanilla);
            }
            return types;
        }
    }

    internal sealed class ScenarioRestoreSystem : ModSystem
    {
        public override void Unload() => TileDataRegistry.Clear();
    }

    //区域还原靠反射 tML 内部的 TileData 枚举已注册的格数据种类；找不到时退化为只记录原版的五种
    internal sealed class TileDataTakeover : ScenarioTakeover
    {
        internal override string Name => "TileDataTypes";

        internal override string Source => "Terraria/TileData.cs: internal static Action<uint, uint> OnCopySingle (each TileData<T> static constructor adds its CopySingle)";

        protected override void Install() {
            if (!TileDataRegistry.CanEnumerate) {
                Fail("TileData.OnCopySingle not found; restore areas record vanilla tile data only");
            }
        }

        protected override void Uninstall() { }
    }
}
