using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Terraria;
using Terraria.ModLoader;

namespace InnoVault.GameSystem
{
    /// <summary>
    /// 描述一次 <see cref="NPCOverride"/> 同步所携带的数据种类，可按位组合
    /// </summary>
    [Flags]
    public enum NPCOverrideSyncField : byte
    {
        /// <summary>无</summary>
        None = 0,
        /// <summary>ai 槽位（热状态，搭车 vanilla SyncNPC 的 ExtraAI 通道，保证与位置/速度原子到达）</summary>
        Ai = 1,
        /// <summary><see cref="NPCOverride.OtherNetWorkSend"/>/<see cref="NPCOverride.OtherNetWorkReceive"/> 自定义数据</summary>
        Other = 2,
        /// <summary><see cref="SyncVarAttribute"/> 标记的字段</summary>
        SyncVar = 4,
        /// <summary>全部</summary>
        All = Ai | Other | SyncVar,
    }

    /// <summary>
    /// <see cref="NPCOverride"/> 的网络同步机制集中实现处，统一走 <see cref="VaultMod"/> 通道，
    /// 所有来源不可信的读取都做了边界与合法性加固
    /// </summary>
    internal static class NPCOverrideNetWork
    {
        //每个客户端每服务端帧允许处理的事件数量上限，用于防止异常/恶意客户端刷包放大
        private const int MaxEventsPerClientPerTick = 64;
        private static readonly Dictionary<int, long> _eventBudgetTick = [];
        private static readonly Dictionary<int, int> _eventBudgetCount = [];

        internal static void Clear() {
            _eventBudgetTick.Clear();
            _eventBudgetCount.Clear();
            _deltaBaselines.Clear();
            _deltaStream.SetLength(0);
            _lastDeltaTick = -1;
        }

        #region Serialization Helpers
        /// <summary>
        /// 使用位掩码仅写入非零的 ai 槽位，减少高频同步带宽
        /// </summary>
        internal static void WriteAiMasked(BinaryWriter writer, float[] ai) {
            ushort mask = 0;
            for (int i = 0; i < NPCOverride.MaxAISlot; i++) {
                if (ai[i] != 0f) {
                    mask |= (ushort)(1 << i);
                }
            }
            writer.Write(mask);
            for (int i = 0; i < NPCOverride.MaxAISlot; i++) {
                if ((mask & (1 << i)) != 0) {
                    writer.Write(ai[i]);
                }
            }
        }

        /// <summary>
        /// 与 <see cref="WriteAiMasked"/> 对应，未发送的槽位会被清零
        /// </summary>
        internal static void ReadAiMasked(BinaryReader reader, float[] ai) {
            ushort mask = reader.ReadUInt16();
            for (int i = 0; i < NPCOverride.MaxAISlot; i++) {
                ai[i] = (mask & (1 << i)) != 0 ? reader.ReadSingle() : 0f;
            }
        }

        //统一的“数据块”写入：[fieldMask][ai?][other?][syncvar?]，Event/Bulk 共用
        private static void WriteBlock(ModPacket packet, NPCOverride ov, NPCOverrideSyncField fields) {
            packet.Write((byte)fields);
            if ((fields & NPCOverrideSyncField.Ai) != 0) {
                WriteAiMasked(packet, ov.ai);
            }
            if ((fields & NPCOverrideSyncField.Other) != 0) {
                ov.OtherNetWorkSend(packet);
            }
            if ((fields & NPCOverrideSyncField.SyncVar) != 0) {
                SyncVarManager.Send(ov, packet);
            }
        }

        //与 WriteBlock 对应，返回实际读取到的字段，便于服务端按相同字段转发
        private static NPCOverrideSyncField ReadBlock(BinaryReader reader, NPCOverride ov) {
            NPCOverrideSyncField fields = (NPCOverrideSyncField)reader.ReadByte();
            if ((fields & NPCOverrideSyncField.Ai) != 0) {
                ReadAiMasked(reader, ov.ai);
            }
            if ((fields & NPCOverrideSyncField.Other) != 0) {
                ov.OtherNetWorkReceive(reader);
            }
            if ((fields & NPCOverrideSyncField.SyncVar) != 0) {
                SyncVarManager.Receive(ov, reader);
            }
            return fields;
        }
        #endregion

        #region Stable ID
        /// <summary>
        /// 在所有重制节点注册完成后，按 <see cref="Terraria.ModLoader.ModType.FullName"/> 字典序确定 wire ID。
        /// 两端内容一致时排序结果天然相同，无需运行时重排
        /// </summary>
        internal static void BuildStableIDs() {
            if (NPCOverride.Instances == null) {
                return;
            }

            List<NPCOverride> sorted = NPCOverride.Instances.OrderBy(o => o.FullName, StringComparer.Ordinal).ToList();
            NPCOverride.OverrideIDToInstances.Clear();
            NPCOverride.TypeToOverrideID.Clear();
            NPCOverride.OverrideIDToType.Clear();

            for (int i = 0; i < sorted.Count; i++) {
                ushort id = (ushort)i;
                NPCOverride ov = sorted[i];
                ov.OverrideID = id;
                NPCOverride.OverrideIDToInstances[id] = ov;
                NPCOverride.TypeToOverrideID[ov.GetType()] = id;
                NPCOverride.OverrideIDToType[id] = ov.GetType();
            }
        }

        /// <summary>
        /// 计算重制节点集合的稳定哈希（FNV-1a 64 位），用于进图握手时检测两端集合是否一致
        /// </summary>
        internal static long ComputeOverrideSetHash() {
            unchecked {
                const long prime = 0x100000001b3;
                long hash = (long)0xCBF29CE484222325UL;
                foreach (string name in NPCOverride.Instances.Select(o => o.FullName).OrderBy(s => s, StringComparer.Ordinal)) {
                    foreach (char c in name) {
                        hash ^= c;
                        hash *= prime;
                    }
                    hash ^= '\n';
                    hash *= prime;
                }
                return hash;
            }
        }
        #endregion

        #region Event Channel
        /// <summary>
        /// 发送一次统一同步事件。<paramref name="toClient"/> 为 -1 时广播，<paramref name="exclude"/> 为忽略的客户端
        /// </summary>
        internal static void SendEvent(NPCOverride ov, NPCOverrideSyncField fields, int toClient = -1, int exclude = -1) {
            if (VaultUtils.isSinglePlayer || fields == NPCOverrideSyncField.None || ov?.npc == null) {
                return;
            }

            ModPacket packet = VaultMod.Instance.GetPacket();
            packet.Write((byte)MessageType.NPCOverride_Event);
            packet.Write((short)ov.npc.whoAmI);
            packet.Write(ov.OverrideID);
            WriteBlock(packet, ov, fields);
            packet.Send(toClient, exclude);
        }

        internal static void HandleEvent(BinaryReader reader, int whoAmI) {
            try {
                bool server = VaultUtils.isServer;
                //服务端对来源于客户端的事件做限流，单个事件包独立，丢弃不会影响其它包
                if (server && !AllowClientEvent(whoAmI)) {
                    return;
                }

                int npcIndex = reader.ReadInt16();
                ushort id = reader.ReadUInt16();

                if (!npcIndex.TryGetNPC(out NPC npc)) {
                    return;
                }
                if (!NPCOverride.OverrideIDToType.TryGetValue(id, out Type type)) {
                    return;
                }
                if (!npc.TryGetOverride(out var values) || !values.TryGetValue(type, out NPCOverride ov)) {
                    return;
                }

                NPCOverrideSyncField fields = ReadBlock(reader, ov);

                //服务端把客户端上报的数据广播给除来源外的所有客户端
                if (server) {
                    SendEvent(ov, fields, -1, whoAmI);
                }
            } catch (Exception ex) {
                VaultMod.LoggerError("NPCOverride.HandleEvent", $"Failed to handle NPCOverride event packet: {ex}");
            }
        }

        private static bool AllowClientEvent(int whoAmI) {
            long tick = Main.GameUpdateCount;
            if (!_eventBudgetTick.TryGetValue(whoAmI, out long lastTick) || lastTick != tick) {
                _eventBudgetTick[whoAmI] = tick;
                _eventBudgetCount[whoAmI] = 1;
                return true;
            }

            int count = _eventBudgetCount[whoAmI];
            if (count >= MaxEventsPerClientPerTick) {
                return false;
            }

            _eventBudgetCount[whoAmI] = count + 1;
            return true;
        }
        #endregion

        #region Bulk (join) sync
        /// <summary>
        /// [客户端] 进图时请求服务端补发当前所有重制节点的冷数据
        /// </summary>
        internal static void RequestBulk() {
            if (!VaultUtils.isClient) {
                return;
            }
            ModPacket packet = VaultMod.Instance.GetPacket();
            packet.Write((byte)MessageType.NPCOverride_BulkRequest);
            packet.Send();
        }

        /// <summary>
        /// [服务端] 向请求的客户端补发当前所有重制节点的 Other/SyncVar 冷数据。
        /// ai 在玩家进图时由 vanilla SyncNPC(ExtraAI) 自动覆盖，无需在此重复同步
        /// </summary>
        internal static void HandleBulkRequest(int whoAmI) {
            if (!VaultUtils.isServer) {
                return;
            }

            const NPCOverrideSyncField fields = NPCOverrideSyncField.Other | NPCOverrideSyncField.SyncVar;
            for (int i = 0; i < Main.maxNPCs; i++) {
                NPC npc = Main.npc[i];
                if (!npc.Alives() || !npc.TryGetOverride(out var values)) {
                    continue;
                }
                foreach (NPCOverride ov in values.Values) {
                    SendEvent(ov, fields, whoAmI);
                }
            }
        }
        #endregion

        #region Handshake
        /// <summary>
        /// [客户端] 请求服务端的重制节点集合校验数据
        /// </summary>
        internal static void RequestHandshake() {
            if (!VaultUtils.isClient) {
                return;
            }
            ModPacket packet = VaultMod.Instance.GetPacket();
            packet.Write((byte)MessageType.NPCOverride_HandshakeRequest);
            packet.Send();
        }

        /// <summary>
        /// [服务端] 下发集合规模与稳定哈希
        /// </summary>
        internal static void HandleHandshakeRequest(int whoAmI) {
            if (!VaultUtils.isServer) {
                return;
            }
            ModPacket packet = VaultMod.Instance.GetPacket();
            packet.Write((byte)MessageType.NPCOverride_Handshake);
            packet.Write(NPCOverride.Instances.Count);
            packet.Write(ComputeOverrideSetHash());
            packet.Send(whoAmI);
        }

        /// <summary>
        /// [客户端] 比对两端集合，不一致时告警（不再做运行时重排，ID 由稳定排序决定）
        /// </summary>
        internal static void HandleHandshake(BinaryReader reader) {
            int serverCount = reader.ReadInt32();
            long serverHash = reader.ReadInt64();
            if (!VaultUtils.isClient) {
                return;
            }

            int localCount = NPCOverride.Instances.Count;
            long localHash = ComputeOverrideSetHash();
            if (serverCount != localCount || serverHash != localHash) {
                string msg = $"NPCOverride validation mismatch (server: {serverCount}/{serverHash:X}" +
                    $", client: {localCount}/{localHash:X}). This is usually caused by different mod lists or versions and may break sync.";
                VaultMod.Instance.Logger.Error(msg);
                VaultUtils.Text(msg, Color.Red);
            }
            else {
                VaultMod.Instance.Logger.Info("NPCOverride validation successful. Client data matches server.");
            }
        }
        #endregion

        #region High-frequency delta channel (Phase 2)
        //受控 cadence 的跨 NPC 增量 ai 流式通道：服务端按距离向各客户端推送其附近且变化过的 ai 槽位。
        //每个客户端维护独立基线，超出范围后丢弃基线、重新进入范围时自然全量补发；并带心跳全量自愈漏包。

        /// <summary>两次增量批处理之间的最小服务端帧间隔</summary>
        internal const int DeltaCadenceTicks = 3;
        /// <summary>强制全量刷新(心跳)的间隔帧数，用于自愈漂移与漏包</summary>
        internal const int DeltaHeartbeatTicks = 60;
        /// <summary>参与增量流式同步的最大距离(像素)，超出则不向该客户端推送</summary>
        internal const float DeltaStreamRange = 16f * 200f;
        private const float DeltaStreamRangeSq = DeltaStreamRange * DeltaStreamRange;

        private struct DeltaBaseline
        {
            public float[] Ai;
            public long LastFullTick;
        }

        //client -> (key:(npcIndex << 16) | overrideID) -> baseline
        private static readonly Dictionary<int, Dictionary<int, DeltaBaseline>> _deltaBaselines = [];
        private static readonly MemoryStream _deltaStream = new();
        private static readonly BinaryWriter _deltaWriter = new(_deltaStream);
        private static long _lastDeltaTick = -1;

        /// <summary>
        /// 服务端每帧驱动：按 cadence 对启用了 <see cref="NPCOverride.StreamNetDelta"/> 的重制节点做按客户端的增量广播
        /// </summary>
        internal static void ServerDeltaTick() {
            if (!VaultUtils.isServer) {
                return;
            }

            long tick = Main.GameUpdateCount;
            if (_lastDeltaTick >= 0 && tick - _lastDeltaTick < DeltaCadenceTicks) {
                return;
            }
            _lastDeltaTick = tick;

            for (int client = 0; client < Main.maxPlayers; client++) {
                Player plr = Main.player[client];
                if (plr == null || !plr.active) {
                    _deltaBaselines.Remove(client);
                    continue;
                }

                if (!_deltaBaselines.TryGetValue(client, out var baseForClient)) {
                    baseForClient = [];
                    _deltaBaselines[client] = baseForClient;
                }

                Vector2 center = plr.Center;
                _deltaStream.SetLength(0);
                int count = 0;

                for (int n = 0; n < Main.maxNPCs; n++) {
                    NPC npc = Main.npc[n];
                    if (!npc.Alives() || !npc.TryGetOverride(out var values)) {
                        continue;
                    }

                    bool inRange = Vector2.DistanceSquared(npc.Center, center) <= DeltaStreamRangeSq;
                    foreach (NPCOverride ov in values.Values) {
                        if (!ov.StreamNetDelta) {
                            continue;
                        }

                        int key = (n << 16) | ov.OverrideID;
                        if (!inRange) {
                            //离开范围：丢弃基线，下次进入范围自然全量补发
                            baseForClient.Remove(key);
                            continue;
                        }

                        bool hasBase = baseForClient.TryGetValue(key, out DeltaBaseline entry);
                        bool full = !hasBase || tick - entry.LastFullTick >= DeltaHeartbeatTicks;
                        entry.Ai ??= new float[NPCOverride.MaxAISlot];

                        ushort mask = 0;
                        for (int i = 0; i < NPCOverride.MaxAISlot; i++) {
                            if (full || entry.Ai[i] != ov.ai[i]) {
                                mask |= (ushort)(1 << i);
                            }
                        }
                        if (mask == 0) {
                            continue;
                        }

                        _deltaWriter.Write((short)n);
                        _deltaWriter.Write(ov.OverrideID);
                        _deltaWriter.Write(mask);
                        for (int i = 0; i < NPCOverride.MaxAISlot; i++) {
                            if ((mask & (1 << i)) != 0) {
                                _deltaWriter.Write(ov.ai[i]);
                                entry.Ai[i] = ov.ai[i];
                            }
                        }
                        if (full) {
                            entry.LastFullTick = tick;
                        }
                        baseForClient[key] = entry;
                        count++;
                    }
                }

                if (count > 0) {
                    ModPacket packet = VaultMod.Instance.GetPacket();
                    packet.Write((byte)MessageType.NPCOverride_DeltaBatch);
                    packet.Write(count);
                    packet.Write(_deltaStream.GetBuffer(), 0, (int)_deltaStream.Length);
                    packet.Send(client);
                }
            }
        }

        private static void HandleDeltaBatch(BinaryReader reader) {
            int count = reader.ReadInt32();
            bool client = VaultUtils.isClient;
            for (int i = 0; i < count; i++) {
                int npcIndex = reader.ReadInt16();
                ushort id = reader.ReadUInt16();
                ushort mask = reader.ReadUInt16();

                NPCOverride ov = null;
                if (client && npcIndex.TryGetNPC(out NPC npc)
                    && NPCOverride.OverrideIDToType.TryGetValue(id, out Type type)
                    && npc.TryGetOverride(out var values)) {
                    values.TryGetValue(type, out ov);
                }

                //无论能否解析，都按 mask 读出对应数量的浮点，保持批量流对齐
                for (int slot = 0; slot < NPCOverride.MaxAISlot; slot++) {
                    if ((mask & (1 << slot)) == 0) {
                        continue;
                    }
                    float value = reader.ReadSingle();
                    if (ov != null) {
                        ov.ai[slot] = value; //增量语义：只覆盖变化的槽位，不清零其它
                    }
                }
            }
        }
        #endregion

        /// <summary>
        /// 进入游戏世界时，请求集合校验与冷数据补发
        /// </summary>
        internal static void OnEnterWorld() {
            RequestHandshake();
            RequestBulk();
        }

        internal static void HandlePacket(MessageType type, BinaryReader reader, int whoAmI) {
            switch (type) {
                case MessageType.NPCOverride_Event:
                    HandleEvent(reader, whoAmI);
                    break;
                case MessageType.NPCOverride_BulkRequest:
                    HandleBulkRequest(whoAmI);
                    break;
                case MessageType.NPCOverride_HandshakeRequest:
                    HandleHandshakeRequest(whoAmI);
                    break;
                case MessageType.NPCOverride_Handshake:
                    HandleHandshake(reader);
                    break;
                case MessageType.NPCOverride_DeltaBatch:
                    HandleDeltaBatch(reader);
                    break;
            }
        }
    }

    /// <summary>
    /// 驱动 <see cref="NPCOverrideNetWork"/> 服务端高频增量同步的节拍
    /// </summary>
    internal sealed class NPCOverrideNetworkSystem : ModSystem
    {
        public override void PostUpdateNPCs() => NPCOverrideNetWork.ServerDeltaTick();
    }
}
