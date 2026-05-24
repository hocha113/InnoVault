namespace InnoVault.BehaviorTrees
{
    /// <summary>
    /// 行为树节点 <c>Tick</c> 的返回状态<br/>
    /// 复合节点根据子节点的返回值决定下一步：<see cref="Success"/>/<see cref="Failure"/>终止子节点的多帧执行，<see cref="Running"/>表示"下一帧继续从同一节点开始"
    /// </summary>
    public enum BTStatus
    {
        /// <summary>节点本帧执行成功</summary>
        Success,
        /// <summary>节点本帧执行失败</summary>
        Failure,
        /// <summary>节点尚未完成，下一帧需要继续 tick（中断时需要调用<see cref="BTNode{TContext}.Reset"/>）</summary>
        Running,
    }
}
