namespace InnoVault.Narrative.Progress
{
    /// <summary>
    /// 叙事进度字段键，由"模组名 + 场景 Key + 字段名"组成，确保跨模组、跨场景不冲突<br/>
    /// 用于 flag / counter / string 等细粒度进度，而场景整体进度走 <see cref="ScenarioProgress"/>
    /// </summary>
    public readonly record struct NarrativeProgressKey(string Mod, string Scenario, string Field)
    {
        /// <summary>序列化用的扁平字符串键</summary>
        public string Flat => $"{Mod}:{Scenario}:{Field}";
        /// <inheritdoc/>
        public override string ToString() => Flat;
    }
}
