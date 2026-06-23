using Microsoft.Xna.Framework;
using Terraria;

namespace InnoVault.Actors
{
    /// <summary>
    /// 描述"玩家站在某个 <see cref="SolidActor"/> 顶面被其承载"这一帧的上下文
    /// <para>
    /// 承载逻辑通过累加 <see cref="Displacement"/> 表达"本帧要把玩家额外位移多少"，由承载驱动器
    /// （<see cref="SolidActorPlayer"/>）在所有消费者（实体自身 + 全局 <see cref="GlobalActor"/>）贡献完毕后
    /// 一次性施加到玩家位置，避免各处直接改写玩家坐标造成的叠加或抖动
    /// </para>
    /// </summary>
    public struct SolidActorCarryContext
    {
        /// <summary>承载玩家的实体</summary>
        public readonly SolidActor Carrier;
        /// <summary>被承载的玩家</summary>
        public readonly Player Player;
        /// <summary>承载实体本帧自身的位移（等于 <see cref="SolidActor.FrameVelocity"/>）</summary>
        public readonly Vector2 CarrierDelta;
        /// <summary>本帧要施加给玩家的累计额外位移；承载驱动器会在所有消费者处理完后统一应用</summary>
        public Vector2 Displacement;
        /// <summary>
        /// 是否把玩家锁定为"站在固体上"的接地状态：承载驱动器会在施加位移后清除向下速度并复位
        /// <see cref="Terraria.Player.fallStart"/>，避免平台升降时玩家被误判为腾空而抖动 / 扑动翅膀
        /// <para>刚体承载默认 <see langword="true"/>；弹床等需要保留竖直速度的承载应在重写中置为 <see langword="false"/></para>
        /// </summary>
        public bool KeepGrounded;

        /// <summary>构造一次承载上下文</summary>
        public SolidActorCarryContext(SolidActor carrier, Player player) {
            Carrier = carrier;
            Player = player;
            CarrierDelta = carrier.FrameVelocity;
            Displacement = Vector2.Zero;
            KeepGrounded = false;
        }
    }

    /// <summary>
    /// 全局承载钩子委托：当任意玩家被某个 <see cref="SolidActor"/> 承载时回调，
    /// 供 <see cref="GlobalActor.CarryPlayer"/> 接入 InnoVault 的全局钩子派发缓存
    /// </summary>
    /// <param name="actor">承载玩家的实体</param>
    /// <param name="ctx">承载上下文，可继续累加 <see cref="SolidActorCarryContext.Displacement"/></param>
    public delegate void CarryPlayerHook(SolidActor actor, ref SolidActorCarryContext ctx);
}
