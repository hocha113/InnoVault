using System.Collections.Generic;
using System.IO;
using System;
using Terraria.DataStructures;
using Terraria.ModLoader;
using Terraria;
using static InnoVault.VaultNetWork;
using static InnoVault.TileProcessors.TileProcessorLoader;

namespace InnoVault.TileProcessors
{
    /// <summary>
    /// 负责关于TP实体的网络工作
    /// </summary>
    public class TileProcessorNetWork
    {
        /// <summary>
        /// 属于TP实体网络工作的独特GUID标签
        /// </summary>
        public const string TP_START_GUID = "{VaultMod_TP_START_GUID_94AE-XYZ-AB34-BY27}";

        /// <inheritdoc/>
        public static void PlaceInWorldNetSend(Mod mod, int type, Point16 point) {
            // 客户端发送同步请求到服务器
            ModPacket packet = mod.GetPacket();
            packet.Write((byte)MessageType.PlaceInWorldSync);
            packet.Write(type);
            packet.WritePoint16(point);
            packet.Send(); //发送到服务器
        }
        /// <inheritdoc/>
        internal static void PlaceInWorldNetReceive(Mod mod, BinaryReader reader, int whoAmI) {
            // 读取放置方块的数据
            int tileType = reader.ReadInt32();
            Point16 point = reader.ReadPoint16();
            AddInWorld(tileType, point, null);
            if (VaultUtils.isServer) {
                ModPacket packet = mod.GetPacket();
                packet.Write((byte)MessageType.PlaceInWorldSync);
                packet.Write(tileType);
                packet.WritePoint16(point);
                packet.Send(-1, whoAmI); //广播给所有其他客户端
            }
        }

        /// <summary>
        /// 发送数据
        /// </summary>
        public static void TileProcessorSendData(TileProcessor tp) {
            if (VaultUtils.isSinglePlayer) {
                return;
            }
            //$"{LoadenName}-SendData: 正在发送数据".LoggerDomp();
            ModPacket modPacket = VaultMod.Instance.GetPacket();
            modPacket.Write((byte)MessageType.TPNetWork);
            modPacket.Write(tp.LoadenName);
            modPacket.WritePoint16(tp.Position);
            tp.SendData(modPacket);
            modPacket.Send();
        }

        /// <summary>
        /// 接收数据
        /// </summary>
        public static void TileProcessorReceiveData(BinaryReader reader, int whoAmI) {
            //"TileProcessorLoader-ReceiveData:正在接收数据".LoggerDomp();
            Dictionary<string, object> data = [];
            string name = reader.ReadString();
            Point16 position = reader.ReadPoint16();
            TileProcessor tileProcessor = null;
            foreach (TileProcessor tp in TP_InWorld) {
                if (tp.LoadenName == name && tp.Position == position) {
                    tileProcessor = tp;
                    tileProcessor.ReceiveData(reader, whoAmI);
                }
            }
            if (tileProcessor != null) {
                if (Main.dedServ) {
                    ModPacket modPacket = VaultMod.Instance.GetPacket();
                    modPacket.Write((byte)MessageType.TPNetWork);
                    modPacket.Write(name);
                    modPacket.WritePoint16(position);
                    tileProcessor.SendData(modPacket);
                    modPacket.Send(-1, whoAmI);
                }
            }
            else {
                throw new Exception($"TileProcessorLoader-ReceiveData: No Corresponding TileProcessor Instance Found : {name}-position[{position}]");
            }
        }

        //服务端是唯一的，即使是主机本质上也只是在本机上托管运行了一个服务端
        //这意味着，如果进入多人模式，这个玩家的客户端是加载于服务端之后的，这里的延时发包将不能解决所有情景下的问题，只能让作为主机的玩家接收到来自服务器的发包信息
        //其他更晚加入的客户端依旧接收不到发包
        //之所以将延时加长到30后主机能接收到发包信息，是因为主机的客户端的加载完成时间于其所托管的服务端是接近的
        //可能只有2~5帧的延迟，所以这个sendTime设置在绝大多数情况下可以让主机客户端吃到发包，但其他客户端则大概率不行
        //对此我给出的解决方案是，让每个客户端在加入后手动向服务器请求一次TP信息，由客户端发送一个代表数据索取的包，服务器收到后向该客户端发送正确的数据并让其接收

        /// <summary>
        /// 让客户端向服务器发出数据请求，请求一个完整的TP数据链
        /// </summary>
        public static void ClientRequest_TPData_Send() {
            if (!VaultUtils.isClient) {
                return;
            }
            //$"TileProcessorLoader-ClientRequest_TPData:玩家{Main.LocalPlayer.name}/客户端id{Main.myPlayer}正在请求服务端TP数据链".LoggerDomp();
            ModPacket modPacket = VaultMod.Instance.GetPacket();
            modPacket.Write((byte)MessageType.ClientRequest_TPData_Send);
            modPacket.Send();
        }

        //另一个需要注意的概念是，世界数据存储在服务端上，退出世界、进入世界的相关钩子如OnWorldLoad和OnWorldUnLoad不会在客户端上运行
        //而存档数据会在服务端关闭后自动写入存档文件，这意味着任意客户端的退出都不能主动更新自己本地的存档数据，只有等待服务端被关闭，这是存储世界数据的一个网络特点
        //所以，不需要试图在卸载世界或者玩家退出服务器时进行任何世界网络数据的同步请求，没必要也没用，服务器自身在开启和关闭时便会处理这一切

        /// <summary>
        /// 向指定客户端发送一个完整的TP数据链
        /// </summary>
        public static void ServerRecovery_TPData(int whoAmI) {
            if (!Main.dedServ) {
                return;
            }
            //"TileProcessorLoader-ServerRecovery_TPData:服务器数据正在响应请求".LoggerDomp();
            ModPacket modPacket = VaultMod.Instance.GetPacket();// 创建一个数据包，用于批量发送多个TP数据
            modPacket.Write((byte)MessageType.Handle_TPData_Receive); // 包类型
            int sendTPCount = 0;

            // 统计活跃的TP数量
            foreach (TileProcessor tp in TP_InWorld) {
                if (!tp.Active || !tp.LoadenWorldSendData) {
                    continue;
                }
                sendTPCount++;
            }
            modPacket.Write(sendTPCount); // 写入活跃的TP数量

            // 发送每个活跃的TP数据
            foreach (TileProcessor tp in TP_InWorld) {
                if (!tp.Active || !tp.LoadenWorldSendData) {
                    continue;
                }

                // 标记节点开始
                modPacket.Write(TP_START_GUID); // 添加分隔标签
                modPacket.Write(tp.LoadenName);
                modPacket.WritePoint16(tp.Position);

                // 发送TileProcessor数据
                tp.SendData(modPacket);
            }

            modPacket.Send(whoAmI); // 将数据包发送给客户端
        }
        /// <summary>
        /// 服务端响应TP数据链的请求后，接收数据
        /// </summary>
        public static void Handle_TPData_Receive(BinaryReader reader) {
            if (!VaultUtils.isClient) {
                return;
            }

            int tpCount = reader.ReadInt32(); // 读取TP数量

            if (tpCount < 0 || tpCount > MaxTileModuleInWorldCount) {
                "TileProcessorLoader-ClientRequest_TPData_Receive: Received invalid TP count, terminating read".LoggerDomp();
                return;
            }

            Dictionary<(string, Point16), TileProcessor> tpDictionary = GetTileProcessorDictionaryByNameAndPosition();

            for (int i = 0; i < tpCount; i++) {
                string marker = reader.ReadString();

                // 确认是否为有效的起始标记
                if (marker != TP_START_GUID) {
                    $"TileProcessorLoader-ClientRequest_TPData_Receive: Invalid mark {marker}，Skip to the next node".LoggerDomp();
                    continue;
                }

                string name = reader.ReadString();
                Point16 position = reader.ReadPoint16();

                if (tpDictionary.TryGetValue((name, position), out TileProcessor tp)) {
                    tp.ReceiveData(reader, -1);
                }
                else {
                    // 跳过该TileProcessor的数据
                    $"TileProcessorLoader-ClientRequest_TPData_Receive: No corresponding TileProcessor instance found: {name}-position[{position}]，Skip".LoggerDomp();
                    SkipToNextMarker(reader);
                }
            }
        }

        /// <summary>
        /// 跳到下一个标记节点
        /// </summary>
        private static void SkipToNextMarker(BinaryReader reader) {
            while (reader.BaseStream.Position < reader.BaseStream.Length) {
                string marker = reader.ReadString();
                if (marker == TP_START_GUID) {
                    // 回退以便下一个处理块能够正确读取标记
                    reader.BaseStream.Position -= marker.Length;
                    break;
                }
            }
        }
    }
}
