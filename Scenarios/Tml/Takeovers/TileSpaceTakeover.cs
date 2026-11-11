using Microsoft.Xna.Framework.Graphics;
using System;
using System.Reflection;
using Terraria;
using Terraria.ID;
using Terraria.Map;

namespace InnoVault.Scenarios
{
    //物块空间重塑：原版在启动时按 8401×2401 一次分配 Main.tile，宽或高超出它的舞台装不下。
    //所有读档、进服与生成都先定好 Main.maxTilesX/Y 再经 WorldGen.clearWorld 清空世界，所以在它之前按即将载入的尺寸重新分配：
    //单人、服务器、客户端（含中途加入的）走的都是这一个入口。装得进原版分配的世界载入时换回原版的分配
    internal sealed class TileSpaceTakeover : ScenarioTakeover
    {
        internal override string Name => "TileSpace";

        internal override string Source => "Terraria/Tilemap.cs: internal Tilemap(ushort width, ushort height) (reallocates every TileData<T> array through TileData.SetLength); "
            + "Terraria/WorldGen.cs clearWorld (called by WorldFile.LoadHeader, Netplay.InnerClientLoop state 4 and our own generation, after Main.maxTilesX/Y are set); "
            + "Terraria/Main.cs: Map = new WorldMap(maxTilesX, maxTilesY), mapTargetX/Y, mapTarget, initMap, mapWasContentLost, checkMap; Terraria/RemoteClient.cs: TileSections";

        protected override void Install() {
            if (!ScenarioTileSpace.Prepare()) {
                Fail("Tilemap(ushort, ushort) constructor not found; stages are limited to the vanilla tile space");
                return;
            }
            On_WorldGen.clearWorld += Hook;
        }

        protected override void Uninstall() {
            On_WorldGen.clearWorld -= Hook;
            ScenarioTileSpace.Restore();
        }

        private static void Hook(On_WorldGen.orig_clearWorld orig) {
            ScenarioTileSpace.Fit(Main.maxTilesX, Main.maxTilesY);
            orig();
        }
    }

    internal static class ScenarioTileSpace
    {
        private static ConstructorInfo _constructor;
        private static int _nativeWidth;
        private static int _nativeHeight;
        private static int _nativeTargetsX;
        private static int _nativeTargetsY;
        //当前的分配是不是我们换上的非原版尺寸；其他模组换的不归我们换回
        private static bool _tilesResized;
        private static bool _mapResized;
        private static bool _targetsResized;

        //当前端能否重塑物块空间；为假时舞台尺寸限制在原版范围内
        internal static bool Available { get; private set; }

        //当前世界超出原版地图贴图的范围，而地图渲染目标没能重建：这块舞台里地图保持关闭
        internal static bool MapUnavailable { get; private set; }

        internal static bool Prepare() {
            _constructor = typeof(Tilemap).GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null, [typeof(ushort), typeof(ushort)], null);
            _nativeWidth = Main.tile.Width;
            _nativeHeight = Main.tile.Height;
            _nativeTargetsX = Main.mapTargetX;
            _nativeTargetsY = Main.mapTargetY;
            Available = _constructor != null;
            return Available;
        }

        //即将载入一个 maxTilesX × maxTilesY 的世界，世界还没有清空。可能运行在读档线程或客户端网络线程上（那时主线程停在菜单，不碰物块）
        internal static void Fit(int maxTilesX, int maxTilesY) {
            if (!Available) {
                return;
            }
            try {
                FitTiles(maxTilesX, maxTilesY);
                FitServerSections(maxTilesX, maxTilesY);
                if (!Main.dedServ) {
                    FitMap(maxTilesX, maxTilesY);
                }
            } catch (Exception ex) {
                VaultMod.Instance?.Logger.Error($"[Scenarios] Resizing the tile space for a {maxTilesX}x{maxTilesY} stage failed: {ex}");
            }
        }

        //模组卸载：换回原版的分配。此时游戏在菜单里，没有载入的世界
        internal static void Restore() {
            if (!Available) {
                return;
            }
            Available = false;
            try {
                if (_tilesResized) {
                    Allocate(_nativeWidth, _nativeHeight);
                    _tilesResized = false;
                }
                if (!Main.dedServ) {
                    FitMap(_nativeWidth - 1, _nativeHeight - 1);
                }
            } catch (Exception ex) {
                VaultMod.Instance?.Logger.Error($"[Scenarios] Restoring the vanilla tile space failed: {ex}");
            }
        }

        private static bool FitsNative(int maxTilesX, int maxTilesY) => maxTilesX + 1 <= _nativeWidth && maxTilesY + 1 <= _nativeHeight;

        private static void FitTiles(int maxTilesX, int maxTilesY) {
            if (FitsNative(maxTilesX, maxTilesY)) {
                if (_tilesResized) {
                    Allocate(_nativeWidth, _nativeHeight);
                    _tilesResized = false;
                }
                return;
            }
            Tilemap current = Main.tile;
            if (maxTilesX + 1 > current.Width || maxTilesY + 1 > current.Height) {
                Allocate(maxTilesX + 1, maxTilesY + 1);
                _tilesResized = true;
            }
        }

        //新建 Tilemap 会经 TileData.SetLength 重新分配所有格数据数组（含模组注册的），原来的内容全部丢弃：只能在世界即将清空时做
        private static void Allocate(int width, int height) {
            if (width <= 0 || height <= 0 || width > ushort.MaxValue || height > ushort.MaxValue) {
                throw new ArgumentOutOfRangeException(nameof(width), $"tile space {width}x{height} is out of range");
            }
            Main.tile = (Tilemap)_constructor.Invoke([(ushort)width, (ushort)height]);
            VaultMod.Instance?.Logger.Info($"[Scenarios] Tile space reallocated to {width}x{height}");
        }

        //服务器按客户端记录“哪些区块已经发过”，这张表在开服时按当时世界的尺寸分配；换到区块更多的舞台之前必须放大，否则下标越界
        private static void FitServerSections(int maxTilesX, int maxTilesY) {
            if (Main.netMode != NetmodeID.Server) {
                return;
            }
            int needX = (maxTilesX - 1) / Main.sectionWidth + 2;
            int needY = (maxTilesY - 1) / Main.sectionHeight + 2;
            RemoteClient[] clients = Netplay.Clients;
            for (int i = 0; i < clients.Length; i++) {
                RemoteClient client = clients[i];
                if (client == null) {
                    continue;
                }
                bool[,] sections = client.TileSections;
                if (sections == null || sections.GetLength(0) < needX || sections.GetLength(1) < needY) {
                    client.TileSections = new bool[needX, needY];
                }
            }
        }

        //地图数据与地图渲染目标也是按原版最大尺寸分配的：数据当场换，渲染目标涉及显卡资源，交给主线程
        private static void FitMap(int maxTilesX, int maxTilesY) {
            bool native = FitsNative(maxTilesX, maxTilesY);
            WorldMap map = Main.Map;
            if (map != null) {
                if (native) {
                    if (_mapResized) {
                        Main.Map = new WorldMap(_nativeWidth, _nativeHeight);
                        _mapResized = false;
                    }
                }
                else if (map.MaxWidth < maxTilesX + 1 || map.MaxHeight < maxTilesY + 1) {
                    Main.Map = new WorldMap(maxTilesX + 1, maxTilesY + 1);
                    _mapResized = true;
                }
            }
            if (native && !_targetsResized) {
                MapUnavailable = false;
                return;
            }
            //原版 checkMap 把最后一列、最后一行的渲染目标建成 400、600 像素（按 8400×2400 算的余数），其余是整块；
            //非原版尺寸时多留一列一行，让真正用到的都是整块。渲染目标按需创建，多出来的不会被建出来
            int targetsX = native ? _nativeTargetsX : Math.Max(_nativeTargetsX, maxTilesX / Main.textureMaxWidth + 2);
            int targetsY = native ? _nativeTargetsY : Math.Max(_nativeTargetsY, maxTilesY / Main.textureMaxHeight + 2);
            Main.QueueMainThreadAction(() => ResizeMapTargets(targetsX, targetsY, native));
        }

        private static void ResizeMapTargets(int targetsX, int targetsY, bool native) {
            try {
                if (targetsX != Main.mapTargetX || targetsY != Main.mapTargetY) {
                    RenderTarget2D[,] old = Main.instance.mapTarget;
                    if (old != null) {
                        foreach (RenderTarget2D target in old) {
                            target?.Dispose();
                        }
                    }
                    Main.mapTargetX = targetsX;
                    Main.mapTargetY = targetsY;
                    Main.instance.mapTarget = new RenderTarget2D[targetsX, targetsY];
                    Main.initMap = new bool[targetsX, targetsY];
                    Main.mapWasContentLost = new bool[targetsX, targetsY];
                }
                _targetsResized = !native;
                MapUnavailable = false;
            } catch (Exception ex) {
                MapUnavailable = !native;
                VaultMod.Instance?.Logger.Error($"[Scenarios] Resizing the map render targets to {targetsX}x{targetsY} failed; the map stays off in this stage: {ex}");
            }
        }
    }
}
