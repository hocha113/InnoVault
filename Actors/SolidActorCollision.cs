using InnoVault.Collisions;
using Microsoft.Xna.Framework;
using System;
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
        /// 在所有实体更新之前提前推进 SolidActor 并刷新活跃列表，保证本帧玩家 / NPC / 弹幕碰撞时
        /// 拿到的是本帧最新位置，而不是上一帧末尾的位置
        /// </summary>
        public override void PreUpdateEntities() {
            RebuildActiveList();
            EarlyUpdateSolids();
        }

        /// <inheritdoc/>
        public override void OnWorldUnload() => activeSolids.Clear();

        private static void EarlyUpdateSolids() {
            bool client = VaultUtils.isClient;

            //遍历本帧快照(activeSolids)，即使 SolidAI 中途生成 / 销毁实体也不会破坏遍历
            for (int i = 0; i < activeSolids.Count; i++) {
                SolidActor solid = activeSolids[i];
                if (!solid.Active || !solid.SolidEnabled) {
                    continue;
                }

                solid.LastPosition = solid.Position;
                solid.PreUpdatedThisFrame = true;
                solid.SolidAI();
                solid.Position += solid.Velocity;

                //客户端在积分后立即重对齐，保证本帧碰撞 / 承载使用已向权威收敛的位置
                if (client) {
                    solid.ApplyClientReconciliation();
                }
            }
        }

        private static void RebuildActiveList() {
            activeSolids.Clear();

            IReadOnlyList<Actor> actors = ActorLoader.ActiveActors;
            for (int i = 0; i < actors.Count; i++) {
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

            float bx = b.Left;
            float by = b.Top;
            float bw = b.Width;
            float bh = b.Height;
            Vector2 workPos = pos;
            Vector2 totalResult = Vector2.Zero;

            //相对运动下实体可能已嵌入盒子（尤其平台在 PreUpdate 中先位移）；先沿最小穿透轴推出
            if (TryGetMinimumSeparation(workPos, w, h, bx, by, bw, bh, box.OneWay, out Vector2 separation)) {
                totalResult += separation;
                workPos += separation;
            }

            Vector2 moveResult = vel;
            Vector2 target = workPos + vel;

            if (target.X + w > bx && target.X < bx + bw && target.Y + h > by && target.Y < by + bh) {
                //依据"当前工作位置"相对盒子的方位决定从哪个面挡住
                if (workPos.Y + h <= by) {
                    //来自上方，落在顶面
                    bool passThrough = box.OneWay && box.AllowFallThrough && fallThrough && (vel.Y <= 1f || fall2);
                    if (!passThrough) {
                        moveResult.Y = by - (workPos.Y + h);
                        Collision.down = true;
                    }
                }
                else if (!box.OneWay && workPos.X + w <= bx) {
                    moveResult.X = bx - (workPos.X + w);
                }
                else if (!box.OneWay && workPos.X >= bx + bw) {
                    moveResult.X = bx + bw - workPos.X;
                }
                else if (!box.OneWay && workPos.Y >= by + bh) {
                    moveResult.Y = by + bh - workPos.Y;
                    Collision.up = true;
                }
                else if (TryGetMinimumSeparation(workPos, w, h, bx, by, bw, bh, box.OneWay, out Vector2 residualSep)) {
                    //仍嵌在内部且 if-else 未命中任何面时，再次最小穿透推出
                    moveResult = residualSep;
                }
            }

            totalResult += moveResult;
            return totalResult;
        }

        private static bool TryGetMinimumSeparation(Vector2 pos, int w, int h, float bx, float by, float bw, float bh, bool oneWay, out Vector2 separation) {
            separation = Vector2.Zero;

            float overlapX = Math.Min(pos.X + w, bx + bw) - Math.Max(pos.X, bx);
            float overlapY = Math.Min(pos.Y + h, by + bh) - Math.Max(pos.Y, by);
            if (overlapX <= 0f || overlapY <= 0f) {
                return false;
            }

            if (oneWay) {
                //单向平台：仅当玩家从上方嵌入时推到顶面，下方仍允许穿过
                if (pos.Y + h <= by + bh * 0.5f) {
                    separation.Y = by - (pos.Y + h);
                    return Math.Abs(separation.Y) > 0f;
                }
                return false;
            }

            if (overlapX < overlapY) {
                float centerX = pos.X + w * 0.5f;
                float boxCenterX = bx + bw * 0.5f;
                separation.X = centerX < boxCenterX ? -overlapX : overlapX;
            }
            else {
                float centerY = pos.Y + h * 0.5f;
                float boxCenterY = by + bh * 0.5f;
                separation.Y = centerY < boxCenterY ? -overlapY : overlapY;
            }

            return separation != Vector2.Zero;
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
