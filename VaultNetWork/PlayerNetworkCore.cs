using System.Collections.Generic;
using System.IO;
using Terraria;
using Terraria.ModLoader;

namespace InnoVault.VaultNetWork
{
    internal sealed class PlayerNetworkLoader : IVaultLoader
    {
        void IVaultLoader.UnLoadData() => PlayerNetworkCore.Clear();
    }

    internal static class PlayerNetworkCore
    {
        private struct InterestState
        {
            public PlayerNetworkDataFlags Flags;
            public long ExpireTick;
            public bool OneShot;
        }

        private static readonly Dictionary<int, Dictionary<int, InterestState>> interestsByTarget = [];
        private static readonly Dictionary<int, long> localRequestCooldownByTarget = [];
        private static readonly Dictionary<int, long> serverRequestCooldownByPair = [];

        internal static void Clear() {
            interestsByTarget.Clear();
            localRequestCooldownByTarget.Clear();
            serverRequestCooldownByPair.Clear();
        }

        internal static void UpdateServerInterests() {
            if (!VaultUtils.isServer || interestsByTarget.Count == 0) {
                return;
            }

            long currentTick = PlayerNetwork.GetCurrentTick();
            List<int> emptyTargets = [];
            foreach (var targetPair in interestsByTarget) {
                List<int> expiredRequesters = [];
                foreach (var requesterPair in targetPair.Value) {
                    if (requesterPair.Value.ExpireTick <= currentTick) {
                        expiredRequesters.Add(requesterPair.Key);
                    }
                }

                foreach (int requester in expiredRequesters) {
                    targetPair.Value.Remove(requester);
                }

                if (targetPair.Value.Count == 0) {
                    emptyTargets.Add(targetPair.Key);
                }
            }

            foreach (int target in emptyTargets) {
                interestsByTarget.Remove(target);
            }
        }

        internal static bool SendRequest(int targetPlayer, PlayerNetworkDataFlags flags, int durationTicks) {
            if (!VaultUtils.isClient || targetPlayer == Main.myPlayer || !IsValidPlayerIndex(targetPlayer)) {
                return false;
            }

            flags = PlayerNetworkPacketIO.NormalizeFlags(flags);
            long currentTick = PlayerNetwork.GetCurrentTick();
            if (localRequestCooldownByTarget.TryGetValue(targetPlayer, out long nextAllowedTick)
                && currentTick < nextAllowedTick) {
                return true;
            }

            localRequestCooldownByTarget[targetPlayer] = currentTick + PlayerNetwork.RequestCooldownTicks;
            ModPacket packet = VaultMod.Instance.GetPacket();
            packet.Write((byte)MessageType.PlayerNet_RequestSnapshot);
            packet.Write((byte)targetPlayer);
            PlayerNetworkPacketIO.WriteFlags(packet, flags);
            packet.Write((ushort)Utils.Clamp(durationTicks, 0, ushort.MaxValue));
            packet.Send();
            return true;
        }

        internal static bool SendRelease(int targetPlayer, PlayerNetworkDataFlags flags) {
            if (!VaultUtils.isClient || targetPlayer == Main.myPlayer || !IsValidPlayerIndex(targetPlayer)) {
                return false;
            }

            ModPacket packet = VaultMod.Instance.GetPacket();
            packet.Write((byte)MessageType.PlayerNet_ReleaseInterest);
            packet.Write((byte)targetPlayer);
            PlayerNetworkPacketIO.WriteFlags(packet, flags);
            packet.Send();
            localRequestCooldownByTarget.Remove(targetPlayer);
            return true;
        }

        internal static void SendLocalSnapshot(PlayerNetworkSnapshot snapshot) {
            if (!VaultUtils.isClient || snapshot.Flags == PlayerNetworkDataFlags.None) {
                return;
            }

            ModPacket packet = VaultMod.Instance.GetPacket();
            packet.Write((byte)MessageType.PlayerNet_Snapshot);
            PlayerNetworkPacketIO.WriteSnapshot(packet, snapshot, false);
            packet.Send();
        }

        internal static void HandlePacket(MessageType type, BinaryReader reader, int whoAmI) {
            switch (type) {
                case MessageType.PlayerNet_RequestSnapshot:
                    HandleRequestSnapshot(reader, whoAmI);
                    break;
                case MessageType.PlayerNet_QuerySnapshot:
                    HandleQuerySnapshot(reader);
                    break;
                case MessageType.PlayerNet_Snapshot:
                    HandleSnapshot(reader, whoAmI);
                    break;
                case MessageType.PlayerNet_ReleaseInterest:
                    HandleReleaseInterest(reader, whoAmI);
                    break;
            }
        }

        private static void HandleRequestSnapshot(BinaryReader reader, int whoAmI) {
            int targetPlayer = reader.ReadByte();
            PlayerNetworkDataFlags flags = PlayerNetworkPacketIO.NormalizeFlags(PlayerNetworkPacketIO.ReadFlags(reader));
            int durationTicks = reader.ReadUInt16();

            if (!VaultUtils.isServer || !IsValidPlayerIndex(whoAmI) || !IsValidPlayerIndex(targetPlayer)
                || whoAmI == targetPlayer || !Main.player[whoAmI].active || !Main.player[targetPlayer].active) {
                return;
            }

            if (IsServerRequestCoolingDown(whoAmI, targetPlayer)) {
                return;
            }

            AddServerInterest(whoAmI, targetPlayer, flags, durationTicks);
            SendQuerySnapshot(targetPlayer, flags, durationTicks);
        }

        private static void HandleReleaseInterest(BinaryReader reader, int whoAmI) {
            int targetPlayer = reader.ReadByte();
            PlayerNetworkDataFlags flags = PlayerNetworkPacketIO.ReadFlags(reader);

            if (!VaultUtils.isServer || !IsValidPlayerIndex(whoAmI) || !IsValidPlayerIndex(targetPlayer)) {
                return;
            }

            RemoveServerInterest(whoAmI, targetPlayer, flags);
        }

        private static void HandleQuerySnapshot(BinaryReader reader) {
            PlayerNetworkDataFlags flags = PlayerNetworkPacketIO.NormalizeFlags(PlayerNetworkPacketIO.ReadFlags(reader));
            int durationTicks = reader.ReadUInt16();

            if (!VaultUtils.isClient) {
                return;
            }

            Main.LocalPlayer.GetModPlayer<VaultPlayer>().RespondPlayerNetworkQuery(flags, durationTicks);
        }

        private static void HandleSnapshot(BinaryReader reader, int whoAmI) {
            long currentTick = PlayerNetwork.GetCurrentTick();
            if (VaultUtils.isServer) {
                if (!IsValidPlayerIndex(whoAmI) || !Main.player[whoAmI].active) {
                    return;
                }

                PlayerNetworkSnapshot snapshot = PlayerNetworkPacketIO.ReadSnapshot(reader, whoAmI, currentTick);
                ForwardSnapshotToInterestedClients(snapshot);
                return;
            }

            if (!VaultUtils.isClient) {
                return;
            }

            int targetPlayer = reader.ReadByte();
            if (!IsValidPlayerIndex(targetPlayer) || !Main.player[targetPlayer].active) {
                PlayerNetworkPacketIO.ReadSnapshot(reader, targetPlayer, currentTick);
                return;
            }

            PlayerNetworkSnapshot remoteSnapshot = PlayerNetworkPacketIO.ReadSnapshot(reader, targetPlayer, currentTick);
            Main.player[targetPlayer].GetModPlayer<VaultPlayer>().CacheNetworkSnapshot(remoteSnapshot);
        }

        private static void AddServerInterest(int requester, int targetPlayer, PlayerNetworkDataFlags flags, int durationTicks) {
            long currentTick = PlayerNetwork.GetCurrentTick();
            long expireTick = currentTick + (durationTicks > 0 ? durationTicks : PlayerNetwork.DefaultCacheTtl);
            bool oneShot = durationTicks <= 0;

            if (!interestsByTarget.TryGetValue(targetPlayer, out Dictionary<int, InterestState> requesters)) {
                requesters = [];
                interestsByTarget[targetPlayer] = requesters;
            }

            if (requesters.TryGetValue(requester, out InterestState state)) {
                state.Flags |= flags;
                state.ExpireTick = state.ExpireTick > expireTick ? state.ExpireTick : expireTick;
                state.OneShot = state.OneShot && oneShot;
            }
            else {
                state = new InterestState {
                    Flags = flags,
                    ExpireTick = expireTick,
                    OneShot = oneShot,
                };
            }

            requesters[requester] = state;
        }

        private static void RemoveServerInterest(int requester, int targetPlayer, PlayerNetworkDataFlags flags) {
            if (!interestsByTarget.TryGetValue(targetPlayer, out Dictionary<int, InterestState> requesters)
                || !requesters.TryGetValue(requester, out InterestState state)) {
                return;
            }

            if (flags == PlayerNetworkDataFlags.None || flags == PlayerNetworkDataFlags.All) {
                requesters.Remove(requester);
            }
            else {
                state.Flags &= ~flags;
                if (state.Flags == PlayerNetworkDataFlags.None) {
                    requesters.Remove(requester);
                }
                else {
                    requesters[requester] = state;
                }
            }

            if (requesters.Count == 0) {
                interestsByTarget.Remove(targetPlayer);
            }
        }

        private static void SendQuerySnapshot(int targetPlayer, PlayerNetworkDataFlags flags, int durationTicks) {
            ModPacket packet = VaultMod.Instance.GetPacket();
            packet.Write((byte)MessageType.PlayerNet_QuerySnapshot);
            PlayerNetworkPacketIO.WriteFlags(packet, flags);
            packet.Write((ushort)Utils.Clamp(durationTicks, 0, ushort.MaxValue));
            packet.Send(targetPlayer);
        }

        private static void ForwardSnapshotToInterestedClients(PlayerNetworkSnapshot snapshot) {
            if (snapshot.Flags == PlayerNetworkDataFlags.None
                || !interestsByTarget.TryGetValue(snapshot.PlayerIndex, out Dictionary<int, InterestState> requesters)) {
                return;
            }

            long currentTick = PlayerNetwork.GetCurrentTick();
            List<int> removeRequesters = [];
            foreach (var requesterPair in requesters) {
                int requester = requesterPair.Key;
                InterestState state = requesterPair.Value;
                if (state.ExpireTick <= currentTick || !IsValidPlayerIndex(requester) || !Main.player[requester].active) {
                    removeRequesters.Add(requester);
                    continue;
                }

                PlayerNetworkSnapshot filteredSnapshot = snapshot.Filter(state.Flags);
                if (filteredSnapshot.Flags != PlayerNetworkDataFlags.None) {
                    SendSnapshotToClient(requester, filteredSnapshot);
                    if (state.OneShot) {
                        removeRequesters.Add(requester);
                    }
                }
            }

            foreach (int requester in removeRequesters) {
                requesters.Remove(requester);
            }

            if (requesters.Count == 0) {
                interestsByTarget.Remove(snapshot.PlayerIndex);
            }
        }

        private static void SendSnapshotToClient(int requester, PlayerNetworkSnapshot snapshot) {
            ModPacket packet = VaultMod.Instance.GetPacket();
            packet.Write((byte)MessageType.PlayerNet_Snapshot);
            PlayerNetworkPacketIO.WriteSnapshot(packet, snapshot, true);
            packet.Send(requester);
        }

        private static bool IsServerRequestCoolingDown(int requester, int targetPlayer) {
            long currentTick = PlayerNetwork.GetCurrentTick();
            int key = requester << 8 | targetPlayer;
            if (serverRequestCooldownByPair.TryGetValue(key, out long nextAllowedTick)
                && currentTick < nextAllowedTick) {
                return true;
            }

            serverRequestCooldownByPair[key] = currentTick + PlayerNetwork.RequestCooldownTicks;
            return false;
        }

        private static bool IsValidPlayerIndex(int playerIndex)
            => playerIndex >= 0 && playerIndex < Main.maxPlayers;
    }
}
