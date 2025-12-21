using Microsoft.Xna.Framework;
using System.IO;
using Terraria;
using Terraria.ModLoader;
using static InnoVault.VaultNetwork;

namespace InnoVault.Actors
{
    /// <summary>
    /// 处理实体网络同步的类
    /// </summary>
    public class ActorNetWork : IVaultLoader
    {
        /// <summary>
        /// 发送创建新实体的网络数据包
        /// </summary>
        /// <param name="id"></param>
        /// <param name="slot"></param>
        /// <param name="position"></param>
        /// <param name="velocity"></param>
        public static void SendNewActor(int id, int slot, Vector2 position, Vector2 velocity) {
            if (VaultUtils.isSinglePlayer) {
                return;
            }
            ModPacket modPacket = VaultMod.Instance.GetPacket();
            modPacket.Write((byte)MessageType.NewActor);
            modPacket.Write(id);
            modPacket.Write(slot);
            modPacket.WriteVector2(position);
            modPacket.WriteVector2(velocity);
            modPacket.Send();
        }

        /// <summary>
        /// 处理创建新实体的网络数据包
        /// </summary>
        /// <param name="reader"></param>
        public static void HandleNewActor(BinaryReader reader) {
            int id = reader.ReadInt32();
            int slot = reader.ReadInt32();
            Vector2 position = reader.ReadVector2();
            Vector2 velocity = reader.ReadVector2();

            if (slot == -1 || VaultUtils.isServer) {
                if (slot == -1) {
                    slot = ActorLoader.FindNextFreeSlot();
                }

                ActorLoader.AddActor(id, slot, position, velocity);

                ModPacket modPacket = VaultMod.Instance.GetPacket();
                modPacket.Write((byte)MessageType.NewActor);
                modPacket.Write(id);
                modPacket.Write(slot);
                modPacket.WriteVector2(position);
                modPacket.WriteVector2(velocity);
                modPacket.Send();
            }
            else {
                ActorLoader.AddActor(id, slot, position, velocity);
            }
        }

        /// <summary>
        /// 发送实体数据同步包
        /// </summary>
        /// <param name="actor"></param>
        public static void SendActorData(Actor actor) {
            if (VaultUtils.isSinglePlayer) {
                return;
            }
            ModPacket modPacket = VaultMod.Instance.GetPacket();
            modPacket.Write((byte)MessageType.ActorData);
            modPacket.Write(actor.WhoAmI);
            actor.SendSyncData(modPacket);
            modPacket.Send();
        }

        /// <summary>
        /// 接收实体数据同步包
        /// </summary>
        /// <param name="reader"></param>
        /// <param name="whoAmI"></param>
        public static void HandleActorData(BinaryReader reader, int whoAmI) {
            Actor actor = ActorLoader.Actors[reader.ReadInt32()];
            actor?.ReceiveSyncData(reader);
            if (VaultUtils.isServer) {
                ModPacket modPacket = VaultMod.Instance.GetPacket();
                modPacket.Write((byte)MessageType.ActorData);
                modPacket.Write(actor.WhoAmI);
                actor.SendSyncData(modPacket);
                modPacket.Send(-1, whoAmI);
            }
        }

        /// <summary>
        /// 发送销毁实体的网络数据包
        /// </summary>
        /// <param name="whoAmI"></param>
        public static void SendKillActor(int whoAmI) {
            if (VaultUtils.isSinglePlayer) return;

            ModPacket modPacket = VaultMod.Instance.GetPacket();
            modPacket.Write((byte)MessageType.KillActor);
            modPacket.Write(whoAmI);
            modPacket.Send();
        }

        /// <summary>
        /// 处理销毁实体的网络数据包
        /// </summary>
        /// <param name="reader"></param>
        /// <param name="senderWhoAmI"></param>
        public static void HandleKillActor(BinaryReader reader, int senderWhoAmI) {
            int targetWhoAmI = reader.ReadInt32();

            ActorLoader.KillActor(targetWhoAmI, network: false);

            if (VaultUtils.isServer) {
                ModPacket modPacket = VaultMod.Instance.GetPacket();
                modPacket.Write((byte)MessageType.KillActor);
                modPacket.Write(targetWhoAmI);
                modPacket.Send(-1, senderWhoAmI);
            }
        }

        internal static void Handle(MessageType type, Mod mod, BinaryReader reader, int whoAmI) {
            if (type == MessageType.NewActor) {
                HandleNewActor(reader);
            }
            else if (type == MessageType.ActorData) {
                HandleActorData(reader, whoAmI);
            }
            else if (type == MessageType.KillActor) {
                HandleKillActor(reader, whoAmI);
            }
        }
    }
}
