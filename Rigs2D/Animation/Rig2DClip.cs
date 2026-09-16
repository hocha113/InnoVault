using InnoVault.Rigs2D.Data;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;

namespace InnoVault.Rigs2D.Animation
{
    /// <summary>
    /// 关键帧插值方式
    /// </summary>
    public enum Rig2DInterpolation
    {
        /// <summary>
        /// 线性（角度按最短弧）
        /// </summary>
        Linear,
        /// <summary>
        /// 阶跃：保持前一个关键帧的值直到下一个关键帧
        /// </summary>
        Step,
    }

    /// <summary>
    /// 一个关键帧：时间（帧）与值
    /// </summary>
    public readonly struct Rig2DKey<T>
    {
        /// <summary>
        /// 关键帧时间（帧，60fps 基准）
        /// </summary>
        public readonly float Time;
        /// <summary>
        /// 关键帧值
        /// </summary>
        public readonly T Value;

        /// <summary>
        /// 构造关键帧
        /// </summary>
        public Rig2DKey(float time, T value) {
            Time = time;
            Value = value;
        }
    }

    /// <summary>
    /// 一根骨骼的关键帧轨道：局部旋转 / 局部偏移 / 骨长三条可选通道
    /// <br/>通道值都是"替换静息值"的绝对局部量；空通道表示不驱动该属性
    /// </summary>
    public sealed class Rig2DTrack
    {
        /// <summary>
        /// 目标骨骼名
        /// </summary>
        public string BoneName { get; }
        /// <summary>
        /// 目标骨骼索引，<see cref="Rig2DClip.Resolve"/> 后有效（缺失为 <c>-1</c>）
        /// </summary>
        public int BoneIndex { get; internal set; } = -1;
        /// <summary>
        /// 本轨道的插值方式
        /// </summary>
        public Rig2DInterpolation Interpolation { get; set; } = Rig2DInterpolation.Linear;
        /// <summary>
        /// 局部旋转关键帧（弧度）
        /// </summary>
        public List<Rig2DKey<float>> Rotation { get; } = [];
        /// <summary>
        /// 局部偏移关键帧（像素，Scale 为 1）
        /// </summary>
        public List<Rig2DKey<Vector2>> Offset { get; } = [];
        /// <summary>
        /// 骨长关键帧（像素，Scale 为 1）
        /// </summary>
        public List<Rig2DKey<float>> Length { get; } = [];

        internal Rig2DTrack(string boneName) {
            BoneName = boneName ?? string.Empty;
        }

        /// <summary>
        /// 按时间升序整理关键帧（构建后调用一次即可）
        /// </summary>
        public void Sort() {
            Rotation.Sort((a, b) => a.Time.CompareTo(b.Time));
            Offset.Sort((a, b) => a.Time.CompareTo(b.Time));
            Length.Sort((a, b) => a.Time.CompareTo(b.Time));
        }

        /// <summary>
        /// 采样旋转通道；无关键帧返回 <see langword="false"/>
        /// </summary>
        public bool SampleRotation(float time, out float value) => SampleAngle(Rotation, time, Interpolation, out value);

        /// <summary>
        /// 采样骨长通道；无关键帧返回 <see langword="false"/>
        /// </summary>
        public bool SampleLength(float time, out float value) => SampleFloat(Length, time, Interpolation, out value);

        /// <summary>
        /// 采样偏移通道；无关键帧返回 <see langword="false"/>
        /// </summary>
        public bool SampleOffset(float time, out Vector2 value) {
            List<Rig2DKey<Vector2>> keys = Offset;
            if (keys.Count == 0) {
                value = Vector2.Zero;
                return false;
            }
            Locate(keys.Count, i => keys[i].Time, time, out int a, out int b, out float t);
            value = Interpolation == Rig2DInterpolation.Step ? keys[a].Value : Vector2.Lerp(keys[a].Value, keys[b].Value, t);
            return true;
        }

        private static bool SampleFloat(List<Rig2DKey<float>> keys, float time, Rig2DInterpolation interp, out float value) {
            if (keys.Count == 0) {
                value = 0f;
                return false;
            }
            Locate(keys.Count, i => keys[i].Time, time, out int a, out int b, out float t);
            value = interp == Rig2DInterpolation.Step ? keys[a].Value : MathHelper.Lerp(keys[a].Value, keys[b].Value, t);
            return true;
        }

        private static bool SampleAngle(List<Rig2DKey<float>> keys, float time, Rig2DInterpolation interp, out float value) {
            if (keys.Count == 0) {
                value = 0f;
                return false;
            }
            Locate(keys.Count, i => keys[i].Time, time, out int a, out int b, out float t);
            if (interp == Rig2DInterpolation.Step) {
                value = keys[a].Value;
            }
            else {
                float from = keys[a].Value;
                value = from + MathHelper.WrapAngle(keys[b].Value - from) * t;
            }
            return true;
        }

        //二分定位包围 time 的两帧；越界钳到端点
        private static void Locate(int count, Func<int, float> timeAt, float time, out int a, out int b, out float t) {
            if (count == 1 || time <= timeAt(0)) {
                a = b = 0;
                t = 0f;
                return;
            }
            int last = count - 1;
            if (time >= timeAt(last)) {
                a = b = last;
                t = 0f;
                return;
            }
            int lo = 0;
            int hi = last;
            while (hi - lo > 1) {
                int mid = (lo + hi) >> 1;
                if (timeAt(mid) <= time) {
                    lo = mid;
                }
                else {
                    hi = mid;
                }
            }
            a = lo;
            b = hi;
            float span = timeAt(hi) - timeAt(lo);
            t = span > 0.0001f ? (time - timeAt(lo)) / span : 0f;
        }
    }

    /// <summary>
    /// 一段命名的关键帧片段：若干骨骼轨道 + 时长 + 是否循环
    /// <br/>片段只描述姿态层，运行时由 <see cref="Rig2DClipPlayer"/> 采样并写入实例的局部覆盖，随后求解器照常运行；
    /// 因此关键帧可以与程序化求解自由混用（关键帧驱动的骨骼不被求解器接管即可）
    /// </summary>
    public sealed class Rig2DClip
    {
        /// <summary>
        /// 片段名
        /// </summary>
        public string Name { get; }
        /// <summary>
        /// 时长（帧）
        /// </summary>
        public float Duration { get; set; }
        /// <summary>
        /// 是否循环
        /// </summary>
        public bool Loop { get; set; } = true;
        /// <summary>
        /// 轨道表
        /// </summary>
        public List<Rig2DTrack> Tracks { get; } = [];

        /// <summary>
        /// 构造片段
        /// </summary>
        public Rig2DClip(string name, float duration) {
            Name = name ?? string.Empty;
            Duration = Math.Max(duration, 1f);
        }

        /// <summary>
        /// 取或建某骨骼的轨道
        /// </summary>
        public Rig2DTrack Track(string boneName) {
            for (int i = 0; i < Tracks.Count; i++) {
                if (string.Equals(Tracks[i].BoneName, boneName, StringComparison.Ordinal)) {
                    return Tracks[i];
                }
            }
            Rig2DTrack t = new(boneName);
            Tracks.Add(t);
            return t;
        }

        /// <summary>
        /// 解析轨道的骨骼索引并整理关键帧顺序
        /// </summary>
        public void Resolve(Rig2DDefinition def) {
            for (int i = 0; i < Tracks.Count; i++) {
                Tracks[i].BoneIndex = def?.BoneIndex(Tracks[i].BoneName) ?? -1;
                Tracks[i].Sort();
            }
        }

        /// <summary>
        /// 把时间折进片段范围：循环取模，非循环钳制
        /// </summary>
        public float WrapTime(float time) {
            if (Duration <= 0f) {
                return 0f;
            }
            if (Loop) {
                time %= Duration;
                if (time < 0f) {
                    time += Duration;
                }
                return time;
            }
            return MathHelper.Clamp(time, 0f, Duration);
        }
    }
}
