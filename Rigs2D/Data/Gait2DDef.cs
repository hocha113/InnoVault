using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;

namespace InnoVault.Rigs2D.Data
{
    /// <summary>
    /// 步态里的一条腿：写哪条足目标通道（矢量，锚点系 x / y）、哪条足角通道（标量，可空），触地相位与侧向错位
    /// </summary>
    public sealed class Gait2DLeg
    {
        /// <summary>
        /// 足目标通道（矢量）
        /// </summary>
        public string Channel { get; set; } = string.Empty;
        /// <summary>
        /// 足角通道（标量，可空）
        /// </summary>
        public string Angle { get; set; }
        /// <summary>
        /// 触地相位（0~1，一个周期两步的双足为 0 / 0.5）；模式里的 <see cref="Gait2DMode.Phases"/> 优先
        /// </summary>
        public float Phase { get; set; }
        /// <summary>
        /// 乘在 <see cref="Gait2DMode.FarOffset"/> 上的错位倍率（远腿 1、近腿 0）
        /// </summary>
        public float Offset { get; set; }
        /// <summary>
        /// 摆越进度通道（标量，可空）：支撑期写 −1，摆越期写 0~1。趾行腿（<c>PawLeg</c>）据此在支撑时按肩髋铺掌、摆越时翻卷
        /// </summary>
        public string Swing { get; set; }
        /// <summary>
        /// 程序化权重通道（标量，可空）：运动层按权重写 1，让趾行腿的掌骨 / 趾角改由求解器现算（姿态里写 0 = 用姿态给的角）
        /// </summary>
        public string Auto { get; set; }

        internal int ChannelIndex = -1;
        internal int AngleIndex = -1;
        internal int SwingIndex = -1;
        internal int AutoIndex = -1;

        /// <summary>
        /// 深拷贝（不含解析结果）
        /// </summary>
        public Gait2DLeg Clone() => new() { Channel = Channel, Angle = Angle, Phase = Phase, Offset = Offset, Swing = Swing, Auto = Auto };
    }

    /// <summary>
    /// 随步态相位的通道波动：<c>amp × cos(2π × cycles × (φ − offset))</c> 按运动层权重加进某条通道。
    /// 四足疾驰的脊柱屈伸、头颈反向稳住、尾巴与耳朵的甩动都用它写，与腿同一个相位，不会跑拍
    /// </summary>
    public sealed class Gait2DWave
    {
        /// <summary>
        /// 目标通道（标量取 <see cref="Amplitude"/>.X，矢量两个分量都用）
        /// </summary>
        public string Channel { get; set; } = string.Empty;
        /// <summary>
        /// 幅度
        /// </summary>
        public Vector2 Amplitude { get; set; }
        /// <summary>
        /// 相位偏移（周期比例，波峰所在相位）
        /// </summary>
        public float Offset { get; set; }
        /// <summary>
        /// 每个步态周期几个波（疾驰的脊柱 1、小跑的点头 2）
        /// </summary>
        public float Cycles { get; set; } = 1f;
        /// <summary>
        /// 幅度按体速渐入（体速 / <see cref="Gait2DDef.BobSpeed"/>，封顶 1）
        /// </summary>
        public bool SpeedScaled { get; set; }

        internal int ChannelIndex = -1;

        /// <summary>
        /// 深拷贝（不含解析结果）
        /// </summary>
        public Gait2DWave Clone() => new() { Channel = Channel, Amplitude = Amplitude, Offset = Offset, Cycles = Cycles, SpeedScaled = SpeedScaled };
    }

    /// <summary>
    /// 一档步态参数（走 / 跑 / 小跑…），字段缺省取一套重步慢行的双足参数；多档之间按混合权重逐项插值
    /// </summary>
    public sealed class Gait2DMode
    {
        /// <summary>
        /// 档名
        /// </summary>
        public string Name { get; set; } = string.Empty;
        /// <summary>
        /// 支撑期占周期的比例
        /// </summary>
        public float Stance { get; set; } = 0.62f;
        /// <summary>
        /// 一整个周期（两步）的步幅，按步幅定周期时用
        /// </summary>
        public float Stride { get; set; } = 560f;
        /// <summary>
        /// 周期帧数下限
        /// </summary>
        public float CycleMin { get; set; } = 56f;
        /// <summary>
        /// 周期帧数上限（静止时取它）
        /// </summary>
        public float CycleMax { get; set; } = 120f;
        /// <summary>
        /// 按体速插周期：体速 <see cref="SpeedSlow"/> 时 <see cref="CycleMax"/>、<see cref="SpeedFast"/> 时 <see cref="CycleMin"/>（两者都为 0 时改按步幅定周期）
        /// </summary>
        public float SpeedSlow { get; set; }
        /// <summary>
        /// 见 <see cref="SpeedSlow"/>
        /// </summary>
        public float SpeedFast { get; set; }
        /// <summary>
        /// 摆越期抬脚高度
        /// </summary>
        public float Lift { get; set; } = 64f;
        /// <summary>
        /// 盆骨起伏幅度
        /// </summary>
        public float Bob { get; set; } = 12f;
        /// <summary>
        /// 起伏波的相位偏移；<see cref="float.NaN"/> = 支撑中段（<see cref="Stance"/> × 0.5）
        /// </summary>
        public float BobOffset { get; set; }
        /// <summary>
        /// 起伏波的幂（大于 1 让低点更尖）
        /// </summary>
        public float BobPow { get; set; } = 1f;
        /// <summary>
        /// 起伏常量偏移
        /// </summary>
        public float BobBase { get; set; }
        /// <summary>
        /// 前倾
        /// </summary>
        public float Lean { get; set; } = 0.06f;
        /// <summary>
        /// 俯仰波幅度（sin(4π(φ − 偏移))，四足点头用）
        /// </summary>
        public float LeanWave { get; set; }
        /// <summary>
        /// 俯仰波相位偏移
        /// </summary>
        public float LeanWaveOffset { get; set; }
        /// <summary>
        /// 起伏与前倾按体速渐入（体速 / <see cref="Gait2DDef.BobSpeed"/>，封顶 1）
        /// </summary>
        public bool SpeedScaled { get; set; } = true;
        /// <summary>
        /// 落脚点前伸（支撑段以此为中心前后各半个行程；<see cref="Centered"/> 关时即触地点）
        /// </summary>
        public float ReachBias { get; set; } = 14f;
        /// <summary>
        /// 逐腿落脚点（覆盖 <see cref="ReachBias"/>）
        /// </summary>
        public float[] Reach { get; set; }
        /// <summary>
        /// 逐腿抬脚高度（覆盖 <see cref="Lift"/>）
        /// </summary>
        public float[] Lifts { get; set; }
        /// <summary>
        /// 逐腿触地相位（覆盖腿上的 <see cref="Gait2DLeg.Phase"/>）
        /// </summary>
        public float[] Phases { get; set; }
        /// <summary>
        /// 支撑段以落脚点为中心（真：前后各半个行程；假：从落脚点往后退一整个行程）
        /// </summary>
        public bool Centered { get; set; } = true;
        /// <summary>
        /// 远腿错位
        /// </summary>
        public float FarOffset { get; set; } = -18f;
        /// <summary>
        /// 摆越抬脚峰的偏斜（小于 1 峰值提前）
        /// </summary>
        public float Skew { get; set; } = 1f;
        /// <summary>
        /// 蹬离后脚尖下压角
        /// </summary>
        public float Toe { get; set; } = 0.55f;
        /// <summary>
        /// 随相位的通道波动（脊柱屈伸、头颈稳定、甩尾）；多档混合时各档按混合权重分别加
        /// </summary>
        public List<Gait2DWave> Waves { get; set; } = [];

        /// <summary>
        /// 深拷贝
        /// </summary>
        public Gait2DMode Clone() {
            Gait2DMode c = (Gait2DMode)MemberwiseClone();
            c.Reach = (float[])Reach?.Clone();
            c.Lifts = (float[])Lifts?.Clone();
            c.Phases = (float[])Phases?.Clone();
            c.Waves = [];
            foreach (Gait2DWave w in Waves) {
                c.Waves.Add(w.Clone());
            }
            return c;
        }
    }

    /// <summary>
    /// 一套运动层定义：腿、盆骨起伏 / 前倾写哪条通道、各档参数、足形常数与地形规则
    /// </summary>
    public sealed class Gait2DDef
    {
        /// <summary>
        /// 定义名
        /// </summary>
        public string Name { get; set; } = string.Empty;
        private List<Gait2DLeg> legs = [];
        private List<Gait2DMode> modes = [];

        /// <summary>
        /// 腿
        /// </summary>
        public List<Gait2DLeg> Legs => legs;
        /// <summary>
        /// 档（混合权重 0 = 第一档，1 = 第二档…）
        /// </summary>
        public List<Gait2DMode> Modes => modes;
        /// <summary>
        /// 盆骨起伏写进的通道（矢量取 <see cref="HipAxis"/> 分量，标量直接加）
        /// </summary>
        public string Hip { get; set; }
        /// <summary>
        /// 盆骨起伏写进矢量通道的哪个分量（0 = x，1 = y）
        /// </summary>
        public int HipAxis { get; set; } = 1;
        /// <summary>
        /// 前倾写进的通道（标量）
        /// </summary>
        public string Tilt { get; set; }
        /// <summary>
        /// 支撑初段脚跟着地的脚角
        /// </summary>
        public float Heel { get; set; } = -0.22f;
        /// <summary>
        /// 脚跟着地段占支撑期比例
        /// </summary>
        public float HeelSpan { get; set; } = 0.12f;
        /// <summary>
        /// 蹬离起点（支撑期比例）
        /// </summary>
        public float ToeOffStart { get; set; } = 0.78f;
        /// <summary>
        /// 蹬离段长度（支撑期比例，自 <see cref="ToeOffStart"/> 起）
        /// </summary>
        public float ToeOffSpan { get; set; } = 0.22f;
        /// <summary>
        /// 蹬离时的脚角
        /// </summary>
        public float ToeOff { get; set; } = 0.32f;
        /// <summary>
        /// 摆越末段（落地前）的脚角
        /// </summary>
        public float SwingToe { get; set; } = -0.2f;
        /// <summary>
        /// 抬脚曲线的幂
        /// </summary>
        public float LiftPow { get; set; } = 0.8f;
        /// <summary>
        /// 抬脚曲线的时间拉伸（大于 1 提前落地、末段贴地前送）
        /// </summary>
        public float LiftStretch { get; set; } = 1.15f;
        /// <summary>
        /// 起伏 / 前倾满额的体速
        /// </summary>
        public float BobSpeed { get; set; } = 8f;
        /// <summary>
        /// 相位推进用的周期帧数下限
        /// </summary>
        public float MinCycle { get; set; } = 8f;
        /// <summary>
        /// 地形：盆骨跟着低的那只脚沉的比例
        /// </summary>
        public float SinkLow { get; set; } = 0.6f;
        /// <summary>
        /// 地形：盆骨按两脚平均高差抬起的比例（只要平均是高的就抬，一脚踩上台阶也会抬）
        /// </summary>
        public float RaiseBoth { get; set; } = 1f;
        /// <summary>
        /// 地形：两脚都比锚点高时，盆骨按较低那只脚的高差抬起的比例（只踩上一只脚不抬，后腿不会被拽直）
        /// </summary>
        public float RaiseLow { get; set; }
        /// <summary>
        /// 地形：探地上沿（能迈上的台阶高，骨架单位）
        /// </summary>
        public float StepUp { get; set; } = 48f;
        /// <summary>
        /// 地形：探地下沿（骨架单位）
        /// </summary>
        public float StepDown { get; set; } = 160f;
        /// <summary>
        /// 支撑脚钉在世界里：支撑期足目标只按实际体速后退，摆越从离地点送到落点。
        /// 关（缺省）时足目标按当前体速现算，匀速时两者逐位相同，但体速一变支撑脚就在地上滑（周期随体速变的档滑得最明显）
        /// </summary>
        public bool LockStance { get; set; }
        /// <summary>
        /// 抬脚按这一步的实际跨距缩（跨距小于此值时按平滑比例压低，0 = 不缩）：原地收步、小碎步不抬高脚。只在 <see cref="LockStance"/> 下生效
        /// </summary>
        public float LiftDistance { get; set; }

        internal int HipIndex = -1;
        internal int TiltIndex = -1;

        /// <summary>
        /// 深拷贝（不含解析结果）
        /// </summary>
        public Gait2DDef Clone() {
            Gait2DDef c = (Gait2DDef)MemberwiseClone();
            c.HipIndex = -1;
            c.TiltIndex = -1;
            c.legs = [];
            c.modes = [];
            foreach (Gait2DLeg l in Legs) {
                c.legs.Add(l.Clone());
            }
            foreach (Gait2DMode m in Modes) {
                c.modes.Add(m.Clone());
            }
            return c;
        }

        /// <summary>
        /// 按名取档下标，缺失 <c>-1</c>
        /// </summary>
        public int ModeIndex(string name) {
            for (int i = 0; i < Modes.Count; i++) {
                if (string.Equals(Modes[i].Name, name, StringComparison.Ordinal)) {
                    return i;
                }
            }
            return -1;
        }
    }
}
