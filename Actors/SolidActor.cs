using Microsoft.Xna.Framework;

namespace InnoVault.Actors
{
    /// <summary>
    /// 可被当作"物块碰撞"处理的可移动实体基类
    /// <para>
    /// 它的碰撞箱 <see cref="SolidBox"/> 会被注入到 <see cref="Terraria.Collision"/> 的碰撞结算里，
    /// 使玩家 / NPC / 弹幕 / 钩爪 都能像撞物块一样与其交互（站立、阻挡、勾住）。
    /// 阻挡判定由 <see cref="SolidActorCollision"/> 统一注入，无需逐实体编写代码。
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
    }
}
