namespace InnoVault.Scenarios
{
    internal enum ScenarioRequestKind
    {
        Activate,
        Deactivate,
        Travel,
    }

    //一次请求在进入执行之前交给裁决方的全部信息；多人实现时它就是请求包的内容
    internal readonly struct ScenarioRequest(ScenarioRequestKind kind, VaultScenario target, VaultScenario parent)
    {
        public ScenarioRequestKind Kind { get; } = kind;
        //激活与切换时是模板，停用时是实例
        public VaultScenario Target { get; } = target;
        public VaultScenario Parent { get; } = parent;
    }

    //权威裁决：每个请求先经它再执行。返回 None 表示放行
    internal interface IScenarioAuthority
    {
        ScenarioRejectReason Review(in ScenarioRequest request);
    }

    //单人：本地就是权威，规则校验由导演层统一完成
    internal sealed class LocalScenarioAuthority : IScenarioAuthority
    {
        public ScenarioRejectReason Review(in ScenarioRequest request) => ScenarioRejectReason.None;
    }

    //多人（客户端与服务器）：v1 一律拦截，导演层在多人下不开会话
    internal sealed class MultiplayerScenarioGate : IScenarioAuthority
    {
        public ScenarioRejectReason Review(in ScenarioRequest request) => ScenarioRejectReason.MultiplayerUnsupported;
    }
}
