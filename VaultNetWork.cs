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
        }

        public static void HandlePacket(Mod mod, BinaryReader reader, int whoAmI) {
            MessageType type = (MessageType)reader.ReadByte();

            if (type == MessageType.PlaceInWorldSync) {
                TileProcessorLoader.PlaceInWorldNetReceive(mod, reader, whoAmI);
            }
            else if (type == MessageType.TPNetWork) {
                TileProcessorLoader.ReceiveData(reader, whoAmI);
            }
        }
    }
}
