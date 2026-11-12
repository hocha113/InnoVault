using InnoVault.GameSystem;
using InnoVault.TileProcessors;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using Terraria;
using Terraria.Audio;
using Terraria.ID;
using Terraria.IO;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using Terraria.Utilities;
using Terraria.WorldBuilding;

namespace InnoVault.Scenarios
{
    //换舞台后端的进入部分：生成出来的舞台直接从内存进入、进入时的液体沉降、抵达后的复活与落点、舞台边界。
    //对照 tML 源码：WorldGen.playWorldCallBack 与 FinishPlayWorld（进入）、WorldFile.LoadWorld 读档成功之后的那一段（收尾）、
    //Player.Spawn（复活）、WorldGen.setWorldSize（世界边界）；升级 tML 后逐个核对
    internal sealed partial class TmlStageBackend
    {
        //WorldFile.OnWorldLoad 是别的类上的事件，只能经编译器生成的同名后备字段取出委托来调用
        private static readonly FieldInfo _worldFileLoaded = typeof(WorldFile).GetField("OnWorldLoad", BindingFlags.NonPublic | BindingFlags.Static);

        //这一端的本地玩家最近一次离开各块舞台时所在的位置（世界像素），按场景 Key 记，只在本次会话里有效
        private readonly Dictionary<string, Vector2> _departures = new(StringComparer.Ordinal);
        //（单人）这次换舞台沿用内存里的角色对象，不从存档重新读取
        private bool _keepPlayer;

        //目标舞台已经备好：要设为当前的世界文件数据，以及怎么进（物块已经在内存里、不必读档；进入时要不要做液体沉降）
        private readonly record struct StageEntry(WorldFileData Data, bool FromMemory, bool SettleLiquids);

        public void OnStageActivated(ScenarioStage stage) {
            //拥有世界文件的一端认得出种子；客户端的随场景状态收到
            if (stage.ResolvedSeed == 0 && Main.netMode != NetmodeID.MultiplayerClient) {
                stage.ResolvedSeed = Main.ActiveWorldFileData?.Seed ?? 0;
            }
            ApplyStageBounds(stage);
        }

        //没有地狱层的舞台：世界底边留在声明的高度上，垫在下面的行落在边界之外。玩家与镜头的边界、NPC 与弹幕的越界清理读的都是 Main.bottomWorld。
        //原版每次清空世界都会按物块空间的行数重算它，所以每一端在舞台场景激活时写一次，之后每帧核对
        internal static void ApplyStageBounds(ScenarioStage stage) {
            if (stage == null || stage.BindsRootWorld || !stage.NoUnderworld || Main.maxTilesY < stage.PhysicalHeight) {
                return;
            }
            float bottom = stage.Height * 16f;
            if (Main.bottomWorld != bottom) {
                Main.bottomWorld = bottom;
            }
        }

        //当前舞台内容的范围（物块坐标）：没有地狱层的舞台不含垫在下面的行。揭开地图、镜头分屏这类“缺省取整块舞台”的地方用它，不直接用物块空间的尺寸
        internal static Rectangle ContentBounds {
            get {
                int rows = Main.maxTilesY;
                if (ScenarioRuntime.Stage?.Stage is { BindsRootWorld: false, NoUnderworld: true } stage) {
                    rows = Math.Min(rows, stage.Height);
                }
                return new Rectangle(0, 0, Main.maxTilesX, rows);
            }
        }

        //没有地狱层的舞台生成完之后，紧贴声明高度的下面铺一行石块封底。原版物块空间的底边本身兜得住液体（Liquid.AddWater 不让最底下 5 行的液体流动），
        //垫出来的行没有这层兜底：舞台底部敞着的水会一直漏下去。这一行在世界底边之外，玩家与镜头都到不了
        private static void SealUnderlay(ScenarioStage stage) {
            int row = stage.Height;
            if (row >= Main.maxTilesY) {
                return;
            }
            for (int x = 0; x < Main.maxTilesX; x++) {
                Tile tile = Main.tile[x, row];
                tile.ClearEverything();
                tile.HasTile = true;
                tile.TileType = TileID.Stone;
            }
        }

        #region 直接进入

        //（单人）生成出来的舞台直接进入。与 WorldGen.playWorldCallBack 同序，只是物块已经在内存里，没有读档这一步
        private static bool EnterFromMemory(bool settleLiquids) {
            for (int i = 0; i < Main.maxPlayers; i++) {
                if (i != Main.myPlayer) {
                    Main.player[i].active = false;
                }
            }
            WorldGen.noMapUpdate = true;
            Main.ToggleGameplayUpdates(state: false);
            FinishGeneratedWorld(settleLiquids);
            if (Main.mapEnabled) {
                Main.Map.Load();
            }
            Main.sectionManager.SetAllSectionsLoaded();
            while (Main.loadMapLock) {
                Thread.Sleep(0);
                if (!Main.mapEnabled) {
                    break;
                }
            }
            if (Main.gameMenu) {
                Main.gameMenu = false;
            }
            Main.QueueMainThreadAction(FinishEnterFromMemory);
            return true;
        }

        //对照 WorldGen.FinishPlayWorld。那里还会把时间与天气换成读档时暂存的值（WorldFile.SetOngoingToTemps）：这块舞台没有读过档，那些暂存值是上一个世界的，不套用
        private static void FinishEnterFromMemory() {
            Main.player[Main.myPlayer].Spawn(PlayerSpawnContext.SpawningIntoWorld);
            SoundEngine.PlaySound(SoundID.MenuClose);
            Main.resetClouds = true;
            WorldGen.noMapUpdate = false;
        }

        //（单人与服务器）刚生成完的舞台补上原版读档成功之后的收尾，顺序与 WorldFile.LoadWorld 的后半段相同：
        //矿物档位 → 模组的世界载入回调 → 模组世界数据 → 液体 → 按世界定的天气与生物参数 → WorldFile.OnWorldLoad（新舞台场景在这里激活）。
        //与存盘再读盘的差别只有两处：物块不经过文件；没有第二次清空世界（ModSystem.ClearWorld 不再派发一遍），生成步骤写进各模组静态状态的内容原样保留。
        //原版读档时对旧版物块做的两项整理（旧人体模型转物块实体、清除冰棒之类的临时物块）不做，需要的舞台声明 LoadAfterGeneration
        private static void FinishGeneratedWorld(bool settleLiquids) {
            WorldGen.loadFailed = false;
            WorldGen.loadSuccess = true;
            WorldGen._lastSeed = Main.ActiveWorldFileData.Seed;
            //生成步骤把 Main.rand 换成了按种子构造的随机数，进入之后的玩法随机不该每次相同
            Main.rand = new UnifiedRandom((int)DateTime.Now.Ticks);
            //起始时刻与原版新建世界一致（存盘读盘的路径上由 SaveWorld 的 resetTime 写进文件）
            Main.time = 13500.0;
            Main.dayTime = true;
            Main.moonPhase = 0;
            Main.bloodMoon = false;
            Main.eclipse = false;
            //清空世界把矿物档位置成了未定，生成步骤没有定下时按物块统计补上
            WorldFile.CheckSavedOreTiers();
            List<(ModSystem System, byte[] Data)> worldData = CaptureModWorldData();
            SystemLoader.OnWorldLoad();
            RestoreModWorldData(worldData);
            CarryGeneratedTileProcessors();
            VaultSave.LoadWithoutWorldFile();
            if (settleLiquids) {
                SettleLiquids();
            }
            else {
                DiscardLiquidWork();
            }
            Main.weatherCounter = WorldGen.genRand.Next(3600, 18000);
            Cloud.resetClouds();
            NPC.setFireFlyChance();
            NPC.SetWorldSpecificMonstersByWorldID();
            RaiseWorldFileLoaded();
        }

        //各模组的世界数据在内存里走一遍存与读，结果与存盘再读盘一致。不少模组在 OnWorldLoad 里复位自己的世界状态、再由 LoadWorldData 读回，
        //生成步骤写进这类状态的内容要靠这一步带过去。存出来的标签当场序列化成字节：标签里的数组与列表可能就是模组手里的那一份，复位时会跟着被清空。
        //InnoVault 自己的系统在这两个钩子里读写的是磁盘文件，不参与，由随后的两步处理
        private static List<(ModSystem System, byte[] Data)> CaptureModWorldData() {
            List<(ModSystem System, byte[] Data)> captured = [];
            foreach (ModSystem system in ModContent.GetContent<ModSystem>()) {
                if (system.Mod is VaultMod) {
                    continue;
                }
                try {
                    TagCompound tag = [];
                    system.SaveWorldData(tag);
                    if (tag.Count == 0) {
                        continue;
                    }
                    using MemoryStream stream = new();
                    TagIO.ToStream(tag, stream, compress: false);
                    captured.Add((system, stream.ToArray()));
                } catch (Exception ex) {
                    ScenarioPlatform.Error($"Scenario.Entry.SaveWorldData.{system.FullName}", $"[Scenarios] {system.FullName}.SaveWorldData threw while entering a generated stage; its world data is not carried: {ex}");
                }
            }
            return captured;
        }

        private static void RestoreModWorldData(List<(ModSystem System, byte[] Data)> captured) {
            foreach ((ModSystem system, byte[] data) in captured) {
                try {
                    using MemoryStream stream = new(data);
                    system.LoadWorldData(TagIO.FromStream(stream, compressed: false));
                } catch (Exception ex) {
                    ScenarioPlatform.Error($"Scenario.Entry.LoadWorldData.{system.FullName}", $"[Scenarios] {system.FullName}.LoadWorldData threw while entering a generated stage: {ex}");
                }
            }
        }

        //生成步骤里摆下并配置过的 TP：玩家出生时 TP 系统会按物块重新扫描、重建全部实体，再把各自的数据读回来。
        //这块舞台没有 TP 存档文件时，把此刻内存里的数据交给它当作要读回的那一份（长期保存的舞台生成后存过盘，文件在就读文件）
        private static void CarryGeneratedTileProcessors() {
            try {
                //长期保存的舞台刚生成完就排了一次后台存盘，等它写完再看文件在不在
                VaultSave.WaitForPendingSave();
                if (TileProcessorLoader.TryReadSavedTPData(out _)) {
                    return;
                }
                TagCompound data = [];
                TileProcessorLoader.SaveWorldData(data);
                TileProcessorLoader.ActiveWorldTagData = data;
            } catch (Exception ex) {
                ScenarioPlatform.Error("Scenario.Entry.TileProcessors", $"[Scenarios] Carrying the TileProcessor data of the generated stage threw: {ex}");
            }
        }

        //与 WorldFile.LoadWorld 里读档之后的那一段相同：全图快速沉降，悬空的液体落下并摊平
        private static void SettleLiquids() {
            WorldGen.gen = true;
            try {
                GenVars.waterLine = Main.maxTilesY;
                Liquid.QuickWater(2);
                WorldGen.WaterCheck();
                Liquid.quickSettle = true;
                int rounds = 0;
                while (Liquid.numLiquid > 0 && rounds < 100000) {
                    rounds++;
                    Liquid.UpdateLiquid();
                }
                Liquid.quickSettle = false;
                WorldGen.WaterCheck();
            } finally {
                Liquid.quickSettle = false;
                WorldGen.gen = false;
            }
        }

        //不做沉降：生成步骤排进队列的液体工作全部丢弃，液体留在摆放的位置；清掉物块上的排队标记，以后被扰动的液体才能重新入队
        private static void DiscardLiquidWork() {
            Liquid.numLiquid = 0;
            LiquidBuffer.numLiquidBuffer = 0;
            for (int x = 0; x < Main.maxTilesX; x++) {
                for (int y = 0; y < Main.maxTilesY; y++) {
                    Tile tile = Main.tile[x, y];
                    tile.CheckingLiquid = false;
                    tile.SkipLiquid = false;
                }
            }
        }

        //WorldFile.OnWorldLoad：原版读档成功时触发。新舞台场景在这个事件里激活，别的模组也可能订阅了它，照样触发一次
        private static void RaiseWorldFileLoaded() {
            if (_worldFileLoaded == null) {
                ScenarioSystem.OnStageFileLoaded();
                return;
            }
            try {
                (_worldFileLoaded.GetValue(null) as Action)?.Invoke();
            } catch (Exception ex) {
                ScenarioPlatform.Error("Scenario.Entry.WorldFileLoaded", $"[Scenarios] A WorldFile.OnWorldLoad handler threw while entering a generated stage: {ex}");
            }
        }

        #endregion

        #region 抵达：复活与落点

        //离开一块舞台之前在主线程调用：记下本地玩家此刻的位置，以后可以落回这里（ScenarioAnchors.Departure，以及回主世界的缺省落点）
        private void RememberDeparture(ScenarioTransition transition) {
            if (Main.dedServ || transition?.From?.Template is not VaultScenario left) {
                return;
            }
            Player player = Main.LocalPlayer;
            if (player != null && player.active) {
                _departures[left.Key] = player.position;
            }
        }

        //倒下期间被带到新舞台：目的地的舞台声明了抵达即复活（缺省）时当场复活，随后由落点流程摆到位；否则留着倒计时，走完后按新舞台的复活规则复活。
        //这一次复活是换舞台的一部分，不套用复活落点规则（否则刚到就可能又被规则送走）
        private static void ReviveOnArrival(Player player, ScenarioTransition transition) {
            if (player == null || !player.dead || player.ghost) {
                return;
            }
            ScenarioStage stage = (transition?.To ?? ScenarioDirector.Stage)?.Stage;
            if (stage != null && !stage.ReviveOnArrival) {
                return;
            }
            ScenarioPlacement.ArrivalRevive = true;
            try {
                player.respawnTimer = 0;
                player.Spawn(PlayerSpawnContext.ReviveFromDeath);
            } finally {
                ScenarioPlacement.ArrivalRevive = false;
            }
        }

        //这次换舞台按哪个锚点名落地：请求里指定的；回主世界又没指定时，看被离开的那块舞台声明的出口锚点。都没有时为空，按缺省落点
        private static string ResolveAnchorName(ScenarioTransition transition) {
            if (transition == null) {
                return null;
            }
            if (transition.Anchor != null) {
                return transition.Anchor;
            }
            return transition.To is RootScenario ? transition.From?.Stage?.ExitAnchor : null;
        }

        private static bool IsBuiltInAnchor(string anchor, string name) => string.Equals(anchor, name, StringComparison.OrdinalIgnoreCase);

        //把本地玩家摆到这次换舞台的落点上。顺序：服务器解析好的落点（多人）→ 指定的锚点（目的地里的锚点，或内置的出生点、上次离开的位置）
        //→ 缺省：主世界回到离开前的位置，场景舞台落在入口锚点，没有时留在出生流程放的位置（舞台出生点）
        private void Land(Player player, ScenarioTransition transition, Point? resolved) {
            if (resolved is Point tile) {
                ScenarioPlacement.PlaceAtTile(player, tile);
                return;
            }
            VaultScenario destination = transition?.To ?? ScenarioDirector.Stage;
            string key = destination?.Template?.Key;
            string anchor = ResolveAnchorName(transition);
            if (anchor != null) {
                if (ScenarioDirector.TryFindAnchor(anchor, out Point requested)) {
                    ScenarioPlacement.PlaceAtTile(player, requested);
                    return;
                }
                if (IsBuiltInAnchor(anchor, ScenarioAnchors.Spawn)) {
                    return;
                }
                if (IsBuiltInAnchor(anchor, ScenarioAnchors.Departure)) {
                    if (key != null && _departures.TryGetValue(key, out Vector2 before)) {
                        ScenarioPlacement.PlaceAt(player, before);
                        return;
                    }
                }
                else {
                    ScenarioPlatform.Info($"[Scenarios] Anchor '{anchor}' was not found in {key}; using the default entry");
                }
            }
            if (destination is RootScenario) {
                if (key != null && _departures.TryGetValue(key, out Vector2 left)) {
                    ScenarioPlacement.PlaceAt(player, left);
                }
            }
            else if (ScenarioDirector.TryFindAnchor(ScenarioAnchors.Entry, out Point entry)) {
                ScenarioPlacement.PlaceAtTile(player, entry);
            }
        }

        #endregion
    }
}
