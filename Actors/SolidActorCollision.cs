using InnoVault.Collisions;
using Microsoft.Xna.Framework;
using System.Collections.Generic;
using Terraria;
using Terraria.ModLoader;

namespace InnoVault.Actors
{
    /// <summary>
    /// <see cref="SolidActor"/> 的碰撞接入层
    /// <para>
    /// 它本身不再挂载任何底层钩子，而是作为通用碰撞钩子 <see cref="VaultCollisionHook"/> 的一个消费者：
    /// 注册 <see cref="TileCollisionHandler"/> / <see cref="SolidCheckHandler"/>，把每个活跃的 <see cref="SolidActor"/>
    /// 当作一块可移动固体叠加进碰撞结果。由于玩家 / NPC / 弹幕 / 钩爪的物块碰撞最终都汇聚到被钩住的咽喉点，
    /// 它们会自动获得对 <see cref="SolidActor"/> 的碰撞响应，无需逐实体改写。
    /// </para>
    /// <para>
    /// 这里只负责"阻挡 / 是否固体"这类无状态语义；"站上去被平台带着走"这类需要实体上下文的承载逻辑由
    /// <see cref="SolidActorPlayer"/> 处理。
    /// </para>
    /// </summary>
    public sealed class SolidActorCollision : ModSystem, IVaultLoader
    {
        //每帧刷新一次的活跃 SolidActor 紧凑列表，热路径(碰撞处理器)直接遍历它，避免 GetActiveActors 反复分配
        private static readonly List<SolidActor> activeSolids = [];
        /// <summary>
        /// 当前世界中参与碰撞的活跃 <see cref="SolidActor"/> 数量，用于热路径快速早退
        /// </summary>
        public static int ActiveCount => activeSolids.Count;
        /// <summary>
        /// 当前世界中参与碰撞的活跃 <see cref="SolidActor"/> 列表（每帧刷新，请勿缓存引用）
        /// </summary>
        public static IReadOnlyList<SolidActor> ActiveSolids => activeSolids;

        void IVaultLoader.LoadData() {
            VaultCollisionHook.AddTileCollisionHandler(OnTileCollision);
            VaultCollisionHook.AddSolidCheckHandler(OnSolidCheck);
        }

        void IVaultLoader.UnLoadData() {
            VaultCollisionHook.RemoveTileCollisionHandler(OnTileCollision);
            VaultCollisionHook.RemoveSolidCheckHandler(OnSolidCheck);
            activeSolids.Clear();
        }

        /// <summary>
        /// 在所有实体更新之前刷新活跃列表，保证本帧玩家 / NPC / 弹幕碰撞时拿到的是最新的盒子
        /// </summary>
        public override void PreUpdateEntities() => RebuildActiveList();

        /// <inheritdoc/>
        public override void OnWorldUnload() => activeSolids.Clear();

        private static void RebuildActiveList() {
            activeSolids.Clear();

            Actor[] actors = ActorLoader.Actors;
            if (actors == null) {
                return;
            }

            for (int i = 0; i < actors.Length; i++) {
                if (actors[i] is SolidActor solid && solid.Active && solid.SolidEnabled) {
                    activeSolids.Add(solid);
                }
            }
        }

        #region 碰撞处理器
        //在原版按物块结算之后，用每个 SolidActor 继续逐轴钳制
        private static void OnTileCollision(in TileCollisionInfo info, ref Vector2 result) {
            int count = activeSolids.Count;
            if (count == 0) {
                return;
            }

            for (int i = 0; i < count; i++) {
                result = ClampAgainstBox(info.Position, result, info.Width, info.Height, activeSolids[i], info.FallThrough, info.Fall2);
            }
        }

        //在原版判定之后补充 SolidActor 的"是否固体"结果
        private static void OnSolidCheck(in SolidCheckInfo info, ref bool result) {
            if (result) {
                return;
            }
            result = OverlapsAnySolid(info.Position, info.Width, info.Height, info.AcceptTopSurfaces);
        }

        //把原版 TileCollision 对单个物块的逐轴钳制逻辑，针对一个 SolidActor 矩形重写(去掉斜坡/半砖分支)
        private static Vector2 ClampAgainstBox(Vector2 pos, Vector2 vel, int w, int h, SolidActor box, bool fallThrough, bool fall2) {
            Rectangle b = box.SolidBox;
            if (b.Width <= 0 || b.Height <= 0) {
                return vel;
            }

            Vector2 result = vel;
            float bx = b.Left;
            float by = b.Top;
            float bw = b.Width;
            float bh = b.Height;
            Vector2 target = pos + result;

            //目标位置不与盒子相交则无需处理
            if (!(target.X + w > bx && target.X < bx + bw && target.Y + h > by && target.Y < by + bh)) {
                return result;
            }

            //用"盒子移动前的边界"判断实体从哪个面接触，再把实体钳制/推挤到"盒子移动后"的当前边界。
            //盒子自身位移发生在实体碰撞结算之后且不会推动实体，若直接用当前边界判定方位，
            //当盒子迎面撞向实体、其近端边界在一帧内越过实体的接触边时，"来自哪个面"的判据会失效，
            //导致实体直接穿过（相反速度互穿）。减去本帧位移即可还原接触发生时的相对方位
            Vector2 fv = box.FrameVelocity;
            float prevTop = by - fv.Y;
            float prevLeft = bx - fv.X;
            float prevRight = bx + bw - fv.X;
            float prevBottom = by + bh - fv.Y;

            //依据"实体相对盒子移动前的方位"决定从哪个面挡住，并钳制到盒子移动后的当前边界
            if (pos.Y + h <= prevTop) {
                //来自上方，落在顶面
                bool passThrough = box.OneWay && box.AllowFallThrough && fallThrough && (vel.Y <= 1f || fall2);
                if (!passThrough) {
                    result.Y = by - (pos.Y + h);
                    Collision.down = true;
                }
            }
            else if (!box.OneWay && pos.X + w <= prevLeft) {
                //从左侧撞入
                result.X = bx - (pos.X + w);
            }
            else if (!box.OneWay && pos.X >= prevRight) {
                //从右侧撞入
                result.X = bx + bw - pos.X;
            }
            else if (!box.OneWay && pos.Y >= prevBottom) {
                //从下方顶头
                result.Y = by + bh - pos.Y;
                Collision.up = true;
            }

            return result;
        }

        //单向平台仅在接受顶面时才算作"固体"，与原版 tileSolidTop 的语义保持一致
        private static bool OverlapsAnySolid(Vector2 pos, int w, int h, bool acceptTopSurfaces) {
            int count = activeSolids.Count;
            if (count == 0) {
                return false;
            }

            Rectangle r = new((int)pos.X, (int)pos.Y, w, h);
            for (int i = 0; i < count; i++) {
                SolidActor box = activeSolids[i];
                if (box.OneWay && !acceptTopSurfaces) {
                    continue;
                }
                if (r.Intersects(box.SolidBox)) {
                    return true;
                }
            }

            return false;
        }
        #endregion
    }
}
