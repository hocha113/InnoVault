namespace InnoVault
{
    /// <summary>
    /// InnoVault网络消息类型枚举
    /// </summary>
    internal enum MessageType : byte
    {
        /// <summary>NPC重制节点统一同步事件（ai/Other/SyncVar，双向，服务端校验后广播）</summary>
        NPCOverride_Event,
        /// <summary>客户端进图时请求服务端补发当前所有NPC重制节点的冷数据</summary>
        NPCOverride_BulkRequest,
        /// <summary>客户端请求服务端的重制节点集合校验数据</summary>
        NPCOverride_HandshakeRequest,
        /// <summary>服务端下发重制节点集合校验数据</summary>
        NPCOverride_Handshake,
        /// <summary>NPC重制节点高频批量delta同步（二期）</summary>
        NPCOverride_DeltaBatch,
        /// <summary>处理：在世界中放置</summary>
        Handler_PlaceInWorld,
        /// <summary>处理：TP实体数据</summary>
        Handler_TileProcessorIndsData,
        /// <summary>处理：客户端TP死亡</summary>
        Handler_TPDeathByClient,
        /// <summary>发送到客户端：TP数据</summary>
        SendToClient_TPData,
        /// <summary>获取服务器：TP数据块</summary>
        GetServer_TPDataChunk,
        /// <summary>获取服务器：最大TP数据块数量</summary>
        GetServer_MaxTPDataChunkCount,
        /// <summary>获取服务器：重置TP数据块网络</summary>
        GetServer_ResetTPDataChunkNet,
        /// <summary>处理：TP右键点击</summary>
        Handler_TPRightClick,
        /// <summary>客户端->服务器: 请求生成一个Actor</summary>
        ActorSpawnRequest,
        /// <summary>服务器->客户端: 生成一个Actor(含全量状态), 也用于晚加入快照</summary>
        ActorSpawn,
        /// <summary>服务器->客户端: 批量Actor增量状态更新</summary>
        ActorUpdate,
        /// <summary>服务器->客户端: 销毁一个Actor</summary>
        ActorKill,
        /// <summary>客户端->服务器: 请求销毁一个Actor</summary>
        ActorKillRequest,
        /// <summary>客户端->服务器: 晚加入时请求全部活跃Actor的快照</summary>
        ActorFullSyncRequest,
        /// <summary>请求指定玩家的基础网络数据快照</summary>
        PlayerNet_RequestSnapshot,
        /// <summary>服务端要求目标客户端采样并响应基础网络数据</summary>
        PlayerNet_QuerySnapshot,
        /// <summary>玩家基础网络数据快照</summary>
        PlayerNet_Snapshot,
        /// <summary>释放指定玩家基础网络数据兴趣</summary>
        PlayerNet_ReleaseInterest,
    }
}
