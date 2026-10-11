using MonoMod.Cil;
using System;
using System.Collections.Generic;
using System.Reflection;
using Terraria;

namespace InnoVault.Scenarios
{
    //海洋判定的直接调用方：背景、音乐、刷怪、火把运气不读 ZoneBeach，而是自己调用 WorldGen.oceanDepths。
    //oceanDepths 本身很小，可能已被内联进调用方，绕行它不可靠，所以改写这几个大方法里的调用点（2026-10-10 实机：180 格宽世界里 ZoneBeach 已取消，背景仍是海洋）
    internal sealed class OceanQueryTakeover : ScenarioTakeover
    {
        private static readonly MethodInfo _oceanDepths = typeof(WorldGen).GetMethod(nameof(WorldGen.oceanDepths), BindingFlags.Public | BindingFlags.Static, [typeof(int), typeof(int)]);

        private readonly HashSet<string> _failed = [];

        internal override string Name => "OceanQueries";

        internal override string Source => "WorldGen.oceanDepths call sites in Main.GetPreferredBGStyleForPlayer, Main.DrawBackground, Main.UpdateAudio_DecideOnNewMusic, Main.UpdateAudio_DecideOnTOWMusic, NPC.SpawnNPC, Player.UpdateTorchLuck_ConsumeCountersAndCalculate";

        protected override void Install() {
            IL_Main.GetPreferredBGStyleForPlayer += Manipulate;
            IL_Main.DrawBackground += Manipulate;
            IL_Main.UpdateAudio_DecideOnNewMusic += Manipulate;
            IL_Main.UpdateAudio_DecideOnTOWMusic += Manipulate;
            IL_NPC.SpawnNPC += Manipulate;
            IL_Player.UpdateTorchLuck_ConsumeCountersAndCalculate += Manipulate;
        }

        protected override void Uninstall() {
            IL_Main.GetPreferredBGStyleForPlayer -= Manipulate;
            IL_Main.DrawBackground -= Manipulate;
            IL_Main.UpdateAudio_DecideOnNewMusic -= Manipulate;
            IL_Main.UpdateAudio_DecideOnTOWMusic -= Manipulate;
            IL_NPC.SpawnNPC -= Manipulate;
            IL_Player.UpdateTorchLuck_ConsumeCountersAndCalculate -= Manipulate;
            _failed.Clear();
        }

        private void Manipulate(ILContext il) {
            string method = il.Method.Name;
            try {
                int replaced = 0;
                ILCursor cursor = new(il);
                while (_oceanDepths != null && cursor.TryGotoNext(MoveType.Before, i => i.MatchCall(_oceanDepths))) {
                    //栈：两个 int 进、一个 bool 出，与原调用同形；跳到原调用上的分支改落到新入口
                    cursor.MoveAfterLabels();
                    cursor.EmitDelegate(static (int x, int y) => ScenarioZoneRules.OceanDepths(x, y));
                    cursor.Remove();
                    replaced++;
                }
                if (replaced == 0) {
                    _failed.Add(method);
                    Fail($"no oceanDepths call in {method}", il);
                    return;
                }
                _failed.Remove(method);
                if (_failed.Count == 0) {
                    Succeed();
                }
            } catch (Exception ex) {
                _failed.Add(method);
                Fail($"{method}: {ex.Message}", il);
            }
        }
    }
}
