using System;

namespace InnoVault.Scenarios.Presets
{
    /// <summary>
    /// 世界档案里的条目：原版按世界文件记录、换一块舞台就会从头开始的那几类状态，见 <see cref="WorldLedger"/>
    /// </summary>
    [Flags]
    public enum LedgerEntries
    {
        /// <summary>什么都不带</summary>
        None = 0,
        /// <summary>进度：困难模式、各首领与事件的击败记录。写回时只增不减</summary>
        Progress = 1,
        /// <summary>特殊种子旗标（醉酒世界、For the worthy、天顶世界等）。带入时在生成步骤执行之前就写好，生成步骤读得到；不写回</summary>
        SpecialSeeds = 2,
        /// <summary>图鉴：击杀数、见过与交谈过的记录，含模组 NPC。带入与写回都是合并（取并集与较大的击杀数）</summary>
        Bestiary = 4,
        /// <summary>旗帜的击杀计数。带入与写回都取较大值</summary>
        KillCounts = 8,
        /// <summary>旅途模式的世界级能力（时间流速与冻结、难度滑块、天气与风的冻结、群落蔓延）。带入与写回都是照搬</summary>
        JourneyPowers = 16,
        /// <summary>时间：时刻、昼夜与月相。带入后新舞台接着离开时的时间走，写回后在外面度过的时间也算在主世界里</summary>
        Time = 32,
        /// <summary>各类记录：进度、特殊种子、图鉴、旗帜计数与旅途能力，不含时间</summary>
        Records = Progress | SpecialSeeds | Bestiary | KillCounts | JourneyPowers,
        /// <summary>全部条目</summary>
        All = Records | Time,
    }

    /// <summary>
    /// 预设组件：世界档案随队伍走。原版把进度、图鉴、旅途能力这些记录存在世界文件里，每块舞台各一份；
    /// 挂了它的舞台场景在有人换进来时，从刚离开的那块舞台带入 <see cref="Inherit"/> 列出的条目；从它回到主世界时，把 <see cref="WriteBack"/>
    /// 列出的条目并回主世界。一路经过的舞台都挂着它时，这份档案就跟着队伍一直往下传<br/>
    /// 只在裁决端（单人与服务器）携带与写入，写入早于场景自己的 OnActivate，客户端经原版的进服同步拿到结果。
    /// 其他模组自己的世界状态不在其中，由它们经转场数据包与 <see cref="GlobalScenario"/> 携带<br/>
    /// <see cref="InheritVanillaProgress"/> 是它只带进度的简写，两者都挂时取并集
    /// </summary>
    public sealed class WorldLedger : ScenarioComponent
    {
        /// <summary>进入这块舞台时带入的条目，缺省是各类记录（不含时间）</summary>
        public LedgerEntries Inherit { get; init; } = LedgerEntries.Records;

        /// <summary>从这块舞台回到主世界时写回的条目，缺省不写回。特殊种子不会写回</summary>
        public LedgerEntries WriteBack { get; init; }
    }
}
