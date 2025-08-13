using Microsoft.Xna.Framework.Graphics;
using System.Reflection;
using Terraria;
using Terraria.ModLoader;

namespace InnoVault.GameSystem
{
    /// <summary>
    /// 所有关于物块行为覆盖和性质加载的钩子在此处挂载
    /// </summary>
    public class TileRebuildLoader : GlobalTile, IVaultLoader
    {
        private delegate bool OnRightClickDelegate(int i, int j);
        void IVaultLoader.LoadData() {
            MethodInfo method = typeof(TileLoader).GetMethod("RightClick", BindingFlags.Static | BindingFlags.Public);
            if (method != null) {
                VaultHook.Add(method, HookRightClick);
            }
        }

        private static bool HookRightClick(OnRightClickDelegate orig, int i, int j) {
            Tile tile = Framing.GetTileSafely(i, j);
            if (TileOverride.TryFetchByID(tile.TileType, out var tileOverrides)) {
                bool? reset = null;
                foreach (var rTile in tileOverrides.Values) {
                    bool? newReset = rTile.RightClick(i, j, tile);
                    if (newReset.HasValue) {
                        reset = newReset.Value;
                    }
                }

                if (reset.HasValue) {
                    return reset.Value;
                }
            }

            return orig.Invoke(i, j);
        }

        /// <inheritdoc/>
        public override bool CanDrop(int i, int j, int type) {
            if (TileOverride.TryFetchByID(type, out var tileOverrides)) {
                bool? reset = null;
                foreach (var rTile in tileOverrides.Values) {
                    bool? newReset = rTile.CanDrop(i, j, type);
                    if (newReset.HasValue) {
                        reset = newReset.Value;
                    }
                }

                if (reset.HasValue) {
                    return reset.Value;
                }
            }
            return true;
        }

        /// <inheritdoc/>
        public override void MouseOver(int i, int j, int type) {
            if (TileOverride.TryFetchByID(type, out var tileOverrides)) {
                foreach (var rTile in tileOverrides.Values) {
                    rTile.MouseOver(i, j);
                }
            }
        }

        /// <inheritdoc/>
        public override bool PreDraw(int i, int j, int type, SpriteBatch spriteBatch) {
            if (TileOverride.TryFetchByID(type, out var tileOverrides)) {
                bool? reset = null;
                foreach (var rTile in tileOverrides.Values) {
                    bool? newReset = rTile.PreDraw(i, j, type, spriteBatch);
                    if (newReset.HasValue) {
                        reset = newReset.Value;
                    }
                }

                if (reset.HasValue) {
                    return reset.Value;
                }
            }
            return true;
        }
    }
}
