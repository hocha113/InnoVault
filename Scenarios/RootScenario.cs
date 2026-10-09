namespace InnoVault.Scenarios
{
    /// <summary>
    /// 根场景：绑定玩家从菜单选中的世界文件。每次从菜单进入世界时激活，保存退出时停用；
    /// 回到主世界就是转场到根场景（<see cref="ScenarioDirector.ReturnHome"/>）<br/>
    /// 想给主世界挂组件的模组，在 <see cref="GlobalScenario.Compose"/> 里判断场景是否为 <see cref="RootScenario"/>
    /// </summary>
    public sealed class RootScenario : VaultScenario
    {
        /// <inheritdoc/>
        protected override void Compose(ScenarioComposer composer) {
            composer.Add(ScenarioWorld.ForRoot());
        }
    }
}
