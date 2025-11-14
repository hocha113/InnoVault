namespace InnoVault.Dimensions
{
    /// <summary>
    /// 维度数据复制接口,用于在维度间传输数据
    /// </summary>
    public interface IDimensionDataTransfer
    {
        /// <summary>
        /// 从主世界复制数据到当前维度
        /// </summary>
        void CopyFromMainWorld() { }

        /// <summary>
        /// 读取从主世界复制的数据
        /// </summary>
        void ReadMainWorldData() { }

        /// <summary>
        /// 复制当前维度的数据以传输到其他维度
        /// </summary>
        void CopyDimensionData() { }

        /// <summary>
        /// 读取从其他维度传输的数据
        /// </summary>
        void ReadDimensionData() { }
    }
}
