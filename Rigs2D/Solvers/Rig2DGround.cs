using Microsoft.Xna.Framework;
using System;

namespace InnoVault.Rigs2D.Solvers
{
    /// <summary>
    /// 地面探测委托：从 <paramref name="from"/> 沿单位向量 <paramref name="dir"/> 扫最多 <paramref name="maxDistance"/> 像素，
    /// 命中返回 <see langword="true"/> 并给出落点
    /// </summary>
    public delegate bool Rig2DGroundProbe(Vector2 from, Vector2 dir, float maxDistance, out Vector2 hit);

    /// <summary>
    /// 步态用的地面探测原件：物块射线（游戏宿主分部 <c>TileProbe</c>）、高度函数适配、平地
    /// </summary>
    public static partial class Rig2DGround
    {
        /// <summary>
        /// 宿主默认探测：游戏里是物块射线（4 像素步进），离线宿主由 <see cref="Rig2DPlatform.GroundProbe"/> 给；都没有时探不到地
        /// </summary>
        public static bool DefaultProbe(Vector2 from, Vector2 dir, float maxDistance, out Vector2 hit) {
            Rig2DGroundProbe probe = Rig2DPlatform.GroundProbe;
            if (probe != null) {
                return probe(from, dir, maxDistance, out hit);
            }
            hit = from + dir * maxDistance;
            return false;
        }

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
