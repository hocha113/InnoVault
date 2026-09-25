using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;

namespace InnoVault.Rigs2D.Runtime
{
    /// <summary>
    /// 一帧的刚体线段（世界）：尾 → 尖。枪、刀、尾巴末节都可以
    /// </summary>
    public readonly struct Rig2DSweepLine
    {
        /// <summary>
        /// 尾端
        /// </summary>
        public readonly Vector2 Butt;
        /// <summary>
        /// 尖端
        /// </summary>
        public readonly Vector2 Tip;

        /// <summary>
        /// 建一条线段
        /// </summary>
        public Rig2DSweepLine(Vector2 butt, Vector2 tip) {
            Butt = butt;
            Tip = tip;
        }

        /// <summary>
        /// 取一根骨的近端 → 尖端
        /// </summary>
        public static Rig2DSweepLine FromBone(in Bone2D bone) => new(bone.Pos, bone.Tip);
    }

    /// <summary>
    /// 骨扫掠：刚体线段的帧间插值。巨物一记爆发里尖端一帧能扫过两三百像素，逐帧取点连线就是一圈多边形，判定也会从目标身边跳过去。
    /// 线段当刚体处理：在 <see cref="PivotFrac"/> 处取枢轴，枢轴位置走 Catmull-Rom，角度解缠后走单调三次插值（硬停处不回弹、过冲原样保留），
    /// 长度线性；帧间按尖端弧长补点（<see cref="DenseStep"/>）。刀光（<see cref="BuildDense(float, List{Vector2}, List{Vector2}, List{float})"/>）
    /// 与扫掠判定（<see cref="SweepIntersects"/>）共用这一份插值
    /// <br/>只读消费方推进来的线段，不碰实体；联机下两端由同输入算出同结果
    /// </summary>
    public sealed class Rig2DBoneSweep
    {
        private readonly List<Rig2DSweepLine> history = new(16);
        private readonly List<Vector2> pivots = new(16);
        private readonly List<float> angles = new(16);
        private readonly List<float> lengths = new(16);
        private readonly List<float> slopes = new(16);

        /// <summary>
        /// 枢轴在线段上的位置（自尾端占全长比）：握手一带
        /// </summary>
        public float PivotFrac { get; set; } = 0.4f;
        /// <summary>
        /// 稠密弧上尖端的点距（世界像素）
        /// </summary>
        public float DenseStep { get; set; } = 9f;
        /// <summary>
        /// 每两帧之间最多补的点数
        /// </summary>
        public int MaxSubSteps { get; set; } = 32;
        /// <summary>
        /// 历史容量（帧）
        /// </summary>
        public int Capacity { get; set; } = 8;
        /// <summary>
        /// 历史（旧 → 新）
        /// </summary>
        public IReadOnlyList<Rig2DSweepLine> History => history;
        /// <summary>
        /// 历史帧数
        /// </summary>
        public int Count => history.Count;

        /// <summary>
        /// 推入本帧线段（超出容量丢最旧的）
        /// </summary>
        public void Push(in Rig2DSweepLine line) {
            history.Add(line);
            int cap = Math.Max(Capacity, 2);
            while (history.Count > cap) {
                history.RemoveAt(0);
            }
        }

        /// <summary>
        /// 推入一根骨本帧的近端 → 尖端
        /// </summary>
        public void Push(in Bone2D bone) => Push(Rig2DSweepLine.FromBone(in bone));

        /// <summary>
        /// 推入本帧线段
        /// </summary>
        public void Push(Vector2 butt, Vector2 tip) => Push(new Rig2DSweepLine(butt, tip));

        /// <summary>
        /// 清空历史（招式结束、瞬移后）
        /// </summary>
        public void Clear() => history.Clear();

        private static float Angle(in Rig2DSweepLine l) => MathF.Atan2(l.Tip.Y - l.Butt.Y, l.Tip.X - l.Butt.X);

        private Vector2 Pivot(in Rig2DSweepLine l) => Vector2.Lerp(l.Butt, l.Tip, PivotFrac);

        private static float Length(in Rig2DSweepLine l) => Vector2.Distance(l.Butt, l.Tip);

        private void Emit(Vector2 pivot, float angle, float len, out Vector2 butt, out Vector2 tip) {
            Vector2 d = Rig2DMath.Dir(angle);
            butt = pivot - d * (len * PivotFrac);
            tip = pivot + d * (len * (1f - PivotFrac));
        }

        /// <summary>
        /// 相邻两帧之间按刚体插一条线段（枢轴线性、角度走最短路）
        /// </summary>
        public void Between(in Rig2DSweepLine a, in Rig2DSweepLine b, float t, out Vector2 butt, out Vector2 tip) {
            float a0 = Angle(a);
            float a1 = a0 + MathHelper.WrapAngle(Angle(b) - a0);
            Emit(Vector2.Lerp(Pivot(a), Pivot(b), t), MathHelper.Lerp(a0, a1, t), MathHelper.Lerp(Length(a), Length(b), t), out butt, out tip);
        }

        /// <summary>
        /// 把历史插成稠密弧：输出尖端、线段上 <paramref name="midFrac"/> 处两条轨迹，以及每点的新旧 <paramref name="fresh"/>（最新一点为 1）
        /// </summary>
        public void BuildDense(float midFrac, List<Vector2> tips, List<Vector2> mids, List<float> fresh) => BuildDense(history, midFrac, tips, mids, fresh);

        /// <summary>
        /// 同上，线段历史由调用方给
        /// </summary>
        public void BuildDense(IReadOnlyList<Rig2DSweepLine> lines, float midFrac, List<Vector2> tips, List<Vector2> mids, List<float> fresh) {
            tips?.Clear();
            mids?.Clear();
            fresh?.Clear();
            int n = lines?.Count ?? 0;
            if (n == 0) {
                return;
            }
            Prepare(lines);
            for (int k = 0; k < n - 1; k++) {
                int steps = Steps(lines, k);
                for (int j = 0; j < steps; j++) {
                    float t = j / (float)steps;
                    Sample(k, t, n, out Vector2 pivot, out float ang, out float len);
                    Append(pivot, ang, len, midFrac, (k + t + 1f) / n, tips, mids, fresh);
                }
            }
            Append(pivots[n - 1], angles[n - 1], lengths[n - 1], midFrac, 1f, tips, mids, fresh);
        }

        /// <summary>
        /// 最近一帧（倒数第二条 → 最后一条）扫过的区域是否与矩形相交：按尖端弧长补的每条插值线段都当胶囊测，
        /// 胶囊取线段上 [<paramref name="fromFrac"/>, 1] 那一截（只算刃段时给 0.6 一类）
        /// </summary>
        /// <param name="rect">世界矩形</param>
        /// <param name="radius">胶囊半径（世界像素）</param>
        /// <param name="fromFrac">刃段起点（占线段长比例，0 = 尾端）</param>
        /// <param name="hitT">命中那条插值线段在帧间的进度（0 = 上一帧，1 = 本帧）</param>
        public bool SweepIntersects(Rectangle rect, float radius, float fromFrac, out float hitT) {
            hitT = 0f;
            int n = history.Count;
            if (n == 0) {
                return false;
            }
            if (n == 1) {
                Rig2DSweepLine only = history[0];
                return Rig2DHit.CapsuleIntersects(Vector2.Lerp(only.Butt, only.Tip, fromFrac), only.Tip, radius, rect);
            }
            Prepare(history);
            int k = n - 2;
            int steps = Steps(history, k);
            for (int j = 1; j <= steps; j++) {
                float t = j / (float)steps;
                Sample(k, t, n, out Vector2 pivot, out float ang, out float len);
                Emit(pivot, ang, len, out Vector2 butt, out Vector2 tip);
                if (Rig2DHit.CapsuleIntersects(Vector2.Lerp(butt, tip, fromFrac), tip, radius, rect)) {
                    hitT = t;
                    return true;
                }
            }
            return false;
        }

        private void Prepare(IReadOnlyList<Rig2DSweepLine> lines) {
            int n = lines.Count;
            pivots.Clear();
            angles.Clear();
            lengths.Clear();
            float prev = 0f;
            for (int k = 0; k < n; k++) {
                Rig2DSweepLine l = lines[k];
                float a = Angle(l);
                if (k > 0) {
                    a = prev + MathHelper.WrapAngle(a - prev);
                }
                prev = a;
                pivots.Add(Pivot(l));
                angles.Add(a);
                lengths.Add(Length(l));
            }
            MonotoneSlopes(angles, slopes);
        }

        private int Steps(IReadOnlyList<Rig2DSweepLine> lines, int k) {
            float arc = MathF.Abs(angles[k + 1] - angles[k]) * MathF.Max(lengths[k], lengths[k + 1]) * (1f - PivotFrac);
            float travel = MathF.Max(Vector2.Distance(lines[k].Tip, lines[k + 1].Tip), arc);
            return Math.Clamp((int)MathF.Ceiling(travel / Math.Max(DenseStep, 0.5f)), 1, Math.Max(MaxSubSteps, 1));
        }

        private void Sample(int k, float t, int n, out Vector2 pivot, out float ang, out float len) {
            Vector2 p0 = pivots[Math.Max(k - 1, 0)];
            Vector2 p1 = pivots[k];
            Vector2 p2 = pivots[k + 1];
            Vector2 p3 = pivots[Math.Min(k + 2, n - 1)];
            pivot = Vector2.CatmullRom(p0, p1, p2, p3, t);
            ang = Hermite(angles[k], angles[k + 1], slopes[k], slopes[k + 1], t);
            len = MathHelper.Lerp(lengths[k], lengths[k + 1], t);
        }

        private void Append(Vector2 pivot, float angle, float len, float midFrac, float age,
            List<Vector2> tips, List<Vector2> mids, List<float> fresh) {
            Emit(pivot, angle, len, out Vector2 butt, out Vector2 tip);
            tips?.Add(tip);
            mids?.Add(Vector2.Lerp(butt, tip, midFrac));
            fresh?.Add(age);
        }

        /// <summary>单调三次切线（Fritsch–Carlson）：相邻两段反向的点切线归零，硬停到位不会甩出一截假回弹</summary>
        private static void MonotoneSlopes(List<float> y, List<float> m) {
            m.Clear();
            int n = y.Count;
            if (n < 2) {
                m.Add(0f);
                return;
            }
            for (int k = 0; k < n; k++) {
                float dPrev = k > 0 ? y[k] - y[k - 1] : y[1] - y[0];
                float dNext = k < n - 1 ? y[k + 1] - y[k] : y[k] - y[k - 1];
                m.Add(dPrev * dNext <= 0f ? 0f : (dPrev + dNext) * 0.5f);
            }
            for (int k = 0; k < n - 1; k++) {
                float d = y[k + 1] - y[k];
                if (MathF.Abs(d) < 1e-6f) {
                    m[k] = 0f;
                    m[k + 1] = 0f;
                    continue;
                }
                float a = m[k] / d;
                float b = m[k + 1] / d;
                float r = a * a + b * b;
                if (r > 9f) {
                    float tau = 3f / MathF.Sqrt(r);
                    m[k] = tau * a * d;
                    m[k + 1] = tau * b * d;
                }
            }
        }

        private static float Hermite(float y0, float y1, float m0, float m1, float t) {
            float t2 = t * t;
            float t3 = t2 * t;
            return (2f * t3 - 3f * t2 + 1f) * y0 + (t3 - 2f * t2 + t) * m0 + (-2f * t3 + 3f * t2) * y1 + (t3 - t2) * m1;
        }
    }
}
