using InnoVault.Rigs2D.Data;
using Microsoft.Xna.Framework;
using System;

namespace InnoVault.Rigs2D.Animation
{
    /// <summary>
    /// 一副通道值（姿态缓冲）：姿态库条目、动画层的中间结果、消费方自己拼的姿态都是它
    /// <br/>下标同 <see cref="Rig2DDefinition.Channels"/>；标量存在 X 分量。插值按各通道的 <see cref="Channel2DDef.Blend"/>：
    /// 线性逐分量、角度走最短弧、弧线绕空间原点按极坐标
    /// <br/>姿态绑在一份定义上：热重载改了通道表后旧缓冲的长度可能不符，消费方应在 <c>OnRigBound</c> 里重建自己持有的缓冲
    /// （<c>[Rig2DPose]</c> 绑定的姿态库条目会自动换新）
    /// </summary>
    public sealed class Rig2DPose
    {
        /// <summary>
        /// 所属定义
        /// </summary>
        public Rig2DDefinition Definition { get; }
        /// <summary>
        /// 各通道值
        /// </summary>
        public Vector2[] Values { get; }
        /// <summary>
        /// 通道数
        /// </summary>
        public int Count => Values.Length;

        /// <summary>
        /// 新建一副取通道缺省值的姿态
        /// </summary>
        public Rig2DPose(Rig2DDefinition def) {
            Definition = def;
            int n = def?.Channels.Count ?? 0;
            Values = new Vector2[n];
            for (int i = 0; i < n; i++) {
                Values[i] = def.Channels[i].Default;
            }
        }

        /// <summary>
        /// 标量读写（越界读 0、写忽略）
        /// </summary>
        public float this[int channel] {
            get => (uint)channel < (uint)Values.Length ? Values[channel].X : 0f;
            set {
                if ((uint)channel < (uint)Values.Length) {
                    Values[channel].X = value;
                }
            }
        }

        /// <summary>
        /// 取向量值（越界返回零）
        /// </summary>
        public Vector2 Vector(int channel) => (uint)channel < (uint)Values.Length ? Values[channel] : Vector2.Zero;

        /// <summary>
        /// 写向量值
        /// </summary>
        public void SetVector(int channel, Vector2 value) {
            if ((uint)channel < (uint)Values.Length) {
                Values[channel] = value;
            }
        }

        /// <summary>
        /// 整副复制
        /// </summary>
        public Rig2DPose CopyFrom(Rig2DPose other) {
            if (other == null || ReferenceEquals(other, this)) {
                return this;
            }
            int n = Math.Min(Count, other.Count);
            Array.Copy(other.Values, Values, n);
            return this;
        }

        /// <summary>
        /// 只复制一组通道
        /// </summary>
        public Rig2DPose CopyGroup(Rig2DPose other, ReadOnlySpan<int> group) {
            if (other == null) {
                return this;
            }
            for (int k = 0; k < group.Length; k++) {
                int i = group[k];
                if ((uint)i < (uint)Values.Length && i < other.Count) {
                    Values[i] = other.Values[i];
                }
            }
            return this;
        }

        /// <summary>
        /// 回到通道缺省值
        /// </summary>
        public Rig2DPose Reset() {
            for (int i = 0; i < Values.Length; i++) {
                Values[i] = Definition.Channels[i].Default;
            }
            return this;
        }

        /// <summary>
        /// 全部清零（加性层的增量姿态从这里起写）
        /// </summary>
        public Rig2DPose Clear() {
            Array.Clear(Values);
            return this;
        }

        /// <summary>
        /// 只在一组通道上做加性叠加
        /// </summary>
        public Rig2DPose Add(Rig2DPose delta, float weight, ReadOnlySpan<int> group) {
            for (int k = 0; k < group.Length; k++) {
                int i = group[k];
                if ((uint)i < (uint)Values.Length && i < delta.Count) {
                    Values[i] += delta.Values[i] * weight;
                }
            }
            return this;
        }

        /// <summary>
        /// 本姿态 = <paramref name="a"/> 与 <paramref name="b"/> 按 <paramref name="t"/> 插值（可与 a / b 为同一对象）
        /// </summary>
        public Rig2DPose Lerp(Rig2DPose a, Rig2DPose b, float t) {
            int n = Math.Min(Count, Math.Min(a.Count, b.Count));
            for (int i = 0; i < n; i++) {
                Values[i] = LerpValue(Definition.Channels[i], a.Values[i], b.Values[i], t);
            }
            return this;
        }

        /// <summary>
        /// 本姿态 = <paramref name="a"/>，但 <paramref name="group"/> 里的通道换成 a 与 b 的插值（遮罩混合：只混上半身一类）
        /// </summary>
        public Rig2DPose Lerp(Rig2DPose a, Rig2DPose b, float t, ReadOnlySpan<int> group) {
            CopyFrom(a);
            for (int k = 0; k < group.Length; k++) {
                int i = group[k];
                if ((uint)i < (uint)Values.Length && i < a.Count && i < b.Count) {
                    Values[i] = LerpValue(Definition.Channels[i], a.Values[i], b.Values[i], t);
                }
            }
            return this;
        }

        /// <summary>
        /// 本姿态向 <paramref name="target"/> 插过去 <paramref name="t"/>
        /// </summary>
        public Rig2DPose BlendTo(Rig2DPose target, float t) => Lerp(this, target, t);

        /// <summary>
        /// 本姿态的 <paramref name="group"/> 通道向 <paramref name="target"/> 插过去 <paramref name="t"/>
        /// </summary>
        public Rig2DPose BlendTo(Rig2DPose target, float t, ReadOnlySpan<int> group) => Lerp(this, target, t, group);

        /// <summary>
        /// 加性叠加：本姿态 += <paramref name="delta"/> × <paramref name="weight"/>（逐分量；角度通道同样直接相加）
        /// </summary>
        public Rig2DPose Add(Rig2DPose delta, float weight) {
            int n = Math.Min(Count, delta.Count);
            for (int i = 0; i < n; i++) {
                Values[i] += delta.Values[i] * weight;
            }
            return this;
        }

        /// <summary>
        /// 按通道定义的插值方式插一个值
        /// </summary>
        public static Vector2 LerpValue(Channel2DDef channel, Vector2 a, Vector2 b, float t) {
            switch (channel.Blend) {
                case Channel2DBlend.Angle:
                    return new Vector2(a.X + MathHelper.WrapAngle(b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);
                case Channel2DBlend.Arc: {
                        float ra = a.Length();
                        float rb = b.Length();
                        if (ra < 0.0001f || rb < 0.0001f) {
                            return Vector2.Lerp(a, b, t);
                        }
                        float aa = MathF.Atan2(a.Y, a.X);
                        float ab = MathF.Atan2(b.Y, b.X);
                        float ang = aa + MathHelper.WrapAngle(ab - aa) * t;
                        float r = ra + (rb - ra) * t;
                        return new Vector2(MathF.Cos(ang) * r, MathF.Sin(ang) * r);
                    }
                default:
                    return new Vector2(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);
            }
        }
    }
}
