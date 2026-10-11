using InnoVault.GameSystem;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent.UI.States;
using Terraria.Graphics.Capture;
using Terraria.IO;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using Terraria.Utilities;
using Terraria.WorldBuilding;

namespace InnoVault.Scenarios
{
    //单人换舞台后端。舞台在磁盘上就是一个原版世界文件；对照 tML 源码：WorldGen.SaveAndQuitCallBack（离开）、
    //WorldFile.CreateMetadata 与 WorldGenerator.GenerateWorld（生成）、WorldGen.playWorldCallBack（进入）；升级 tML 后逐个核对
    internal sealed class TmlStageBackend : IScenarioStageBackend
    {
        //与 WorldFile.CreateMetadata 相同；不小于 777389080577 时地图文件按世界 GUID 命名
        private const ulong GeneratorVersion = 1198295875585uL;
        //BackupIO 是 internal 类型：为真时 tML 保存世界不再往 Worlds/Backups 归档
        private static readonly FieldInfo _archiveLock = typeof(Main).Assembly
            .GetType("Terraria.ModLoader.BackupIO")?.GetField("archiveLock", BindingFlags.Public | BindingFlags.Static);

        private WorldFileData _root;
        private Guid _rootId;
        private bool _rootArchiveLock;
        private Vector2? _returnAnchor;
        private volatile bool _busy;
        private volatile bool _showingGenerationProgress;

        private string ScenarioDirectory => Path.Combine(VaultSave.RootPath, "Scenarios", _rootId.ToString("N"));
        private string QuitRecordPath => Path.Combine(ScenarioDirectory, "quit.txt");

        public string CheckReady(ScenarioTransition transition) {
            if (_busy) {
                return "a previous travel is still running";
            }
            if (_root == null) {
                return "root world identity is unknown";
            }
            if (Main.gameMenu) {
                return "the game is on the menu";
            }
            Player player = Main.LocalPlayer;
            if (player == null || !player.active) {
                return "the local player is not active";
            }
            if (player.dead) {
                return "the local player is dead";
            }
            if (SubworldLibraryInterop.AnySubworldActive()) {
                return "a Subworld Library subworld is active";
            }
            return null;
        }

        public void Begin(ScenarioTransition transition) {
            _busy = true;
            //PreSaveAndQuit 会派发到场景系统的 EndSession，它按阶段认出这是换舞台途中而忽略
            ScenarioDirector.TravelPhase = ScenarioTravelPhase.Saving;
            if (transition.From is RootScenario) {
                _returnAnchor = Main.LocalPlayer.position;
            }
            try {
                SystemLoader.PreSaveAndQuit();
            } catch (Exception ex) {
                ScenarioPlatform.Error("Scenario.PreSaveAndQuit", $"[Scenarios] PreSaveAndQuit threw during travel: {ex}");
            }
            Main.menuMode = 10;
            Main.gameMenu = true;
            Main.statusText = ScenarioText.Saving.Value;
            //切换通常在一帧更新的中途发起；主线程动作队列在本帧 DoUpdate 结束后才执行，那时再开线程，
            //存盘不会与本帧剩余的实体更新并发，下一帧的 DoUpdate 也已经被 gameMenu 停住
            Main.QueueMainThreadAction(() => ThreadPool.QueueUserWorkItem(_ => Run(transition)));
        }

        //落点：请求的锚点、入口锚点；回到主世界时没有请求锚点就回到离开前的位置
        public void OnPlayerEntered(ScenarioTransition transition) {
            Player player = Main.LocalPlayer;
            bool home = transition.To is RootScenario;
            if (transition.Anchor != null) {
                if (ScenarioDirector.TryFindAnchor(transition.Anchor, out Point requested)) {
                    ScenarioPlacement.PlaceAtTile(player, requested);
                    _returnAnchor = null;
                    return;
                }
                ScenarioPlatform.Info($"[Scenarios] Anchor '{transition.Anchor}' was not found in {transition.To?.Key}; using the default entry");
            }
            if (home) {
                if (_returnAnchor is Vector2 anchor) {
                    ScenarioPlacement.PlaceAt(player, anchor);
                }
                _returnAnchor = null;
            }
            else if (ScenarioDirector.TryFindAnchor(ScenarioAnchors.Entry, out Point entry)) {
                ScenarioPlacement.PlaceAtTile(player, entry);
            }
        }

        public void OnSessionStarted() {
            _root = Main.ActiveWorldFileData;
            _rootId = _root.UniqueId != Guid.Empty
                ? _root.UniqueId
                : new Guid(MD5.HashData(Encoding.UTF8.GetBytes(_root.Path ?? _root.Name ?? string.Empty)));
            _rootArchiveLock = GetArchiveLock();
            _returnAnchor = null;
        }

        public void OnSessionEnding(VaultScenario stage) {
            if (_root == null) {
                return;
            }
            try {
                if (stage == null || stage is RootScenario) {
                    TryDelete(QuitRecordPath);
                    return;
                }
                Directory.CreateDirectory(ScenarioDirectory);
                File.WriteAllText(QuitRecordPath, stage.Template.Key);
            } catch (Exception ex) {
                ScenarioPlatform.Error("Scenario.QuitRecord", $"[Scenarios] Failed to record the quit location: {ex.Message}");
            }
        }

        public string ConsumeQuitLocation() {
            if (_root == null || !File.Exists(QuitRecordPath)) {
                return null;
            }
            try {
                string key = File.ReadAllText(QuitRecordPath).Trim();
                TryDelete(QuitRecordPath);
                return key.Length == 0 ? null : key;
            } catch (Exception ex) {
                ScenarioPlatform.Error("Scenario.QuitRecord", $"[Scenarios] Failed to read the quit location: {ex.Message}");
                return null;
            }
        }

        public void OnWorldUnloadedOutsideTravel() {
            if (_root != null) {
                SetArchiveLock(_rootArchiveLock);
            }
            _root = null;
            _returnAnchor = null;
        }

        public void Reset() {
            _root = null;
            _returnAnchor = null;
            _busy = false;
            _showingGenerationProgress = false;
        }

        //线程池上执行：离开、进入；任何一步失败都退回根世界，根世界也进不去就结束会话
        private void Run(ScenarioTransition transition) {
            try {
                Leave(transition.From);
                if (!Enter(transition)) {
                    FallBackHome(transition, "the target stage failed to load");
                }
            } catch (Exception ex) {
                ScenarioPlatform.Error("Scenario.Travel.Run", $"[Scenarios] Travel to {transition.To?.Key} failed: {ex}");
                FallBackHome(transition, ex.Message);
            } finally {
                _showingGenerationProgress = false;
                _busy = false;
            }
        }

        //WorldGen.SaveAndQuitCallBack 去掉回主菜单的部分；一次性舞台不存盘，离开后删除其文件
        private void Leave(VaultScenario from) {
            ScenarioStage fromStage = from?.Stage;
            bool transient = fromStage is { BindsRootWorld: false, Persistence: StagePersistence.Transient };
            string fromPath = Main.ActiveWorldFileData?.Path;
            Guid fromId = Main.ActiveWorldFileData?.UniqueId ?? Guid.Empty;

            SoundEngine.StopTrackedSounds();
            CaptureInterface.ResetFocus();
            Main.ActivePlayerFileData.StopPlayTimer();
            //原版只在 mapEnabled 为真时随玩家一起保存地图；地图策略关掉它期间探索过的部分不能丢
            ScenarioMapPolicy.Reset();
            Player.SavePlayer(Main.ActivePlayerFileData);
            Player.ClearPlayerTempInfo();
            Rain.ClearRain();
            if (!transient) {
                WorldFile.CacheSaveTime();
                WorldFile.SaveWorld();
            }
            //先等 InnoVault 的后台存盘写完再派发卸载：它在执行时才读路径与各模组状态，卸载会清空后者
            VaultSave.WaitForPendingSave();
            SystemLoader.OnWorldUnload();
            Main.fastForwardTimeToDawn = false;
            Main.fastForwardTimeToDusk = false;
            Main.UpdateTimeRate();
            if (transient) {
                DeleteStageFiles(fromPath, fromId);
            }
        }

        private bool Enter(ScenarioTransition transition) {
            VaultScenario to = transition.To;
            ScenarioStage stage = to.Stage;
            WorldFileData target;
            if (stage.BindsRootWorld) {
                SetArchiveLock(_rootArchiveLock);
                Main.statusText = ScenarioText.ReturningHome.Value;
                target = _root;
            }
            else {
                SetArchiveLock(true);
                string path = GetStagePath(to);
                TagCompound meta = ReadMeta(path);
                bool regenerate = stage.Persistence == StagePersistence.Transient
                    || !File.Exists(path)
                    || meta == null
                    || meta.GetInt("version") != stage.Version;
                if (regenerate) {
                    target = Generate(to, stage, path);
                }
                else {
                    target = WorldFile.GetAllMetadata(path, false);
                    ApplyPersistedAnchors(to, meta);
                }
                Main.statusText = ScenarioText.Entering.Value;
            }
            target.SetAsActive();
            //原版菜单在主线程读取玩家；模组的 ModPlayer.LoadData 可能碰图形资源，离开主线程会出错
            RunOnMainThread(ReloadPlayer);
            ScenarioDirector.TravelPhase = ScenarioTravelPhase.Loading;
            WorldGen.playWorldCallBack(null);
            return !WorldGen.loadFailed && WorldGen.loadSuccess;
        }

        //对照 WorldFile.CreateMetadata 建元数据，对照 WorldGenerator.GenerateWorld 跑生成步骤（自己处理异常，不走原版的致命错误页）
        private WorldFileData Generate(VaultScenario scenario, ScenarioStage stage, string path) {
            Main.statusText = ScenarioText.Generating.Value;
            ScenarioDirector.TravelPhase = ScenarioTravelPhase.Generating;
            Guid id = DeriveGuid(scenario.Key);
            DeleteStageFiles(path, id);
            Directory.CreateDirectory(Path.GetDirectoryName(path));

            int seed = stage.Seed ?? (BitConverter.ToInt32(id.ToByteArray(), 4) & int.MaxValue);
            WorldFileData data = new(path, false) {
                Name = scenario.Name,
                GameMode = Main.GameMode,
                CreationTime = DateTime.Now,
                Metadata = FileMetadata.FromCurrentSettings(FileType.World),
                WorldGeneratorVersion = GeneratorVersion,
                UniqueId = id,
            };
            data.SetSeed(seed.ToString());
            data.SetWorldSize(stage.Width, stage.Height);
            data.SetAsActive();

            ScenarioAnchors anchors = scenario.GetComponent<ScenarioAnchors>();
            Dictionary<string, Point> declared = anchors?.Snapshot();
            WorldGen.generatingWorld = true;
            WorldGen.gen = true;
            try {
                Main.maxTilesX = stage.Width;
                Main.maxTilesY = stage.Height;
                Main.worldName = data.Name;
                Main.worldID = DeriveWorldId(id);
                WorldGen.clearWorld();
                Main.worldSurface = stage.ResolveSurfaceLevel();
                Main.rockLayer = stage.ResolveRockLevel();
                Point spawn = anchors != null && anchors.TryGet(ScenarioAnchors.Entry, out Point entry)
                    ? entry
                    : new Point(stage.Width / 2, (int)Main.worldSurface);
                Main.spawnTileX = spawn.X;
                Main.spawnTileY = spawn.Y;
                WorldGen._genRand = new UnifiedRandom(seed);
                Main.rand = new UnifiedRandom(seed);
                GenVars.structures = new StructureMap();
                WorldGen.RandomizeBackgrounds(WorldGen.genRand);
                WorldGen.RandomizeTreeStyle();
                WorldGen.RandomizeCaveBackgrounds();
                WorldGen.RandomizeMoonState(WorldGen.genRand);
                SystemLoader.PreWorldGen();
                RunPasses(stage, seed);
            } finally {
                WorldGenerator.CurrentGenerationProgress = null;
                _showingGenerationProgress = false;
                WorldGen.gen = false;
                WorldGen.generatingWorld = false;
            }
            Main.menuMode = 10;
            Main.statusText = ScenarioText.Saving.Value;
            WorldFile.SaveWorld(false, resetTime: true);
            WriteMeta(path, stage.Version, CollectGeneratedAnchors(anchors, declared));
            return data;
        }

        private void RunPasses(ScenarioStage stage, int seed) {
            List<GenPass> passes = [];
            stage.CollectPasses(passes);
            passes.RemoveAll(static pass => pass == null || !pass.Enabled);
            if (passes.Count == 0) {
                return;
            }
            WorldGenConfiguration configuration = WorldGenConfiguration.FromEmbeddedPath("Terraria.GameContent.WorldBuilding.Configuration.json");
            GenerationProgress progress = new();
            foreach (GenPass pass in passes) {
                progress.TotalWeight += pass.Weight;
            }
            WorldGenerator.CurrentGenerationProgress = progress;
            _showingGenerationProgress = true;
            //原版生成进度界面只在主线程切换；生成很快结束时这次切换就不做了
            Main.QueueMainThreadAction(() => {
                if (_showingGenerationProgress) {
                    Main.MenuUI.SetState(new UIWorldLoad());
                    Main.menuMode = 888;
                }
            });
            foreach (GenPass pass in passes) {
                WorldGen._genRand = new UnifiedRandom(seed);
                Main.rand = new UnifiedRandom(seed);
                progress.Start(pass.Weight);
                pass.Apply(progress, configuration.GetPassConfiguration(pass.Name));
                progress.End();
            }
        }

        //生成期间新写入或改动过的锚点；代码里写死的锚点不持久化，随代码更新
        private static Dictionary<string, Point> CollectGeneratedAnchors(ScenarioAnchors anchors, Dictionary<string, Point> declared) {
            Dictionary<string, Point> generated = new(StringComparer.OrdinalIgnoreCase);
            if (anchors == null) {
                return generated;
            }
            foreach (var pair in anchors.Points) {
                if (declared == null || !declared.TryGetValue(pair.Key, out Point before) || before != pair.Value) {
                    generated[pair.Key] = pair.Value;
                }
            }
            return generated;
        }

        private static void ApplyPersistedAnchors(VaultScenario scenario, TagCompound meta) {
            if (scenario.GetComponent<ScenarioAnchors>() is not ScenarioAnchors anchors || !meta.TryGet("anchors", out TagCompound saved)) {
                return;
            }
            foreach (var pair in saved) {
                if (pair.Value is int[] { Length: 2 } xy) {
                    anchors.Set(pair.Key, new Point(xy[0], xy[1]));
                }
            }
        }

        //从磁盘重新读取玩家：原版出生时用读取玩家文件那一刻的快照恢复光标、向导、重铸槽物品，复用内存里的玩家会复制或丢失它们
        private static void ReloadPlayer() {
            PlayerFileData current = Main.ActivePlayerFileData;
            try {
                PlayerFileData reloaded = Player.LoadPlayer(current.Path, current.IsCloudSave);
                if (reloaded?.Player == null || reloaded.Player.loadStatus != 0) {
                    ScenarioPlatform.Error("Scenario.ReloadPlayer", $"[Scenarios] Reloading the player from {current.Path} failed; keeping the in-memory player");
                    return;
                }
                reloaded.SetAsActive();
            } catch (Exception ex) {
                ScenarioPlatform.Error("Scenario.ReloadPlayer", $"[Scenarios] Reloading the player threw; keeping the in-memory player: {ex}");
            }
        }

        //从线程池把动作交给主线程并等它执行完。菜单期间主线程每帧都会清空动作队列
        private static void RunOnMainThread(Action action) {
            ManualResetEventSlim done = new(false);
            Exception error = null;
            Main.QueueMainThreadAction(() => {
                try {
                    action();
                } catch (Exception ex) {
                    error = ex;
                } finally {
                    done.Set();
                }
            });
            if (!done.Wait(TimeSpan.FromSeconds(30))) {
                throw new TimeoutException("the main thread did not run a queued travel step within 30 seconds");
            }
            if (error != null) {
                throw new InvalidOperationException("a travel step on the main thread failed", error);
            }
        }

        private void FallBackHome(ScenarioTransition failed, string reason) {
            ScenarioPlatform.Error("Scenario.FallBack", $"[Scenarios] Travel to {failed.To?.Key} failed ({reason}); returning to the root world");
            if (failed.To is RootScenario || _root == null) {
                Abort();
                return;
            }
            if (failed.To?.Stage is { BindsRootWorld: false } stage) {
                QuarantineStage(failed.To, stage);
            }
            ScenarioTransition home = ScenarioDirector.RedirectTravelHome(failed);
            if (home == null) {
                Abort();
                return;
            }
            try {
                Main.menuMode = 10;
                Main.gameMenu = true;
                SetArchiveLock(_rootArchiveLock);
                Main.statusText = ScenarioText.ReturningHome.Value;
                ScenarioDirector.TravelPhase = ScenarioTravelPhase.Loading;
                _root.SetAsActive();
                WorldGen.playWorldCallBack(null);
                if (WorldGen.loadFailed || !WorldGen.loadSuccess) {
                    Abort();
                }
            } catch (Exception ex) {
                ScenarioPlatform.Error("Scenario.FallBack", $"[Scenarios] Returning to the root world failed: {ex}");
                Abort();
            }
        }

        //根世界也进不去：结束会话。原版读档失败时会自己切到错误页，这里只在还停在加载画面时回标题
        private static void Abort() {
            ScenarioDirector.AbortTravel();
            Main.gameMenu = true;
            if (Main.menuMode is 10 or 888) {
                Main.menuMode = 0;
            }
        }

        //读不进来的舞台：一次性的直接删掉；长期保存的改名为 .bad 留作排查，下次进入时重新生成
        private void QuarantineStage(VaultScenario scenario, ScenarioStage stage) {
            string path = GetStagePath(scenario);
            if (stage.Persistence == StagePersistence.Transient) {
                DeleteStageFiles(path, DeriveGuid(scenario.Key));
                return;
            }
            try {
                if (File.Exists(path)) {
                    File.Move(path, path + ".bad", overwrite: true);
                }
                TryDelete(MetaPath(path));
            } catch (Exception ex) {
                ScenarioPlatform.Error("Scenario.Quarantine", $"[Scenarios] Failed to quarantine {path}: {ex.Message}");
            }
        }

        private string GetStagePath(VaultScenario scenario)
            => Path.Combine(ScenarioDirectory, $"{_rootId:N}_{SafeFileName(scenario.Key)}.wld");

        internal static string SafeFileName(string key) {
            StringBuilder builder = new(key.Length);
            char[] invalid = Path.GetInvalidFileNameChars();
            foreach (char c in key) {
                builder.Append(c == '/' || c == '\\' || Array.IndexOf(invalid, c) >= 0 ? '_' : c);
            }
            return builder.ToString();
        }

        //舞台元数据：生成版本号与生成期间写入的锚点
        private static string MetaPath(string stagePath) => Path.ChangeExtension(stagePath, ".meta");

        private static TagCompound ReadMeta(string stagePath) {
            try {
                return File.Exists(MetaPath(stagePath)) ? TagIO.FromFile(MetaPath(stagePath)) : null;
            } catch (Exception ex) {
                ScenarioPlatform.Error("Scenario.Meta", $"[Scenarios] Failed to read {MetaPath(stagePath)}; the stage will be regenerated: {ex.Message}");
                return null;
            }
        }

        private static void WriteMeta(string stagePath, int version, Dictionary<string, Point> anchors) {
            try {
                TagCompound saved = [];
                foreach (var pair in anchors) {
                    saved[pair.Key] = new[] { pair.Value.X, pair.Value.Y };
                }
                TagIO.ToFile(new TagCompound { ["version"] = version, ["anchors"] = saved }, MetaPath(stagePath));
            } catch (Exception ex) {
                ScenarioPlatform.Error("Scenario.Meta", $"[Scenarios] Failed to write {MetaPath(stagePath)}: {ex.Message}");
            }
        }

        //舞台的 GUID 由根世界 GUID 与场景 Key 确定性派生：同一场景始终复用同一份地图文件
        private Guid DeriveGuid(string key) {
            byte[] root = _rootId.ToByteArray();
            byte[] name = Encoding.UTF8.GetBytes(key);
            byte[] buffer = new byte[root.Length + name.Length];
            root.CopyTo(buffer, 0);
            name.CopyTo(buffer, root.Length);
            return new Guid(MD5.HashData(buffer));
        }

        //玩家的床位出生点表按世界名 + worldID 记录（上限 200 条），worldID 固定才不会每次重新生成都多占一条
        private static int DeriveWorldId(Guid id) {
            int value = BitConverter.ToInt32(id.ToByteArray(), 0) & int.MaxValue;
            return value == 0 ? 1 : value;
        }

        private static void DeleteStageFiles(string path, Guid id) {
            if (string.IsNullOrEmpty(path)) {
                return;
            }
            string twld = Path.ChangeExtension(path, ".twld");
            TryDelete(path);
            TryDelete(path + ".bak");
            TryDelete(twld);
            TryDelete(twld + ".bak");
            TryDelete(MetaPath(path));
            SaveWorld.DeleteWorldData(Path.GetFileNameWithoutExtension(path));
            DeleteMap(id);
            ScenarioRestoreJournal.DeleteFor(id);
        }

        private static void DeleteMap(Guid id) {
            PlayerFileData player = Main.ActivePlayerFileData;
            if (id == Guid.Empty || string.IsNullOrEmpty(player?.Path) || player.IsCloudSave) {
                return;
            }
            string map = Path.Combine(player.Path[..^4], id + ".map");
            TryDelete(map);
            TryDelete(map + ".bak");
            TryDelete(Path.ChangeExtension(map, ".tmap"));
            TryDelete(Path.ChangeExtension(map, ".tmap") + ".bak");
        }

        private static void TryDelete(string path) {
            try {
                if (File.Exists(path)) {
                    File.Delete(path);
                }
            } catch (Exception ex) {
                ScenarioPlatform.Error($"Scenario.Delete.{path}", $"[Scenarios] Failed to delete {path}: {ex.Message}");
            }
        }

        private static bool GetArchiveLock() => _archiveLock?.GetValue(null) is true;

        private static void SetArchiveLock(bool value) => _archiveLock?.SetValue(null, value);
    }
}
