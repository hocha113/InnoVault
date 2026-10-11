namespace InnoVault.Scenarios
{
    /// <summary>
    /// 场景组件基类。在 <see cref="VaultScenario"/> 的 Compose 里经 <see cref="ScenarioComposer.Add"/> 挂到场景实例上，
    /// 随实例创建、激活、停用，一个组件对象只属于一个实例<br/>
    /// 激活时组件先于场景执行，停用时组件在场景之后、按挂载的逆序执行
    /// </summary>
    public abstract class ScenarioComponent
    {
        /// <summary>组件所属的场景实例，挂载后才有值</summary>
        public VaultScenario Scenario { get; private set; }

        /// <summary>
        /// 多个处于激活中的场景挂了同类组件时的叠加层级：越大越靠上，同级按激活顺序，后激活的在上。
        /// 每个属性取最上层已设置的值，没设置的看下一层
        /// </summary>
        public int LayerPriority { get; set; }

        //带舞台坐标的组件（区域、锚点、还原区）只在声明它们的舞台里有意义，挂了它们的场景不能放在会话层
        internal virtual bool BindsToStage => false;

        /// <summary>挂到场景实例上时调用，此时场景仍在 Compose 中，尚未激活</summary>
        protected virtual void OnAttach() { }

        /// <summary>
        /// Compose 结束后调用，检查这个组件的声明是否完整；返回空表示合法，否则请求以
        /// <see cref="ScenarioRejectReason.InvalidComposition"/> 被拒绝，返回值作为说明
        /// </summary>
        protected virtual string Validate() => null;

        /// <summary>所属场景激活时调用，时机与线程见 <see cref="VaultScenario"/> 的 OnActivate</summary>
        /// <param name="transition">引起这次激活的转场</param>
        protected virtual void OnActivate(ScenarioTransition transition) { }

        /// <summary>所属场景进入激活状态后、第一次更新之前调用，主线程</summary>
        protected virtual void OnStart() { }

        /// <summary>所属场景激活期间每个游戏帧调用，先于场景自身的 Update，主线程</summary>
        protected virtual void Update() { }

        /// <summary>所属场景停用时调用，时机与线程见 <see cref="VaultScenario"/> 的 OnDeactivate</summary>
        /// <param name="transition">引起这次停用的转场</param>
        protected virtual void OnDeactivate(ScenarioTransition transition) { }

        internal void Attach(VaultScenario scenario) {
            Scenario = scenario;
            OnAttach();
        }

        internal string InvokeValidate() => Validate();
        internal void InvokeActivate(ScenarioTransition transition) => OnActivate(transition);
        internal void InvokeStart() => OnStart();
        internal void InvokeUpdate() => Update();
        internal void InvokeDeactivate(ScenarioTransition transition) => OnDeactivate(transition);
    }
}
