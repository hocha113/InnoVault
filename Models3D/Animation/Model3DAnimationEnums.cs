namespace InnoVault.Models3D.Animation
{
    /// <summary>
    /// 动画通道目标属性类型
    /// <br/>对应 glTF <c>animation.channels[*].target.path</c>
    /// </summary>
    public enum Model3DAnimationPath
    {
        /// <summary>
        /// 平移
        /// </summary>
        Translation,
        /// <summary>
        /// 旋转（四元数）
        /// </summary>
        Rotation,
        /// <summary>
        /// 缩放
        /// </summary>
        Scale,
        /// <summary>
        /// Morph target 权重
        /// <br/>当前版本不消费，加载阶段会被归并为无效通道
        /// </summary>
        Weights,
    }

    /// <summary>
    /// 动画采样器插值方式
    /// <br/>对应 glTF <c>animation.samplers[*].interpolation</c>
    /// </summary>
    public enum Model3DInterpolation
    {
        /// <summary>
        /// 阶梯插值（沿用前一个关键帧值，到下一个时间点突变）
        /// </summary>
        Step,
        /// <summary>
        /// 线性插值
        /// </summary>
        Linear,
        /// <summary>
        /// 立方样条插值
        /// <br/>当前实现以 Linear 兜底（仍可读取，但加载阶段会写诊断）
        /// </summary>
        CubicSpline,
    }
}
