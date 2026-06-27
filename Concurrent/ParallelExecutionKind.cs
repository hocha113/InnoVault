namespace InnoVault.Concurrent
{
    /// <summary>
    /// 通用的并行更新策略，描述一个实体/元素在更新阶段如何被调度<br/>
    /// 默认<see cref="Serial"/>，与单线程行为逐字节一致；只有显式声明为并行的类型才会进入多线程<br/>
    /// 这是一个与具体业务无关的opt-in词汇，TileProcessor、Actor等子系统共用它
    /// </summary>
    public enum ParallelExecutionKind
    {
        /// <summary>
        /// 串行：在主线程上按原有逻辑顺序更新，行为与历史完全一致（默认值）
        /// </summary>
        Serial,
        /// <summary>
        /// 独立并行：更新逻辑只读写自身实例字段，不触碰其它实体或全局可变状态<br/>
        /// 副作用（生成物/发包/随机数）必须走框架提供的线程安全入口，框架会用全量并行调度它们<br/>
        /// 并行 body 内不得直接调用依赖主线程的原版全局 API（如 <c>Main.rand</c>、<c>Dust.NewDust</c>、<c>Gore.NewGore</c>、
        /// <c>Projectile.NewProjectile</c>、<c>Item.NewItem</c>、<c>NPC.NewNPC</c> 及直接发包等），这类操作必须经框架提供的 <c>Defer*</c> 延迟入口
        /// </summary>
        Independent,
        /// <summary>
        /// 分组并行：与相邻实体互相作用（管道、机器等）<br/>
        /// 框架按声明的邻接把它们连成连通"岛屿"，岛与岛之间并行、岛内串行<br/>
        /// 岛内现有的跨实体读写逻辑无需改动，因为岛内不存在并发
        /// </summary>
        Grouped
    }
}
