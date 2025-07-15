using InnoVault.GameSystem;
using InnoVault.TileProcessors;
using System.IO;
using Terraria.ModLoader;

namespace InnoVault
{
    internal class VaultNetWork : IVaultLoader
    {
        internal enum MessageType : byte
        {
            NPCOverrideAI,
            NPCOverrideOtherAI,
            Handler_PlaceInWorld,
            Handler_TileProcessorIndsData,
            Handler_TPDeathByClient,
            SendToClient_TPData,
            SendToClient_TPDataChunk,
            SendToClient_MaxTPDataChunkCount,
            GetSever_TPDataChunk,
            GetSever_MaxTPDataChunkCount,
            GetSever_TPDataChunkPacketStartPos,
        }

        internal static void HandlePacket(Mod mod, BinaryReader reader, int whoAmI) {
            MessageType type = (MessageType)reader.ReadByte();
            NPCOverride.HandlePacket(type, reader);
            TileProcessorNetWork.HandlePacket(type, mod, reader, whoAmI);
        }
    }
}
