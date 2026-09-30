using InnoVault.Rigs2D.Runtime;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using Terraria;
using Terraria.ID;

namespace InnoVault.Rigs2D.Physics
{
    //游戏宿主：物块版接触场
    public static partial class Rig2DContactFields
    {
        /// <summary>
        /// 完整物块格（小体型、会被头顶岩壁挡住的刚体）：实心物块全挡，平台只挡从上面来的（<paramref name="platforms"/>）。
        /// 斜面与半砖按整格算
        /// </summary>
        public static Rig2DContactField Tiles(bool platforms = true)
            => TileGrid(SolidTile, 16f, platforms ? PlatformTile : null);

        /// <summary>
        /// 只认顶面的地表（巨物、虚体）：每列只取「上方一格不是实心」的那层地面，参考高度在圆顶之上，
        /// 所以身体穿过头顶的岩层不会被卡住，只会落在脚下真正露天（或洞穴地面）的那层上。斜面与半砖取精确顶面高度
        /// </summary>
        /// <param name="lookUp">参考高度在圆顶之上的余量（像素）</param>
        /// <param name="maxScan">每列向下找地面的最大距离（像素）</param>
        /// <param name="platforms">平台是否算地面</param>
        /// <param name="sampleStep">高度场采样间距（像素）</param>
        public static Rig2DContactField TileSurface(float lookUp = 24f, float maxScan = 640f, bool platforms = true, float sampleStep = 8f)
            => HeightField((x, refY) => TileSurfaceY(x, refY, maxScan, platforms), sampleStep, lookUp);

        /// <summary>
        /// x 处从 <paramref name="fromY"/> 往下 <paramref name="maxScan"/> 像素内第一层顶面的精确 y（上方一格不是实心的实心格 / 平台）；没有返回 <see cref="float.NaN"/>
        /// </summary>
        public static float TileSurfaceY(float x, float fromY, float maxScan = 640f, bool platforms = true) {
            int tx = (int)MathF.Floor(x / 16f);
            int ty0 = (int)MathF.Floor(fromY / 16f);
            int row = SurfaceRow(tx, ty0, Math.Max(1, (int)MathF.Ceiling(maxScan / 16f)), platforms);
            if (row == int.MinValue) {
                return float.NaN;
            }
            return row * 16f + LocalTop(Main.tile[tx, row], x - tx * 16f);
        }

        //每帧一张列缓存：同一帧里大量圆会问同一列同一起点
        private static readonly Dictionary<long, int> surfaceCache = [];
        private static uint surfaceCacheTick = uint.MaxValue;

        private static int SurfaceRow(int tx, int ty0, int rows, bool platforms) {
            uint tick = Main.GameUpdateCount;
            if (tick != surfaceCacheTick) {
                surfaceCache.Clear();
                surfaceCacheTick = tick;
            }
            long key = ((long)tx << 34) ^ ((long)(ty0 & 0x1FFFF) << 16) ^ ((long)Math.Min(rows, 0x7FFF) << 1) ^ (platforms ? 1L : 0L);
            if (surfaceCache.TryGetValue(key, out int cached)) {
                return cached;
            }
            int found = int.MinValue;
            if (tx >= 0 && tx < Main.maxTilesX) {
                int start = Math.Max(ty0, 1);
                int end = Math.Min(ty0 + rows, Main.maxTilesY - 1);
                for (int ty = start; ty <= end; ty++) {
                    bool ground = SolidTile(tx, ty) || platforms && PlatformTile(tx, ty);
                    if (ground && !SolidFull(tx, ty - 1)) {
                        found = ty;
                        break;
                    }
                }
            }
            surfaceCache[key] = found;
            return found;
        }

        //格内顶面高度：半砖 8，底斜（1 向左下、2 向右下）按 x 线性，顶斜（3 / 4）的顶面是平的
        private static float LocalTop(Tile tile, float lx) {
            if (tile.IsHalfBlock) {
                return 8f;
            }
            lx = MathHelper.Clamp(lx, 0f, 16f);
            return tile.Slope switch {
                SlopeType.SlopeDownLeft => 16f - lx,
                SlopeType.SlopeDownRight => lx,
                _ => 0f,
            };
        }

        private static bool SolidTile(int tx, int ty) {
            if (!WorldGen.InWorld(tx, ty)) {
                return false;
            }
            Tile t = Main.tile[tx, ty];
            return t.HasTile && !t.IsActuated && Main.tileSolid[t.TileType] && !Main.tileSolidTop[t.TileType];
        }

        //上方这一格会不会把"顶面"盖住：完整实心格（非半砖、非底斜）才算盖住
        private static bool SolidFull(int tx, int ty) {
            if (!SolidTile(tx, ty)) {
                return false;
            }
            Tile t = Main.tile[tx, ty];
            return !t.IsHalfBlock && t.Slope != SlopeType.SlopeDownLeft && t.Slope != SlopeType.SlopeDownRight;
        }

        private static bool PlatformTile(int tx, int ty) {
            if (!WorldGen.InWorld(tx, ty)) {
                return false;
            }
            Tile t = Main.tile[tx, ty];
            return t.HasTile && !t.IsActuated && Main.tileSolidTop[t.TileType];
        }
    }
}
