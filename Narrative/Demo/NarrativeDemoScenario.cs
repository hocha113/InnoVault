using InnoVault.Narrative.Composition;
using InnoVault.Narrative.Core;
using Terraria.ID;

namespace InnoVault.Narrative.Demo
{
    /// <summary>
    /// 框架内置的演示场景，用于验证对话、表情、选择分支、标签跳转、阻塞奖励弹窗与非阻塞提示弹窗。<br/>
    /// 它<b>不配置触发策略</b>，因此不会自动触发，仅可通过 <c>/narrativedemo</c> 命令手动启动
    /// </summary>
    internal sealed class NarrativeDemoScenario : NarrativeScenario
    {
        private static readonly CharacterId Guide = CharacterId.ForMod("InnoVault", "Guide");

        /// <inheritdoc/>
        protected override void Build(NarrativeComposer n) {
            n.Say(Guide, "Welcome to the InnoVault Narrative demo.")
             .Say(Guide, "happy", "This line is tagged with a different expression id.")
             .Choice(Guide, "Would you like a gift?", c => c
                 .Option("yes", "Yes, please", NarrativeTarget.Goto("gift"))
                 .Option("no", "No thanks", NarrativeTarget.Goto("bye")))

             .Label("gift").Say(Guide, "Here you go, take this.")
             .Reward(ItemID.Wood, 10, "Demo Reward")
             .Popup(Popups.Message("Tip", "Popups can also be plain messages.").Claimable(false).Hold(2f), blocking: false)
             .Say(Guide, "Hope you like it.")
             .Goto("end")

             .Label("bye").Say(Guide, "No problem, maybe next time.")

             .Label("end").Say(Guide, "Demo complete. Thanks for trying the framework!");
        }
    }
}
