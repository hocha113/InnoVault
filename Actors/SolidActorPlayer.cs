using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ModLoader;

namespace InnoVault.Actors
{
    /// <summary>
    /// <see cref="SolidActor"/> 的玩家承载层
    /// <para>
    /// 阻挡 / 站立判定由 <see cref="SolidActorCollision"/> 的碰撞注入负责；这里只补一件原版碰撞函数无法表达的事：
    /// 当玩家站在一个移动平台顶面时，把平台本帧的位移 <see cref="SolidActor.FrameVelocity"/> 叠加给玩家，
    /// 实现"被平台带着走"。仅对本地玩家解算（其位置本就由本地客户端权威并向其他端同步）。
    /// </para>
    /// </summary>
    public sealed class SolidActorPlayer : ModPlayer
    {
        /// <inheritdoc/>
        public override void PostUpdate() {
            if (Main.dedServ || Player.whoAmI != Main.myPlayer) {
                return;
            }

            int count = SolidActorCollision.ActiveCount;
            if (count == 0) {
                return;
            }

            System.Collections.Generic.IReadOnlyList<SolidActor> solids = SolidActorCollision.ActiveSolids;
            Rectangle p = Player.Hitbox;

            for (int i = 0; i < count; i++) {
                SolidActor box = solids[i];
                Rectangle b = box.SolidBox;

                bool horizontalOverlap = p.Right > b.Left && p.Left < b.Right;
                if (!horizontalOverlap) {
                    continue;
                }

                //玩家底部贴着盒子顶面且没有上升，判定为"站在平台上"
                bool restingOnTop = Player.velocity.Y >= 0f && p.Bottom >= b.Top - 2 && p.Bottom <= b.Top + 8;
                if (!restingOnTop) {
                    continue;
                }

                Vector2 delta = box.FrameVelocity;
                //水平始终跟随；竖直只在平台上升时带动，下降交给重力贴合以避免抖动
                Player.position.X += delta.X;
                if (delta.Y < 0f) {
                    Player.position.Y += delta.Y;
                }
                break;
            }
        }
    }
}
