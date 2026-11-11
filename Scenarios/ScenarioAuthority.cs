namespace InnoVault.Scenarios
{
    /// <summary>
    /// 场景请求的种类
    /// </summary>
    public enum ScenarioRequestKind
    {
        /// <summary>激活一个不拥有舞台的场景</summary>
        Activate,
        /// <summary>停用一个不拥有舞台的场景实例</summary>
        Deactivate,
        /// <summary>换到一个拥有舞台的场景；回到主世界也是它，目标是 <see cref="RootScenario"/></summary>
        Travel,
    }

    /// <summary>
    /// 多人模式下一个客户端发给服务器的场景请求。服务器在执行之前把它交给 <see cref="VaultScenario.AuthorizeRequest"/> 与
    /// <see cref="GlobalScenario.AuthorizeRequest"/> 裁决<br/>
    /// 框架的缺省是放行：任何客户端都可以发起，换舞台会带走服务器上的所有玩家。谁有权发起、要不要其他玩家同意，由消费者在裁决钩子里决定；
    /// 需要先征求同意时，在钩子里拒绝并留下这个对象，条件满足后在服务器上调用 <see cref="ScenarioDirector.Execute"/> 照原样执行<br/>
    /// 请求里的参数来自客户端，不可信：钩子应当自己校验 <see cref="Instance"/> 上的值与 <see cref="Requester"/> 的处境
    /// </summary>
    public sealed class ScenarioRequest
    {
        internal ScenarioRequest(ScenarioRequestKind kind, VaultScenario template, VaultScenario instance, VaultScenario parent,
            VaultScenario from, string anchor, int requester, byte[] arguments) {
            Kind = kind;
            Template = template;
            Instance = instance;
            Parent = parent;
            From = from;
            Anchor = anchor;
            Requester = requester;
            Arguments = arguments ?? [];
        }

        /// <summary>请求的种类</summary>
        public ScenarioRequestKind Kind { get; }

        /// <summary>目标场景的模板；停用请求里是要停用的实例的模板</summary>
        public VaultScenario Template { get; }

        /// <summary>
        /// 激活与换舞台：已经按请求里的参数组合好、尚未激活的实例，可以读它的字段与组件来校验；
        /// 停用：要停用的那个处于激活中的实例
        /// </summary>
        public VaultScenario Instance { get; }

        /// <summary>激活请求指定的父场景实例，没有指定时为空</summary>
        public VaultScenario Parent { get; }

        /// <summary>
        /// 换舞台请求里是当前的舞台场景实例，即请求被执行时将被离开的那一个；其余请求里为空。
        /// 它的 <see cref="VaultScenario.AuthorizeRequest"/> 也会被问到：要不要放人走，由被离开的舞台自己说了算
        /// </summary>
        public VaultScenario From { get; }

        /// <summary>换舞台请求的落点锚点名，没有指定时为空</summary>
        public string Anchor { get; }

        /// <summary>发来请求的玩家索引（<c>Main.player</c> 的下标）</summary>
        public int Requester { get; }

        //客户端按 [SyncVar] 成员序列化的激活参数，Execute 时原样套用到新实例上
        internal byte[] Arguments { get; }
    }
}
