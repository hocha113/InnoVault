using Microsoft.Xna.Framework;
using System.Collections.Generic;
using Terraria;
using Terraria.ModLoader;

namespace InnoVault.Actors
{
    /// <summary>
    /// <see cref="SolidActor"/> 的玩家承载驱动器
    /// <para>
    /// 阻挡 / 站立判定由 <see cref="SolidActorCollision"/> 的碰撞注入负责；本类只补一件原版碰撞函数无法表达的事：
    /// 玩家站在移动实体顶面时，将实体的运动传导给玩家（"被平台带着走"）
    /// </para>
    /// <para>
    /// 本类不再写死承载方式，而是作为驱动器：检测"玩家站在哪个实体顶面"，构造 <see cref="SolidActorCarryContext"/>，
    /// 依次派发给实体自身的 <see cref="SolidActor.CarryPlayer"/> 与全局 <see cref="GlobalActor.CarryPlayer"/>，
    /// 最后把累计位移一次性施加给玩家。具体承载手感（刚体 / 传送带 / 弹床 / 冰面…）由这些可重写的消费者定义
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

            IReadOnlyList<SolidActor> solids = SolidActorCollision.ActiveSolids;
            Rectangle p = Player.Hitbox;

            for (int i = 0; i < count; i++) {
                SolidActor box = solids[i];
                if (!IsStandingOn(p, box.SolidBox)) {
                    continue;
                }
                if (!box.CanCarryPlayer(Player)) {
                    continue;
                }

                ResolveCarry(box);
                break;
            }
        }

        //玩家底部贴着实体顶面、且没有上升，判定为"站在该实体上"
        private bool IsStandingOn(Rectangle player, Rectangle box) {
            bool horizontalOverlap = player.Right > box.Left && player.Left < box.Right;
            if (!horizontalOverlap) {
                return false;
            }
            return Player.velocity.Y >= 0f && player.Bottom >= box.Top - 2 && player.Bottom <= box.Top + 8;
        }

        //派发承载：实体自身消费者 → 全局消费者 → 统一施加累计位移
        private void ResolveCarry(SolidActor box) {
            SolidActorCarryContext ctx = new(box, Player);

            bool runGlobals = box.CarryPlayer(ref ctx);

            if (runGlobals && ActorLoader.HookCarryPlayer != null) {
                foreach (GlobalActor global in ActorLoader.HookCarryPlayer.Enumerate()) {
                    global.CarryPlayer(box, ref ctx);
                }
            }

            Player.position += ctx.Displacement;
        }
    }
}
