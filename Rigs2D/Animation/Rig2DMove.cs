using InnoVault.Rigs2D.Data;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;

namespace InnoVault.Rigs2D.Animation
{
    /// <summary>
    /// 解析后的招式（全体实例共享、只读）：<see cref="Sample"/> 给出第 t 帧的整副通道值
    /// <br/>求值：各通道组按自己的时间轴（领先 / 落后、个别段加减帧、个别段换缓动）定位到一段，
    /// 段内进度 = 帧内时间 / 帧数（到位段为 (帧内时间 + 1) / 帧数），按缓动在该段起止姿态之间插值；
    /// 首段之前与末段之后都沿用段内公式、由缓动钳进度。之后依次叠弧线抬脚、强调包络（表内顺序）
    /// <br/>联机：纯函数，时间由消费方用已同步的计时写入
    /// </summary>
    public sealed class Rig2DMove
    {
        private const int PoseBase = -2;

        private sealed class Timeline
        {
            public float Lead;
            public float[] Start;
            public float[] Frames;
            public Rig2DEase[] Ease;
        }

        private struct Arc
        {
            public int Channel;
            public int Slot;
            public int AngleChannel;
            public int Axis;
            public int Out;
            public float Lift;
            public float Max;
            public float Sign;
            public float Scale;
            public float AngleAmount;
        }

        private sealed class Accent
        {
            public int Slot;
            public int After;
            public int Over;
            public float Span;
            public Rig2DEase Curve;
            public bool Swing;
            public int[] Channels;
            public Vector2[] Amounts;
        }

        /// <summary>
        /// 招式名
        /// </summary>
        public string Name { get; }
        /// <summary>
        /// 所属定义
        /// </summary>
        public Rig2DDefinition Definition { get; }
        /// <summary>
        /// 主时间轴总帧数
        /// </summary>
        public float Duration { get; }
        /// <summary>
        /// 起点是不是起招那刻的底姿（<c>@base</c>）
        /// </summary>
        public bool StartsFromBase => segFrom.Length > 0 && segFrom[0] == PoseBase;

        private readonly string[] segNames;
        private readonly int[] segFrom;
        private readonly int[] segTo;
        private readonly bool[] arrive;
        private readonly Timeline[] slots;
        private readonly int[] slotOf;
        private readonly Arc[] arcs;
        private readonly Accent[] accents;
        private readonly Dictionary<string, float> events = new(StringComparer.Ordinal);
        private readonly int[] slotSeg;
        private readonly float[] slotQ;
        private readonly float[] slotE;
        private Rig2DPose scratch;

        private Rig2DMove(Rig2DDefinition def, string name, int segments, int slotCount) {
            Definition = def;
            Name = name;
            segNames = new string[segments];
            segFrom = new int[segments];
            segTo = new int[segments];
            arrive = new bool[segments];
            slots = new Timeline[slotCount];
            slotOf = new int[def.Channels.Count];
            slotSeg = new int[slotCount];
            slotQ = new float[slotCount];
            slotE = new float[slotCount];
            arcs = [];
            accents = [];
        }

        private Rig2DMove(Rig2DMove shape, Arc[] arcs, Accent[] accents, float duration) {
            Definition = shape.Definition;
            Name = shape.Name;
            segNames = shape.segNames;
            segFrom = shape.segFrom;
            segTo = shape.segTo;
            arrive = shape.arrive;
            slots = shape.slots;
            slotOf = shape.slotOf;
            slotSeg = shape.slotSeg;
            slotQ = shape.slotQ;
            slotE = shape.slotE;
            this.arcs = arcs;
            this.accents = accents;
            Duration = duration;
            foreach (KeyValuePair<string, float> kv in shape.events) {
                events[kv.Key] = kv.Value;
            }
        }

        //==================== 解析 ====================

        internal static Rig2DMove Build(Rig2DDefinition def, Move2DDef m, Action<string> fail) {
            int n = m.Segments.Count;
            if (n == 0) {
                fail($"move '{m.Name}' has no segments");
                return null;
            }
            List<string> groupNames = [.. m.Groups.Keys];
            Rig2DMove shape = new(def, m.Name, n, groupNames.Count + 1);

            int PoseRef(string name, string what) {
                if (string.IsNullOrEmpty(name) || name == "@base") {
                    return PoseBase;
                }
                int p = def.PoseIndex(name);
                if (p < 0) {
                    fail($"move '{m.Name}' {what} pose '{name}' not found");
                    return PoseBase;
                }
                return p;
            }

            int prev = PoseRef(m.Start, "start");
            Timeline main = new() { Start = new float[n], Frames = new float[n], Ease = new Rig2DEase[n] };
            float cursor = 0f;
            for (int i = 0; i < n; i++) {
                Move2DSegment s = m.Segments[i];
                shape.segNames[i] = s.Name ?? string.Empty;
                shape.segFrom[i] = prev;
                shape.segTo[i] = string.IsNullOrEmpty(s.To) ? prev : PoseRef(s.To, $"segment '{s.Name}'");
                shape.arrive[i] = s.Arrive;
                main.Frames[i] = Math.Max(s.Frames, 0.0001f);
                main.Start[i] = cursor;
                main.Ease[i] = Rig2DEase.Parse(s.Ease);
                cursor += main.Frames[i];
                prev = shape.segTo[i];
            }
            shape.slots[0] = main;
            float duration = cursor;

            for (int g = 0; g < groupNames.Count; g++) {
                Move2DGroup mg = m.Groups[groupNames[g]];
                Timeline tl = new() { Lead = mg.Lead, Start = new float[n], Frames = new float[n], Ease = new Rig2DEase[n] };
                float c = 0f;
                for (int i = 0; i < n; i++) {
                    string seg = shape.segNames[i];
                    float frames = main.Frames[i];
                    if (mg.Resize.TryGetValue(seg, out float delta)) {
                        frames = Math.Max(frames + delta, 1f);
                    }
                    tl.Frames[i] = frames;
                    tl.Start[i] = c;
                    tl.Ease[i] = mg.Ease.TryGetValue(seg, out string e) ? Rig2DEase.Parse(e) : main.Ease[i];
                    c += frames;
                }
                shape.slots[g + 1] = tl;
                int[] channels = def.ChannelGroup(groupNames[g]);
                if (channels.Length == 0) {
                    fail($"move '{m.Name}' group '{groupNames[g]}' is not a channel group (or is empty)");
                }
                foreach (int ch in channels) {
                    if (shape.slotOf[ch] == 0) {
                        shape.slotOf[ch] = g + 1;
                    }
                }
            }

            int SlotOf(string group) {
                if (string.IsNullOrEmpty(group)) {
                    return 0;
                }
                int i = groupNames.IndexOf(group);
                if (i < 0) {
                    fail($"move '{m.Name}' timing group '{group}' is not declared in its groups");
                    return 0;
                }
                return i + 1;
            }

            int SegOf(string seg, string what) {
                int i = Array.IndexOf(shape.segNames, seg ?? string.Empty);
                if (i < 0) {
                    fail($"move '{m.Name}' {what} segment '{seg}' not found");
                    return 0;
                }
                return i;
            }

            List<Arc> arcList = [];
            foreach (Move2DArc a in m.Arcs) {
                int ch = def.ChannelIndex(a.Channel);
                if (ch < 0) {
                    fail($"move '{m.Name}' arc channel '{a.Channel}' not found");
                    continue;
                }
                int angle = string.IsNullOrEmpty(a.AngleChannel) ? -1 : def.ChannelIndex(a.AngleChannel);
                if (!string.IsNullOrEmpty(a.AngleChannel) && angle < 0) {
                    fail($"move '{m.Name}' arc angle channel '{a.AngleChannel}' not found");
                }
                arcList.Add(new Arc {
                    Channel = ch,
                    Slot = SlotOf(a.Group),
                    AngleChannel = angle,
                    Axis = Math.Clamp(a.Axis, 0, 1),
                    Out = Math.Clamp(a.Out, 0, 1),
                    Lift = a.Lift,
                    Max = Math.Max(a.Max, 0.0001f),
                    Sign = a.Sign,
                    Scale = a.Scale,
                    AngleAmount = a.AngleAmount,
                });
            }

            List<Accent> accentList = [];
            foreach (Move2DAccent a in m.Accents) {
                List<int> chans = [];
                List<Vector2> amounts = [];
                foreach (KeyValuePair<string, Vector2> kv in a.Channels) {
                    int ch = def.ChannelIndex(kv.Key);
                    if (ch < 0) {
                        fail($"move '{m.Name}' accent channel '{kv.Key}' not found");
                        continue;
                    }
                    chans.Add(ch);
                    amounts.Add(kv.Value);
                }
                accentList.Add(new Accent {
                    Slot = SlotOf(a.Group),
                    After = SegOf(a.After, "accent"),
                    Over = string.IsNullOrEmpty(a.Over) ? -1 : SegOf(a.Over, "accent span"),
                    Span = a.Span,
                    Curve = Rig2DEase.Parse(a.Curve),
                    Swing = string.Equals(a.Direction, "swing", StringComparison.OrdinalIgnoreCase),
                    Channels = [.. chans],
                    Amounts = [.. amounts],
                });
            }

            Rig2DMove move = new(shape, [.. arcList], [.. accentList], duration);
            foreach (KeyValuePair<string, Move2DEvent> kv in m.Events) {
                Move2DEvent e = kv.Value;
                int slot = SlotOf(e.Group);
                int seg = SegOf(e.Segment, $"event '{kv.Key}'");
                Timeline tl = move.slots[slot];
                float local = tl.Start[seg] + (e.AtEnd ? tl.Frames[seg] : 0f);
                move.events[kv.Key] = local - tl.Lead + e.Offset;
            }
            return move;
        }

        //==================== 求值 ====================

        /// <summary>
        /// 第 <paramref name="t"/> 帧（主时间）的整副通道值写进 <paramref name="into"/>；<paramref name="basePose"/> 供 <c>@base</c> 引用（可空：取通道缺省值）
        /// </summary>
        public void Sample(float t, Rig2DPose basePose, Rig2DPose into) {
            Rig2DDefinition def = Definition;
            int n = Math.Min(into.Count, def.Channels.Count);
            for (int s = 0; s < slots.Length; s++) {
                Timeline tl = slots[s];
                float tg = t + tl.Lead;
                int i = Locate(tl, tg);
                float u = tg - tl.Start[i];
                float q = arrive[i] ? (u + 1f) / tl.Frames[i] : MathF.Max(u, 0f) / tl.Frames[i];
                slotSeg[s] = i;
                slotQ[s] = q;
                slotE[s] = tl.Ease[i].Evaluate(q);
            }
            for (int c = 0; c < n; c++) {
                int s = slotOf[c];
                int i = slotSeg[s];
                into.Values[c] = Rig2DPose.LerpValue(def.Channels[c], PoseValue(segFrom[i], c, basePose), PoseValue(segTo[i], c, basePose), slotE[s]);
            }
            for (int k = 0; k < arcs.Length; k++) {
                ref Arc a = ref arcs[k];
                if (a.Channel >= n) {
                    continue;
                }
                int i = slotSeg[a.Slot];
                float q = MathHelper.Clamp(slotQ[a.Slot], 0f, 1f);
                Vector2 from = PoseValue(segFrom[i], a.Channel, basePose);
                Vector2 to = PoseValue(segTo[i], a.Channel, basePose);
                float d = a.Axis == 0 ? to.X - from.X : to.Y - from.Y;
                float arc = MathF.Sin(MathF.PI * q);
                float lift = MathF.Min(MathF.Abs(d) * a.Lift, a.Max);
                float delta = a.Sign * (lift * arc * a.Scale);
                if (a.Out == 0) {
                    into.Values[a.Channel].X += delta;
                }
                else {
                    into.Values[a.Channel].Y += delta;
                }
                if (a.AngleChannel >= 0 && a.AngleChannel < n) {
                    into.Values[a.AngleChannel].X += a.AngleAmount * MathF.Sin(MathHelper.TwoPi * q) * (lift / a.Max);
                }
            }
            for (int k = 0; k < accents.Length; k++) {
                Accent a = accents[k];
                Timeline tl = slots[a.Slot];
                float origin = tl.Start[a.After] + tl.Frames[a.After];
                float span = a.Span > 0f ? a.Span : (a.Over >= 0 ? MathF.Max(tl.Frames[a.Over], 1f) : 1f);
                float w = a.Curve.Evaluate((t + tl.Lead - origin) / span);
                if (w == 0f) {
                    continue;
                }
                for (int j = 0; j < a.Channels.Length; j++) {
                    int c = a.Channels[j];
                    if (c >= n) {
                        continue;
                    }
                    float dir = a.Swing ? SwingSign(c, a.After, basePose) : 1f;
                    Vector2 amount = a.Amounts[j];
                    into.Values[c].X += w * amount.X * dir;
                    if (!def.Channels[c].IsScalar) {
                        into.Values[c].Y += w * amount.Y * dir;
                    }
                }
            }
        }

        /// <summary>
        /// 只取某通道在第 <paramref name="t"/> 帧的值（位移曲线：两帧之差即这一帧的前冲量，消费方决定是否并入物理）
        /// </summary>
        public Vector2 SampleChannel(float t, int channel, Rig2DPose basePose = null) {
            if ((uint)channel >= (uint)Definition.Channels.Count) {
                return Vector2.Zero;
            }
            scratch ??= new Rig2DPose(Definition);
            Sample(t, basePose, scratch);
            return scratch.Values[channel];
        }

        /// <summary>
        /// 事件在主时间上的帧；缺失 NaN
        /// </summary>
        public float EventTime(string name) => events.TryGetValue(name, out float e) ? e : float.NaN;

        /// <summary>
        /// 事件是否落在 (<paramref name="from"/>, <paramref name="to"/>]
        /// </summary>
        public bool Crossed(string name, float from, float to) => events.TryGetValue(name, out float e) && from < e && e <= to;

        /// <summary>
        /// 全部事件名
        /// </summary>
        public IEnumerable<string> EventNames => events.Keys;

        /// <summary>
        /// 某段在主时间上的起始帧；缺失 NaN
        /// </summary>
        public float SegmentStart(string name) {
            int i = Array.IndexOf(segNames, name);
            return i >= 0 ? slots[0].Start[i] : float.NaN;
        }

        private static int Locate(Timeline tl, float t) {
            int last = tl.Start.Length - 1;
            for (int i = 0; i < last; i++) {
                if (t < tl.Start[i] + tl.Frames[i]) {
                    return i;
                }
            }
            return last;
        }

        private Vector2 PoseValue(int pose, int channel, Rig2DPose basePose) {
            if (pose == PoseBase) {
                return basePose != null && channel < basePose.Count ? basePose.Values[channel] : Definition.Channels[channel].Default;
            }
            Rig2DPose p = Definition.PoseValue(pose);
            return p != null && channel < p.Count ? p.Values[channel] : Definition.Channels[channel].Default;
        }

        private float SwingSign(int channel, int segment, Rig2DPose basePose) {
            float from = PoseValue(segFrom[segment], channel, basePose).X;
            float to = PoseValue(segTo[segment], channel, basePose).X;
            float d = Definition.Channels[channel].Blend == Channel2DBlend.Angle ? MathHelper.WrapAngle(to - from) : to - from;
            return MathF.Sign(d);
        }
    }
}
