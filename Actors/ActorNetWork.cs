using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using Terraria;
using Terraria.ModLoader;

namespace InnoVault.Actors
{
    /// <summary>
    /// 处理实体网络同步的类
    /// <para>
    /// 采用"服务器单一权威"模型: 槽位与 <see cref="Actor.Generation"/> 由服务器集中分配，权威状态只由服务器
    /// 广播；客户端只发送生成 / 销毁请求与晚加入请求，并通过 <see cref="Actor.ApplyClientReconciliation"/>
    /// 向权威状态平滑收敛。所有接收处理器都会做范围、generation 与有限性校验，非法或过期的数据包被安全丢弃
    /// </para>
    /// </summary>
    public class ActorNetWork : IVaultLoader
    {
        #region 调参常量
        /// <summary>强制全量同步(心跳)的间隔帧数，用于自愈累积漂移与漏包</summary>
        public const int HeartbeatTicks = 60;
        /// <summary>同一实体两次增量广播之间的最小间隔帧数</summary>
        public const int MinSendIntervalTicks = 4;
        /// <summary>客户端重对齐的硬吸附距离(像素)，误差超过则直接瞬移到权威位置</summary>
        public const float HardSnapDistance = 16f * 10f;
        /// <summary>硬吸附距离的平方</summary>
        public const float HardSnapDistanceSq = HardSnapDistance * HardSnapDistance;
        /// <summary>客户端位置每帧向权威目标收敛的比例</summary>
        public const float PositionSmoothing = 0.25f;
        /// <summary>客户端旋转每帧向权威目标收敛的比例</summary>
        public const float RotationSmoothing = 0.25f;
        /// <summary>小于该平方距离视为已对齐，不再插值</summary>
        public const float ReconcileEpsilonSq = 0.01f;
        /// <summary>旋转误差(弧度)超过则直接吸附</summary>
        public const float RotationSnap = 0.5f;

        //单个 ActorUpdate 批量包的负载上限，超出则分块发送，避免触及 ModPacket 大小限制
        private const int MaxBatchPayloadBytes = 60000;
        #endregion

        //服务器侧批量广播复用缓冲，避免逐实体分配字节数组(网络处理均在主线程，无需加锁)
        private static readonly MemoryStream batchStream = new();
        private static readonly BinaryWriter batchWriter = new(batchStream);
        private static readonly MemoryStream fieldStream = new();
        private static readonly BinaryWriter fieldWriter = new(fieldStream);

        void IVaultLoader.UnLoadData() {
            ResetBatch();
            ResetField();
        }

        #region 辅助
        private static long CurrentTick => (long)Main.GameUpdateCount;
        private static bool IsValidType(int typeId) => Actor.IDToInstance.ContainsKey(typeId);
        private static bool IsValidSlot(int slot) => slot >= 0 && slot < ActorLoader.MaxActorCount;
        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool IsFinite(Vector2 value) => IsFinite(value.X) && IsFinite(value.Y);

        private static void ResetBatch() {
            batchStream.SetLength(0);
            batchStream.Position = 0;
        }

        private static void ResetField() {
            fieldStream.SetLength(0);
            fieldStream.Position = 0;
        }
        #endregion

        #region 生成
        /// <summary>
        /// (客户端)请求服务器生成一个Actor
        /// </summary>
        public static void SendActorSpawnRequest(int type, Vector2 position, Vector2 velocity) {
            if (VaultUtils.isSinglePlayer) {
                return;
            }
            ModPacket modPacket = VaultMod.Instance.GetPacket();
            modPacket.Write((byte)MessageType.ActorSpawnRequest);
            modPacket.Write(type);
            modPacket.WriteVector2(position);
            modPacket.WriteVector2(velocity);
            modPacket.Send();
        }

        private static void HandleActorSpawnRequest(BinaryReader reader) {
            int type = reader.ReadInt32();
            Vector2 position = reader.ReadVector2();
            Vector2 velocity = reader.ReadVector2();

            if (!VaultUtils.isServer || !IsValidType(type) || !IsFinite(position) || !IsFinite(velocity)) {
                return;
            }

            //服务器集中分配槽位与 generation，并在内部广播生成包
            ActorLoader.ServerSpawn(type, position, velocity);
        }

        /// <summary>
        /// (服务器)向所有客户端广播一个Actor的生成包(含全量状态与附加数据)，并建立增量基线
        /// </summary>
        public static void BroadcastActorSpawn(Actor actor) {
            if (VaultUtils.isSinglePlayer) {
                return;
            }

            SyncVarManager.ResetBaseline(actor);
            actor.NetUpdate = false;
            actor.LastBroadcastTick = CurrentTick;
            actor.LastFullSyncTick = CurrentTick;

            ModPacket modPacket = VaultMod.Instance.GetPacket();
            WriteSpawn(modPacket, actor);
            modPacket.Send();
        }

        private static void SendActorSpawnTo(Actor actor, int toClient) {
            ModPacket modPacket = VaultMod.Instance.GetPacket();
            WriteSpawn(modPacket, actor);
            modPacket.Send(toClient);
        }

        private static void WriteSpawn(BinaryWriter writer, Actor actor) {
            writer.Write((byte)MessageType.ActorSpawn);
            writer.Write((ushort)actor.ID);
            writer.Write((ushort)actor.WhoAmI);
            writer.Write(actor.Generation);
            SyncVarManager.WriteFull(actor, writer);
            actor.SendExtraData(writer);
        }

        private static void HandleActorSpawn(BinaryReader reader) {
            int type = reader.ReadUInt16();
            int slot = reader.ReadUInt16();
            ushort generation = reader.ReadUInt16();

            //生成包为独立数据包，校验失败时直接丢弃剩余负载(包结束后自然废弃)即可
            if (!VaultUtils.isClient || !IsValidType(type) || !IsValidSlot(slot)) {
                return;
            }

            //同槽位同 generation 的重复生成(晚加入快照与实时广播竞态)直接忽略
            Actor existing = ActorLoader.Actors[slot];
            if (existing != null && existing.Active && existing.Generation == generation && existing.ID == type) {
                return;
            }

            try {
                ActorLoader.NetworkSpawn(type, slot, generation, reader);
            } catch (Exception ex) {
                VaultMod.Instance.Logger.Error($"HandleActorSpawn failed (type {type}, slot {slot}): {ex}");
            }
        }
        #endregion

        #region 增量状态
        /// <summary>
        /// (服务器)在每帧末尾对活跃实体进行节流的增量广播，合并为分块的批量包
        /// </summary>
        internal static void ServerBroadcastTick(List<Actor> active) {
            if (!VaultUtils.isServer || active.Count == 0) {
                return;
            }

            long now = CurrentTick;
            ResetBatch();
            int count = 0;

            for (int i = 0; i < active.Count; i++) {
                Actor actor = active[i];
                if (actor == null || !actor.Active) {
                    continue;
                }

                bool heartbeat = now - actor.LastFullSyncTick >= HeartbeatTicks;
                bool forceFull = heartbeat || actor.NetUpdate;
                if (!forceFull && now - actor.LastBroadcastTick < MinSendIntervalTicks) {
                    continue;
                }

                ResetField();
                if (!SyncVarManager.WriteState(actor, fieldWriter, forceFull)) {
                    continue;
                }

                int entrySize = 6 + (int)fieldStream.Length; //typeId(2) + slot(2) + generation(2)
                if (count > 0 && batchStream.Length + entrySize > MaxBatchPayloadBytes) {
                    FlushBatch(count);
                    ResetBatch();
                    count = 0;
                }

                batchWriter.Write((ushort)actor.ID);
                batchWriter.Write((ushort)actor.WhoAmI);
                batchWriter.Write(actor.Generation);
                batchWriter.Write(fieldStream.GetBuffer(), 0, (int)fieldStream.Length);
                count++;

                actor.LastBroadcastTick = now;
                if (forceFull) {
                    actor.LastFullSyncTick = now;
                }
                actor.NetUpdate = false;
            }

            if (count > 0) {
                FlushBatch(count);
            }
        }

        private static void FlushBatch(int count) {
            ModPacket modPacket = VaultMod.Instance.GetPacket();
            modPacket.Write((byte)MessageType.ActorUpdate);
            modPacket.Write(count);
            modPacket.Write(batchStream.GetBuffer(), 0, (int)batchStream.Length);
            modPacket.Send();
        }

        private static void HandleActorUpdate(BinaryReader reader) {
            int count = reader.ReadInt32();
            if (!VaultUtils.isClient) {
                return;
            }

            for (int i = 0; i < count; i++) {
                int type = reader.ReadUInt16();
                int slot = reader.ReadUInt16();
                ushort generation = reader.ReadUInt16();

                //无法解析类型则后续条目长度未知，只能中止本批(剩余由心跳自愈)
                if (!IsValidType(type)) {
                    return;
                }

                Actor actor = IsValidSlot(slot) ? ActorLoader.Actors[slot] : null;
                bool match = actor != null && actor.Active && actor.Generation == generation && actor.ID == type;

                try {
                    if (match) {
                        actor.ClientReceiveState(reader);
                    }
                    else {
                        //身份不匹配: 读弃该条目以保持批量流对齐
                        SyncVarManager.SkipState(Actor.IDToInstance[type].GetType(), reader);
                    }
                } catch (Exception ex) {
                    VaultMod.Instance.Logger.Error($"HandleActorUpdate entry failed (type {type}, slot {slot}): {ex}");
                    return;
                }
            }
        }
        #endregion

        #region 销毁
        /// <summary>
        /// (服务器)向所有客户端广播销毁一个Actor
        /// </summary>
        public static void SendActorKill(int slot, ushort generation) {
            if (VaultUtils.isSinglePlayer) {
                return;
            }
            ModPacket modPacket = VaultMod.Instance.GetPacket();
            modPacket.Write((byte)MessageType.ActorKill);
            modPacket.Write((ushort)slot);
            modPacket.Write(generation);
            modPacket.Send();
        }

        private static void HandleActorKill(BinaryReader reader) {
            int slot = reader.ReadUInt16();
            ushort generation = reader.ReadUInt16();
            if (!VaultUtils.isClient || !IsValidSlot(slot)) {
                return;
            }
            ActorLoader.NetworkKill(slot, generation);
        }

        /// <summary>
        /// (客户端)请求服务器销毁一个Actor
        /// </summary>
        public static void SendActorKillRequest(int slot, ushort generation) {
            if (!VaultUtils.isClient) {
                return;
            }
            ModPacket modPacket = VaultMod.Instance.GetPacket();
            modPacket.Write((byte)MessageType.ActorKillRequest);
            modPacket.Write((ushort)slot);
            modPacket.Write(generation);
            modPacket.Send();
        }

        private static void HandleActorKillRequest(BinaryReader reader) {
            int slot = reader.ReadUInt16();
            ushort generation = reader.ReadUInt16();
            if (!VaultUtils.isServer || !IsValidSlot(slot)) {
                return;
            }

            Actor actor = ActorLoader.Actors[slot];
            if (actor != null && actor.Active && actor.Generation == generation) {
                ActorLoader.KillActor(slot, network: true);
            }
        }
        #endregion

        #region 晚加入全量同步
        /// <summary>
        /// (客户端)晚加入时请求服务器下发全部活跃Actor的快照
        /// </summary>
        public static void SendActorFullSyncRequest() {
            if (!VaultUtils.isClient) {
                return;
            }
            ModPacket modPacket = VaultMod.Instance.GetPacket();
            modPacket.Write((byte)MessageType.ActorFullSyncRequest);
            modPacket.Send();
        }

        private static void HandleActorFullSyncRequest(int whoAmI) {
            if (!VaultUtils.isServer) {
                return;
            }

            IReadOnlyList<Actor> active = ActorLoader.ActiveActors;
            for (int i = 0; i < active.Count; i++) {
                Actor actor = active[i];
                if (actor != null && actor.Active) {
                    SendActorSpawnTo(actor, whoAmI);
                }
            }
        }
        #endregion

        internal static void Handle(MessageType type, Mod mod, BinaryReader reader, int whoAmI) {
            switch (type) {
                case MessageType.ActorSpawnRequest:
                    HandleActorSpawnRequest(reader);
                    break;
                case MessageType.ActorSpawn:
                    HandleActorSpawn(reader);
                    break;
                case MessageType.ActorUpdate:
                    HandleActorUpdate(reader);
                    break;
                case MessageType.ActorKill:
                    HandleActorKill(reader);
                    break;
                case MessageType.ActorKillRequest:
                    HandleActorKillRequest(reader);
                    break;
                case MessageType.ActorFullSyncRequest:
                    HandleActorFullSyncRequest(whoAmI);
                    break;
            }
        }
    }
}
