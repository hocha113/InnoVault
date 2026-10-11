namespace InnoVault.Scenarios.Presets
{
    /// <summary>
    /// 预设组件：玩家在挂了它的舞台场景里保存退出后，下次从菜单进入根世界时自动换回这个场景<br/>
    /// 框架默认留在根世界；<see cref="GlobalScenario.ResumeAfterRelog"/> 的表态优先于这个组件
    /// </summary>
    public sealed class ResumeOnRelog : ScenarioComponent
    {
    }
}
