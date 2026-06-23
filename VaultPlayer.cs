using InnoVault.Actors;
using InnoVault.GameSystem;
using InnoVault.TileProcessors;
using InnoVault.UIHandles;
using InnoVault.VaultNetworks;
using Microsoft.Xna.Framework;
using System;
using Terraria;
using Terraria.ModLoader;

namespace InnoVault
{
    internal sealed class VaultPlayer : ModPlayer
    {
        private PlayerNetworkSnapshot networkSnapshot;
        private bool hasNetworkSnapshot;
        private PlayerNetworkDataFlags activeNetworkFlags;
        private int activeNetworkTicks;
        private PlayerNetworkSnapshot lastSentNetworkSnapshot;
        private bool hasLastSentNetworkSnapshot;
        private long lastNetworkSnapshotSendTick;

        public override void OnEnterWorld() {
            UIHandleLoader.OnEnterWorld();
            NPCOverrideNetWork.OnEnterWorld();
            TileProcessorNetWork.ClientRequest_TPData_Send();
            ActorNetWork.SendActorFullSyncRequest();
        }

        public override void PostUpdate() {
            UIInputGuard.Tick();
            UpdateActiveNetworkQuery();
        }

        internal bool TryGetNetworkSnapshot(out PlayerNetworkSnapshot snapshot, int maxAgeTicks) {
            snapshot = networkSnapshot;
            return hasNetworkSnapshot && networkSnapshot.IsFresh(maxAgeTicks);
        }

        internal void CacheNetworkSnapshot(PlayerNetworkSnapshot snapshot) {
            networkSnapshot = snapshot;
            hasNetworkSnapshot = snapshot.Flags != PlayerNetworkDataFlags.None;
        }

        internal void RespondPlayerNetworkQuery(PlayerNetworkDataFlags flags, int durationTicks) {
            if (!VaultUtils.isClient || Main.myPlayer != Player.whoAmI || flags == PlayerNetworkDataFlags.None) {
                return;
            }

            TrySendPlayerNetworkSnapshot(flags, true);
            if (durationTicks <= 0) {
                return;
            }

            activeNetworkFlags |= flags;
            activeNetworkTicks = Math.Max(activeNetworkTicks, durationTicks);
        }

        internal static bool TryCreateLocalNetworkSnapshot(Player player, PlayerNetworkDataFlags requestedFlags
            , out PlayerNetworkSnapshot snapshot) {
            snapshot = default;
            if (Main.dedServ || player == null || player.whoAmI != Main.myPlayer) {
                return false;
            }

            requestedFlags = PlayerNetworkPacketIO.NormalizeFlags(requestedFlags);
            PlayerNetworkDataFlags actualFlags = PlayerNetworkDataFlags.None;
            Vector2 mouseWorld = Vector2.Zero;
            Vector2 mouseDirection = Vector2.Zero;
            bool mouseLeft = false;
            bool mouseRight = false;

            bool needsMouseWorld = (requestedFlags & PlayerNetworkDataFlags.MouseWorld) != 0;
            bool needsMouseDirection = (requestedFlags & PlayerNetworkDataFlags.MouseDirection) != 0;
            if (needsMouseWorld || needsMouseDirection) {
                mouseWorld = Main.MouseWorld;
                if (needsMouseWorld) {
                    actualFlags |= PlayerNetworkDataFlags.MouseWorld;
                }

                Vector2 toMouse = mouseWorld - player.Center;
                if (needsMouseDirection && toMouse.LengthSquared() > 1f) {
                    mouseDirection = Vector2.Normalize(toMouse);
                    actualFlags |= PlayerNetworkDataFlags.MouseDirection;
                }
            }

            if ((requestedFlags & PlayerNetworkDataFlags.MouseButtons) != 0) {
                mouseLeft = Main.mouseLeft;
                mouseRight = Main.mouseRight;
                actualFlags |= PlayerNetworkDataFlags.MouseButtons;
            }

            if (actualFlags == PlayerNetworkDataFlags.None) {
                return false;
            }

            snapshot = new PlayerNetworkSnapshot(player.whoAmI, actualFlags, mouseWorld, mouseDirection
                , mouseLeft, mouseRight, PlayerNetwork.GetCurrentTick(), true);
            return true;
        }

        private void UpdateActiveNetworkQuery() {
            if (!VaultUtils.isClient || Main.myPlayer != Player.whoAmI || activeNetworkTicks <= 0) {
                return;
            }

            activeNetworkTicks--;
            TrySendPlayerNetworkSnapshot(activeNetworkFlags, false);
            if (activeNetworkTicks <= 0) {
                activeNetworkFlags = PlayerNetworkDataFlags.None;
            }
        }

        private void TrySendPlayerNetworkSnapshot(PlayerNetworkDataFlags flags, bool force) {
            if (!TryCreateLocalNetworkSnapshot(Player, flags, out PlayerNetworkSnapshot snapshot)) {
                return;
            }

            long currentTick = PlayerNetwork.GetCurrentTick();
            if (!force) {
                long elapsedTicks = currentTick - lastNetworkSnapshotSendTick;
                if (elapsedTicks < PlayerNetwork.MinSnapshotIntervalTicks || !ShouldSendNetworkSnapshot(snapshot, elapsedTicks)) {
                    return;
                }
            }

            PlayerNetworkCore.SendLocalSnapshot(snapshot);
            lastSentNetworkSnapshot = snapshot;
            hasLastSentNetworkSnapshot = true;
            lastNetworkSnapshotSendTick = currentTick;
        }

        private bool ShouldSendNetworkSnapshot(PlayerNetworkSnapshot snapshot, long elapsedTicks) {
            if (!hasLastSentNetworkSnapshot || elapsedTicks >= PlayerNetwork.SnapshotHeartbeatTicks) {
                return true;
            }

            if ((snapshot.Flags & PlayerNetworkDataFlags.MouseButtons) != 0
                && (snapshot.MouseLeft != lastSentNetworkSnapshot.MouseLeft
                || snapshot.MouseRight != lastSentNetworkSnapshot.MouseRight)) {
                return true;
            }

            if ((snapshot.Flags & PlayerNetworkDataFlags.MouseDirection) != 0) {
                if ((lastSentNetworkSnapshot.Flags & PlayerNetworkDataFlags.MouseDirection) == 0) {
                    return true;
                }

                float dot = Vector2.Dot(snapshot.MouseDirection, lastSentNetworkSnapshot.MouseDirection);
                dot = MathHelper.Clamp(dot, -1f, 1f);
                if (Math.Acos(dot) >= PlayerNetwork.MouseDirectionAngleThreshold) {
                    return true;
                }
            }

            if ((snapshot.Flags & PlayerNetworkDataFlags.MouseWorld) != 0) {
                if ((lastSentNetworkSnapshot.Flags & PlayerNetworkDataFlags.MouseWorld) == 0
                    || snapshot.MouseWorld.DistanceSQ(lastSentNetworkSnapshot.MouseWorld) >= PlayerNetwork.MouseWorldDistanceThresholdSq) {
                    return true;
                }
            }

            return false;
        }
    }
}
