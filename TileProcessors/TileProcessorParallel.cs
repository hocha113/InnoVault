using InnoVault.Concurrent;
using System;
using System.Collections.Generic;
using Terraria;
using Terraria.DataStructures;
using Terraria.Utilities;
using static InnoVault.TileProcessors.TileProcessorLoader;

namespace InnoVault.TileProcessors
{
    /// <summary>
    /// 提供给<see cref="TileProcessor.CollectGroupLinks"/>的邻接收集器<br/>
    /// 在其中push本实体的邻居物块坐标，框架据此把同一连通网络的TP划入同一并行岛屿<br/>
    /// 只需声明位置邻接即可（无需依赖运行期连接状态），框架保证"交互图 ⊆ 邻接图"时的线程安全
    /// </summary>
    public readonly struct TPGroupLinkBuilder
    {
        private readonly List<Point16> _links;

        internal TPGroupLinkBuilder(List<Point16> links) => _links = links;

        /// <summary>
        /// 声明一个邻居物块坐标，框架会解析其左上角并与对应的Grouped型TP建立同岛关系
        /// </summary>
        public void Link(Point16 neighbor) => _links.Add(neighbor);

        /// <summary>
        /// 声明一个邻居物块坐标，框架会解析其左上角并与对应的Grouped型TP建立同岛关系
        /// </summary>
        public void Link(int x, int y) => _links.Add(new Point16(x, y));
    }

    /// <summary>
    /// TP更新并行调度的薄适配层：把TP领域的概念（按<see cref="ParallelExecutionKind"/>分桶、
    /// 物块坐标邻接、按ID计数、死亡/击杀语义）桥接到通用并行引擎<see cref="VaultParallel"/>
    /// 与通用连通分量划分器<see cref="IslandPartitioner{T}"/><br/>
    /// 所有通用的并发机制（命令缓冲、对象池、线程本地随机数、岛屿并查集、负载均衡、开关）都不在此处特化
    /// </summary>
    public static class TileProcessorParallel
    {
        //强制重建岛屿的间隔（即使没有脏标记也定期刷新，容错任何被遗漏的拓扑变化）
        private const int MaxRebuildIntervalFrames = 600;

        //TP领域的连通岛屿划分器实例
        private static readonly IslandPartitioner<TileProcessor> partitioner = new();
        //当前活跃的Grouped型TP扫描结果（复用，仅在重建岛屿时填充）
        private static readonly List<TileProcessor> groupedScan = new(256);
        //CollectGroupLinks 的复用收集缓冲
        private static readonly List<Point16> linkScratch = new(8);

        //因调度级异常而自动禁用的标志，仅作用于TP子系统，与Actor子系统互不影响
        //它独立于用户手动的EnableParallel总开关：换世界(Clear)时复位，使新世界可重新尝试并行
        private static bool autoDisabledByError;

        #region 开关与诊断（转发到通用引擎）
        /// <summary>
        /// 并行更新总开关，置为<see langword="false"/>则完全回退到历史的单线程更新路径，便于排障<br/>
        /// 这是用户可手动控制的全局主开关，与"因异常自动禁用"是两条独立的回退路径
        /// </summary>
        public static bool EnableParallel {
            get => VaultParallel.EnableParallel;
            set => VaultParallel.EnableParallel = value;
        }
        /// <summary>
        /// 最大并行度，<![CDATA[<=0]]>表示交由运行时按可用核心数自动决定
        /// </summary>
        public static int MaxDegreeOfParallelism {
            get => VaultParallel.MaxDegreeOfParallelism;
            set => VaultParallel.MaxDegreeOfParallelism = value;
        }
        /// <summary>
        /// 当世界中TP实体数量低于该阈值时不启用并行（并行调度本身存在开销，少量实体并行反而更慢）
        /// </summary>
        public static int MinCountForParallel {
            get => VaultParallel.MinCountForParallel;
            set => VaultParallel.MinCountForParallel = value;
        }
        /// <summary>
        /// 是否当前正处于并行更新阶段
        /// </summary>
        public static bool InParallelPhase => VaultParallel.InParallelPhase;
        /// <summary>
        /// 诊断：上一帧是否真正走了并行路径
        /// </summary>
        public static bool LastFrameUsedParallel { get; private set; }
        /// <summary>
        /// 诊断：上一次构建出的岛屿数量
        /// </summary>
        public static int LastIslandCount => partitioner.Islands.Count;

        //供 TileProcessor.Rand 使用的线程本地随机数访问
        internal static UnifiedRandom CurrentRand => VaultParallel.CurrentRandom;
        #endregion

        /// <summary>
        /// 标记TP拓扑发生变化（实体增删、连接关系改变等），下一帧会按需重建并行岛屿，线程安全
        /// </summary>
        public static void MarkTopologyDirty() => partitioner.MarkDirty();

        /// <summary>
        /// 给定世界中的TP实体总数，判断本帧是否应当走并行路径：需同时满足"全局主开关开启"与"TP未因异常被自动禁用"
        /// </summary>
        internal static bool ShouldRunParallel(int inWorldCount) => !autoDisabledByError && VaultParallel.ShouldRunParallel(inWorldCount);

        /// <summary>
        /// 因调度级异常自动禁用TP并行并回退串行（仅影响TP子系统，不波及Actor）<br/>
        /// 不写全局<see cref="EnableParallel"/>，避免一次异常跨子系统污染；换世界<see cref="Clear"/>时复位
        /// </summary>
        internal static void AutoDisableParallel() => autoDisabledByError = true;

        /// <summary>
        /// 设置本帧是否走了并行路径（仅诊断用）
        /// </summary>
        internal static void SetUsedParallel(bool value) => LastFrameUsedParallel = value;

        #region 线程安全入口（供TileProcessor的Defer*/Rand/Kill等调用）
        /// <summary>
        /// 延迟一个副作用动作：并行阶段入当前线程缓冲，串行阶段（主线程）立即执行
        /// </summary>
        internal static void Defer(Action action) => VaultParallel.Defer(action);

        /// <summary>
        /// 入队一条报错文本，统一在Phase 2主线程通过聊天框输出（聊天写入非线程安全）
        /// </summary>
        internal static void EnqueueErrorText(string text) => VaultParallel.EnqueueError(text);

        /// <summary>
        /// 入队一个"死亡判定"产生的击杀（会触发服务端死亡广播），统一在Phase 2主线程处理
        /// </summary>
        internal static void EnqueueDeath(TileProcessor tp) => VaultParallel.Defer(() => TileProcessorSystem.KillNow(tp));

        /// <summary>
        /// 入队一个显式击杀（不广播，对应直接调用<see cref="TileProcessor.Kill"/>的语义）
        /// </summary>
        internal static void EnqueueKill(TileProcessor tp) => VaultParallel.Defer(tp.Kill);

        /// <summary>
        /// 累加某个TP类型的世界计数：并行阶段写线程本地累加器，串行阶段直接写全局字典
        /// </summary>
        internal static void AddInWorldCount(int id) {
            if (VaultParallel.InParallelPhase) {
                ParallelCommandBuffer buf = VaultParallel.CurrentBuffer;
                if (buf != null && (uint)id < (uint)buf.Counters.Length) {
                    buf.Counters[id]++;
                    return;
                }
            }
            //仅串行单线程兜底：并行期 ID 恒 ∈ [0, TP_ID_Count)、Counters 大小恒 = TP_ID_Count、buf 恒非空，必然走上面的线程本地累加分支而不可达此处，因此对普通字典的写入不会并发
            if (TP_ID_To_InWorld_Count.ContainsKey(id)) {
                TP_ID_To_InWorld_Count[id]++;
            }
        }
        #endregion

        #region 岛屿构建（TP领域邻接 -> 通用划分器）
        /// <summary>
        /// 按需重建并行岛屿，把TP的物块坐标邻接解析喂给通用划分器
        /// </summary>
        internal static void EnsureIslands()
            => partitioner.EnsureBuilt((int)Main.GameUpdateCount, MaxRebuildIntervalFrames, CollectGroupedItems, CollectNeighbors);

        //仅在需要重建时被调用：扫描世界收集当前活跃的Grouped型TP
        private static IReadOnlyList<TileProcessor> CollectGroupedItems() {
            groupedScan.Clear();
            List<TileProcessor> inWorld = TP_InWorld;
            for (int i = 0; i < inWorld.Count; i++) {
                TileProcessor tp = inWorld[i];
                if (tp != null && tp.Active && tp.ParallelKind == ParallelExecutionKind.Grouped) {
                    groupedScan.Add(tp);
                }
            }
            return groupedScan;
        }

        //TP领域的邻接解析：物块坐标 -> 左上角 -> TP实例 -> 同岛
        private static void CollectNeighbors(int index, TileProcessor tp, IslandPartitioner<TileProcessor>.LinkSink sink) {
            linkScratch.Clear();
            TPGroupLinkBuilder builder = new(linkScratch);
            try {
                tp.CollectGroupLinks(ref builder);
            } catch (Exception ex) {
                VaultMod.LoggerError("@TPParallel.CollectGroupLinks", $"{tp}: {ex.Message}");
                return;
            }

            for (int k = 0; k < linkScratch.Count; k++) {
                Point16 p = linkScratch[k];
                if (!VaultUtils.SafeGetTopLeft(p.X, p.Y, out Point16 topLeft)) {
                    topLeft = p;
                }
                if (TP_Point_To_Instance.TryGetValue(topLeft, out TileProcessor nbr)
                    && nbr != null && nbr.Active && nbr.ParallelKind == ParallelExecutionKind.Grouped) {
                    sink.LinkItem(nbr);
                }
            }
        }
        #endregion

        #region 阶段编排（转发到通用引擎）
        /// <summary>
        /// 进入并行阶段
        /// </summary>
        internal static void BeginParallelPhase() => VaultParallel.BeginPhase(TP_ID_Count);

        /// <summary>
        /// 退出并行阶段
        /// </summary>
        internal static void EndParallelPhase() => VaultParallel.EndPhase();

        /// <summary>
        /// 并行更新独立桶里的全部TP（彼此无依赖，全量并行）
        /// </summary>
        internal static void RunIndependent(IReadOnlyList<TileProcessor> list, Action<TileProcessor> body) => VaultParallel.RunBatch(list, body);

        /// <summary>
        /// 并行更新分组桶：岛间并行、岛内串行
        /// </summary>
        internal static void RunIslands(Action<TileProcessor> body) => VaultParallel.RunGroups(partitioner.Islands, body);

        /// <summary>
        /// 在主线程有序排空并行阶段累积的延迟操作：先合并各线程的世界计数，再执行延迟动作/输出报错文本
        /// </summary>
        internal static void DrainDeferred() {
            //领域特定的合并：把各线程本地累加器汇总到全局计数字典
            IReadOnlyList<ParallelCommandBuffer> buffers = VaultParallel.ActiveBuffers;
            for (int b = 0; b < buffers.Count; b++) {
                int[] counters = buffers[b].Counters;
                int len = Math.Min(counters.Length, TP_ID_Count);
                for (int id = 0; id < len; id++) {
                    int add = counters[id];
                    if (add != 0 && TP_ID_To_InWorld_Count.ContainsKey(id)) {
                        TP_ID_To_InWorld_Count[id] += add;
                    }
                }
            }

            //通用排空：延迟动作（生成物/发包/死亡/击杀）+ 报错文本 + 回收缓冲
            VaultParallel.DrainActionsAndErrors();
        }
        #endregion

        /// <summary>
        /// 世界卸载时清理并行调度的所有缓存状态，防止污染下一个世界
        /// </summary>
        internal static void Clear() {
            VaultParallel.Clear();
            partitioner.Clear();
            groupedScan.Clear();
            linkScratch.Clear();
            LastFrameUsedParallel = false;
            //复位"因异常自动禁用"，使新世界可重新尝试并行（避免一次异常永久降级）
            autoDisabledByError = false;
        }
    }
}
