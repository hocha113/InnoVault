namespace InnoVault.BehaviorTrees
{
    /// <summary>
    /// 行为树系统的加载器；目前只承担"卸载时清空所有 BT 探针"的职责，无需扫描程序集（节点是纯 POCO，无注册表）<br/>
    /// 若将来希望"按属性自动注册可复用 BT 模板"，可以在此扩展<see cref="IVaultLoader.LoadData"/>
    /// </summary>
    internal sealed class VaultBehaviorTreeLoader : IVaultLoader
    {
        /// <inheritdoc/>
        void IVaultLoader.UnLoadData() {
            BehaviorTreeDebugger.ClearAll();
        }
    }
}
