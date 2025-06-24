using InnoVault.GameSystem;
using InnoVault.TileProcessors;
using System.IO;
using Terraria.ModLoader;

namespace InnoVault
{
    internal class VaultNetWork : IVaultLoader
    {
        public enum MessageType : byte
        {
            PlaceInWorldSync,
            TPNetWork,
            ClientRequest_TPData_Send,
            Handle_TPData_Receive,
            ServerTPDeathVerify,
            NPCOverrideAI,
            NPCOverrideOtherAI,
        }

        public static void HandlePacket(Mod mod, BinaryReader reader, int whoAmI) {
            MessageType type = (MessageType)reader.ReadByte();

            if (type == MessageType.PlaceInWorldSync) {
                TileProcessorNetWork.PlaceInWorldNetReceive(mod, reader, whoAmI);
            }
            else if (type == MessageType.TPNetWork) {
                TileProcessorNetWork.TileProcessorReceiveData(reader, whoAmI);
            }
            else if (type == MessageType.ClientRequest_TPData_Send) {
                TileProcessorNetWork.ServerRecovery_TPData(reader, whoAmI);
            }
            else if (type == MessageType.Handle_TPData_Receive) {
                TileProcessorNetWork.Handle_TPData_Receive(reader);
            }
            else if (type == MessageType.ServerTPDeathVerify) {
                TileProcessorNetWork.HandlerTPDeathByClient(reader);
            }
            else if (type == MessageType.NPCOverrideAI) {
                NPCOverride.NetAIReceive(reader);
            }
            else if (type == MessageType.NPCOverrideOtherAI) {
                NPCOverride.OtherNetWorkReceiveHander(reader);
            }
        }
    }
}
