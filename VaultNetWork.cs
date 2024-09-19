using InnoVault.TileProcessors;
using System.IO;
using Terraria.ModLoader;

namespace InnoVault
{
    internal class VaultNetWork : IVaultLoader
    {
        public enum MessageType : byte
        {
            PlaceInWorldSync, // 新增消息类型，用于同步放置操作
        }

        public static void HandlePacket(Mod mod, BinaryReader reader, int whoAmI) {
            MessageType type = (MessageType)reader.ReadByte();

            if (type == MessageType.PlaceInWorldSync) {
                TileProcessorLoader.NetReceive(mod, reader, whoAmI);
            }
        }
    }
}
