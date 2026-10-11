using InnoVault.Scenarios.Presets;
using System;
using Terraria;
using Terraria.GameContent.Events;
using Terraria.ModLoader.IO;

namespace InnoVault.Scenarios
{
    //InheritVanillaProgress 的执行者：离开世界时（世界仍在、尚未存盘）抓取原版进度写进转场数据包，
    //进入时（所有模组 LoadWorldData 之后、玩家出生之前）读回。只在两端有组件时动作
    internal sealed class VanillaProgressCarrier : GlobalScenario
    {
        private const string InheritKey = "InnoVault:VanillaProgress";
        private const string WriteBackKey = "InnoVault:VanillaProgressBack";

        private static readonly (string Name, Func<bool> Get, Action<bool> Set)[] _flags = [
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

        public override void OnDeactivate(VaultScenario scenario, ScenarioTransition transition) {
            if (transition.Kind != ScenarioTransitionKind.Travel || !ReferenceEquals(scenario, transition.From)) {
                return;
            }
            if (transition.To?.GetComponent<InheritVanillaProgress>() != null) {
                transition.Payload[InheritKey] = Capture();
            }
            if (transition.To is RootScenario && scenario.GetComponent<InheritVanillaProgress>() is { WriteBack: true }) {
                transition.Payload[WriteBackKey] = Capture();
            }
        }

        public override void OnActivate(VaultScenario scenario, ScenarioTransition transition) {
            if (transition.Kind != ScenarioTransitionKind.Travel || !ReferenceEquals(scenario, transition.To)) {
                return;
            }
            if (scenario.GetComponent<InheritVanillaProgress>() != null && transition.Payload.TryGet(InheritKey, out TagCompound inherited)) {
                Apply(inherited, overwrite: true);
            }
            if (scenario is RootScenario && transition.Payload.TryGet(WriteBackKey, out TagCompound written)) {
                Apply(written, overwrite: false);
            }
        }

        private static TagCompound Capture() {
            TagCompound tag = [];
            foreach (var flag in _flags) {
                tag[flag.Name] = flag.Get();
            }
            return tag;
        }

        //overwrite 为假时只把 false 改成 true（写回只增不减）
        private static void Apply(TagCompound tag, bool overwrite) {
            foreach (var flag in _flags) {
                if (!tag.ContainsKey(flag.Name)) {
                    continue;
                }
                bool value = tag.GetBool(flag.Name);
                if (overwrite || value) {
                    flag.Set(value);
                }
            }
        }
    }
}
