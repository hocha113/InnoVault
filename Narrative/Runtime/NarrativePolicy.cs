using InnoVault.Narrative.Progress;
using System;
using Terraria;

namespace InnoVault.Narrative.Runtime
{
    /// <summary>
    /// 声明式触发策略，由 <see cref="NarrativeScheduler"/> 评估。策略实例通常只创建一次，<br/>
    /// 因此不要在创建策略时缓存世界 / 玩家 / NPC 的瞬时结果；应在各个委托被调度器调用时实时读取<br/>
    /// 关键：完成标记走两阶段——触发时只写 <see cref="ScenarioProgress.Triggered"/>，<br/>
    /// 真正播放完毕后才由框架写 <see cref="ScenarioProgress.Completed"/> 并回调 <see cref="OnCompleted"/>
    /// </summary>
    public sealed class NarrativePolicy
    {
        /// <summary>是否已完成；<see langword="null"/> 时默认读取进度存储中的 <see cref="ScenarioProgress.Completed"/></summary>
        public Func<INarrativeProgressStore, bool> IsCompleted { get; set; }
        /// <summary>触发条件判定</summary>
        public Func<INarrativeProgressStore, Player, bool> CanTrigger { get; set; }
        /// <summary>优先级，越大越优先</summary>
        public int Priority { get; set; }
        /// <summary>是否可重复触发（忽略已完成判定）</summary>
        public bool Repeatable { get; set; }
        /// <summary>额外的阻塞判定，返回 <see langword="true"/> 时本策略本帧不触发</summary>
        public Func<bool> Blocked { get; set; }
        /// <summary>触发（开始播放）时回调</summary>
        public Action<INarrativeProgressStore> OnTriggered { get; set; }
        /// <summary>真正播放完成时回调（在框架写入完成标记之后）</summary>
        public Action<INarrativeProgressStore> OnCompleted { get; set; }
    }
}
