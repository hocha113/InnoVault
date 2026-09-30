using InnoVault.Actors;
using InnoVault.Combat;
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
            NPCOverrideNetWork.HandlePacket(type, reader, whoAmI);
            TileProcessorNetWork.HandlePacket(type, mod, reader, whoAmI);
            ActorNetWork.Handle(type, mod, reader, whoAmI);
            PlayerNetworkCore.HandlePacket(type, reader, whoAmI);
            if (type == MessageType.HitEvent) {
                HitEvents.Handle(reader, whoAmI);
            }
        }
    }
}
