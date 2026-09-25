using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;

namespace InnoVault.Rigs2D.Data
{
    /// <summary>
    /// 招式的一段：插到哪张姿态、用多少帧、什么缓动
    /// </summary>
    public sealed class Move2DSegment
    {
        /// <summary>
        /// 段名（分组改段长 / 改缓动、强调包络与事件按名引用）
        /// </summary>
        public string Name { get; set; } = string.Empty;
        /// <summary>
        /// 目标姿态名；<c>@base</c> = 起招那刻的底姿；空 = 定住（保持上一段的目标）
        /// </summary>
        public string To { get; set; }
        /// <summary>
        /// 帧数
        /// </summary>
        public float Frames { get; set; } = 1f;
        /// <summary>
        /// 缓动（文本写法见 <c>Rig2DEase.Parse</c>）
        /// </summary>
        public string Ease { get; set; } = "linear";
        /// <summary>
        /// 到位语义：进度按 <c>(帧内时间 + 1) / 帧数</c> 算，段内最后一帧就到目标（爆发段用；否则按 <c>帧内时间 / 帧数</c>）
        /// </summary>
        public bool Arrive { get; set; }

        /// <summary>
        /// 复制
        /// </summary>
        public Move2DSegment Clone() => new() {
            Name = Name,
            To = To,
            Frames = Frames,
            Ease = Ease,
            Arrive = Arrive,
        };
    }

    /// <summary>
    /// 一个通道组在招式里的时间轴差异：整体领先（负为落后）、个别段加减帧、个别段换缓动。
    /// 同一张姿态表按身体分节错开时间取样，就是「下盘领先、躯干次之、手臂按主时间、武器最后甩到」
    /// </summary>
    public sealed class Move2DGroup
    {
        /// <summary>
        /// 领先帧数（正 = 比主时间早，负 = 晚）
        /// </summary>
        public float Lead { get; set; }
        /// <summary>
        /// 段名 → 帧数增减（结果至少 1 帧）
        /// </summary>
        public Dictionary<string, float> Resize { get; } = new(StringComparer.Ordinal);
        /// <summary>
        /// 段名 → 缓动覆盖
        /// </summary>
        public Dictionary<string, string> Ease { get; } = new(StringComparer.Ordinal);

        /// <summary>
        /// 复制
        /// </summary>
        public Move2DGroup Clone() {
            Move2DGroup c = new() { Lead = Lead };
            foreach (KeyValuePair<string, float> kv in Resize) {
                c.Resize[kv.Key] = kv.Value;
            }
            foreach (KeyValuePair<string, string> kv in Ease) {
                c.Ease[kv.Key] = kv.Value;
            }
            return c;
        }
    }

    /// <summary>
    /// 弧线抬脚：某向量通道在一段里沿 <see cref="Axis"/> 挪了多远，就在 <see cref="Out"/> 分量上按 <c>sin(π·进度)</c> 拱起多高
    /// （<c>min(|位移| × Lift, Max)</c>，乘 <see cref="Scale"/>，方向 <see cref="Sign"/>），可顺带给一个角度通道加 <c>AngleAmount·sin(2π·进度)·(拱高 / Max)</c>
    /// </summary>
    public sealed class Move2DArc
    {
        /// <summary>
        /// 被抬的向量通道
        /// </summary>
        public string Channel { get; set; } = string.Empty;
        /// <summary>
        /// 取时间轴的通道组（空 = 主时间）
        /// </summary>
        public string Group { get; set; }
        /// <summary>
        /// 位移 → 拱高的比例
        /// </summary>
        public float Lift { get; set; } = 0.45f;
        /// <summary>
        /// 拱高上限
        /// </summary>
        public float Max { get; set; } = 72f;
        /// <summary>
        /// 量位移的分量（0 = x、1 = y）
        /// </summary>
        public int Axis { get; set; }
        /// <summary>
        /// 被拱起的分量（0 = x、1 = y）
        /// </summary>
        public int Out { get; set; } = 1;
        /// <summary>
        /// 拱起方向（屏幕 y 向下，抬脚是 −1）
        /// </summary>
        public float Sign { get; set; } = -1f;
        /// <summary>
        /// 拱高倍率
        /// </summary>
        public float Scale { get; set; } = 1f;
        /// <summary>
        /// 顺带摆动的角度通道（可空）
        /// </summary>
        public string AngleChannel { get; set; }
        /// <summary>
        /// 角度摆幅
        /// </summary>
        public float AngleAmount { get; set; }

        /// <summary>
        /// 复制
        /// </summary>
        public Move2DArc Clone() => (Move2DArc)MemberwiseClone();
    }

    /// <summary>
    /// 强调包络：以某组时间轴上某段的结束为原点、按 <see cref="Span"/> 帧归一，按曲线给若干通道叠加量。
    /// 甩过头（<c>whip</c>，方向随该段挥向）与出手后沉胯（<c>sinDecay</c>）都是它
    /// </summary>
    public sealed class Move2DAccent
    {
        /// <summary>
        /// 取时间轴的通道组（空 = 主时间）
        /// </summary>
        public string Group { get; set; }
        /// <summary>
        /// 原点：这一段的结束
        /// </summary>
        public string After { get; set; } = string.Empty;
        /// <summary>
        /// 归一帧数；≤ 0 时取 <see cref="Over"/> 段的帧数
        /// </summary>
        public float Span { get; set; }
        /// <summary>
        /// 归一用的段名（<see cref="Span"/> 未给时）
        /// </summary>
        public string Over { get; set; }
        /// <summary>
        /// 包络曲线（<c>whip</c> / <c>sinDecay:0.3</c> / 任意缓动）
        /// </summary>
        public string Curve { get; set; } = "whip";
        /// <summary>
        /// 方向：<c>swing</c> = 该通道在 <see cref="After"/> 段里的挥向符号（角度通道按最短弧）；其余 = +1
        /// </summary>
        public string Direction { get; set; }
        /// <summary>
        /// 通道 → 叠加量（标量在 X）
        /// </summary>
        public Dictionary<string, Vector2> Channels { get; } = new(StringComparer.Ordinal);

        /// <summary>
        /// 复制
        /// </summary>
        public Move2DAccent Clone() {
            Move2DAccent c = new() {
                Group = Group,
                After = After,
                Span = Span,
                Over = Over,
                Curve = Curve,
                Direction = Direction,
            };
            foreach (KeyValuePair<string, Vector2> kv in Channels) {
                c.Channels[kv.Key] = kv.Value;
            }
            return c;
        }
    }

    /// <summary>
    /// 招式事件：某组时间轴上某段的开始或结束（加偏移），换算到主时间
    /// </summary>
    public sealed class Move2DEvent
    {
        /// <summary>
        /// 通道组（空 = 主时间）
        /// </summary>
        public string Group { get; set; }
        /// <summary>
        /// 段名
        /// </summary>
        public string Segment { get; set; } = string.Empty;
        /// <summary>
        /// <c>start</c> / <c>end</c>
        /// </summary>
        public bool AtEnd { get; set; } = true;
        /// <summary>
        /// 帧偏移
        /// </summary>
        public float Offset { get; set; }

        /// <summary>
        /// 复制
        /// </summary>
        public Move2DEvent Clone() => (Move2DEvent)MemberwiseClone();
    }

    /// <summary>
    /// 一个招式（段式写法）：起点姿态 → 若干段（每段插到一张姿态），各通道组按自己的时间轴取样，
    /// 再叠弧线抬脚、强调包络；事件换算到主时间。解析后由 <c>Rig2DMove</c> 求值
    /// </summary>
    public sealed class Move2DDef
    {
        /// <summary>
        /// 招式名
        /// </summary>
        public string Name { get; set; } = string.Empty;
        /// <summary>
        /// 起点姿态名；<c>@base</c>（缺省）= 起招那刻的底姿
        /// </summary>
        public string Start { get; set; } = "@base";
        /// <summary>
        /// 段表
        /// </summary>
        public List<Move2DSegment> Segments { get; } = [];
        /// <summary>
        /// 通道组名 → 时间轴差异
        /// </summary>
        public Dictionary<string, Move2DGroup> Groups { get; } = new(StringComparer.Ordinal);
        /// <summary>
        /// 弧线抬脚
        /// </summary>
        public List<Move2DArc> Arcs { get; } = [];
        /// <summary>
        /// 强调包络（甩过头、沉胯一类），按表内顺序在弧线之后叠加
        /// </summary>
        public List<Move2DAccent> Accents { get; } = [];
        /// <summary>
        /// 事件
        /// </summary>
        public Dictionary<string, Move2DEvent> Events { get; } = new(StringComparer.Ordinal);

        /// <summary>
        /// 复制
        /// </summary>
        public Move2DDef Clone() {
            Move2DDef c = new() {
                Name = Name,
                Start = Start,
            };
            foreach (Move2DSegment s in Segments) {
                c.Segments.Add(s.Clone());
            }
            foreach (KeyValuePair<string, Move2DGroup> kv in Groups) {
                c.Groups[kv.Key] = kv.Value.Clone();
            }
            foreach (Move2DArc a in Arcs) {
                c.Arcs.Add(a.Clone());
            }
            foreach (Move2DAccent a in Accents) {
                c.Accents.Add(a.Clone());
            }
            foreach (KeyValuePair<string, Move2DEvent> kv in Events) {
                c.Events[kv.Key] = kv.Value.Clone();
            }
            return c;
        }
    }
}
