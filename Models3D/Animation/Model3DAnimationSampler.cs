using Microsoft.Xna.Framework;
using System;

namespace InnoVault.Models3D.Animation
{
    /// <summary>
    /// 一个动画采样器
    /// <br/>由 <see cref="Input"/>（关键帧时间，秒）和 <see cref="Output"/>（按 <see cref="Stride"/> 紧排列的浮点数）组成
    /// <br/>同一个 sampler 可被多个 <see cref="Model3DAnimationChannel"/> 引用，
    /// 因此采样行为不依赖通道，只依赖目标分量数（由调用方根据 channel.Path 决定调 <see cref="SampleVec3"/> 还是 <see cref="SampleQuat"/>）
    /// </summary>
    public sealed class Model3DAnimationSampler
    {
        /// <summary>
        /// 关键帧时间数组，单位为秒，严格递增
        /// </summary>
        public float[] Input { get; }
        /// <summary>
        /// 关键帧值数组
        /// <br/>按 <see cref="Stride"/> 个 float 为一组紧排列；CubicSpline 时每组实际是 3 段 stride（in, value, out）
        /// </summary>
        public float[] Output { get; }
        /// <summary>
        /// 单个关键帧值的分量数
        /// <br/>VEC3 为 3，VEC4 为 4
        /// </summary>
        public int Stride { get; }
        /// <summary>
        /// 插值方式
        /// <br/>当前实现把 <see cref="Model3DInterpolation.CubicSpline"/> 降级为线性
        /// </summary>
        public Model3DInterpolation Interpolation { get; }

        /// <summary>
        /// 起始时间，等同于 <see cref="Input"/> 首元素；为空时返回 0
        /// </summary>
        public float StartTime => Input != null && Input.Length > 0 ? Input[0] : 0f;
        /// <summary>
        /// 结束时间，等同于 <see cref="Input"/> 末元素；为空时返回 0
        /// </summary>
        public float EndTime => Input != null && Input.Length > 0 ? Input[Input.Length - 1] : 0f;

        /// <summary>
        /// 构造一个采样器
        /// </summary>
        /// <param name="input">关键帧时间</param>
        /// <param name="output">关键帧值</param>
        /// <param name="stride">分量数</param>
        /// <param name="interpolation">插值类型</param>
        public Model3DAnimationSampler(float[] input, float[] output, int stride, Model3DInterpolation interpolation) {
            Input = input ?? Array.Empty<float>();
            Output = output ?? Array.Empty<float>();
            Stride = stride > 0 ? stride : 1;
            Interpolation = interpolation;
        }

        /// <summary>
        /// 在 <paramref name="time"/> 处采样为 <see cref="Vector3"/>
        /// <br/>边界处 / 单关键帧时返回最近邻；CubicSpline 当前等价 Linear
        /// </summary>
        public Vector3 SampleVec3(float time) {
            if (Input.Length == 0 || Output.Length < Stride) {
                return Vector3.Zero;
            }
            ResolveSegment(time, out int i0, out int i1, out float t);
            if (Interpolation == Model3DInterpolation.Step || i0 == i1 || t <= 0f) {
                return ReadVec3(i0);
            }
            if (t >= 1f) {
                return ReadVec3(i1);
            }
            Vector3 a = ReadVec3(i0);
            Vector3 b = ReadVec3(i1);
            return Vector3.Lerp(a, b, t);
        }

        /// <summary>
        /// 在 <paramref name="time"/> 处采样为 <see cref="Quaternion"/>
        /// <br/>线性插值使用 <see cref="Quaternion"/>.Slerp；CubicSpline 当前等价 Linear
        /// </summary>
        public Quaternion SampleQuat(float time) {
            if (Input.Length == 0 || Output.Length < Stride) {
                return Quaternion.Identity;
            }
            ResolveSegment(time, out int i0, out int i1, out float t);
            if (Interpolation == Model3DInterpolation.Step || i0 == i1 || t <= 0f) {
                Quaternion q0 = ReadQuat(i0);
                return q0;
            }
            if (t >= 1f) {
                return ReadQuat(i1);
            }
            Quaternion a = ReadQuat(i0);
            Quaternion b = ReadQuat(i1);
            return Quaternion.Slerp(a, b, t);
        }

        private Vector3 ReadVec3(int keyframe) {
            int baseIdx = keyframe * Stride;
            //CubicSpline 数据布局为 [inTangent, value, outTangent]，取 value 段（偏移 Stride）
            if (Interpolation == Model3DInterpolation.CubicSpline) {
                baseIdx += Stride;
            }
            if (baseIdx + 2 >= Output.Length) {
                return Vector3.Zero;
            }
            return new Vector3(Output[baseIdx], Output[baseIdx + 1], Output[baseIdx + 2]);
        }

        private Quaternion ReadQuat(int keyframe) {
            int baseIdx = keyframe * Stride;
            if (Interpolation == Model3DInterpolation.CubicSpline) {
                baseIdx += Stride;
            }
            if (baseIdx + 3 >= Output.Length) {
                return Quaternion.Identity;
            }
            return new Quaternion(Output[baseIdx], Output[baseIdx + 1], Output[baseIdx + 2], Output[baseIdx + 3]);
        }

        private void ResolveSegment(float time, out int i0, out int i1, out float t) {
            int count = Input.Length;
            if (count <= 1) {
                i0 = 0;
                i1 = 0;
                t = 0f;
                return;
            }
            if (time <= Input[0]) {
                i0 = 0;
                i1 = 0;
                t = 0f;
                return;
            }
            if (time >= Input[count - 1]) {
                i0 = count - 1;
                i1 = count - 1;
                t = 0f;
                return;
            }
            int lo = 0;
            int hi = count - 1;
            while (lo + 1 < hi) {
                int mid = (lo + hi) >> 1;
                if (Input[mid] <= time) {
                    lo = mid;
                }
                else {
                    hi = mid;
                }
            }
            i0 = lo;
            i1 = lo + 1;
            float a = Input[i0];
            float b = Input[i1];
            float span = b - a;
            t = span > 0f ? MathHelper.Clamp((time - a) / span, 0f, 1f) : 0f;
        }
    }
}
