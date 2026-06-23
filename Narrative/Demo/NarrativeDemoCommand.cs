using InnoVault.Narrative.Core;
using InnoVault.Narrative.Portraits;
using InnoVault.Narrative.Runtime;
using Terraria;
using Terraria.ModLoader;

namespace InnoVault.Narrative.Demo
{
    /// <summary>
    /// 启动内置 Narrative 演示场景的调试命令：<c>/narrativedemo</c><br/>
    /// 它只注册一个临时角色档案并启动演示场景；演示奖励自包含发放，不会改动全局奖励服务
    /// </summary>
    internal sealed class NarrativeDemoCommand : ModCommand
    {
        public override CommandType Type => CommandType.Chat;
        public override string Command => "narrativedemo";
        public override string Description => "Start the InnoVault Narrative demo scenario";

        public override void Action(CommandCaller caller, string input, string[] args) {
            if (Main.dedServ) {
                return;
            }

            PortraitRegistry.Register(CharacterId.ForMod("InnoVault", "Guide")).Name("Guide");

            if (!NarrativeRunner.Begin<NarrativeDemoScenario>()) {
                caller.Reply("Narrative demo scenario was not found.");
            }
        }
    }
}
