using InnoVault.Actors;
using InnoVault.GameSystem;
using InnoVault.TileProcessors;
using InnoVault.VaultNetWork;
using System.IO;
using Terraria.ModLoader;

namespace InnoVault
{
    /// <summary>
    /// InnoVault网络消息类型枚举
    /// </summary>
    internal enum MessageType : byte
    {
        /// <summary>NPC重写的其他AI</summary>
        NPCOverrideOtherAI,
        /// <summary>NPC重写网络</summary>
        NPCOverrideNetWork,
        /// <summary>发送到客户端：请求所有NPC重写数据</summary>
        SendToClient_NPCOverrideRequestAllData,
        /// <summary>处理：NPC重写请求所有数据</summary>
        Handler_NPCOverrideRequestAllData,
        /// <summary>请求NPC重写验证</summary>
        RequestNPCOverrideValidation,
        /// <summary>同步NPC重写验证</summary>
        SyncNPCOverrideValidation,
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
        /// <summary>新建Actor</summary>
        NewActor,
        /// <summary>Actor数据</summary>
        ActorData,
        /// <summary>销毁Actor</summary>
        KillActor,
        /// <summary>请求活跃的Actor</summary>
        RequestActiveActors,
        /// <summary>请求指定玩家的基础网络数据快照</summary>
        PlayerNet_RequestSnapshot,
        /// <summary>服务端要求目标客户端采样并响应基础网络数据</summary>
        PlayerNet_QuerySnapshot,
        /// <summary>玩家基础网络数据快照</summary>
        PlayerNet_Snapshot,
        /// <summary>释放指定玩家基础网络数据兴趣</summary>
        PlayerNet_ReleaseInterest,
    }

    internal class VaultNetMessage : IVaultLoader
    {
        internal static void HandlePacket(Mod mod, BinaryReader reader, int whoAmI) {
            MessageType type = (MessageType)reader.ReadByte();
            NPCOverride.HandlePacket(type, reader, whoAmI);
            TileProcessorNetWork.HandlePacket(type, mod, reader, whoAmI);
            ActorNetWork.Handle(type, mod, reader, whoAmI);
            PlayerNetworkCore.HandlePacket(type, reader, whoAmI);
        }
    }
}
