using System.Collections.Generic;
using Terraria.WorldBuilding;

namespace InnoVault.Scenarios
{
    /// <summary>
    /// 作用于所有场景的全局钩子，其他模组借它参与任意场景：给场景挂节点、否决激活、往舞台的生成里加步骤、在转场数据包里携带自己的数据<br/>
    /// 激活时的顺序：<see cref="PreActivate"/> → 各节点 → 场景 → <see cref="OnActivate"/>；停用时 <see cref="OnDeactivate"/> 最先执行
    /// </summary>
    public abstract class GlobalScenario : VaultType<GlobalScenario>
    {
        /// <summary>
        /// 在场景自身的 Compose 之后调用，可以给场景追加节点或子场景。与 Compose 一样只做声明，不要产生副作用
        /// </summary>
        /// <param name="scenario">正在组合的场景实例</param>
        /// <param name="composer">组合构建器</param>
        public virtual void Compose(VaultScenario scenario, ScenarioComposer composer) { }

        /// <summary>激活前的否决点，任意一个全局钩子返回 <see langword="false"/> 都会否决这次激活</summary>
        /// <param name="scenario">将要激活的场景实例</param>
        /// <param name="transition">将要发生的转场</param>
        public virtual bool CanActivate(VaultScenario scenario, ScenarioTransition transition) => true;

        /// <summary>
        /// 多人模式下服务器收到客户端发来的请求时调用，在场景自身的 <see cref="VaultScenario.AuthorizeRequest"/> 之后；
        /// 任意一个全局钩子返回 <see langword="false"/> 都会以 <see cref="ScenarioRejectReason.Unauthorized"/> 拒绝这次请求。
        /// 发起权限、投票与确认这类门阀从这里接入，见 <see cref="ScenarioRequest"/>
        /// </summary>
        /// <param name="scenario">请求涉及的场景实例，与 <see cref="ScenarioRequest.Instance"/> 相同</param>
        /// <param name="request">客户端的请求</param>
        public virtual bool AuthorizeRequest(VaultScenario scenario, ScenarioRequest request) => true;

        /// <summary>
        /// 一块舞台的生成步骤收集完之后调用，可以追加、插入或移除步骤：其他模组借此往指定的舞台里加自己的内容。
        /// 只在裁决端、只在需要生成时调用
        /// </summary>
        /// <param name="scenario">即将生成舞台的场景实例（尚未激活）</param>
        /// <param name="passes">按执行顺序排列的生成步骤，可以直接改写这个列表</param>
        public virtual void ModifyPasses(VaultScenario scenario, List<GenPass> passes) { }

        /// <summary>
        /// 一块舞台即将生成时调用：世界已经清空并按舞台的尺寸定好，生成步骤尚未执行。只在裁决端、只在需要生成时调用，
        /// 单人运行在生成线程上。生成步骤要读的世界状态（例如随转场带来的特殊种子旗标）在这里写入
        /// </summary>
        /// <param name="scenario">即将生成舞台的场景实例（尚未激活）</param>
        /// <param name="transition">这次换舞台，数据包已经由离开的一方写好</param>
        public virtual void PreGenerate(VaultScenario scenario, ScenarioTransition transition) { }

        /// <summary>
        /// 场景激活时最先调用，早于各节点与场景自身的 OnActivate。随转场带来的状态（继承的进度、数据包里的世界旗标）在这里写进世界，
        /// 场景自己的 OnActivate 读到的就已经是带过来之后的值。时机与线程同 <see cref="OnActivate"/>
        /// </summary>
        /// <param name="scenario">正在激活的场景实例</param>
        /// <param name="transition">引起这次激活的转场</param>
        public virtual void PreActivate(VaultScenario scenario, ScenarioTransition transition) { }

        /// <summary>场景激活时调用，在场景自身的 OnActivate 之后</summary>
        /// <param name="scenario">被激活的场景实例</param>
        /// <param name="transition">引起这次激活的转场</param>
        public virtual void OnActivate(VaultScenario scenario, ScenarioTransition transition) { }

        /// <summary>场景进入激活状态后、第一次更新之前调用，在场景自身的 OnStart 之后</summary>
        /// <param name="scenario">场景实例</param>
        public virtual void OnStart(VaultScenario scenario) { }

        /// <summary>
        /// 玩家上次在这个舞台场景里保存退出，这次从菜单进入根世界后，是否换回去。
        /// 返回空表示不表态；任意全局钩子表态后以最后一个表态为准，都不表态时看场景是否挂了 <see cref="Presets.ResumeOnRelog"/>
        /// </summary>
        /// <param name="scenario">刚组合好、尚未激活的场景实例，只用于查询，不会被激活</param>
        public virtual bool? ResumeAfterRelog(VaultScenario scenario) => null;

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
