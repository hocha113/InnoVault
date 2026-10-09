using Terraria.ModLoader.IO;

namespace InnoVault.Scenarios
{
    /// <summary>
    /// 转场的种类
    /// </summary>
    public enum ScenarioTransitionKind
    {
        /// <summary>从菜单进入世界，根场景激活，会话开始</summary>
        SessionStart,
        /// <summary>保存退出回到菜单，所有场景停用，会话结束</summary>
        SessionEnd,
        /// <summary>一个不拥有世界的场景被激活</summary>
        Activation,
        /// <summary>一个不拥有世界的场景被停用</summary>
        Deactivation,
        /// <summary>世界层切换：离开当前世界场景，进入另一个拥有世界的场景</summary>
        Travel,
    }

    /// <summary>
    /// 一次激活、停用或世界切换的描述，在这次转场涉及的所有钩子之间共享同一个对象<br/>
    /// <see cref="From"/> 是被离开或被停用的实例，<see cref="To"/> 是被进入或被激活的实例：
    /// <list type="bullet">
    /// <item><see cref="ScenarioTransitionKind.SessionStart"/>：From 为空，To 为根场景</item>
    /// <item><see cref="ScenarioTransitionKind.SessionEnd"/>：From 为当时的世界场景，To 为空</item>
    /// <item><see cref="ScenarioTransitionKind.Activation"/>：From 为空，To 为被激活的实例</item>
    /// <item><see cref="ScenarioTransitionKind.Deactivation"/>：From 为被停用的实例，To 为空</item>
    /// <item><see cref="ScenarioTransitionKind.Travel"/>：From 为旧世界场景，To 为新世界场景</item>
    /// </list>
    /// 因父场景停用而被连带停用的子场景，收到的是父场景那一次转场
    /// </summary>
    public sealed class ScenarioTransition
    {
        internal ScenarioTransition(ScenarioTransitionKind kind, VaultScenario from, VaultScenario to) {
            Kind = kind;
            From = from;
            To = to;
        }

        /// <summary>转场的种类</summary>
        public ScenarioTransitionKind Kind { get; }

        /// <summary>被离开或被停用的实例，可能为空</summary>
        public VaultScenario From { get; }

        /// <summary>被进入或被激活的实例，可能为空</summary>
        public VaultScenario To { get; }

        /// <summary>
        /// 转场数据包。世界切换时，离开的一方在 OnDeactivate 里写，进入的一方在 OnActivate 里读；
        /// 其他种类的转场里它只是本次转场参与者之间共享的暂存区。框架自己不往里写任何数据
        /// </summary>
        public TagCompound Payload { get; } = [];

        /// <summary>这次转场是否伴随世界层变化（会话开始、会话结束、世界切换）</summary>
        public bool ChangesWorld => Kind is ScenarioTransitionKind.SessionStart
            or ScenarioTransitionKind.SessionEnd
            or ScenarioTransitionKind.Travel;
    }
}
