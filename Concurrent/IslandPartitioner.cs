using System;
using System.Collections.Generic;
using System.Threading;

namespace InnoVault.Concurrent
{
    /// <summary>
    /// 通用的连通分量划分器：基于并查集把一组元素按"邻接关系"划分为若干互不相连的"岛屿"<br/>
    /// 与具体业务无关——调用方只需提供"如何枚举某元素的邻居"，划分器即可把可并行的独立子图分离出来<br/>
    /// 内部所有缓冲均复用，并支持按拓扑版本号节流重建
    /// </summary>
    /// <typeparam name="T">参与划分的元素类型（引用类型）</typeparam>
    public sealed class IslandPartitioner<T> where T : class
    {
        /// <summary>
        /// 邻接收集委托：对索引为<paramref name="index"/>的元素<paramref name="item"/>，
        /// 通过<paramref name="sink"/>声明它的邻居，划分器据此合并连通分量
        /// </summary>
        public delegate void NeighborCollector(int index, T item, LinkSink sink);

        /// <summary>
        /// 邻接声明入口，传递给<see cref="NeighborCollector"/>，用于把当前元素与其邻居标记为同岛
        /// </summary>
        public readonly struct LinkSink
        {
            private readonly IslandPartitioner<T> owner;
            private readonly int fromIndex;

            internal LinkSink(IslandPartitioner<T> owner, int fromIndex) {
                this.owner = owner;
                this.fromIndex = fromIndex;
            }

            /// <summary>
            /// 按元素实例声明一个邻居（划分器会自动解析其索引；不在参与集合内则忽略）
            /// </summary>
            public void LinkItem(T neighbor) {
                if (neighbor != null && owner.indexOf.TryGetValue(neighbor, out int j)) {
                    owner.Union(fromIndex, j);
                }
            }

            /// <summary>
            /// 按元素索引声明一个邻居
            /// </summary>
            public void LinkIndex(int neighborIndex) {
                if ((uint)neighborIndex < (uint)owner.count) {
                    owner.Union(fromIndex, neighborIndex);
                }
            }
        }

        //并查集复用数组
        private int[] parent = Array.Empty<int>();
        private int[] rootToIsland = Array.Empty<int>();
        //元素 -> 索引 的复用映射（每次重建清空重填）
        private readonly Dictionary<T, int> indexOf = new();
        //划分出的岛屿（复用外层List，内层List走对象池）
        private readonly List<List<T>> islands = new();
        private readonly Stack<List<T>> listPool = new();

        private int count;
        private int version;
        private int cachedVersion = -1;
        private int lastBuildFrame = -100000;
        private bool built;

        /// <summary>
        /// 上一次构建出的岛屿（外层为各岛，内层为岛内元素，保持传入顺序）
        /// </summary>
        public List<List<T>> Islands => islands;
        /// <summary>
        /// 当前拓扑版本号
        /// </summary>
        public int Version => version;

        /// <summary>
        /// 标记拓扑变化（增删元素、邻接关系改变等），下次<see cref="EnsureBuilt"/>会触发重建，线程安全
        /// </summary>
        public void MarkDirty() => Interlocked.Increment(ref version);

        /// <summary>
        /// 按需重建岛屿：拓扑脏或达到强制刷新间隔时调用<paramref name="getItems"/>取得当前元素集合并重建，
        /// 否则直接复用上一次的结果<br/>
        /// 应在无并发的阶段（主线程）调用
        /// </summary>
        /// <param name="currentFrame">当前帧号，用于强制刷新间隔判断</param>
        /// <param name="forceIntervalFrames">即使无脏标记，超过该帧数也强制重建一次以容错遗漏的标脏</param>
        /// <param name="getItems">仅在需要重建时被调用，返回当前参与划分的元素集合</param>
        /// <param name="collect">邻接收集委托</param>
        public List<List<T>> EnsureBuilt(int currentFrame, int forceIntervalFrames, Func<IReadOnlyList<T>> getItems, NeighborCollector collect) {
            bool dirty = version != cachedVersion;
            bool force = currentFrame - lastBuildFrame >= forceIntervalFrames;
            if (built && !dirty && !force) {
                return islands;
            }

            Rebuild(getItems(), collect);
            cachedVersion = version;
            lastBuildFrame = currentFrame;
            built = true;
            return islands;
        }

        private void Rebuild(IReadOnlyList<T> items, NeighborCollector collect) {
            //回收上一次的岛屿内层List
            for (int i = 0; i < islands.Count; i++) {
                islands[i].Clear();
                listPool.Push(islands[i]);
            }
            islands.Clear();
            indexOf.Clear();

            count = items.Count;
            if (count == 0) {
                return;
            }

            //并查集与索引映射初始化
            if (parent.Length < count) {
                parent = new int[count];
            }
            if (rootToIsland.Length < count) {
                rootToIsland = new int[count];
            }
            for (int i = 0; i < count; i++) {
                parent[i] = i;
                indexOf[items[i]] = i;
            }

            //收集邻接并合并
            for (int i = 0; i < count; i++) {
                collect(i, items[i], new LinkSink(this, i));
            }

            //按根分组成岛屿，保持元素的传入顺序
            for (int i = 0; i < count; i++) {
                rootToIsland[i] = -1;
            }
            for (int i = 0; i < count; i++) {
                int r = Find(i);
                int islandIndex = rootToIsland[r];
                if (islandIndex < 0) {
                    islandIndex = islands.Count;
                    rootToIsland[r] = islandIndex;
                    islands.Add(RentList());
                }
                islands[islandIndex].Add(items[i]);
            }
        }

        private int Find(int x) {
            while (parent[x] != x) {
                parent[x] = parent[parent[x]];
                x = parent[x];
            }
            return x;
        }

        private void Union(int a, int b) {
            int ra = Find(a);
            int rb = Find(b);
            if (ra != rb) {
                parent[ra] = rb;
            }
        }

        private List<T> RentList() => listPool.Count > 0 ? listPool.Pop() : new List<T>(16);

        /// <summary>
        /// 清空全部缓存状态（世界卸载/重置时调用）
        /// </summary>
        public void Clear() {
            for (int i = 0; i < islands.Count; i++) {
                islands[i].Clear();
                listPool.Push(islands[i]);
            }
            islands.Clear();
            indexOf.Clear();
            count = 0;
            version = 0;
            cachedVersion = -1;
            lastBuildFrame = -100000;
            built = false;
        }
    }
}
