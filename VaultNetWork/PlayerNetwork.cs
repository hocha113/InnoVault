using Microsoft.Xna.Framework;
using Terraria;

namespace InnoVault.VaultNetWork
{
    /// <summary>
    /// 提供玩家基础网络数据的按需请求与缓存读取 API。
    /// </summary>
    public static class PlayerNetwork
    {
        /// <summary>默认缓存有效时间，单位为游戏帧。</summary>
        public const int DefaultCacheTtl = 30;
        /// <summary>默认连续兴趣续订时间，单位为游戏帧。</summary>
        public const int DefaultInterestTtl = 30;

        internal const int MinSnapshotIntervalTicks = 4;
        internal const int SnapshotHeartbeatTicks = 30;
        internal const int RequestCooldownTicks = 6;
        internal const float MouseDirectionAngleThreshold = 0.16f;
        internal const float MouseWorldDistanceThresholdSq = 16f;

        /// <summary>
        /// 尝试读取玩家基础网络数据快照。该方法只读取缓存，不会产生网络包。
        /// </summary>
        public static bool TryGetSnapshot(Player player, out PlayerNetworkSnapshot snapshot, int maxAgeTicks = DefaultCacheTtl)
            => TryGetSnapshot(player, PlayerNetworkDataFlags.None, out snapshot, maxAgeTicks);

        /// <summary>
        /// 尝试读取玩家指向鼠标的单位方向。该方法只读取缓存，不会产生网络包。
        /// </summary>
        public static bool TryGetMouseDirection(Player player, out Vector2 direction, int maxAgeTicks = DefaultCacheTtl) {
            direction = Vector2.Zero;
            if (!TryGetSnapshot(player, PlayerNetworkDataFlags.MouseDirection, out PlayerNetworkSnapshot snapshot, maxAgeTicks)) {
                return false;
            }

            if (snapshot.Has(PlayerNetworkDataFlags.MouseDirection)) {
                direction = snapshot.MouseDirection;
                return true;
            }

            if (!snapshot.Has(PlayerNetworkDataFlags.MouseWorld)) {
                return false;
            }

            Vector2 toMouse = snapshot.MouseWorld - player.Center;
            if (toMouse.LengthSquared() <= 1f) {
                return false;
            }

            direction = Vector2.Normalize(toMouse);
            return true;
        }

        /// <summary>
        /// 尝试用玩家中心与鼠标方向重建近似鼠标世界坐标。该方法只读取缓存，不会产生网络包。
        /// </summary>
        public static bool TryGetApproxMouseWorld(Player player, out Vector2 mouseWorld
            , float distance = 500f, int maxAgeTicks = DefaultCacheTtl) {
            mouseWorld = Vector2.Zero;
            if (!TryGetMouseDirection(player, out Vector2 direction, maxAgeTicks)) {
                return false;
            }

            mouseWorld = player.Center + direction * distance;
            return true;
        }

        /// <summary>
        /// 请求指定玩家的一次性基础网络数据。命中未过期缓存时不会发包。
        /// </summary>
        public static bool RequestSnapshot(Player player
            , PlayerNetworkDataFlags flags = PlayerNetworkDataFlags.BasicInput) {
            flags = PlayerNetworkPacketIO.NormalizeFlags(flags);
            if (!CanRequestRemotePlayer(player)) {
                return false;
            }

            if (TryGetSnapshot(player, flags, out _, DefaultCacheTtl)) {
                return true;
            }

            return PlayerNetworkCore.SendRequest(player.whoAmI, flags, 0);
        }

        /// <summary>
        /// 续订对指定玩家基础网络数据的短期兴趣，用于连续追踪场景。
        /// </summary>
        public static bool KeepAlive(Player player, PlayerNetworkDataFlags flags
            , int durationTicks = DefaultInterestTtl) {
            flags = PlayerNetworkPacketIO.NormalizeFlags(flags);
            if (!CanRequestRemotePlayer(player)) {
                return false;
            }

            durationTicks = Utils.Clamp(durationTicks, 1, ushort.MaxValue);
            return PlayerNetworkCore.SendRequest(player.whoAmI, flags, durationTicks);
        }

        /// <summary>
        /// 释放对指定玩家基础网络数据的兴趣。
        /// </summary>
        public static bool Release(Player player, PlayerNetworkDataFlags flags = PlayerNetworkDataFlags.All) {
            if (!CanRequestRemotePlayer(player)) {
                return false;
            }

            return PlayerNetworkCore.SendRelease(player.whoAmI, flags & PlayerNetworkDataFlags.All);
        }

        internal static long GetCurrentTick() => unchecked((long)Main.GameUpdateCount);

        internal static bool TryGetSnapshot(Player player, PlayerNetworkDataFlags requiredFlags
            , out PlayerNetworkSnapshot snapshot, int maxAgeTicks) {
            snapshot = default;
            if (!IsValidPlayer(player)) {
                return false;
            }

            if (!Main.dedServ && player.whoAmI == Main.myPlayer) {
                PlayerNetworkDataFlags flags = requiredFlags == PlayerNetworkDataFlags.None
                    ? PlayerNetworkDataFlags.BasicInput
                    : requiredFlags;
                return VaultPlayer.TryCreateLocalNetworkSnapshot(player, flags, out snapshot);
            }

            VaultPlayer vaultPlayer = player.GetModPlayer<VaultPlayer>();
            if (!vaultPlayer.TryGetNetworkSnapshot(out snapshot, maxAgeTicks)) {
                return false;
            }

            return requiredFlags == PlayerNetworkDataFlags.None || snapshot.Has(requiredFlags);
        }

        internal static bool IsValidPlayer(Player player)
            => player != null && player.active && player.whoAmI >= 0 && player.whoAmI < Main.maxPlayers;

        private static bool CanRequestRemotePlayer(Player player) {
            if (!IsValidPlayer(player) || VaultUtils.isSinglePlayer || !VaultUtils.isClient) {
                return false;
            }

            return player.whoAmI != Main.myPlayer;
        }
    }
}
