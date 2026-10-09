namespace InnoVault.Scenarios
{
    /// <summary>
    /// 作用于所有场景的全局钩子，其他模组借它参与任意场景：给场景挂组件、否决激活、在转场数据包里携带自己的数据<br/>
    /// 激活时全局钩子在组件与场景之后执行，停用时在它们之前执行
    /// </summary>
    public abstract class GlobalScenario : VaultType<GlobalScenario>
    {
        /// <summary>
        /// 在场景自身的 Compose 之后调用，可以给场景追加组件或子场景。与 Compose 一样只做声明，不要产生副作用
        /// </summary>
        /// <param name="scenario">正在组合的场景实例</param>
        /// <param name="composer">组合构建器</param>
        public virtual void Compose(VaultScenario scenario, ScenarioComposer composer) { }

        /// <summary>激活前的否决点，任意一个全局钩子返回 <see langword="false"/> 都会否决这次激活</summary>
        /// <param name="scenario">将要激活的场景实例</param>
        /// <param name="transition">将要发生的转场</param>
        public virtual bool CanActivate(VaultScenario scenario, ScenarioTransition transition) => true;

        /// <summary>场景激活时调用，在场景自身的 OnActivate 之后</summary>
        /// <param name="scenario">被激活的场景实例</param>
        /// <param name="transition">引起这次激活的转场</param>
        public virtual void OnActivate(VaultScenario scenario, ScenarioTransition transition) { }

        /// <summary>场景进入激活状态后、第一次更新之前调用，在场景自身的 OnStart 之后</summary>
        /// <param name="scenario">场景实例</param>
        public virtual void OnStart(VaultScenario scenario) { }

        /// <summary>场景停用时调用，在场景自身的 OnDeactivate 之前</summary>
        /// <param name="scenario">被停用的场景实例</param>
        /// <param name="transition">引起这次停用的转场</param>
        public virtual void OnDeactivate(VaultScenario scenario, ScenarioTransition transition) { }

        /// <inheritdoc/>
        protected override void VaultRegister() {
            Instances.Add(this);
            TypeToInstance[GetType()] = this;
        }

        /// <inheritdoc/>
        public override void VaultSetup() {
            SetStaticDefaults();
        }
    }
}
