namespace InnoVault.Scenarios.Presets
{
    /// <summary>
    /// 预设组件：进入挂了它的舞台场景时，继承离开那块舞台的原版进度（困难模式、各首领与事件的击败记录）<br/>
    /// <see cref="WriteBack"/> 为真时，离开它回到主世界会把在场景里新达成的进度写回主世界，只增不减<br/>
    /// 数据经转场数据包携带，键为 <c>InnoVault:VanillaProgress</c>；其他模组的进度不在其中，需要自己经 <see cref="GlobalScenario"/> 携带
    /// </summary>
    public sealed class InheritVanillaProgress : ScenarioComponent
    {
        /// <summary>离开回到主世界时是否把新达成的进度写回</summary>
        public bool WriteBack { get; init; }
    }
}
