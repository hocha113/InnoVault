namespace InnoVault.StateMachines
{
    /// <summary>
    /// 状态机的网络同步抽象。具体实现把"当前状态 ID"写入某个被原版网络层自动同步的位置（最常见的是<c>npc.ai[slot]</c>），<br/>
    /// 客户端则从同一位置读取并由<see cref="VaultStateMachine{TContext}.Update"/>反推状态<br/>
    /// 在<see cref="VaultStateMachine{TContext}.NetSync"/>为<see langword="null"/>时，状态机即纯本地驱动，不做任何同步
    /// </summary>
    /// <typeparam name="TContext">状态机上下文类型</typeparam>
    public interface INetStateSync<TContext>
    {
        /// <summary>
        /// 服务端在切换状态后调用，把<paramref name="stateId"/>写到同步介质（通常是<c>npc.ai[slot]</c>）<br/>
        /// 实现需自行设置必要的<c>netUpdate</c>等标志
        /// </summary>
        void WriteState(VaultStateMachine<TContext> machine, int stateId);
        /// <summary>
        /// 客户端每帧读出当前同步介质中的状态 ID；返回 -1 表示"未设置/无效"<br/>
        /// 由<see cref="VaultStateMachine{TContext}.Update"/>对比本地状态后决定是否补正
        /// </summary>
        int ReadState(VaultStateMachine<TContext> machine);
    }
}
