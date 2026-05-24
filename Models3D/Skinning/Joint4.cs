namespace InnoVault.Models3D.Skinning
{
    /// <summary>
    /// 4 个骨骼索引的紧凑表示
    /// <br/>对应 glTF <c>JOINTS_0</c> 顶点属性，索引到 <see cref="Model3DSkeleton.Joints"/>
    /// <br/>使用 <see cref="ushort"/> 已可覆盖 65k 根骨头，远超实用上限
    /// </summary>
    public struct Joint4
    {
        /// <summary>
        /// 第 1 个骨骼索引
        /// </summary>
        public ushort I0;
        /// <summary>
        /// 第 2 个骨骼索引
        /// </summary>
        public ushort I1;
        /// <summary>
        /// 第 3 个骨骼索引
        /// </summary>
        public ushort I2;
        /// <summary>
        /// 第 4 个骨骼索引
        /// </summary>
        public ushort I3;

        /// <summary>
        /// 构造一个四骨骼索引组合
        /// </summary>
        public Joint4(ushort i0, ushort i1, ushort i2, ushort i3) {
            I0 = i0;
            I1 = i1;
            I2 = i2;
            I3 = i3;
        }
    }
}
