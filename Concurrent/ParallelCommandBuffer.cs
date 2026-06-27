using System;
using System.Collections.Generic;
using Terraria.Utilities;

namespace InnoVault.Concurrent
{
    /// <summary>
    /// 每个工作线程独占的命令缓冲，记录并行阶段产生的延迟副作用与线程本地累加器<br/>
    /// 由于每个缓冲只被单一线程访问，热路径无需加锁；统一由主线程在并行阶段结束后排空<br/>
    /// 这是一个与具体业务无关的通用并发原语
    /// </summary>
    public sealed class ParallelCommandBuffer
    {
        /// <summary>
        /// 延迟到主线程执行的副作用动作队列
        /// </summary>
        internal readonly List<Action> Actions = new(16);
        /// <summary>
        /// 本线程独占的随机数发生器，避免争用全局<see cref="Terraria.Main.rand"/>
        /// </summary>
        public UnifiedRandom Rand;
        /// <summary>
        /// 本线程独占的整型累加器数组，常用于按某种ID统计计数，最终由调用方合并
        /// </summary>
        public int[] Counters = Array.Empty<int>();

        /// <summary>
        /// 帧开始时重置缓冲：清空动作、按需扩容并清零累加器
        /// </summary>
        internal void Reset(int counterCount) {
            Actions.Clear();
            if (Counters.Length < counterCount) {
                Counters = new int[counterCount];
            }
            else if (Counters.Length > 0) {
                Array.Clear(Counters, 0, Counters.Length);
            }
        }

        /// <summary>
        /// 入队一个延迟副作用动作
        /// </summary>
        public void Defer(Action action) => Actions.Add(action);
    }
}
