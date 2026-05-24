namespace InnoVault.Models3D.Animation
{
    /// <summary>
    /// 一条动画通道
    /// <br/>对应 glTF <c>animation.channels[*]</c>，但 <c>target.node</c> 已经在加载阶段
    /// 解析为 <see cref="SkeletonIndex"/> + <see cref="JointIndex"/>
    /// <br/>同一个目标节点若属于多个 skin，加载器会复制多份通道，每份指向对应的 (skeleton, joint)
    /// </summary>
    public sealed class Model3DAnimationChannel
    {
        /// <summary>
        /// 目标骨架索引
        /// <br/>指向 <see cref="Runtime.Vault3DModel.Skeletons"/>
        /// </summary>
        public int SkeletonIndex { get; }
        /// <summary>
        /// 在目标骨架中的骨骼索引
        /// <br/>指向 <see cref="Skinning.Model3DSkeleton.Joints"/>
        /// </summary>
        public int JointIndex { get; }
        /// <summary>
        /// 通道驱动的属性类型
        /// </summary>
        public Model3DAnimationPath Path { get; }
        /// <summary>
        /// 关联的采样器
        /// </summary>
        public Model3DAnimationSampler Sampler { get; }

        /// <summary>
        /// 构造一条动画通道
        /// </summary>
        public Model3DAnimationChannel(int skeletonIndex, int jointIndex
            , Model3DAnimationPath path, Model3DAnimationSampler sampler) {
            SkeletonIndex = skeletonIndex;
            JointIndex = jointIndex;
            Path = path;
            Sampler = sampler;
        }
    }
}
