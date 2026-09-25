using Microsoft.Xna.Framework;
using System;
using System.Globalization;

namespace InnoVault.Rigs2D.Animation
{
    /// <summary>
    /// 缓动曲线的种类
    /// </summary>
    public enum Rig2DEaseKind : byte
    {
        /// <summary>
        /// 线性
        /// </summary>
        Linear,
        /// <summary>
        /// 平滑阶跃 <c>x²(3 − 2x)</c>
        /// </summary>
        Smooth,
        /// <summary>
        /// 缓起 <c>x²</c>
        /// </summary>
        EaseIn,
        /// <summary>
        /// 缓收 <c>1 − (1 − x)²</c>
        /// </summary>
        EaseOut,
        /// <summary>
        /// 硬到位：<c>1 − (1 − x)^p × (1 − b·x)</c>（p 缺省 3.2、b 缺省 0.12）；斩是事件不是过程，首帧就到七成
        /// </summary>
        HardStop,
        /// <summary>
        /// 加速撞上去：<c>x^k</c>（k 缺省 1.7）；巨物出手，末帧最快
        /// </summary>
        Heavy,
        /// <summary>
        /// 过冲缓收：<c>缓收(x) + a·sin(πx)·(1 − x)</c>（a 缺省 0.06）
        /// </summary>
        Overshoot,
        /// <summary>
        /// 阶跃：到终点才跳
        /// </summary>
        Step,
        /// <summary>
        /// 甩过头包络（叠加用）：<c>0 &lt; x &lt; 1.25</c> 时 <c>sin(1.6πx)·e^(−3.2x) / 0.445</c>，到位为 0、约两成处甩到 1、六成回正再反带一点
        /// </summary>
        Whip,
        /// <summary>
        /// 衰减正弦鼓包（叠加用）：<c>0 &lt; x &lt; 1</c> 时 <c>sin(πx)·(1 − d·x)</c>（d 缺省 0.3），出手后沉胯吸收一类
        /// </summary>
        SinDecay,
        /// <summary>
        /// 正弦拱（叠加用）：<c>sin(πx)</c>，抬脚弧线
        /// </summary>
        Arc,
    }

    /// <summary>
    /// 一条缓动曲线：种类 + 至多两个参数。文本写法 <c>"smooth"</c> / <c>"heavy:1.7"</c> / <c>"hardStop:3.2,0.12"</c> / <c>"overshoot:0.06"</c> / <c>"sinDecay:0.3"</c>
    /// <br/>插值用的曲线（Linear ~ Step）先把进度钳到 0~1；叠加用的包络（Whip / SinDecay / Arc）自带定义域，域外为 0
    /// </summary>
    public readonly struct Rig2DEase : IEquatable<Rig2DEase>
    {
        /// <summary>
        /// 种类
        /// </summary>
        public readonly Rig2DEaseKind Kind;
        /// <summary>
        /// 参数一（NaN = 取种类缺省）
        /// </summary>
        public readonly float A;
        /// <summary>
        /// 参数二（NaN = 取种类缺省）
        /// </summary>
        public readonly float B;

        /// <summary>
        /// 构造
        /// </summary>
        public Rig2DEase(Rig2DEaseKind kind, float a = float.NaN, float b = float.NaN) {
            Kind = kind;
            A = a;
            B = b;
        }

        /// <summary>
        /// 线性
        /// </summary>
        public static Rig2DEase Linear => new(Rig2DEaseKind.Linear);

        /// <summary>
        /// 求值
        /// </summary>
        public float Evaluate(float x) {
            switch (Kind) {
                case Rig2DEaseKind.Smooth: {
                        x = MathHelper.Clamp(x, 0f, 1f);
                        return x * x * (3f - 2f * x);
                    }
                case Rig2DEaseKind.EaseIn: {
                        x = MathHelper.Clamp(x, 0f, 1f);
                        return x * x;
                    }
                case Rig2DEaseKind.EaseOut: {
                        x = MathHelper.Clamp(x, 0f, 1f);
                        return 1f - (1f - x) * (1f - x);
                    }
                case Rig2DEaseKind.HardStop: {
                        float k = MathHelper.Clamp(x, 0f, 1f);
                        float p = float.IsNaN(A) ? 3.2f : A;
                        float b = float.IsNaN(B) ? 0.12f : B;
                        return 1f - MathF.Pow(1f - k, p) * (1f - b * k);
                    }
                case Rig2DEaseKind.Heavy:
                    return MathF.Pow(MathHelper.Clamp(x, 0f, 1f), float.IsNaN(A) ? 1.7f : A);
                case Rig2DEaseKind.Overshoot: {
                        x = MathHelper.Clamp(x, 0f, 1f);
                        float a = float.IsNaN(A) ? 0.06f : A;
                        float e = 1f - (1f - x) * (1f - x);
                        return e + a * MathF.Sin(MathF.PI * x) * (1f - x);
                    }
                case Rig2DEaseKind.Step:
                    return x >= 1f ? 1f : 0f;
                case Rig2DEaseKind.Whip:
                    if (x <= 0f || x >= 1.25f) {
                        return 0f;
                    }
                    return MathF.Sin(x * MathF.PI * 1.6f) * MathF.Exp(-x * 3.2f) / 0.445f;
                case Rig2DEaseKind.SinDecay:
                    if (x <= 0f || x >= 1f) {
                        return 0f;
                    }
                    return MathF.Sin(MathF.PI * x) * (1f - (float.IsNaN(A) ? 0.3f : A) * x);
                case Rig2DEaseKind.Arc:
                    return MathF.Sin(MathF.PI * MathHelper.Clamp(x, 0f, 1f));
                default:
                    return MathHelper.Clamp(x, 0f, 1f);
            }
        }

        /// <summary>
        /// 从文本解析；认不出按线性
        /// </summary>
        public static Rig2DEase Parse(string text) {
            if (string.IsNullOrWhiteSpace(text)) {
                return Linear;
            }
            string s = text.Trim();
            float a = float.NaN;
            float b = float.NaN;
            int colon = s.IndexOf(':');
            if (colon >= 0) {
                string[] args = s[(colon + 1)..].Split(',');
                if (args.Length > 0 && float.TryParse(args[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float pa)) {
                    a = pa;
                }
                if (args.Length > 1 && float.TryParse(args[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float pb)) {
                    b = pb;
                }
                s = s[..colon];
            }
            Rig2DEaseKind kind = s.ToLowerInvariant() switch {
                "smooth" => Rig2DEaseKind.Smooth,
                "easein" => Rig2DEaseKind.EaseIn,
                "easeout" => Rig2DEaseKind.EaseOut,
                "hardstop" => Rig2DEaseKind.HardStop,
                "heavy" => Rig2DEaseKind.Heavy,
                "overshoot" => Rig2DEaseKind.Overshoot,
                "step" => Rig2DEaseKind.Step,
                "whip" => Rig2DEaseKind.Whip,
                "sindecay" => Rig2DEaseKind.SinDecay,
                "arc" => Rig2DEaseKind.Arc,
                _ => Rig2DEaseKind.Linear,
            };
            return new Rig2DEase(kind, a, b);
        }

        /// <summary>
        /// 写回文本（与 <see cref="Parse"/> 互逆）
        /// </summary>
        public override string ToString() {
            string name = Kind switch {
                Rig2DEaseKind.EaseIn => "easeIn",
                Rig2DEaseKind.EaseOut => "easeOut",
                Rig2DEaseKind.HardStop => "hardStop",
                Rig2DEaseKind.SinDecay => "sinDecay",
                _ => Kind.ToString().ToLowerInvariant(),
            };
            if (float.IsNaN(A)) {
                return name;
            }
            string args = A.ToString("R", CultureInfo.InvariantCulture);
            if (!float.IsNaN(B)) {
                args += "," + B.ToString("R", CultureInfo.InvariantCulture);
            }
            return name + ":" + args;
        }

        /// <inheritdoc/>
        public bool Equals(Rig2DEase other) => Kind == other.Kind && A.Equals(other.A) && B.Equals(other.B);

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is Rig2DEase e && Equals(e);

        /// <inheritdoc/>
        public override int GetHashCode() => HashCode.Combine(Kind, A, B);
    }
}
