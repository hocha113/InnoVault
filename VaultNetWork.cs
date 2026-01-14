using InnoVault.Actors;
using InnoVault.Dimensions;
using InnoVault.GameContent;
using InnoVault.GameSystem;
using InnoVault.TileProcessors;
using System.IO;
using Terraria.ModLoader;

namespace InnoVault
{
    /// <summary>
    /// InnoVault网络消息类型枚举
    /// </summary>
    public enum MessageType : byte
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
        /// <summary>绑定玩家</summary>
        TetheredPlayer,
        /// <summary>绑定玩家：左下</summary>
        TetheredPlayer_DownLeft,
        /// <summary>绑定玩家：右下</summary>
        TetheredPlayer_DownRight,
        /// <summary>绑定玩家：鼠标位置</summary>
        TetheredPlayer_InMousePos,
        /// <summary>添加静态免疫</summary>
        AddStaticImmunity,
        /// <summary>通过弹幕添加静态免疫</summary>
        AddStaticImmunityByProj,
        /// <summary>通过物品添加静态免疫</summary>
        AddStaticImmunityByItem,
        /// <summary>设置静态免疫</summary>
        SetStaticImmunity,
        /// <summary>使用静态免疫</summary>
        UseStaticImmunity,
        /// <summary>处理：在世界中放置</summary>
        Handler_PlaceInWorld,
        /// <summary>处理：TP实体数据</summary>
        Handler_TileProcessorIndsData,
        /// <summary>处理：客户端TP死亡</summary>
        Handler_TPDeathByClient,
        /// <summary>发送到客户端：TP数据</summary>
        SendToClient_TPData,
        /// <summary>发送到客户端：TP数据块</summary>
        SendToClient_TPDataChunk,
        /// <summary>发送到客户端：最大TP数据块数量</summary>
        SendToClient_MaxTPDataChunkCount,
        /// <summary>获取服务器：TP数据块</summary>
        GetServer_TPDataChunk,
        /// <summary>获取服务器：最大TP数据块数量</summary>
        GetServer_MaxTPDataChunkCount,
        /// <summary>获取服务器：TP数据块包起始位置</summary>
        GetServer_TPDataChunkPacketStartPos,
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
        /// <summary>维度系统消息</summary>
        DimensionMessage,
    }

    internal class VaultNetwork : IVaultLoader
    {
        internal static void HandlePacket(Mod mod, BinaryReader reader, int whoAmI) {
            MessageType type = (MessageType)reader.ReadByte();
            NPCOverride.HandlePacket(type, reader, whoAmI);
            StaticImmunitySystem.HandlePacket(type, reader, whoAmI);
            TetheredPlayer.HandlePacket(type, reader, whoAmI);
            TileProcessorNetWork.HandlePacket(type, mod, reader, whoAmI);
            ActorNetWork.Handle(type, mod, reader, whoAmI);
            DimensionNetwork.HandlePacket(type, reader, whoAmI);
        }
    }
}
