using System.IO;
using Terraria.ModLoader;
using Terraria;
using InnoVault.TileProcessors;

namespace InnoVault
{
    internal class VaultNetWork : IVaultLoader
    {
        public enum MessageType : byte
        {
            TileOperatorLoader,
            TO_InWorld_NetWork,
        }

        public static void HandlePacket(Mod mod, BinaryReader reader, int whoAmI) {
            MessageType type = (MessageType)reader.ReadByte();

            if (type == MessageType.TileOperatorLoader) {
                TileProcessorLoader.NetReceive(mod, reader, whoAmI);
            }
            else if (type == MessageType.TO_InWorld_NetWork) {
                TileProcessorLoader.NetReceive_InWorldTO(mod, reader, whoAmI);
            }
        }
    }
}
