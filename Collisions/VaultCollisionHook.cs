using InnoVault.GameSystem;
using Microsoft.Xna.Framework;
using System.Collections.Generic;
using System.Reflection;
using Terraria;

namespace InnoVault.Collisions
{
    /// <summary>
    /// 区分一次"是否固体"查询来自原版的哪个入口
    /// </summary>
    public enum SolidCheckSource
    {
        /// <summary>来自 <see cref="Collision.SolidCollision(Vector2, int, int)"/> 系列</summary>
        SolidCollision,
        /// <summary>来自 <see cref="Collision.SolidTiles(Vector2, int, int)"/> 系列</summary>
        SolidTiles
    }

    /// <summary>
    /// 一次 <see cref="Collision.TileCollision"/> 结算的上下文，字段语义与原版同名参数一致
    /// </summary>
    public readonly struct TileCollisionInfo
    {
        /// <summary>参与结算实体的左上角世界坐标</summary>
        public readonly Vector2 Position;
        /// <summary>结算前实体期望的速度（原版的输入速度）</summary>
        public readonly Vector2 Velocity;
        /// <summary>实体碰撞箱宽度（像素）</summary>
        public readonly int Width;
        /// <summary>实体碰撞箱高度（像素）</summary>
        public readonly int Height;
        /// <summary>是否允许向下穿过平台</summary>
        public readonly bool FallThrough;
        /// <summary>原版的 fall2 参数</summary>
        public readonly bool Fall2;
        /// <summary>重力方向（1 正常，-1 反重力）</summary>
        public readonly int GravDir;

        /// <summary>构造一次结算上下文</summary>
        public TileCollisionInfo(Vector2 position, Vector2 velocity, int width, int height, bool fallThrough, bool fall2, int gravDir) {
            Position = position;
            Velocity = velocity;
            Width = width;
            Height = height;
            FallThrough = fallThrough;
            Fall2 = fall2;
            GravDir = gravDir;
        }
    }

    /// <summary>
    /// 一次"是否压在固体上"查询的上下文，字段语义与原版同名参数一致
    /// </summary>
    public readonly struct SolidCheckInfo
    {
        /// <summary>被查询矩形的左上角世界坐标</summary>
        public readonly Vector2 Position;
        /// <summary>被查询矩形宽度（像素）</summary>
        public readonly int Width;
        /// <summary>被查询矩形高度（像素）</summary>
        public readonly int Height;
        /// <summary>是否把"仅顶面固体"（如平台）也算作固体</summary>
        public readonly bool AcceptTopSurfaces;
        /// <summary>本次查询来自原版的哪个入口</summary>
        public readonly SolidCheckSource Source;

        /// <summary>构造一次查询上下文</summary>
        public SolidCheckInfo(Vector2 position, int width, int height, bool acceptTopSurfaces, SolidCheckSource source) {
            Position = position;
            Width = width;
            Height = height;
            AcceptTopSurfaces = acceptTopSurfaces;
            Source = source;
        }
    }

    /// <summary>
    /// 在原版 <see cref="Collision.TileCollision"/> 结算之后被调用，用于把额外的"虚拟固体"叠加进碰撞结果
    /// </summary>
    /// <param name="info">本次结算的上下文（只读）</param>
    /// <param name="result">当前结算出的速度，初始为原版结果，按引用进一步钳制即可</param>
    public delegate void TileCollisionHandler(in TileCollisionInfo info, ref Vector2 result);

    /// <summary>
    /// 在原版"是否固体"判定之后被调用，用于补充额外的"虚拟固体"判定
    /// </summary>
    /// <param name="info">本次查询的上下文（只读）</param>
    /// <param name="result">当前判定结果，初始为原版结果，按引用修改即可（通常只会把 false 改为 true）</param>
    public delegate void SolidCheckHandler(in SolidCheckInfo info, ref bool result);

    /// <summary>
    /// 通用的"物块碰撞逻辑钩子"层
    /// <para>
    /// 该类独占地钩住 <see cref="Collision"/> 中真正的碰撞咽喉点（<see cref="Collision.TileCollision"/>、
    /// <see cref="Collision.SolidCollision(Vector2, int, int)"/>、<see cref="Collision.SolidTiles(Vector2, int, int)"/> 及其重载），
    /// 并把它们重新发布为可被任意功能订阅的扩展点。任何想让"自由实体 / 虚拟区域"被当作物块参与碰撞的功能
    /// （可移动平台、传送带、力场、自定义单向砖等）都只需注册一个处理器，而无需各自重复挂载底层钩子
    /// </para>
    /// <para>
    /// 处理器在原版结算之后按优先级升序依次执行（数值越小越先、同值按注册先后），后执行者可覆盖先执行者的结果
    /// 没有任何处理器时，所有入口都会以零额外开销直接返回原版结果
    /// </para>
    /// </summary>
    public sealed class VaultCollisionHook : IVaultLoader
    {
        private readonly struct HandlerEntry<T>
        {
            public readonly T Handler;
            public readonly int Priority;
            public readonly long Sequence;
            public HandlerEntry(T handler, int priority, long sequence) {
                Handler = handler;
                Priority = priority;
                Sequence = sequence;
            }
        }

        private static readonly List<HandlerEntry<TileCollisionHandler>> tileHandlers = [];
        private static readonly List<HandlerEntry<SolidCheckHandler>> solidHandlers = [];
        //在热路径(碰撞函数)中遍历的不可变快照，注册/注销时重建，避免遍历时的排序与并发问题
        private static TileCollisionHandler[] tileSnapshot = [];
        private static SolidCheckHandler[] solidSnapshot = [];
        private static long sequenceCounter;

        /// <summary>是否存在任意 <see cref="TileCollisionHandler"/></summary>
        public static bool HasTileCollisionHandlers => tileSnapshot.Length > 0;
        /// <summary>是否存在任意 <see cref="SolidCheckHandler"/></summary>
        public static bool HasSolidCheckHandlers => solidSnapshot.Length > 0;

        #region 注册 / 注销
        /// <summary>
        /// 注册一个 <see cref="TileCollisionHandler"/>，在每次 <see cref="Collision.TileCollision"/> 结算后参与钳制
        /// </summary>
        /// <param name="handler">处理器</param>
        /// <param name="priority">优先级，数值越小越先执行，后执行者可覆盖先执行者</param>
        public static void AddTileCollisionHandler(TileCollisionHandler handler, int priority = 0) {
            if (handler == null) {
                return;
            }
            tileHandlers.Add(new HandlerEntry<TileCollisionHandler>(handler, priority, sequenceCounter++));
            RebuildTileSnapshot();
        }

        /// <summary>
        /// 注销一个先前注册的 <see cref="TileCollisionHandler"/>
        /// </summary>
        /// <returns>成功移除返回 <see langword="true"/></returns>
        public static bool RemoveTileCollisionHandler(TileCollisionHandler handler) {
            int index = tileHandlers.FindIndex(e => e.Handler == handler);
            if (index < 0) {
                return false;
            }
            tileHandlers.RemoveAt(index);
            RebuildTileSnapshot();
            return true;
        }

        /// <summary>
        /// 注册一个 <see cref="SolidCheckHandler"/>，在每次"是否固体"查询后参与补充判定
        /// </summary>
        /// <param name="handler">处理器</param>
        /// <param name="priority">优先级，数值越小越先执行，后执行者可覆盖先执行者</param>
        public static void AddSolidCheckHandler(SolidCheckHandler handler, int priority = 0) {
            if (handler == null) {
                return;
            }
            solidHandlers.Add(new HandlerEntry<SolidCheckHandler>(handler, priority, sequenceCounter++));
            RebuildSolidSnapshot();
        }

        /// <summary>
        /// 注销一个先前注册的 <see cref="SolidCheckHandler"/>
        /// </summary>
        /// <returns>成功移除返回 <see langword="true"/></returns>
        public static bool RemoveSolidCheckHandler(SolidCheckHandler handler) {
            int index = solidHandlers.FindIndex(e => e.Handler == handler);
            if (index < 0) {
                return false;
            }
            solidHandlers.RemoveAt(index);
            RebuildSolidSnapshot();
            return true;
        }

        private static void RebuildTileSnapshot() {
            tileHandlers.Sort(CompareTile);
            TileCollisionHandler[] array = new TileCollisionHandler[tileHandlers.Count];
            for (int i = 0; i < tileHandlers.Count; i++) {
                array[i] = tileHandlers[i].Handler;
            }
            tileSnapshot = array;
        }

        private static void RebuildSolidSnapshot() {
            solidHandlers.Sort(CompareSolid);
            SolidCheckHandler[] array = new SolidCheckHandler[solidHandlers.Count];
            for (int i = 0; i < solidHandlers.Count; i++) {
                array[i] = solidHandlers[i].Handler;
            }
            solidSnapshot = array;
        }

        private static int CompareTile(HandlerEntry<TileCollisionHandler> a, HandlerEntry<TileCollisionHandler> b) {
            int c = a.Priority.CompareTo(b.Priority);
            return c != 0 ? c : a.Sequence.CompareTo(b.Sequence);
        }

        private static int CompareSolid(HandlerEntry<SolidCheckHandler> a, HandlerEntry<SolidCheckHandler> b) {
            int c = a.Priority.CompareTo(b.Priority);
            return c != 0 ? c : a.Sequence.CompareTo(b.Sequence);
        }
        #endregion

        #region 底层钩子挂载
        private delegate Vector2 Orig_TileCollision(Vector2 pos, Vector2 vel, int w, int h, bool fallThrough, bool fall2, int gravDir);
        private delegate bool Orig_SolidCollision3(Vector2 pos, int w, int h);
        private delegate bool Orig_SolidCollision4(Vector2 pos, int w, int h, bool acceptTopSurfaces);
        private delegate bool Orig_SolidTiles3(Vector2 pos, int w, int h);
        private delegate bool Orig_SolidTiles4(Vector2 pos, int w, int h, bool allowTopSurfaces);

        void IVaultLoader.LoadData() {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.Static;

            MethodInfo tileCollision = typeof(Collision).GetMethod(nameof(Collision.TileCollision), flags, null,
                [typeof(Vector2), typeof(Vector2), typeof(int), typeof(int), typeof(bool), typeof(bool), typeof(int)], null);
            MethodInfo solid3 = typeof(Collision).GetMethod(nameof(Collision.SolidCollision), flags, null,
                [typeof(Vector2), typeof(int), typeof(int)], null);
            MethodInfo solid4 = typeof(Collision).GetMethod(nameof(Collision.SolidCollision), flags, null,
                [typeof(Vector2), typeof(int), typeof(int), typeof(bool)], null);
            MethodInfo tiles3 = typeof(Collision).GetMethod(nameof(Collision.SolidTiles), flags, null,
                [typeof(Vector2), typeof(int), typeof(int)], null);
            MethodInfo tiles4 = typeof(Collision).GetMethod(nameof(Collision.SolidTiles), flags, null,
                [typeof(Vector2), typeof(int), typeof(int), typeof(bool)], null);

            if (tileCollision != null) {
                VaultHook.Add(tileCollision, On_TileCollision);
            }
            if (solid3 != null) {
                VaultHook.Add(solid3, On_SolidCollision3);
            }
            if (solid4 != null) {
                VaultHook.Add(solid4, On_SolidCollision4);
            }
            if (tiles3 != null) {
                VaultHook.Add(tiles3, On_SolidTiles3);
            }
            if (tiles4 != null) {
                VaultHook.Add(tiles4, On_SolidTiles4);
            }
        }

        void IVaultLoader.UnLoadData() {
            tileHandlers.Clear();
            solidHandlers.Clear();
            tileSnapshot = [];
            solidSnapshot = [];
            sequenceCounter = 0;
        }
        #endregion

        #region 钩子分发
        private static Vector2 On_TileCollision(Orig_TileCollision orig, Vector2 pos, Vector2 vel, int w, int h, bool fallThrough, bool fall2, int gravDir) {
            Vector2 result = orig(pos, vel, w, h, fallThrough, fall2, gravDir);

            TileCollisionHandler[] handlers = tileSnapshot;
            if (handlers.Length == 0) {
                return result;
            }

            TileCollisionInfo info = new(pos, vel, w, h, fallThrough, fall2, gravDir);
            for (int i = 0; i < handlers.Length; i++) {
                handlers[i](in info, ref result);
            }

            return result;
        }

        private static bool On_SolidCollision3(Orig_SolidCollision3 orig, Vector2 pos, int w, int h)
            => DispatchSolidCheck(orig(pos, w, h), pos, w, h, acceptTopSurfaces: false, SolidCheckSource.SolidCollision);

        private static bool On_SolidCollision4(Orig_SolidCollision4 orig, Vector2 pos, int w, int h, bool acceptTopSurfaces)
            => DispatchSolidCheck(orig(pos, w, h, acceptTopSurfaces), pos, w, h, acceptTopSurfaces, SolidCheckSource.SolidCollision);

        private static bool On_SolidTiles3(Orig_SolidTiles3 orig, Vector2 pos, int w, int h)
            => DispatchSolidCheck(orig(pos, w, h), pos, w, h, acceptTopSurfaces: false, SolidCheckSource.SolidTiles);

        private static bool On_SolidTiles4(Orig_SolidTiles4 orig, Vector2 pos, int w, int h, bool allowTopSurfaces)
            => DispatchSolidCheck(orig(pos, w, h, allowTopSurfaces), pos, w, h, allowTopSurfaces, SolidCheckSource.SolidTiles);

        private static bool DispatchSolidCheck(bool origResult, Vector2 pos, int w, int h, bool acceptTopSurfaces, SolidCheckSource source) {
            SolidCheckHandler[] handlers = solidSnapshot;
            if (handlers.Length == 0) {
                return origResult;
            }

            bool result = origResult;
            SolidCheckInfo info = new(pos, w, h, acceptTopSurfaces, source);
            for (int i = 0; i < handlers.Length; i++) {
                handlers[i](in info, ref result);
            }

            return result;
        }
        #endregion
    }
}
