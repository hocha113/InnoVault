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

        /// <summary>挂到场景实例上时调用，此时场景仍在 Compose 中，尚未激活</summary>
        protected virtual void OnAttach() { }

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

        internal void InvokeActivate(ScenarioTransition transition) => OnActivate(transition);
        internal void InvokeStart() => OnStart();
        internal void InvokeUpdate() => Update();
        internal void InvokeDeactivate(ScenarioTransition transition) => OnDeactivate(transition);
    }
}
