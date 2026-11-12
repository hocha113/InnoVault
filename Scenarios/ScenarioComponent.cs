namespace InnoVault.Scenarios
{
    /// <summary>
    /// 场景组件：承载一项具体能力的场景节点（规则、镜头、环境、区域、舞台等）。框架自带的能力都是组件，
    /// 自己的能力也从它继承；树形结构、开关、作用范围与生命周期钩子都在 <see cref="ScenarioNode"/> 上<br/>
    /// 在 <see cref="VaultScenario"/> 的 Compose 里经 <see cref="ScenarioComposer.Add(ScenarioComponent)"/> 挂到场景实例上，
    /// 随实例创建、激活、停用，一个组件对象只属于一个实例<br/>
    /// 激活时组件先于场景执行，停用时组件在场景之后、按挂载的逆序执行
    /// </summary>
    public abstract class ScenarioComponent : ScenarioNode
    {
    }
}
