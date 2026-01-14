namespace InnoVault.Dimensions
{
    /// <summary>
    /// 维度层级,用于组织维度的层次结构
    /// </summary>
    public enum DimensionLayerEnum
    {
        /// <summary>
        /// 主世界层
        /// </summary>
        MainWorld = 0,

        /// <summary>
        /// 平行维度层,与主世界同级但独立
        /// </summary>
        Parallel = 1,

        /// <summary>
        /// 子维度层,依附于某个父维度
        /// </summary>
        Sub = 2,

        /// <summary>
        /// 口袋维度层,小型独立空间
        /// </summary>
        Pocket = 3,

        /// <summary>
        /// 临时维度层,短期存在的维度
        /// </summary>
        Temporary = 4
    }
}
