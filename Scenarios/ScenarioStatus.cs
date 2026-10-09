namespace InnoVault.Scenarios
{
    /// <summary>
    /// 场景实例的生命周期状态。实例只走一遍 <see cref="Pending"/> → <see cref="Activating"/> → <see cref="Active"/> →
    /// <see cref="Deactivating"/> → <see cref="Ended"/>，停用后不再复用；场景模板永远停在 <see cref="Pending"/>
    /// </summary>
    public enum ScenarioStatus
    {
        /// <summary>已创建、尚未激活；场景模板始终处于这个状态</summary>
        Pending,
        /// <summary>正在激活：组件、场景与全局钩子的 OnActivate 正在执行</summary>
        Activating,
        /// <summary>已激活，参与每帧更新</summary>
        Active,
        /// <summary>正在停用：子场景、全局钩子、场景与组件的 OnDeactivate 正在执行</summary>
        Deactivating,
        /// <summary>已停用，实例不会再被激活</summary>
        Ended,
    }
}
