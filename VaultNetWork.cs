using InnoVault.GameContent;
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
            TetheredPlayer,
            TetheredPlayer_DownLeft,
            TetheredPlayer_DownRight,
            TetheredPlayer_InMousePos,
            AddStaticImmunity,
            SetStaticImmunity,
            UseStaticImmunity,
            Handler_PlaceInWorld,
            Handler_TileProcessorIndsData,
            Handler_TPDeathByClient,
            SendToClient_TPData,
            SendToClient_TPDataChunk,
            SendToClient_MaxTPDataChunkCount,
            GetSever_TPDataChunk,
            GetSever_MaxTPDataChunkCount,
            GetSever_TPDataChunkPacketStartPos,
            GetSever_ResetTPDataChunkNet,
        }

        internal static void HandlePacket(Mod mod, BinaryReader reader, int whoAmI) {
            MessageType type = (MessageType)reader.ReadByte();
            NPCOverride.HandlePacket(type, reader);
            StaticImmunitySystem.HandlePacket(type, reader, whoAmI);
            TetheredPlayer.HandlePacket(type, reader, whoAmI);
            TileProcessorNetWork.HandlePacket(type, mod, reader, whoAmI);
        }
    }
}
