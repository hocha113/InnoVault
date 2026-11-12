namespace InnoVault.Scenarios.Presets
{
    /// <summary>
    /// 预设组件：进入挂了它的舞台场景时，继承离开那块舞台的原版进度（困难模式、各首领与事件的击败记录）<br/>
    /// <see cref="WriteBack"/> 为真时，离开它回到主世界会把在场景里新达成的进度写回主世界，只增不减<br/>
    /// 它是 <see cref="WorldLedger"/> 只带进度（<see cref="LedgerEntries.Progress"/>）的简写；图鉴、旗帜计数、旅途能力、特殊种子与时间也要跟着走时改挂 <see cref="WorldLedger"/>。
    /// 其他模组的进度不在其中，需要自己经 <see cref="GlobalScenario"/> 携带
    /// </summary>
    public sealed class InheritVanillaProgress : ScenarioComponent
    {
        /// <summary>离开回到主世界时是否把新达成的进度写回</summary>
        public bool WriteBack { get; init; }
    }
}
