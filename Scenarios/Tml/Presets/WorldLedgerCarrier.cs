using InnoVault.Scenarios.Presets;
using System;
using System.Collections.Generic;
using System.IO;
using Terraria;
using Terraria.GameContent.Bestiary;
using Terraria.GameContent.Creative;
using Terraria.GameContent.Events;
using Terraria.ID;
using Terraria.ModLoader.IO;

namespace InnoVault.Scenarios
{
    //WorldLedger 与 InheritVanillaProgress 的执行者。离开一块舞台时（舞台仍在、尚未存盘）按目的地的声明抓取原版记录写进转场数据包，
    //进入时在所有激活钩子之前写回世界（GlobalScenario.PreActivate）：场景自己的 OnActivate 读到的已经是带过来的值。
    //都是世界状态：多人下由服务器抓取与写入，客户端经原版的世界信息包与进服时的图鉴、旅途能力、旗帜计数同步拿到结果（换舞台时这套进服同步会重走一遍）。
    //数据包里的键带框架保留的前缀，不下发给客户端
    internal sealed class WorldLedgerCarrier : GlobalScenario
    {
        private const string InheritKey = ScenarioDirector.ReservedPayloadPrefix + "Ledger";
        private const string WriteBackKey = ScenarioDirector.ReservedPayloadPrefix + "LedgerBack";

        private static readonly (string Name, Func<bool> Get, Action<bool> Set)[] _progress = [
            ("hardMode", () => Main.hardMode, v => Main.hardMode = v),
            ("downedSlimeKing", () => NPC.downedSlimeKing, v => NPC.downedSlimeKing = v),
            ("downedBoss1", () => NPC.downedBoss1, v => NPC.downedBoss1 = v),
            ("downedBoss2", () => NPC.downedBoss2, v => NPC.downedBoss2 = v),
            ("downedBoss3", () => NPC.downedBoss3, v => NPC.downedBoss3 = v),
            ("downedQueenBee", () => NPC.downedQueenBee, v => NPC.downedQueenBee = v),
            ("downedDeerclops", () => NPC.downedDeerclops, v => NPC.downedDeerclops = v),
            ("downedQueenSlime", () => NPC.downedQueenSlime, v => NPC.downedQueenSlime = v),
            ("downedMechBossAny", () => NPC.downedMechBossAny, v => NPC.downedMechBossAny = v),
            ("downedMechBoss1", () => NPC.downedMechBoss1, v => NPC.downedMechBoss1 = v),
            ("downedMechBoss2", () => NPC.downedMechBoss2, v => NPC.downedMechBoss2 = v),
            ("downedMechBoss3", () => NPC.downedMechBoss3, v => NPC.downedMechBoss3 = v),
            ("downedPlantBoss", () => NPC.downedPlantBoss, v => NPC.downedPlantBoss = v),
            ("downedGolemBoss", () => NPC.downedGolemBoss, v => NPC.downedGolemBoss = v),
            ("downedFishron", () => NPC.downedFishron, v => NPC.downedFishron = v),
            ("downedEmpressOfLight", () => NPC.downedEmpressOfLight, v => NPC.downedEmpressOfLight = v),
            ("downedAncientCultist", () => NPC.downedAncientCultist, v => NPC.downedAncientCultist = v),
            ("downedTowerSolar", () => NPC.downedTowerSolar, v => NPC.downedTowerSolar = v),
            ("downedTowerVortex", () => NPC.downedTowerVortex, v => NPC.downedTowerVortex = v),
            ("downedTowerNebula", () => NPC.downedTowerNebula, v => NPC.downedTowerNebula = v),
            ("downedTowerStardust", () => NPC.downedTowerStardust, v => NPC.downedTowerStardust = v),
            ("downedMoonlord", () => NPC.downedMoonlord, v => NPC.downedMoonlord = v),
            ("downedGoblins", () => NPC.downedGoblins, v => NPC.downedGoblins = v),
            ("downedPirates", () => NPC.downedPirates, v => NPC.downedPirates = v),
            ("downedFrost", () => NPC.downedFrost, v => NPC.downedFrost = v),
            ("downedMartians", () => NPC.downedMartians, v => NPC.downedMartians = v),
            ("downedClown", () => NPC.downedClown, v => NPC.downedClown = v),
            ("downedHalloweenTree", () => NPC.downedHalloweenTree, v => NPC.downedHalloweenTree = v),
            ("downedHalloweenKing", () => NPC.downedHalloweenKing, v => NPC.downedHalloweenKing = v),
            ("downedChristmasTree", () => NPC.downedChristmasTree, v => NPC.downedChristmasTree = v),
            ("downedChristmasSantank", () => NPC.downedChristmasSantank, v => NPC.downedChristmasSantank = v),
            ("downedChristmasIceQueen", () => NPC.downedChristmasIceQueen, v => NPC.downedChristmasIceQueen = v),
            ("downedOldOnesArmyT1", () => DD2Event.DownedInvasionT1, v => DD2Event.DownedInvasionT1 = v),
            ("downedOldOnesArmyT2", () => DD2Event.DownedInvasionT2, v => DD2Event.DownedInvasionT2 = v),
            ("downedOldOnesArmyT3", () => DD2Event.DownedInvasionT3, v => DD2Event.DownedInvasionT3 = v),
        ];

        //清空世界会把这些旗标全部复位，新生成的舞台里它们都是假
        private static readonly (Func<bool> Get, Action<bool> Set)[] _seeds = [
            (() => Main.getGoodWorld, v => Main.getGoodWorld = v),
            (() => Main.drunkWorld, v => Main.drunkWorld = v),
            (() => Main.tenthAnniversaryWorld, v => Main.tenthAnniversaryWorld = v),
            (() => Main.dontStarveWorld, v => Main.dontStarveWorld = v),
            (() => Main.notTheBeesWorld, v => Main.notTheBeesWorld = v),
            (() => Main.remixWorld, v => Main.remixWorld = v),
            (() => Main.noTrapsWorld, v => Main.noTrapsWorld = v),
            (() => Main.zenithWorld, v => Main.zenithWorld = v),
        ];

        //带过来的时间要等场景开始时才写：读档进入的舞台在玩家出生之后还会被存档里的时间盖一次（WorldFile.SetOngoingToTemps）
        private static VaultScenario _timeOwner;
        private static TagCompound _pendingTime;

        public override void OnDeactivate(VaultScenario scenario, ScenarioTransition transition) {
            if (transition.Kind == ScenarioTransitionKind.SessionEnd) {
                _timeOwner = null;
                _pendingTime = null;
            }
            if (!ScenarioDirector.IsAuthority || transition.Kind != ScenarioTransitionKind.Travel || !ReferenceEquals(scenario, transition.From)) {
                return;
            }
            LedgerEntries inherit = transition.To == null ? LedgerEntries.None : Declared(transition.To).Inherit;
            if (inherit != LedgerEntries.None) {
                transition.Payload[InheritKey] = Capture(inherit);
            }
            if (transition.To is RootScenario) {
                LedgerEntries back = Declared(scenario).WriteBack & ~LedgerEntries.SpecialSeeds;
                if (back != LedgerEntries.None) {
                    transition.Payload[WriteBackKey] = Capture(back);
                }
            }
        }

        //特殊种子旗标在生成步骤执行之前就写好，按它们分支的生成内容读得到
        public override void PreGenerate(VaultScenario scenario, ScenarioTransition transition) {
            if (transition.Payload.TryGet(InheritKey, out TagCompound inherited)) {
                ApplySeeds(inherited);
            }
        }

        public override void PreActivate(VaultScenario scenario, ScenarioTransition transition) {
            if (!ScenarioDirector.IsAuthority || transition.Kind != ScenarioTransitionKind.Travel || !ReferenceEquals(scenario, transition.To)) {
                return;
            }
            if (transition.Payload.TryGet(InheritKey, out TagCompound inherited)) {
                ApplySeeds(inherited);
                ApplyProgress(inherited, overwrite: true);
                ApplyRecords(inherited);
                HoldTime(scenario, inherited);
            }
            if (scenario is RootScenario && transition.Payload.TryGet(WriteBackKey, out TagCompound written)) {
                //写回：主世界有自己的存档，进度只增不减，其余条目按各自的合并方式
                ApplyProgress(written, overwrite: false);
                ApplyRecords(written);
                HoldTime(scenario, written);
            }
        }

        public override void OnStart(VaultScenario scenario) {
            if (!ReferenceEquals(scenario, _timeOwner)) {
                return;
            }
            TagCompound time = _pendingTime;
            _timeOwner = null;
            _pendingTime = null;
            if (time == null || !ScenarioDirector.IsAuthority) {
                return;
            }
            Main.time = time.GetDouble("time");
            Main.dayTime = time.GetBool("day");
            Main.moonPhase = time.GetInt("moon");
            if (Main.netMode == NetmodeID.Server) {
                NetMessage.SendData(MessageID.WorldData);
            }
        }

        //一个场景声明的带入与写回条目：它上面所有启用着的 WorldLedger 与 InheritVanillaProgress 的并集
        private static (LedgerEntries Inherit, LedgerEntries WriteBack) Declared(VaultScenario scenario) {
            LedgerEntries inherit = LedgerEntries.None;
            LedgerEntries back = LedgerEntries.None;
            foreach (ScenarioNode node in scenario.AllNodes) {
                if (!node.EnabledInTree) {
                    continue;
                }
                if (node is WorldLedger ledger) {
                    inherit |= ledger.Inherit;
                    back |= ledger.WriteBack;
                }
                else if (node is InheritVanillaProgress progress) {
                    inherit |= LedgerEntries.Progress;
                    if (progress.WriteBack) {
                        back |= LedgerEntries.Progress;
                    }
                }
            }
            return (inherit, back);
        }

        private static TagCompound Capture(LedgerEntries entries) {
            TagCompound tag = [];
            if (entries.HasFlag(LedgerEntries.Progress)) {
                TagCompound progress = [];
                foreach (var flag in _progress) {
                    progress[flag.Name] = flag.Get();
                }
                tag["progress"] = progress;
            }
            if (entries.HasFlag(LedgerEntries.SpecialSeeds)) {
                int mask = 0;
                for (int i = 0; i < _seeds.Length; i++) {
                    if (_seeds[i].Get()) {
                        mask |= 1 << i;
                    }
                }
                tag["seeds"] = mask;
            }
            if (entries.HasFlag(LedgerEntries.Bestiary)) {
                tag["bestiary"] = CaptureBestiary();
            }
            if (entries.HasFlag(LedgerEntries.KillCounts)) {
                tag["killCounts"] = (int[])NPC.killCount.Clone();
            }
            if (entries.HasFlag(LedgerEntries.JourneyPowers)) {
                using MemoryStream stream = new();
                using (BinaryWriter writer = new(stream)) {
                    CreativePowerManager.Instance.SaveToWorld(writer);
                    writer.Flush();
                    tag["powers"] = stream.ToArray();
                }
            }
            if (entries.HasFlag(LedgerEntries.Time)) {
                tag["time"] = new TagCompound {
                    ["time"] = Main.time,
                    ["day"] = Main.dayTime,
                    ["moon"] = Main.moonPhase,
                };
            }
            return tag;
        }

        //图鉴按 NPC 的持久标识记录（原版存档只写原版 NPC，模组 NPC 由 tML 另存）：这里逐个标识问一遍，原版与模组的都带上
        private static TagCompound CaptureBestiary() {
            BestiaryUnlocksTracker tracker = Main.BestiaryTracker;
            TagCompound kills = [];
            List<string> sights = [];
            List<string> chats = [];
            HashSet<string> seen = [];
            foreach (string id in ContentSamples.NpcPersistentIdsByNetIds.Values) {
                if (id == null || !seen.Add(id)) {
                    continue;
                }
                int count = tracker.Kills.GetKillCount(id);
                if (count > 0) {
                    kills[id] = count;
                }
                if (tracker.Sights.GetWasNearbyBefore(id)) {
                    sights.Add(id);
                }
                if (tracker.Chats.GetWasChatWith(id)) {
                    chats.Add(id);
                }
            }
            return new TagCompound {
                ["kills"] = kills,
                ["sights"] = sights,
                ["chats"] = chats,
            };
        }

        private static void ApplySeeds(TagCompound tag) {
            if (!tag.ContainsKey("seeds")) {
                return;
            }
            int mask = tag.GetInt("seeds");
            for (int i = 0; i < _seeds.Length; i++) {
                _seeds[i].Set((mask & (1 << i)) != 0);
            }
        }

        //overwrite 为假时只把 false 改成 true（写回只增不减）
        private static void ApplyProgress(TagCompound tag, bool overwrite) {
            if (!tag.TryGet("progress", out TagCompound progress)) {
                return;
            }
            foreach (var flag in _progress) {
                if (!progress.ContainsKey(flag.Name)) {
                    continue;
                }
                bool value = progress.GetBool(flag.Name);
                if (overwrite || value) {
                    flag.Set(value);
                }
            }
        }

        //图鉴与旗帜计数合并（新生成的舞台里它们是空的，合并就是照搬），旅途能力照搬
        private static void ApplyRecords(TagCompound tag) {
            if (tag.TryGet("bestiary", out TagCompound bestiary)) {
                BestiaryUnlocksTracker tracker = Main.BestiaryTracker;
                if (bestiary.TryGet("kills", out TagCompound kills)) {
                    foreach (KeyValuePair<string, object> pair in kills) {
                        if (pair.Value is int count && count > tracker.Kills.GetKillCount(pair.Key)) {
                            tracker.Kills.SetKillCountDirectly(pair.Key, count);
                        }
                    }
                }
                foreach (string id in bestiary.GetList<string>("sights")) {
                    tracker.Sights.SetWasSeenDirectly(id);
                }
                foreach (string id in bestiary.GetList<string>("chats")) {
                    tracker.Chats.SetWasChatWithDirectly(id);
                }
            }
            if (tag.ContainsKey("killCounts")) {
                int[] counts = tag.GetIntArray("killCounts");
                int length = Math.Min(counts.Length, NPC.killCount.Length);
                for (int i = 0; i < length; i++) {
                    if (counts[i] > NPC.killCount[i]) {
                        NPC.killCount[i] = counts[i];
                    }
                }
            }
            if (tag.ContainsKey("powers")) {
                try {
                    using MemoryStream stream = new(tag.GetByteArray("powers"), writable: false);
                    using BinaryReader reader = new(stream);
                    CreativePowerManager.Instance.LoadFromWorld(reader, Main.curRelease);
                } catch (Exception ex) {
                    ScenarioPlatform.Error("Scenario.Ledger.Powers", $"[Scenarios] Carrying the journey powers across the stage change threw: {ex}");
                }
            }
        }

        private static void HoldTime(VaultScenario scenario, TagCompound tag) {
            if (tag.TryGet("time", out TagCompound time)) {
                _timeOwner = scenario;
                _pendingTime = time;
            }
        }
    }
}
