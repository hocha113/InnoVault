using Microsoft.Xna.Framework;
using Terraria;

namespace InnoVault.Actors
{
    /// <summary>
    /// 可被当作"物块碰撞"处理的可移动实体基类
    /// <para>
    /// 它的碰撞箱 <see cref="SolidBox"/> 会被注入到 <see cref="Terraria.Collision"/> 的碰撞结算里，
    /// 使玩家 / NPC / 弹幕 / 钩爪 都能像撞物块一样与其交互（站立、阻挡、勾住）
    /// 阻挡判定由 <see cref="SolidActorCollision"/> 统一注入，无需逐实体编写代码
    /// </para>
    /// </summary>
    public abstract class SolidActor : Actor
    {
        /// <summary>
        /// 上一帧的左上角位置，用于计算本帧自身位移 <see cref="FrameVelocity"/>
        /// </summary>
        public Vector2 LastPosition;
        /// <summary>
        /// 本帧平台自身的位移量，承载玩家时会把这个量叠加给玩家，实现"被平台带着走"
        /// </summary>
        public Vector2 FrameVelocity => Position - LastPosition;
        /// <summary>
        /// 为 <see langword="true"/> 时只阻挡顶面（类似木平台，可从下方穿过、可按↓+跳穿下）；
        /// 为 <see langword="false"/> 时四面都是固体
        /// </summary>
        public bool OneWay;
        /// <summary>
        /// 作为单向平台时，是否允许玩家按↓+跳穿下，默认允许
        /// </summary>
        public bool AllowFallThrough = true;
        /// <summary>
        /// 是否参与碰撞，临时关闭碰撞时设为 <see langword="false"/>（实体仍会正常更新与绘制）
        /// </summary>
        public bool SolidEnabled = true;
        /// <summary>
        /// 本帧是否已在 <see cref="SolidActorCollision"/> 的 <c>PreUpdateEntities</c> 中提前完成 AI 与位移，
        /// 供 <see cref="ActorLoader"/> 在 <c>PostUpdateEverything</c> 中跳过重复更新
        /// </summary>
        internal bool PreUpdatedThisFrame;
        /// <summary>
        /// 提供给碰撞注入层使用的纯矩形碰撞箱，默认等于 <see cref="Actor.HitBox"/>，
        /// 子类可重写以使用独立于绘制尺寸的碰撞区域
        /// </summary>
        public virtual Rectangle SolidBox => HitBox;

        /// <summary>
        /// 已被密封，请勿重写。该实现会在移动前记录 <see cref="LastPosition"/> 后再调用 <see cref="SolidAI"/>，
        /// 重写它会破坏 <see cref="FrameVelocity"/> 的计算
        /// </summary>
        public sealed override void AI() {
            LastPosition = Position;
            SolidAI();
        }

        /// <summary>
        /// 子类在此编写平台的移动逻辑（巡逻、电梯、跟随某实体等），用法等同于普通 <see cref="Actor.AI"/>
        /// </summary>
        public virtual void SolidAI() {

        }

        /// <summary>
        /// 玩家能否被该实体承载（站在顶面被带着走），默认 <see langword="true"/>
        /// <br>返回 <see langword="false"/> 时玩家仍会被碰撞阻挡，但不会被该实体带动</br>
        /// </summary>
        /// <param name="player">候选被承载的玩家</param>
        public virtual bool CanCarryPlayer(Player player) => true;

        /// <summary>
        /// 玩家站在该实体顶面时每帧调用一次，用于把实体的运动传导给玩家
        /// <para>
        /// 通过累加 <see cref="SolidActorCarryContext.Displacement"/> 表达"本帧把玩家额外位移多少"，
        /// 由承载驱动器统一施加。默认实现为刚体承载：水平与竖直<b>双向</b>跟随实体本帧位移，并置
        /// <see cref="SolidActorCarryContext.KeepGrounded"/> 为 <see langword="true"/> 把玩家锁定为接地状态
        /// 双向竖直承载（而非只在上升时带动）可消除平台下降时玩家与顶面之间裂开的缝隙——正是该缝隙导致玩家
        /// 被反复判定为腾空，从而出现抖动 / 扑翅
        /// </para>
        /// <para>
        /// 重写它可实现更丰富的接触行为，例如：传送带（在 <see cref="SolidActorCarryContext.Displacement"/> 上叠加水平量）、
        /// 弹床（改写 <c>ctx.Player.velocity</c> 并置 <see cref="SolidActorCarryContext.KeepGrounded"/> 为 <see langword="false"/>）、
        /// 冰面（对 <see cref="SolidActorCarryContext.CarrierDelta"/> 做衰减）等
        /// </para>
        /// </summary>
        /// <param name="ctx">承载上下文</param>
        /// <returns>返回 <see langword="false"/> 可阻止后续的全局承载逻辑 <see cref="GlobalActor.CarryPlayer"/></returns>
        public virtual bool CarryPlayer(ref SolidActorCarryContext ctx) {
            ctx.Displacement += ctx.CarrierDelta;
            ctx.KeepGrounded = true;
            return true;
        }
    }
}
