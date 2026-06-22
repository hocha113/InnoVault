using InnoVault.GameSystem;
using Microsoft.Xna.Framework;
using System.Collections.Generic;
using Terraria;
using Terraria.ID;

namespace InnoVault.Actors
{
    /// <summary>
    /// 让钩爪（aiStyle 7）能够勾住可移动的 <see cref="SolidActor"/> 并被其带着走
    /// <para>
    /// 本类不再是独立的 <see cref="Terraria.ModLoader.GlobalProjectile"/>，而是作为 <see cref="ProjOverride"/> 的一个
    /// 全局（<see cref="TargetID"/> 为 -1）消费者：通过 InnoVault 在 <c>ProjectileLoader.GrappleCanLatchOnTo</c> 上挂载的
    /// 统一钩子，把"被 <see cref="SolidActor"/> 覆盖的图格"声明为可勾图格。原版钩爪 AI（<c>AI_007_GrapplingHooks</c>）随后会
    /// 原生执行 latch 的全部行为——锚点吸附、勾住音效、瞬移钩瞬移、网络同步、多钩数量裁剪、拉拽、缩回判定、链条与特殊视觉等，
    /// 全部由原版自己完成，从而天然兼容所有原版与模组钩爪
    /// </para>
    /// <para>
    /// 唯一需要补充的是原版没有的能力：原版 latched 锚点是静止图格，这里在 <see cref="PostAI"/> 中让锚点随盒子位移而平移，
    /// 使"勾住移动物被拽着飞"得以成立。由于全局 <see cref="ProjOverride"/> 是无每弹幕状态的共享单例，跟随采用无状态的
    /// 速度增量方式（每帧把盒子本帧位移叠加到钩头），无需保存勾附偏移
    /// </para>
    /// </summary>
    public sealed class SolidActorGrapple : ProjOverride
    {
        /// <summary>作用于所有钩爪类弹幕，故为全局覆盖</summary>
        public override int TargetID => -1;

        /// <summary>
        /// 治本核心：把被 <see cref="SolidActor"/> 覆盖的图格声明为"可勾"，让原版钩爪 AI 原生完成 latch 全流程。
        /// 飞行时的勾住判定与 latched 后的保持判定都会经过此处。
        /// </summary>
        public override bool? GrappleCanLatchOnTo(Player player, int x, int y) {
            if (SolidActorCollision.ActiveCount == 0) {
                return null;//没有盒子，完全交给原版图格判定
            }

            Rectangle tileRect = new(x * 16, y * 16, 16, 16);
            IReadOnlyList<SolidActor> solids = SolidActorCollision.ActiveSolids;
            for (int i = 0; i < solids.Count; i++) {
                if (solids[i].SolidBox.Intersects(tileRect)) {
                    return true;//该格被盒子覆盖，可勾
                }
            }

            //该格不属于任何盒子，返回 null 交给原版判定真实图格；绝不返回 false 以免否决原版的勾附判定
            return null;
        }

        /// <summary>
        /// 原版唯一缺失的能力：latched 锚点是静止图格，这里让它随盒子平移
        /// 在原版 AI 之后执行，且原版 latched 分支不会改写锚点位置，这里的写入可稳定生效
        /// </summary>
        public override void PostAI() {
            //最廉价的门：无盒子时一次静态读取即返回（全局覆盖会对所有弹幕调用 PostAI，需尽早早退）
            if (SolidActorCollision.ActiveCount == 0) {
                return;
            }

            Projectile proj = projectile;
            //仅处理已勾住（ai[0]==2）的钩爪
            if (proj.aiStyle != ProjAIStyleID.Hook || proj.ai[0] != 2f) {
                return;
            }

            //找到当前钩头所附着的盒子，按其本帧位移平移锚点（无状态：相对位置由"同步平移"自然保持）
            IReadOnlyList<SolidActor> solids = SolidActorCollision.ActiveSolids;
            Rectangle hook = proj.Hitbox;
            for (int i = 0; i < solids.Count; i++) {
                SolidActor box = solids[i];
                if (box.SolidBox.Intersects(hook)) {
                    proj.position += box.FrameVelocity;
                    return;
                }
            }
        }
    }
}
