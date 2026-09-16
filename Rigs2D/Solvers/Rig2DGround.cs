using Microsoft.Xna.Framework;
using System;
using Terraria;

namespace InnoVault.Rigs2D.Solvers
{
    /// <summary>
    /// 地面探测委托：从 <paramref name="from"/> 沿单位向量 <paramref name="dir"/> 扫最多 <paramref name="maxDistance"/> 像素，
    /// 命中返回 <see langword="true"/> 并给出落点
    /// </summary>
    public delegate bool Rig2DGroundProbe(Vector2 from, Vector2 dir, float maxDistance, out Vector2 hit);

    /// <summary>
    /// 步态用的地面探测原件：物块射线、高度函数适配、平地
    /// </summary>
    public static class Rig2DGround
    {
        /// <summary>
        /// 物块射线：按 <paramref name="step"/> 像素步进，遇到实心或斜面物块即命中（落点取进入物块前的最后一个采样点）
        /// </summary>
        public static bool TileProbe(Vector2 from, Vector2 dir, float maxDistance, out Vector2 hit, float step = 4f) {
            step = Math.Max(step, 1f);
            Vector2 p = from;
            Vector2 prev = from;
            float travelled = 0f;
            while (travelled <= maxDistance) {
                int tx = (int)(p.X / 16f);
                int ty = (int)(p.Y / 16f);
                if (WorldGen.InWorld(tx, ty, 1) && WorldGen.SolidOrSlopedTile(tx, ty)) {
                    hit = prev;
                    return true;
                }
                prev = p;
                p += dir * step;
                travelled += step;
            }
            hit = from + dir * maxDistance;
            return false;
        }

        /// <summary>
        /// 默认探测（物块射线，4 像素步进）
        /// </summary>
        public static bool TileProbe(Vector2 from, Vector2 dir, float maxDistance, out Vector2 hit)
            => TileProbe(from, dir, maxDistance, out hit, 4f);

        /// <summary>
        /// 用"(x, 参考 y) → 地面 y"的高度函数适配成探测器（只支持竖直向下的探测方向）
        /// </summary>
        public static Rig2DGroundProbe FromHeight(Func<float, float, float> groundAt) {
            if (groundAt == null) {
                return null;
            }
            return (Vector2 from, Vector2 dir, float maxDistance, out Vector2 hit) => {
                float y = groundAt(from.X, from.Y);
                hit = new Vector2(from.X, y);
                float dist = y - from.Y;
                return dist <= maxDistance;
            };
        }

        /// <summary>
        /// 平地探测（图鉴舞台里的一条虚拟沙线）
        /// </summary>
        public static Rig2DGroundProbe Flat(float groundY) {
            return (Vector2 from, Vector2 dir, float maxDistance, out Vector2 hit) => {
                hit = new Vector2(from.X, groundY);
                return groundY - from.Y <= maxDistance;
            };
        }
    }
}
