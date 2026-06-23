using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using Terraria;
using Terraria.ModLoader;

namespace InnoVault.Actors
{
    /// <summary>
    /// <see cref="SolidActor"/> 的玩家承载驱动器
    /// <para>
    /// 阻挡 / 站立判定由 <see cref="SolidActorCollision"/> 的碰撞注入负责；本类补两件原版碰撞函数无法表达的事：
    /// 1. 在玩家物理更新前把平台本帧位移传导给玩家（"被平台带着走"）
    /// 2. 在物理更新后修正与移动固体相关的速度（相对运动互穿、顶头时保留微小下落速度以免误判为落地）
    /// </para>
    /// </summary>
    public sealed class SolidActorPlayer : ModPlayer
    {
        private const float HeadBumpFallSpeed = 0.1f;

        /// <summary>
        /// 在玩家物理 / TileCollision 之前承载，使本帧碰撞结算时玩家已与平台同步位移
        /// </summary>
        public override void PreUpdate() {
            if (Main.dedServ) {
                return;
            }

            TryCarryPlayer();
        }

        /// <summary>
        /// 物理更新后修正与移动固体相关的速度
        /// </summary>
        public override void PostUpdate() {
            if (Main.dedServ) {
                return;
            }

            ResolveSolidVelocity();
        }

        private void TryCarryPlayer() {
            int count = SolidActorCollision.ActiveCount;
            if (count == 0) {
                return;
            }

            IReadOnlyList<SolidActor> solids = SolidActorCollision.ActiveSolids;
            Rectangle p = Player.Hitbox;

            for (int i = 0; i < count; i++) {
                SolidActor box = solids[i];
                if (!IsStandingOn(p, box)) {
                    continue;
                }
                if (!box.CanCarryPlayer(Player)) {
                    continue;
                }

                ResolveCarry(box);
                break;
            }
        }

        //上一帧或本帧顶面都算"站在上面"，避免平台先位移后判定失败
        private bool IsStandingOn(Rectangle player, SolidActor box) {
            if (Player.velocity.Y < 0f) {
                return false;
            }
            Rectangle prevBox = box.LastPosition.GetRectangle(box.Size);
            return IsStandingOnFooting(player, prevBox) || IsStandingOnFooting(player, box.SolidBox);
        }

        //玩家底部贴着实体顶面，判定为"站在该实体上"
        private static bool IsStandingOnFooting(Rectangle player, Rectangle box) {
            bool horizontalOverlap = player.Right > box.Left && player.Left < box.Right;
            if (!horizontalOverlap) {
                return false;
            }
            return player.Bottom >= box.Top - 2 && player.Bottom <= box.Top + 8;
        }

        private void ResolveCarry(SolidActor box) {
            SolidActorCarryContext ctx = new(box, Player);

            bool runGlobals = box.CarryPlayer(ref ctx);

            if (runGlobals && ActorLoader.HookCarryPlayer != null) {
                foreach (GlobalActor global in ActorLoader.HookCarryPlayer.Enumerate()) {
                    global.CarryPlayer(box, ref ctx);
                }
            }

            Player.position += ctx.Displacement;

            if (ctx.KeepGrounded) {
                GroundOn(box);
            }
        }

        //把玩家锁定为"稳定站在该固体上"的接地状态，消除升降平台上的抖动与扑翅
        //清除向下速度 + 复位 fallStart，使后续动画 / 落地判定不会把玩家当成腾空
        private void GroundOn(SolidActor box) {
            //向下速度清零（接地）；上升速度（跳跃 / 弹床）保留，避免吞掉起跳
            if (Player.velocity.Y > 0f) {
                Player.velocity.Y = 0f;
            }

            //轻贴顶面：消除承载后可能残留的亚像素缝隙 / 重叠，保证下一帧仍被判定为接地
            float desiredBottom = box.SolidBox.Top;
            float currentBottom = Player.position.Y + Player.height;
            float gap = desiredBottom - currentBottom;
            if (gap > 0f && gap <= 8f) {
                Player.position.Y += gap;
            }

            //复位 fallStart：让原版的下落 / 摔伤 / 动画状态机不把"被平台带动"误认为坠落
            Player.fallStart = (int)(Player.position.Y / 16f);
        }

        private void ResolveSolidVelocity() {
            int count = SolidActorCollision.ActiveCount;
            if (count == 0) {
                return;
            }

            IReadOnlyList<SolidActor> solids = SolidActorCollision.ActiveSolids;
            Rectangle p = Player.Hitbox;

            for (int i = 0; i < count; i++) {
                SolidActor box = solids[i];
                Rectangle b = box.SolidBox;

                if (IsHeadBump(p, b) && Player.velocity.Y < 0f) {
                    //顶头时保留微小下落速度，避免 velocity.Y==0 被误判为站在地面（翅膀收回等）
                    Player.velocity.Y = HeadBumpFallSpeed;
                }

                if (box.OneWay || !p.Intersects(b)) {
                    continue;
                }

                ResolveHorizontalBlockVelocity(Player, p, b, box);
            }
        }

        //玩家自下方向上撞到实体底面
        private static bool IsHeadBump(Rectangle player, Rectangle box) {
            bool horizontalOverlap = player.Right > box.Left && player.Left < box.Right;
            if (!horizontalOverlap) {
                return false;
            }
            return player.Top <= box.Bottom + 2 && player.Bottom > box.Center.Y;
        }

        //消去玩家相对盒子的"向墙内"速度分量，解决相反速度互穿
        private static void ResolveHorizontalBlockVelocity(Player player, Rectangle playerBox, Rectangle box, SolidActor carrier) {
            float overlapX = Math.Min(playerBox.Right, box.Right) - Math.Max(playerBox.Left, box.Left);
            float overlapY = Math.Min(playerBox.Bottom, box.Bottom) - Math.Max(playerBox.Top, box.Top);
            if (overlapX <= 0f || overlapY <= 0f || overlapX >= overlapY) {
                return;
            }

            float relVelX = player.velocity.X - carrier.Velocity.X;
            if (playerBox.Center.X < box.Center.X) {
                if (relVelX > 0f) {
                    player.velocity.X = carrier.Velocity.X;
                }
            }
            else if (relVelX < 0f) {
                player.velocity.X = carrier.Velocity.X;
            }
        }
    }
}
