using System;
using Terraria.Localization;

namespace InnoVault.Scenarios.Presets
{
    /// <summary>
    /// 预设组件：身处挂了它的舞台场景时，暂停菜单（设置窗口）下方多出一个返回按钮，点一下就离开这块舞台：缺省回主世界，
    /// 指定了 <see cref="Destination"/> 时换到那块舞台（多人模式下是发给服务器的请求，照常经裁决钩子）<br/>
    /// 进出要经过自己的入口（先做检查、演出或表决）的模组派生一个子类，重写 <see cref="Request"/><br/>
    /// 不挂时暂停菜单保持原样，怎么离开由场景自己决定。在舞台里保存并退出后下次进来去哪里，见 <see cref="ResumeOnRelog"/>
    /// </summary>
    public class PauseMenuReturn : ScenarioComponent
    {
        /// <summary>
        /// 按钮上的文字，为空时用框架的缺省文本：回主世界是 <see cref="ScenarioText.ReturnHome"/>，去别的舞台是 <see cref="ScenarioText.Return"/>
        /// </summary>
        public LocalizedText Text { get; init; }

        /// <summary>目的地里的落点锚点名，为空时按这次换舞台的缺省落点（回主世界时见 <see cref="ScenarioStage.ExitAnchor"/>）</summary>
        public string Anchor { get; init; }

        /// <summary>
        /// 要换去的舞台场景类型（<see cref="VaultScenario"/> 的子类），为空时回主世界。从一块舞台里再进去的下一层舞台，用它退回上一层
        /// </summary>
        public Type Destination { get; init; }

        /// <summary>
        /// 按钮此刻能不能点，缺省看导演层此刻能不能受理请求（<see cref="ScenarioDirector.Availability"/>）。不能点时按钮照常显示，点了只有一声提示音
        /// </summary>
        protected internal virtual bool Available => ScenarioDirector.Availability == ScenarioRejectReason.None;

        /// <summary>
        /// 点下按钮时调用（主线程，只在点按钮的那个客户端上），返回请求有没有被受理：受理后设置窗口随之收起。
        /// 缺省按 <see cref="Destination"/> 与 <see cref="Anchor"/> 向导演层发起；重写它可以改走自己的进出入口
        /// </summary>
        protected internal virtual bool Request() {
            ScenarioResult result = Destination == null
                ? ScenarioDirector.ReturnHome(Anchor)
                : ScenarioDirector.Travel(VaultScenario.FindTemplate(Destination), Anchor);
            if (result.Accepted || result.Pending) {
                return true;
            }
            ScenarioPlatform.Info($"[Scenarios] Leaving {Scenario?.Key} from the pause menu was rejected: {result}");
            return false;
        }

        /// <inheritdoc/>
        protected override string Validate()
            => Destination != null && !typeof(VaultScenario).IsAssignableFrom(Destination) ? $"{Destination.FullName} is not a VaultScenario" : null;
    }
}
