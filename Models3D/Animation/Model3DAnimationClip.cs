using System;

namespace InnoVault.Models3D.Animation
{
    /// <summary>
    /// 一段命名动画
    /// <br/>对应 glTF <c>animations[]</c>，由若干条 <see cref="Model3DAnimationChannel"/> 组成
    /// <br/><see cref="Duration"/> 取自所有 sampler input 的最大时间，保证 Loop 取模时不会越界
    /// </summary>
    public sealed class Model3DAnimationClip
    {
        /// <summary>
        /// Clip 名称
        /// <br/>来自 glTF <c>animation.name</c>，未命名时由加载器生成
        /// </summary>
        public string Name { get; }
        /// <summary>
        /// Clip 总时长，单位秒
        /// </summary>
        public float Duration { get; }
        /// <summary>
        /// 所有有效通道
        /// <br/>无效（目标节点不在任何骨架中、或属性当前不支持）的通道在加载阶段就已被丢弃
        /// </summary>
        public Model3DAnimationChannel[] Channels { get; }

        /// <summary>
        /// 构造一个 Clip
        /// </summary>
        public Model3DAnimationClip(string name, float duration, Model3DAnimationChannel[] channels) {
            Name = name ?? string.Empty;
            Duration = duration > 0f ? duration : 0f;
            Channels = channels ?? Array.Empty<Model3DAnimationChannel>();
        }
    }
}
