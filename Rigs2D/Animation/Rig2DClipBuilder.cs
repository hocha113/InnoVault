using InnoVault.Rigs2D.Data;
using Microsoft.Xna.Framework;
using System;

namespace InnoVault.Rigs2D.Animation
{
    /// <summary>
    /// 代码优先的关键帧片段构建器：链式选骨、逐键写旋转 / 偏移 / 骨长，<see cref="Build"/> 产出已整理的 <see cref="Rig2DClip"/>
    /// </summary>
    /// <example>
    /// <code>
    /// Rig2DClip idle = new Rig2DClipBuilder("idle", duration: 120f, loop: true)
    ///     .Bone("upperR").Rotate(0f, -0.4f).Rotate(60f, 0.3f).Rotate(120f, -0.4f)
    ///     .Bone("clawR").Step().Rotate(0f, 0f).Rotate(90f, 0.5f)
    ///     .Build(rigDefinition);
    /// </code>
    /// </example>
    public sealed class Rig2DClipBuilder
    {
        private readonly Rig2DClip clip;
        private Rig2DTrack track;

        /// <summary>
        /// 新建构建器
        /// </summary>
        /// <param name="name">片段名</param>
        /// <param name="duration">时长（帧）</param>
        /// <param name="loop">是否循环</param>
        public Rig2DClipBuilder(string name, float duration, bool loop = true) {
            clip = new Rig2DClip(name, duration) { Loop = loop };
        }

        /// <summary>
        /// 选中（或新建）某骨骼的轨道，之后的键都写进它
        /// </summary>
        public Rig2DClipBuilder Bone(string boneName) {
            track = clip.Track(boneName);
            return this;
        }

        /// <summary>
        /// 当前轨道改为阶跃插值
        /// </summary>
        public Rig2DClipBuilder Step() {
            Require().Interpolation = Rig2DInterpolation.Step;
            return this;
        }

        /// <summary>
        /// 当前轨道改为线性插值（默认）
        /// </summary>
        public Rig2DClipBuilder Linear() {
            Require().Interpolation = Rig2DInterpolation.Linear;
            return this;
        }

        /// <summary>
        /// 写一个局部旋转键（弧度）
        /// </summary>
        public Rig2DClipBuilder Rotate(float time, float radians) {
            Require().Rotation.Add(new Rig2DKey<float>(time, radians));
            return this;
        }

        /// <summary>
        /// 写一个局部旋转键（角度）
        /// </summary>
        public Rig2DClipBuilder RotateDeg(float time, float degrees) => Rotate(time, MathHelper.ToRadians(degrees));

        /// <summary>
        /// 写一个局部偏移键（像素，Scale 为 1）
        /// </summary>
        public Rig2DClipBuilder Offset(float time, Vector2 offset) {
            Require().Offset.Add(new Rig2DKey<Vector2>(time, offset));
            return this;
        }

        /// <summary>
        /// 写一个骨长键（像素，Scale 为 1）
        /// </summary>
        public Rig2DClipBuilder Length(float time, float length) {
            Require().Length.Add(new Rig2DKey<float>(time, length));
            return this;
        }

        /// <summary>
        /// 产出片段；给了定义就顺手解析骨骼索引（否则在首次播放时解析）
        /// </summary>
        public Rig2DClip Build(Rig2DDefinition definition = null) {
            if (definition != null) {
                clip.Resolve(definition);
            }
            else {
                for (int i = 0; i < clip.Tracks.Count; i++) {
                    clip.Tracks[i].Sort();
                }
            }
            return clip;
        }

        private Rig2DTrack Require()
            => track ?? throw new InvalidOperationException("Rig2DClipBuilder: call Bone(...) before writing keys");
    }
}
