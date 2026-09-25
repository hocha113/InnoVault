using Microsoft.Xna.Framework;
using System;
using Terraria;

namespace InnoVault.Rigs2D.Solvers
{
    //游戏宿主：物块射线探测
    public static partial class Rig2DGround
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
    }
}
