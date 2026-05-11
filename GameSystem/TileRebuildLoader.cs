using InnoVault.TileProcessors;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq.Expressions;
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
        private static readonly List<VaultHookMethodCache<TileOverride>> hooks = [];
        internal static VaultHookMethodCache<TileOverride> HookPreTileDraw;
        void IVaultLoader.LoadData() {
            MethodInfo method = typeof(TileLoader).GetMethod("RightClick", BindingFlags.Static | BindingFlags.Public);
            if (method != null) {
                VaultHook.Add(method, OnRightClickHook);
            }
            On_Main.DoDraw_WallsAndBlacks += On_TileDrawing_DrawHook;
        }
        void IVaultLoader.SetupData() {
            HookPreTileDraw = AddHook<Action<SpriteBatch>>(t => t.PreTileDraw);
        }
        void IVaultLoader.UnLoadData() {
            On_Main.DoDraw_WallsAndBlacks -= On_TileDrawing_DrawHook;
            hooks.Clear();
            HookPreTileDraw = null;
            VaultTypeRegistry<TileOverride>.ClearRegisteredVaults();
        }

        private static VaultHookMethodCache<TileOverride> AddHook<F>(Expression<Func<TileOverride, F>> func) where F : Delegate {
            VaultHookMethodCache<TileOverride> hook = VaultHookMethodCache<TileOverride>.Create(func);
            hooks.Add(hook);
            return hook;
        }

        private static bool OnRightClickHook(OnRightClickDelegate orig, int i, int j) {
            Tile tile = Framing.GetTileSafely(i, j);
            bool? result = null;
            if (TileOverride.TryFetchByID(tile.TileType, out var tileOverrides)) {
                foreach (var rTile in tileOverrides.Values) {
                    bool? newResult = rTile.RightClick(i, j, tile);
                    if (newResult.HasValue) {
                        result = newResult.Value;
                    }
                }

                if (result.HasValue) {
                    return result.Value;
                }
            }
            //智能光标(Smart Cursor)对一些可交互物块走的内部路径不会消费 Main.mouseRightRelease，
            //这会导致按住右键时每一帧都进到本 hook 里，进而把 TP.RightClick 与广播包反复触发。
            //通过 mouseRightRelease 做一层按下沿(press edge)守门：仅在按下当帧才向 TP 派发右键事件
            if (Main.mouseRightRelease
                && TileProcessorLoader.TargetTileTypes.Contains(tile.TileType)
                && VaultUtils.SafeGetTopLeft(i, j, out var point)
                && TPUtils.TryGetTP(point, out var tp)) {
                result = tp.RightClick(i, j, tile, Main.LocalPlayer);
                TileProcessorNetWork.SendTPRightClick(i, j, Main.myPlayer);
                if (result.HasValue) {
                    return result.Value;
                }
            }

            return orig.Invoke(i, j);
        }

        //集中管理所有TileDrawing_Draw钩子
        private static void On_TileDrawing_DrawHook(On_Main.orig_DoDraw_WallsAndBlacks orig, Main self) {
            orig.Invoke(self);
            //不要在主页面进行绘制，也不要在全屏地图界面进行绘制
            if (Main.gameMenu) {
                return;
            }
            Main.spriteBatch.End();
            Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, Main.DefaultSamplerState, DepthStencilState.None, Main.Rasterizer, null, Main.Transform);
            TileProcessorSystem.PreDrawTiles();
            foreach (var tileOverride in HookPreTileDraw.Enumerate()) {
                tileOverride.PreTileDraw(Main.spriteBatch);
            }
            Main.spriteBatch.End();
            Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, Main.DefaultSamplerState, DepthStencilState.None, Main.Rasterizer, null, Main.Transform);
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

            if (VaultUtils.SafeGetTopLeft(i, j, out var point) && TPUtils.TryGetTP(point, out var tp)) {
                tp.HoverTile();
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
