using System;

namespace InnoVault.Scenarios
{
    /// <summary>
    /// 场景组合构建器，只在 Compose 期间有效：给场景实例挂节点（可以带着已经挂好的子节点）、声明随它一起激活的子场景<br/>
    /// Compose 每次激活都会对新实例重新执行一遍，所以这里只做声明，不要产生副作用
    /// </summary>
    public sealed class ScenarioComposer
    {
        private bool _sealed;

        internal ScenarioComposer(VaultScenario scenario) {
            Scenario = scenario;
        }

        /// <summary>正在组合的场景实例</summary>
        public VaultScenario Scenario { get; }

        /// <summary>
        /// 给场景挂一个组件。一个组件对象只能挂到一个实例上；一个场景最多挂一个 <see cref="ScenarioStage"/>
        /// </summary>
        /// <param name="component">要挂载的组件</param>
        /// <exception cref="ArgumentNullException">组件为空</exception>
        /// <exception cref="InvalidOperationException">Compose 已经结束，组件已挂到别的实例上，或重复挂载舞台组件</exception>
        public ScenarioComposer Add(ScenarioComponent component) => Add((ScenarioNode)component);

        /// <summary>
        /// 给场景挂一个节点，连同它下面已经挂好的子节点（<see cref="ScenarioNode.AddChild"/> / <see cref="ScenarioNodeExtensions.With"/>）。
        /// 一个节点对象只能挂到一个实例上；一个场景最多挂一个 <see cref="ScenarioStage"/>，而且必须直接挂在场景上<br/>
        /// 第一个直接挂在场景上的 <see cref="ScenarioArea"/> 是场景的主区域，没有挂在别的区域下面的节点都以它为作用范围；
        /// 其余区域只管自己下面的子节点
        /// </summary>
        /// <param name="node">要挂载的节点</param>
        /// <exception cref="ArgumentNullException">节点为空</exception>
        /// <exception cref="InvalidOperationException">Compose 已经结束，节点已挂在别处，或重复挂载舞台组件</exception>
        public ScenarioComposer Add(ScenarioNode node) {
            ArgumentNullException.ThrowIfNull(node);
            ThrowIfSealed();
            Scenario.AttachComponent(node);
            return this;
        }

        /// <summary>
        /// 声明一个子场景：本场景激活后它随之激活，本场景停用前它先停用。子场景必须是不拥有舞台的场景
        /// </summary>
        /// <typeparam name="T">子场景类型</typeparam>
        /// <exception cref="InvalidOperationException">Compose 已经结束，子场景类型没有注册，或与本场景是同一类型</exception>
        public ScenarioComposer Child<T>() where T : VaultScenario {
            ThrowIfSealed();
            VaultScenario template = VaultScenario.FindTemplate(typeof(T))
                ?? throw new InvalidOperationException($"Child scenario {typeof(T).FullName} is not registered");
            Scenario.AddChildTemplate(template);
            return this;
        }

        internal void Seal() {
            _sealed = true;
            Scenario.FinishCompose();
        }

        private void ThrowIfSealed() {
            if (_sealed) {
                throw new InvalidOperationException($"{Scenario.Key} has finished composing; nodes and children can only be declared inside Compose");
            }
        }
    }
}
