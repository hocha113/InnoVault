using InnoVault.GameContent;
using InnoVault.GameSystem;
using InnoVault.TileProcessors;
using System.IO;
using Terraria.ModLoader;

namespace InnoVault
{
    internal class VaultNetwork : IVaultLoader
    {
        internal enum MessageType : byte
        {
            NPCOverrideOtherAI,
            NPCOverrideNetWork,
            SendToClient_NPCOverrideRequestAllData,
            Handler_NPCOverrideRequestAllData,
            RequestNPCOverrideValidation,
            SyncNPCOverrideValidation,
            TetheredPlayer,
            TetheredPlayer_DownLeft,
            TetheredPlayer_DownRight,
            TetheredPlayer_InMousePos,
            AddStaticImmunity,
            AddStaticImmunityByProj,
            AddStaticImmunityByItem,
            SetStaticImmunity,
            UseStaticImmunity,
            Handler_PlaceInWorld,
            Handler_TileProcessorIndsData,
            Handler_TPDeathByClient,
            SendToClient_TPData,
            SendToClient_TPDataChunk,
            SendToClient_MaxTPDataChunkCount,
            GetServer_TPDataChunk,
            GetServer_MaxTPDataChunkCount,
            GetServer_TPDataChunkPacketStartPos,
            GetServer_ResetTPDataChunkNet,
            Handler_TPRightClick,
        }

        internal static void HandlePacket(Mod mod, BinaryReader reader, int whoAmI) {
            MessageType type = (MessageType)reader.ReadByte();
            NPCOverride.HandlePacket(type, reader, whoAmI);
            StaticImmunitySystem.HandlePacket(type, reader, whoAmI);
            TetheredPlayer.HandlePacket(type, reader, whoAmI);
            TileProcessorNetWork.HandlePacket(type, mod, reader, whoAmI);
        }
    }
}
