namespace InnoVault.Scenarios
{
    /// <summary>
    /// 分支节点：没有自己的行为，只用来把一组节点收在一起。开关它（<see cref="ScenarioNode.Enabled"/>）就是开关整棵子树，
    /// 适合按玩法阶段成组切换规则、环境、镜头与区域：在 Compose 里给每个阶段建一个分支，运行时按路径取出来开关
    /// （<see cref="VaultScenario.GetNode{T}(string)"/>）<br/>
    /// 它不改变子节点的作用范围：子节点听的仍然是最近的祖先区域（<see cref="ScenarioNode.ScopeArea"/>）
    /// </summary>
    public class ScenarioBranch : ScenarioNode
    {
        /// <summary>建一个分支节点</summary>
        /// <param name="name">节点名，为空时是类型名</param>
        public ScenarioBranch(string name = null) {
            if (name != null) {
                Name = name;
            }
        }
    }
}
