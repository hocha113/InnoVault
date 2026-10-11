using Mono.Cecil.Cil;
using MonoMod.Cil;
using System;
using System.Reflection;
using Terraria;

namespace InnoVault.Scenarios
{
    //区域判定：高度区域与海滩在 UpdateBiomes 中段算出，生物群落与场景效果在其后读取。
    //绕行 UpdateBiomes 再改标志已经太晚，所以在海滩赋值之后插入一次覆盖
    internal sealed class ZoneTakeover : ScenarioTakeover
    {
        private static readonly MethodInfo _setZoneBeach = typeof(Player).GetProperty(nameof(Player.ZoneBeach))?.GetSetMethod();

        internal override string Name => "Zones";

        internal override string Source => "Terraria/Player.cs UpdateBiomes: ZoneBeach = WorldGen.oceanDepths(...) (the only ZoneBeach assignment in Player.cs)";

        protected override void Install() => IL_Player.UpdateBiomes += Manipulate;

        protected override void Uninstall() => IL_Player.UpdateBiomes -= Manipulate;

        private void Manipulate(ILContext il) {
            try {
                ILCursor cursor = new(il);
                if (_setZoneBeach == null || !cursor.TryGotoNext(MoveType.After, i => i.MatchCall(_setZoneBeach) || i.MatchCallvirt(_setZoneBeach))) {
                    Fail("ZoneBeach setter call not found", il);
                    return;
                }
                //栈：赋值语句之后栈为空，压入 this 交给覆盖调用消费
                cursor.Emit(OpCodes.Ldarg_0);
                cursor.EmitDelegate(static (Player player) => ScenarioZoneRules.Apply(player));
                Succeed();
            } catch (Exception ex) {
                Fail(ex.Message, il);
            }
        }
    }

    internal static class ScenarioZoneRules
    {
        //海洋判定：规则指定为准；各层都没设置时，场景舞台宽度不足两倍海滩宽度（地表全判海滩）就取消
        internal static bool OceanSuppressed() {
            bool? setting = ScenarioRuntime.Rules.Pick(static r => r.NoOceanZone);
            return setting ?? (ScenarioRuntime.InScenarioStage && Main.maxTilesX < 2 * WorldGen.beachDistance);
        }

        internal static bool OceanDepths(int x, int y) => !OceanSuppressed() && WorldGen.oceanDepths(x, y);

        internal static void Apply(Player player) {
            ScenarioLayers<ScenarioRules> rules = ScenarioRuntime.Rules;
            if (!rules.Any && !ScenarioRuntime.InScenarioStage) {
                return;
            }
            if (OceanSuppressed()) {
                player.ZoneBeach = false;
            }
            if (!rules.Any) {
                return;
            }
            if (rules.Pick(static r => r.NoUnderworldZone) == true && player.ZoneUnderworldHeight) {
                player.ZoneUnderworldHeight = false;
                player.ZoneRockLayerHeight = true;
            }
            if (rules.Pick(static r => r.NoSpaceZone) == true && player.ZoneSkyHeight) {
                player.ZoneSkyHeight = false;
                player.ZoneOverworldHeight = true;
            }
            for (int i = 0; i < rules.Count; i++) {
                try {
                    rules[i].ModifyZones(player);
                } catch (Exception ex) {
                    ScenarioPlatform.Error($"Scenario.ModifyZones.{rules[i].Scenario?.Key}", $"[Scenarios] {rules[i].Scenario?.Key} ModifyZones threw: {ex}");
                }
            }
        }
    }
}
