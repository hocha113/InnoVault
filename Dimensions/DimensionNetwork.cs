using System.IO;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace InnoVault.Dimensions
{
    /// <summary>
    /// 维度网络消息类型
    /// </summary>
    internal enum DimensionNetType : byte
    {
        /// <summary>
        /// 请求进入维度
        /// </summary>
        EnterDimension = 0,
        /// <summary>
        /// 同步维度状态
        /// </summary>
        SyncDimensionState = 1,
        /// <summary>
        /// 维度数据同步
        /// </summary>
        SyncDimensionData = 2
    }

    /// <summary>
    /// 维度系统的网络处理类
    /// </summary>
    public static class DimensionNetwork
    {
        /// <summary>
        /// 获取维度网络消息的ModPacket
        /// </summary>
        internal static ModPacket GetPacket(DimensionNetType type) {
            ModPacket packet = VaultMod.Instance.GetPacket();
            packet.Write((byte)MessageType.DimensionMessage);
            packet.Write((byte)type);
            return packet;
        }

        /// <summary>
        /// 处理维度相关的网络消息
        /// </summary>
        internal static void HandlePacket(MessageType type, BinaryReader reader, int whoAmI) {
            if (type != MessageType.DimensionMessage) {
                return;
            }
            switch ((DimensionNetType)reader.ReadByte()) {
                case DimensionNetType.EnterDimension:
                    HandleEnterDimension(reader, whoAmI);
                    break;
                case DimensionNetType.SyncDimensionState:
                    HandleSyncDimensionState(reader, whoAmI);
                    break;
                case DimensionNetType.SyncDimensionData:
                    HandleSyncDimensionData(reader, whoAmI);
                    break;
            }
        }

        /// <summary>
        /// 发送进入维度请求
        /// </summary>
        internal static void SendEnterDimensionPacket(int dimensionIndex) {
            if (Main.netMode != NetmodeID.MultiplayerClient) {
                return;
            }

            ModPacket packet = GetPacket(DimensionNetType.EnterDimension);
            packet.Write(dimensionIndex);
            packet.Send();
        }

        /// <summary>
        /// 处理进入维度请求
        /// </summary>
        private static void HandleEnterDimension(BinaryReader reader, int whoAmI) {
            int dimensionIndex = reader.ReadInt32();

            if (Main.netMode == NetmodeID.Server) {
                //服务器处理：验证并广播给其他客户端
                if (dimensionIndex >= 0 && dimensionIndex < Dimension.Dimensions.Count) {
                    //向所有客户端广播维度状态变化
                    ModPacket packet = GetPacket(DimensionNetType.SyncDimensionState);
                    packet.Write(whoAmI);
                    packet.Write(dimensionIndex);
                    packet.Send(-1, whoAmI);
                }
            }
        }

        /// <summary>
        /// 处理维度状态同步
        /// </summary>
        private static void HandleSyncDimensionState(BinaryReader reader, int whoAmI) {
            int playerIndex = reader.ReadInt32();
            int dimensionIndex = reader.ReadInt32();

            if (Main.netMode == NetmodeID.MultiplayerClient) {
                //客户端接收：更新本地状态
                //这里可以显示其他玩家进入维度的提示等
                VaultMod.Instance.Logger.Debug($"Player {playerIndex} entered dimension {dimensionIndex}");
            }
        }

        /// <summary>
        /// 发送维度数据同步
        /// </summary>
        internal static void SendDimensionData(int toClient = -1, int ignoreClient = -1) {
            if (Main.netMode != NetmodeID.Server) {
                return;
            }

            ModPacket packet = GetPacket(DimensionNetType.SyncDimensionData);

            //写入当前维度索引
            int currentIndex = DimensionSystem.Current != null ? DimensionSystem.Current.ID : -1;
            packet.Write(currentIndex);

            packet.Send(toClient, ignoreClient);
        }

        /// <summary>
        /// 处理维度数据同步
        /// </summary>
        private static void HandleSyncDimensionData(BinaryReader reader, int whoAmI) {
            int dimensionIndex = reader.ReadInt32();

            if (Main.netMode == NetmodeID.MultiplayerClient) {
                //客户端更新本地维度状态
                VaultMod.Instance.Logger.Debug($"Synced dimension index: {dimensionIndex}");
            }
        }
    }
}
