using Microsoft.Xna.Framework;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Terraria.Utilities;

namespace InnoVault.Concurrent
{
    /// <summary>
    /// 通用的并行执行引擎，与具体业务无关，提供：分批/分组并行执行、每线程命令缓冲与对象池、
    /// 线程本地随机数、延迟副作用与报错文本的有序排空、以及总开关与并行度控制<br/>
    /// 任何需要"主线程编排 + 工作线程纯计算 + 主线程提交副作用"模式的子系统都可复用它<br/>
    /// 典型用法：<see cref="BeginPhase"/> → <see cref="RunBatch"/> / <see cref="RunGroups"/> →
    /// <see cref="EndPhase"/> → 读取<see cref="ActiveBuffers"/>做领域合并 → <see cref="DrainActionsAndErrors"/>
    /// </summary>
    public static class VaultParallel
    {
        #region 开关与诊断
        /// <summary>
        /// 并行总开关，置为<see langword="false"/>可强制相关子系统回退到单线程路径，便于排障
        /// </summary>
        public static bool EnableParallel = true;
        /// <summary>
        /// 最大并行度，<![CDATA[<=0]]>表示交由运行时按可用核心数自动决定
        /// </summary>
        public static int MaxDegreeOfParallelism = -1;
        /// <summary>
        /// 元素数量低于该阈值时不建议并行（调度开销大于收益），由<see cref="ShouldRunParallel"/>判定
        /// </summary>
        public static int MinCountForParallel = 256;

        /// <summary>
        /// 是否当前正处于并行阶段。所有线程安全入口据此决定"立即执行"还是"延迟入队"<br/>
        /// 仅由主线程在进入/退出并行阶段时翻转
        /// </summary>
        public static volatile bool InParallelPhase;
        #endregion

        #region 线程本地状态
        /// <summary>
        /// 当前线程的命令缓冲，仅在并行阶段内非空
        /// </summary>
        [ThreadStatic]
        public static ParallelCommandBuffer CurrentBuffer;
        /// <summary>
        /// 当前线程的随机数发生器，仅在并行阶段内非空
        /// </summary>
        [ThreadStatic]
        public static UnifiedRandom CurrentRandom;
        #endregion

        #region 缓冲池与队列
        private static readonly ConcurrentStack<ParallelCommandBuffer> bufferPool = new();
        private static readonly List<ParallelCommandBuffer> activeBuffers = new(32);
        private static readonly object activeBuffersLock = new();
        private static readonly ConcurrentQueue<string> errorTextQueue = new();
        private static int counterCountSnapshot;
        private static int randSeedCounter;

        /// <summary>
        /// 本次并行阶段中被实际使用的命令缓冲列表（在<see cref="EndPhase"/>之后、
        /// <see cref="DrainActionsAndErrors"/>之前由主线程读取，用于领域特定的合并，如计数汇总）
        /// </summary>
        public static IReadOnlyList<ParallelCommandBuffer> ActiveBuffers => activeBuffers;
        #endregion

        /// <summary>
        /// 给定元素数量，判断是否应当走并行路径
        /// </summary>
        public static bool ShouldRunParallel(int count) => EnableParallel && count >= MinCountForParallel;

        /// <summary>
        /// 延迟一个副作用动作：并行阶段入当前线程缓冲，否则立即执行
        /// </summary>
        public static void Defer(Action action) {
            if (action == null) {
                return;
            }
            if (InParallelPhase && CurrentBuffer != null) {
                CurrentBuffer.Actions.Add(action);
            }
            else {
                action();
            }
        }

        /// <summary>
        /// 入队一条报错文本，统一在<see cref="DrainActionsAndErrors"/>中由主线程通过聊天框输出
        /// </summary>
        public static void EnqueueError(string text) => errorTextQueue.Enqueue(text);

        /// <summary>
        /// 进入并行阶段。<paramref name="counterCount"/>为每线程累加器数组的大小（无需累加器可传0）
        /// </summary>
        public static void BeginPhase(int counterCount) {
            counterCountSnapshot = counterCount;
            lock (activeBuffersLock) {
                activeBuffers.Clear();
            }
            InParallelPhase = true;
        }

        /// <summary>
        /// 退出并行阶段
        /// </summary>
        public static void EndPhase() {
            InParallelPhase = false;
            CurrentBuffer = null;
            CurrentRandom = null;
        }

        /// <summary>
        /// 全量并行执行：把<paramref name="items"/>分摊到多个工作线程，对每个元素调用<paramref name="body"/><br/>
        /// 适用于彼此无依赖的独立元素
        /// </summary>
        public static void RunBatch<T>(IReadOnlyList<T> items, Action<T> body) {
            int n = items.Count;
            if (n == 0) {
                return;
            }
            Parallel.For(0, n, BuildOptions(), LocalInit,
                (i, _, buf) => {
                    CurrentBuffer = buf;
                    CurrentRandom = buf.Rand;
                    body(items[i]);
                    return buf;
                }, LocalFinally);
        }

        /// <summary>
        /// 分组并行执行：组与组之间并行、组内串行，对每个元素调用<paramref name="body"/><br/>
        /// 适用于"同组元素相互作用、不同组互不影响"的场景（如连通网络/岛屿）<br/>
        /// 会按组大小降序调度以平衡负载，避免大组拖尾
        /// </summary>
        public static void RunGroups<T>(List<List<T>> groups, Action<T> body) {
            if (groups.Count == 0) {
                return;
            }
            groups.Sort(static (a, b) => b.Count - a.Count);
            OrderablePartitioner<List<T>> partitioner = Partitioner.Create(groups, EnumerablePartitionerOptions.NoBuffering);
            Parallel.ForEach(partitioner, BuildOptions(), LocalInit,
                (group, _, buf) => {
                    CurrentBuffer = buf;
                    CurrentRandom = buf.Rand;
                    for (int k = 0; k < group.Count; k++) {
                        body(group[k]);
                    }
                    return buf;
                }, LocalFinally);
        }

        /// <summary>
        /// 在主线程有序排空：执行所有延迟动作、输出报错文本，并回收命令缓冲到对象池<br/>
        /// 若需要做领域特定的合并（如计数），应在调用本方法之前先遍历<see cref="ActiveBuffers"/>
        /// </summary>
        public static void DrainActionsAndErrors() {
            for (int b = 0; b < activeBuffers.Count; b++) {
                List<Action> actions = activeBuffers[b].Actions;
                for (int a = 0; a < actions.Count; a++) {
                    try {
                        actions[a]();
                    } catch (Exception ex) {
                        VaultMod.LoggerError("@VaultParallel.DrainAction", ex.Message);
                    }
                }
            }

            while (errorTextQueue.TryDequeue(out string text)) {
                VaultUtils.Text(text, Color.Red);
            }

            for (int b = 0; b < activeBuffers.Count; b++) {
                bufferPool.Push(activeBuffers[b]);
            }
            lock (activeBuffersLock) {
                activeBuffers.Clear();
            }
        }

        private static ParallelOptions BuildOptions() {
            ParallelOptions options = new();
            if (MaxDegreeOfParallelism > 0) {
                options.MaxDegreeOfParallelism = MaxDegreeOfParallelism;
            }
            return options;
        }

        private static ParallelCommandBuffer LocalInit() {
            if (!bufferPool.TryPop(out ParallelCommandBuffer buf)) {
                buf = new ParallelCommandBuffer();
            }
            buf.Reset(counterCountSnapshot);
            buf.Rand ??= new UnifiedRandom(unchecked(Environment.TickCount * 397 + Interlocked.Increment(ref randSeedCounter)));
            lock (activeBuffersLock) {
                activeBuffers.Add(buf);
            }
            CurrentBuffer = buf;
            CurrentRandom = buf.Rand;
            return buf;
        }

        private static void LocalFinally(ParallelCommandBuffer buf) {
            CurrentBuffer = null;
            CurrentRandom = null;
        }

        /// <summary>
        /// 清空全部缓存状态（世界卸载/重置时调用）
        /// </summary>
        public static void Clear() {
            InParallelPhase = false;
            CurrentBuffer = null;
            CurrentRandom = null;
            lock (activeBuffersLock) {
                activeBuffers.Clear();
            }
            while (errorTextQueue.TryDequeue(out _)) { }
        }
    }
}
