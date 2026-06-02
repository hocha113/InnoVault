using Microsoft.Xna.Framework;
using System;
using Terraria;

namespace InnoVault.VaultNetWork
{
    /// <summary>
    /// 描述玩家基础网络数据快照中包含的字段。
    /// </summary>
    [Flags]
    public enum PlayerNetworkDataFlags : byte
    {
        /// <summary>没有请求任何字段。</summary>
        None = 0,
        /// <summary>玩家指向鼠标的单位方向。</summary>
        MouseDirection = 1 << 0,
        /// <summary>玩家鼠标的世界坐标。</summary>
        MouseWorld = 1 << 1,
        /// <summary>玩家鼠标左右键状态。</summary>
        MouseButtons = 1 << 2,
        /// <summary>轻量瞄准数据，默认只包含鼠标方向。</summary>
        BasicAim = MouseDirection,
        /// <summary>基础输入数据，包含鼠标方向、鼠标世界坐标与鼠标按键。</summary>
        BasicInput = MouseDirection | MouseWorld | MouseButtons,
        /// <summary>当前框架支持的所有基础字段。</summary>
        All = MouseDirection | MouseWorld | MouseButtons,
    }

    /// <summary>
    /// 玩家基础网络数据的只读快照。
    /// </summary>
    public readonly struct PlayerNetworkSnapshot
    {
        /// <summary>快照所属玩家的索引。</summary>
        public int PlayerIndex { get; }
        /// <summary>该快照实际包含的字段。</summary>
        public PlayerNetworkDataFlags Flags { get; }
        /// <summary>玩家鼠标的世界坐标，仅在包含 <see cref="PlayerNetworkDataFlags.MouseWorld"/> 时有效。</summary>
        public Vector2 MouseWorld { get; }
        /// <summary>玩家指向鼠标的单位方向，仅在包含 <see cref="PlayerNetworkDataFlags.MouseDirection"/> 时有效。</summary>
        public Vector2 MouseDirection { get; }
        /// <summary>玩家鼠标左键状态，仅在包含 <see cref="PlayerNetworkDataFlags.MouseButtons"/> 时有效。</summary>
        public bool MouseLeft { get; }
        /// <summary>玩家鼠标右键状态，仅在包含 <see cref="PlayerNetworkDataFlags.MouseButtons"/> 时有效。</summary>
        public bool MouseRight { get; }
        /// <summary>本地接收或采样该快照时的游戏帧。</summary>
        public long UpdateTick { get; }
        /// <summary>该快照是否来自本地玩家实时采样。</summary>
        public bool IsLocalPlayer { get; }

        /// <summary>
        /// 创建一个玩家基础网络数据快照。
        /// </summary>
        public PlayerNetworkSnapshot(int playerIndex, PlayerNetworkDataFlags flags, Vector2 mouseWorld
            , Vector2 mouseDirection, bool mouseLeft, bool mouseRight, long updateTick, bool isLocalPlayer) {
            PlayerIndex = playerIndex;
            Flags = flags & PlayerNetworkDataFlags.All;
            MouseWorld = mouseWorld;
            MouseDirection = mouseDirection;
            MouseLeft = mouseLeft;
            MouseRight = mouseRight;
            UpdateTick = updateTick;
            IsLocalPlayer = isLocalPlayer;
        }

        /// <summary>
        /// 检查快照是否包含指定字段。
        /// </summary>
        public bool Has(PlayerNetworkDataFlags flags) => (Flags & flags) == flags;

        /// <summary>
        /// 获取快照相对当前游戏帧的年龄。
        /// </summary>
        public int AgeTicks => (int)Math.Max(0, PlayerNetwork.GetCurrentTick() - UpdateTick);

        /// <summary>
        /// 检查快照是否仍在允许的缓存时间内。
        /// </summary>
        public bool IsFresh(int maxAgeTicks) => maxAgeTicks < 0 || AgeTicks <= maxAgeTicks;

        /// <summary>
        /// 使用鼠标方向和玩家中心重建一个近似鼠标世界坐标。
        /// </summary>
        public Vector2 GetApproxMouseWorld(Player player, float distance = 500f) => player.Center + MouseDirection * distance;

        internal PlayerNetworkSnapshot Filter(PlayerNetworkDataFlags requestedFlags) {
            PlayerNetworkDataFlags flags = Flags & requestedFlags & PlayerNetworkDataFlags.All;
            return new PlayerNetworkSnapshot(PlayerIndex, flags, MouseWorld, MouseDirection
                , MouseLeft, MouseRight, UpdateTick, IsLocalPlayer);
        }
    }
}
