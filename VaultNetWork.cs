using InnoVault.TileProcessors;
using System.Collections.Generic;
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
        }

        internal static List<NetWorkEvent> NetWorkEvents = new List<NetWorkEvent>();

        public static void HandlePacket(Mod mod, BinaryReader reader, int whoAmI) {
            MessageType type = (MessageType)reader.ReadByte();

            if (type == MessageType.PlaceInWorldSync) {
                TileProcessorLoader.PlaceInWorldNetReceive(mod, reader, whoAmI);
            }
            else if (type == MessageType.TPNetWork) {
                TileProcessorLoader.TileProcessorReceiveData(reader, whoAmI);
            }
            else if (type == MessageType.ClientRequest_TPData_Send) {
                TileProcessorLoader.ServerRecovery_TPData(whoAmI);
            }
            else if (type == MessageType.Handle_TPData_Receive) {
                TileProcessorLoader.Handle_TPData_Receive(reader);
            }
        }

        public static void UpdateNetWorkEvent() {
            if (VaultUtils.isSinglePlayer) {
                NetWorkEvents = new List<NetWorkEvent>();
                return;
            }
            foreach (var netWorkEvent in NetWorkEvents) {
                netWorkEvent.Update();
            }
            NetWorkEvents.RemoveAll(n => n.sendTime <= 0);
        }
    }

    internal class NetWorkEvent
    {
        public int sendTime;
        public virtual void SendEvent() { }
        public void Update() {
            if (--sendTime <= 0) {
                SendEvent();
            }
        }
    }

    internal class TPNetWorkEvent : NetWorkEvent
    {
        TileProcessor tpEntity;
        public TPNetWorkEvent(int sendTime, TileProcessor tpEntity) {
            this.sendTime = sendTime;
            this.tpEntity = tpEntity;
        }
        public override void SendEvent() => TileProcessorLoader.TileProcessorSendData(tpEntity);
        public static void Add(int sendTime, TileProcessor tpEntity) 
            => VaultNetWork.NetWorkEvents.Add(new TPNetWorkEvent(sendTime, tpEntity));
    }

    internal class VaultNetSystem : ModSystem
    {
        public override void PostUpdateEverything() => VaultNetWork.UpdateNetWorkEvent();
    }
}
