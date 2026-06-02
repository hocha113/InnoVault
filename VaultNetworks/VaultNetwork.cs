using InnoVault.Actors;
using InnoVault.GameSystem;
using InnoVault.TileProcessors;
using System.IO;
using Terraria.ModLoader;

namespace InnoVault.VaultNetworks
{
    internal static class VaultNetwork
    {
        public static void HandlePacket(BinaryReader reader, Mod mod, int whoAmI) {
            MessageType type = (MessageType)reader.ReadByte();
            NPCOverride.HandlePacket(type, reader, whoAmI);
            TileProcessorNetWork.HandlePacket(type, mod, reader, whoAmI);
            ActorNetWork.Handle(type, mod, reader, whoAmI);
            PlayerNetworkCore.HandlePacket(type, reader, whoAmI);
        }
    }
}
