using System;
using System.IO;
using System.Linq;
using Terraria.ModLoader;
using static InnoVault.VaultNetwork;

namespace InnoVault.Dimensions
{
    /// <summary>
    /// 维度网络处理器
    /// </summary>
    internal class DimensionNetwork : IVaultLoader
    {
        /// <summary>
        /// 维度网络消息类型
        /// </summary>
        internal enum DimensionMessageType : byte
        {
            /// <summary>
            /// 维度切换请求/通知
            /// </summary>
            DimensionSwitch = 200,
            /// <summary>
            /// 维度数据同步
            /// </summary>
            DimensionDataSync = 201,
            /// <summary>
            /// 玩家进入维度通知
            /// </summary>
            PlayerEnterDimension = 202,
            /// <summary>
            /// 玩家离开维度通知
            /// </summary>
            PlayerLeaveDimension = 203
        }

        void IVaultLoader.LoadData() {
            // 初始化网络相关数据
        }

        void IVaultLoader.UnLoadData() {
            // 清理网络相关数据
        }

        /// <summary>
        /// 处理维度相关的网络包
        /// </summary>
        internal static void HandlePacket(MessageType type, BinaryReader reader, int whoAmI) {
            try {
                // 这里预留给未来的维度网络功能
                // 目前维度切换通过DimensionLoader中的方法处理
            } catch (Exception ex) {
                VaultMod.Instance.Logger.Error($"Error handling dimension packet: {ex}");
            }
        }

        /// <summary>
        /// 发送维度切换包（客户端到服务器）
        /// </summary>
        internal static void SendDimensionSwitch(int targetIndex) {
            try {
                if (!VaultUtils.isClient)
                    return;

                ModPacket packet = VaultMod.Instance.GetPacket();
                packet.Write((byte)DimensionMessageType.DimensionSwitch);
                packet.Write(targetIndex);
                packet.Send();
            } catch (Exception ex) {
                VaultMod.Instance.Logger.Error($"Error sending dimension switch packet: {ex}");
            }
        }

        /// <summary>
        /// 接收维度切换包（服务器接收）
        /// </summary>
        internal static void ReceiveDimensionSwitch(BinaryReader reader, int whoAmI) {
            try {
                if (!VaultUtils.isServer)
                    return;

                int targetIndex = reader.ReadInt32();

                // 验证切换请求是否合法
                if (targetIndex < -1 || targetIndex >= DimensionLoader.registeredDimensions.Count) {
                    VaultMod.Instance.Logger.Warn(
                        $"Player {whoAmI} requested invalid dimension index: {targetIndex}");
                    return;
                }

                // 广播维度切换给所有客户端
                BroadcastDimensionSwitch(targetIndex, whoAmI);
            } catch (Exception ex) {
                VaultMod.Instance.Logger.Error($"Error receiving dimension switch: {ex}");
            }
        }

        /// <summary>
        /// 广播维度切换给所有客户端
        /// </summary>
        internal static void BroadcastDimensionSwitch(int targetIndex, int excludePlayer = -1) {
            try {
                if (!VaultUtils.isServer)
                    return;

                ModPacket packet = VaultMod.Instance.GetPacket();
                packet.Write((byte)DimensionMessageType.DimensionSwitch);
                packet.Write(targetIndex);
                packet.Send(-1, excludePlayer);
            } catch (Exception ex) {
                VaultMod.Instance.Logger.Error($"Error broadcasting dimension switch: {ex}");
            }
        }

        /// <summary>
        /// 同步维度数据（服务器到客户端）
        /// </summary>
        internal static void SyncDimensionData(int targetPlayer = -1) {
            try {
                if (!VaultUtils.isServer)
                    return;

                if (DimensionLoader.currentDimension == null)
                    return;

                ModPacket packet = VaultMod.Instance.GetPacket();
                packet.Write((byte)DimensionMessageType.DimensionDataSync);

                // 写入当前维度索引
                int currentIndex = DimensionLoader.dimensionsByIndex
                    .FirstOrDefault(kvp => kvp.Value == DimensionLoader.currentDimension).Key;
                packet.Write(currentIndex);

                // TODO: 添加更多维度状态数据

                packet.Send(targetPlayer);
            } catch (Exception ex) {
                VaultMod.Instance.Logger.Error($"Error syncing dimension data: {ex}");
            }
        }

        /// <summary>
        /// 接收维度数据同步
        /// </summary>
        internal static void ReceiveDimensionDataSync(BinaryReader reader) {
            try {
                if (!VaultUtils.isClient)
                    return;

                int currentIndex = reader.ReadInt32();

                // 更新客户端的当前维度状态
                if (currentIndex >= 0 && currentIndex < DimensionLoader.registeredDimensions.Count) {
                    DimensionLoader.currentDimension = DimensionLoader.registeredDimensions[currentIndex];
                }
                else if (currentIndex == -1) {
                    DimensionLoader.currentDimension = null;
                }

                // TODO: 读取更多维度状态数据
            } catch (Exception ex) {
                VaultMod.Instance.Logger.Error($"Error receiving dimension data sync: {ex}");
            }
        }
    }
}
