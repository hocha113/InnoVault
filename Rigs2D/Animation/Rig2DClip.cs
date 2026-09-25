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
    /// 一个关键帧：时间（帧）、值，以及到达本键那一段的缓动（缺省线性）
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
        /// 从上一键插到本键时用的缓动
        /// </summary>
        public readonly Rig2DEase Ease;

        /// <summary>
        /// 构造关键帧（线性）
        /// </summary>
        public Rig2DKey(float time, T value) {
            Time = time;
            Value = value;
            Ease = Rig2DEase.Linear;
        }

        /// <summary>
        /// 构造关键帧（指定到达本键那一段的缓动）
        /// </summary>
        public Rig2DKey(float time, T value, Rig2DEase ease) {
            Time = time;
            Value = value;
            Ease = ease;
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
        /// 深拷贝轨道（关键帧逐个复制；不含已解析的骨骼索引）
        /// </summary>
        public Rig2DTrack Clone() {
            Rig2DTrack c = new(BoneName) { Interpolation = Interpolation };
            c.Rotation.AddRange(Rotation);
            c.Offset.AddRange(Offset);
            c.Length.AddRange(Length);
            return c;
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
            t = EaseSegment(keys[b].Ease, t, a, b);
            value = Interpolation == Rig2DInterpolation.Step ? keys[a].Value : Vector2.Lerp(keys[a].Value, keys[b].Value, t);
            return true;
        }

        private static bool SampleFloat(List<Rig2DKey<float>> keys, float time, Rig2DInterpolation interp, out float value) {
            if (keys.Count == 0) {
                value = 0f;
                return false;
            }
            Locate(keys.Count, i => keys[i].Time, time, out int a, out int b, out float t);
            t = EaseSegment(keys[b].Ease, t, a, b);
            value = interp == Rig2DInterpolation.Step ? keys[a].Value : MathHelper.Lerp(keys[a].Value, keys[b].Value, t);
            return true;
        }

        //到达 b 键那一段的缓动；线性不动 t（旧片段逐位不变）
        internal static float EaseSegment(Rig2DEase ease, float t, int a, int b)
            => a == b || ease.Kind == Rig2DEaseKind.Linear ? t : ease.Evaluate(t);

        private static bool SampleAngle(List<Rig2DKey<float>> keys, float time, Rig2DInterpolation interp, out float value) {
            if (keys.Count == 0) {
                value = 0f;
                return false;
            }
            Locate(keys.Count, i => keys[i].Time, time, out int a, out int b, out float t);
            t = EaseSegment(keys[b].Ease, t, a, b);
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
        internal static void Locate(int count, Func<int, float> timeAt, float time, out int a, out int b, out float t) {
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
    /// 一条通道轨：按时间给某通道的值（标量在 X），逐键缓动；角度通道按最短弧插
    /// </summary>
    public sealed class Rig2DChannelTrack
    {
        /// <summary>
        /// 目标通道名
        /// </summary>
        public string ChannelName { get; }
        /// <summary>
        /// 目标通道索引，<see cref="Rig2DClip.Resolve"/> 后有效（缺失为 <c>-1</c>）
        /// </summary>
        public int ChannelIndex { get; internal set; } = -1;
        /// <summary>
        /// 关键帧
        /// </summary>
        public List<Rig2DKey<Vector2>> Keys { get; } = [];

        internal Rig2DChannelTrack(string channelName) {
            ChannelName = channelName ?? string.Empty;
        }

        /// <summary>
        /// 深拷贝
        /// </summary>
        public Rig2DChannelTrack Clone() {
            Rig2DChannelTrack c = new(ChannelName);
            c.Keys.AddRange(Keys);
            return c;
        }

        /// <summary>
        /// 采样；无关键帧返回 <see langword="false"/>
        /// </summary>
        public bool Sample(float time, Channel2DDef channel, out Vector2 value) {
            if (Keys.Count == 0) {
                value = Vector2.Zero;
                return false;
            }
            Rig2DTrack.Locate(Keys.Count, i => Keys[i].Time, time, out int a, out int b, out float t);
            t = Rig2DTrack.EaseSegment(Keys[b].Ease, t, a, b);
            value = channel != null ? Rig2DPose.LerpValue(channel, Keys[a].Value, Keys[b].Value, t) : Vector2.Lerp(Keys[a].Value, Keys[b].Value, t);
            return true;
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
        /// 通道轨表（播放头把采样值写进实例通道，按主权重与淡入淡出混合）
        /// </summary>
        public List<Rig2DChannelTrack> ChannelTracks { get; } = [];
        /// <summary>
        /// 事件：名 → 帧（落步、判定开合、刃到位一类；用 <see cref="Crossed"/> 查某帧是否刚越过）
        /// </summary>
        public Dictionary<string, float> Events { get; } = new(StringComparer.Ordinal);

        /// <summary>
        /// 取或建某通道的轨道
        /// </summary>
        public Rig2DChannelTrack ChannelTrack(string channelName) {
            for (int i = 0; i < ChannelTracks.Count; i++) {
                if (string.Equals(ChannelTracks[i].ChannelName, channelName, StringComparison.Ordinal)) {
                    return ChannelTracks[i];
                }
            }
            Rig2DChannelTrack t = new(channelName);
            ChannelTracks.Add(t);
            return t;
        }

        /// <summary>
        /// 事件是否在 (<paramref name="from"/>, <paramref name="to"/>] 之间（循环片段按折回后的时间判）
        /// </summary>
        public bool Crossed(string eventName, float from, float to) {
            if (!Events.TryGetValue(eventName, out float e)) {
                return false;
            }
            return from < e && e <= to;
        }

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
        /// 深拷贝片段（轨道与关键帧全部复制；未解析，需要调用方再 <see cref="Resolve"/>）
        /// </summary>
        public Rig2DClip Clone() {
            Rig2DClip c = new(Name, Duration) { Loop = Loop };
            for (int i = 0; i < Tracks.Count; i++) {
                c.Tracks.Add(Tracks[i].Clone());
            }
            for (int i = 0; i < ChannelTracks.Count; i++) {
                c.ChannelTracks.Add(ChannelTracks[i].Clone());
            }
            foreach (KeyValuePair<string, float> kv in Events) {
                c.Events[kv.Key] = kv.Value;
            }
            return c;
        }

        /// <summary>
        /// 解析轨道的骨骼 / 通道索引并整理关键帧顺序
        /// </summary>
        public void Resolve(Rig2DDefinition def) {
            for (int i = 0; i < Tracks.Count; i++) {
                Tracks[i].BoneIndex = def?.BoneIndex(Tracks[i].BoneName) ?? -1;
                Tracks[i].Sort();
            }
            for (int i = 0; i < ChannelTracks.Count; i++) {
                ChannelTracks[i].ChannelIndex = def?.ChannelIndex(ChannelTracks[i].ChannelName) ?? -1;
                ChannelTracks[i].Keys.Sort((a, b) => a.Time.CompareTo(b.Time));
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
