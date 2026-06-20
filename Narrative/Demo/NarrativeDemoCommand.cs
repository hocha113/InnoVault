using Terraria;
using Terraria.ModLoader;

namespace InnoVault.Narrative
{
    /// <summary>
    /// 启动内置 Narrative 演示场景的调试命令：<c>/narrativedemo</c>。<br/>
    /// 它会注册一个临时角色档案、在无消费者注入时挂上演示用奖励服务，然后启动演示场景
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
            NarrativeServices.RewardGrant ??= new DemoRewardGrantService();

            if (!NarrativeRunner.Begin<NarrativeDemoScenario>()) {
                caller.Reply("Narrative demo scenario was not found.");
            }
        }
    }
}
