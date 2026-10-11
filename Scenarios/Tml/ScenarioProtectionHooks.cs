using System;
using System.Collections.Generic;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace InnoVault.Scenarios
{
    internal enum ProtectionKind
    {
        Mining,
        Placing,
        WallBreaking,
        Hammering,
        Explosions,
        LiquidTools,
        Wiring,
    }

    //物块保护的判定：任意一个处于激活中的保护组件覆盖这一格、打开了对应开关、且没有放行，就拦下
    internal static class ScenarioProtectionRules
    {
        internal static bool Blocks(ProtectionKind kind, Player player, int i, int j) {
            if (ScenarioProtection.Bypassed) {
                return false;
            }
            ScenarioProtection[] protections = ScenarioRuntime.Protections;
            for (int k = 0; k < protections.Length; k++) {
                ScenarioProtection protection = protections[k];
                if (!Enabled(protection, kind) || !protection.Covers(i, j)) {
                    continue;
                }
                try {
                    if (!protection.CanModify(player, i, j)) {
                        return true;
                    }
                } catch (Exception ex) {
                    ScenarioPlatform.Error($"Scenario.CanModify.{protection.Scenario?.Key}", $"[Scenarios] {protection.Scenario?.Key} CanModify threw: {ex}");
                    return true;
                }
            }
            return false;
        }

        private static bool Enabled(ScenarioProtection protection, ProtectionKind kind) => kind switch {
            ProtectionKind.Mining => protection.NoMining,
            ProtectionKind.Placing => protection.NoPlacing,
            ProtectionKind.WallBreaking => protection.NoWallBreaking,
            ProtectionKind.Hammering => protection.NoHammering,
            ProtectionKind.Explosions => protection.NoExplosions,
            ProtectionKind.LiquidTools => protection.NoLiquidTools,
            ProtectionKind.Wiring => protection.NoWiring,
            _ => false,
        };

        //物块钩子里拿到的玩家：只有本地玩家正在用工具时才算玩家造成的改动
        internal static Player ActingPlayer() {
            Player player = Main.LocalPlayer;
            return player != null && player.active && player.itemAnimation > 0 ? player : null;
        }
    }

    internal sealed class ScenarioProtectionTile : GlobalTile
    {
        //挖掘时的光标反馈与原版玩家挖掘路径
        public override bool CanKillTile(int i, int j, int type, ref bool blockDamaged) {
            return !ScenarioProtectionRules.Blocks(ProtectionKind.Mining, ScenarioProtectionRules.ActingPlayer(), i, j);
        }

        //所有经 WorldGen.KillTile 的破坏（弹幕、NPC、连带掉落）在这里兜住
        public override void KillTile(int i, int j, int type, ref bool fail, ref bool effectOnly, ref bool noItem) {
            if (!fail && ScenarioProtectionRules.Blocks(ProtectionKind.Mining, ScenarioProtectionRules.ActingPlayer(), i, j)) {
                fail = true;
                noItem = true;
            }
        }

        public override bool CanPlace(int i, int j, int type) {
            return !ScenarioProtectionRules.Blocks(ProtectionKind.Placing, ScenarioProtectionRules.ActingPlayer(), i, j);
        }

        public override bool CanReplace(int i, int j, int type, int tileTypeBeingPlaced) {
            return !ScenarioProtectionRules.Blocks(ProtectionKind.Placing, ScenarioProtectionRules.ActingPlayer(), i, j);
        }

        public override bool CanExplode(int i, int j, int type) {
            return !ScenarioProtectionRules.Blocks(ProtectionKind.Explosions, null, i, j);
        }

        public override bool Slope(int i, int j, int type) {
            return !ScenarioProtectionRules.Blocks(ProtectionKind.Hammering, ScenarioProtectionRules.ActingPlayer(), i, j);
        }
    }

    internal sealed class ScenarioProtectionWall : GlobalWall
    {
        public override bool CanPlace(int i, int j, int type) {
            return !ScenarioProtectionRules.Blocks(ProtectionKind.Placing, ScenarioProtectionRules.ActingPlayer(), i, j);
        }

        public override bool CanExplode(int i, int j, int type) {
            return !ScenarioProtectionRules.Blocks(ProtectionKind.Explosions, null, i, j);
        }

        public override void KillWall(int i, int j, int type, ref bool fail) {
            if (!fail && ScenarioProtectionRules.Blocks(ProtectionKind.WallBreaking, ScenarioProtectionRules.ActingPlayer(), i, j)) {
                fail = true;
            }
        }
    }

    //液体工具与电线工具没有物块钩子，在使用时按光标格拦截
    internal sealed class ScenarioProtectionItem : GlobalItem
    {
        private static readonly HashSet<int> _liquidTools = [
            ItemID.EmptyBucket, ItemID.WaterBucket, ItemID.LavaBucket, ItemID.HoneyBucket,
            ItemID.BottomlessBucket, ItemID.BottomlessLavaBucket, ItemID.BottomlessHoneyBucket, ItemID.BottomlessShimmerBucket,
            ItemID.SuperAbsorbantSponge, ItemID.LavaAbsorbantSponge, ItemID.HoneyAbsorbantSponge, ItemID.UltraAbsorbantSponge,
        ];

        private static readonly HashSet<int> _wiringTools = [
            ItemID.Wrench, ItemID.BlueWrench, ItemID.GreenWrench, ItemID.YellowWrench, ItemID.MulticolorWrench,
            ItemID.WireCutter, ItemID.Actuator, ItemID.ActuationRod, ItemID.WireKite,
        ];

        public override bool CanUseItem(Item item, Player player) {
            if (player.whoAmI != Main.myPlayer || ScenarioRuntime.Protections.Length == 0) {
                return true;
            }
            ProtectionKind? kind = _liquidTools.Contains(item.type) ? ProtectionKind.LiquidTools
                : _wiringTools.Contains(item.type) ? ProtectionKind.Wiring
                : null;
            if (kind is not ProtectionKind toolKind) {
                return true;
            }
            return !ScenarioProtectionRules.Blocks(toolKind, player, Player.tileTargetX, Player.tileTargetY);
        }
    }
}
