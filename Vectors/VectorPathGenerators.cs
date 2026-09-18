using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;

namespace InnoVault.Vectors
{
    /// <summary>
    /// 程序化点列生成：闪电折线、对既有点列做抖动、按最大段长细分
    /// <br/>全部使用确定性哈希，不碰 <c>Main.rand</c>：同一 seed 两端结果一致，逐帧换 seed 即得闪烁
    /// </summary>
    public static class VectorPathGenerators
    {
        /// <summary>确定性哈希，返回 [-1, 1]</summary>
        public static float Hash(int index, int seed) {
            uint h = (uint)(index * 374761393) ^ (uint)(seed * 668265263);
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return (h & 0xFFFF) / 32767.5f - 1f;
        }

        /// <summary>
        /// 生成一条 <paramref name="from"/> → <paramref name="to"/> 的闪电折线：内部点沿垂直方向随机偏移，两端固定
        /// </summary>
        /// <param name="from">起点</param>
        /// <param name="to">终点</param>
        /// <param name="segments">段数（点数 = 段数 + 1，至少 2 段）</param>
        /// <param name="amplitude">垂直偏移的最大幅度</param>
        /// <param name="seed">随机种子，逐帧或每 N tick 换一次即闪烁</param>
        /// <param name="output">输出点列，长度至少 segments + 1</param>
        /// <param name="taper">两端衰减：1 = 中间偏移最大、两端趋零；0 = 全程等幅</param>
        /// <param name="alongJitter">沿线方向的随机抖动比例（0~0.5），让分段不等长</param>
        /// <returns>实际写入的点数</returns>
        public static int Lightning(Vector2 from, Vector2 to, int segments, float amplitude, int seed, Span<Vector2> output, float taper = 0.5f, float alongJitter = 0.2f) {
            segments = Math.Max(segments, 2);
            int count = Math.Min(segments + 1, output.Length);
            if (count < 2) {
                return 0;
            }
            Vector2 dir = to - from;
            float len = dir.Length();
            if (len < 1e-6f) {
                for (int i = 0; i < count; i++) {
                    output[i] = from;
                }
                return count;
            }
            dir /= len;
            Vector2 nrm = new(-dir.Y, dir.X);
            alongJitter = MathHelper.Clamp(alongJitter, 0f, 0.5f);
            for (int i = 0; i < count; i++) {
                float f = i / (float)(count - 1);
                if (i > 0 && i < count - 1) {
                    f += Hash(i * 2 + 1, seed) * alongJitter / (count - 1);
                }
                float env = MathHelper.Lerp(1f, MathF.Sin(f * MathHelper.Pi), MathHelper.Clamp(taper, 0f, 1f));
                float side = i == 0 || i == count - 1 ? 0f : Hash(i * 2, seed) * amplitude * env;
                output[i] = from + dir * (len * f) + nrm * side;
            }
            return count;
        }

        /// <summary>生成闪电折线（分配新数组）</summary>
        public static Vector2[] Lightning(Vector2 from, Vector2 to, int segments, float amplitude, int seed, float taper = 0.5f, float alongJitter = 0.2f) {
            Vector2[] pts = new Vector2[Math.Max(segments, 2) + 1];
            Lightning(from, to, segments, amplitude, seed, pts, taper, alongJitter);
            return pts;
        }

        /// <summary>
        /// 对点列做随机偏移：每个点沿其局部法线偏移 <paramref name="amplitude"/> × [-1, 1]
        /// </summary>
        /// <param name="source">原点列</param>
        /// <param name="destination">输出，长度不小于原点列</param>
        /// <param name="amplitude">偏移幅度</param>
        /// <param name="seed">随机种子</param>
        /// <param name="pinEnds">两端是否固定不动</param>
        /// <returns>写入的点数</returns>
        public static int Jitter(ReadOnlySpan<Vector2> source, Span<Vector2> destination, float amplitude, int seed, bool pinEnds = true) {
            int n = Math.Min(source.Length, destination.Length);
            for (int i = 0; i < n; i++) {
                Vector2 p = source[i];
                if (pinEnds && (i == 0 || i == n - 1) || n < 2) {
                    destination[i] = p;
                    continue;
                }
                Vector2 prev = source[Math.Max(i - 1, 0)];
                Vector2 next = source[Math.Min(i + 1, n - 1)];
                Vector2 tangent = next - prev;
                if (tangent.LengthSquared() < 1e-12f) {
                    destination[i] = p;
                    continue;
                }
                tangent.Normalize();
                Vector2 nrm = new(-tangent.Y, tangent.X);
                destination[i] = p + nrm * (Hash(i, seed) * amplitude);
            }
            return n;
        }

        /// <summary>对点列做随机偏移（分配新数组）</summary>
        public static Vector2[] Jitter(ReadOnlySpan<Vector2> source, float amplitude, int seed, bool pinEnds = true) {
            Vector2[] dst = new Vector2[source.Length];
            Jitter(source, dst, amplitude, seed, pinEnds);
            return dst;
        }

        /// <summary>
        /// 把点列中超过 <paramref name="maxLength"/> 的段等分插点，结果写入 <paramref name="output"/>（先清空）
        /// </summary>
        /// <returns>输出点数</returns>
        public static int Subdivide(ReadOnlySpan<Vector2> points, float maxLength, List<Vector2> output, bool closed = false) {
            output.Clear();
            int n = points.Length;
            if (n == 0) {
                return 0;
            }
            maxLength = MathF.Max(maxLength, 1e-3f);
            int segCount = closed ? n : n - 1;
            for (int i = 0; i < segCount; i++) {
                Vector2 a = points[i];
                Vector2 b = points[(i + 1) % n];
                output.Add(a);
                int parts = (int)MathF.Ceiling(Vector2.Distance(a, b) / maxLength);
                for (int k = 1; k < parts; k++) {
                    output.Add(Vector2.Lerp(a, b, k / (float)parts));
                }
            }
            if (!closed) {
                output.Add(points[n - 1]);
            }
            return output.Count;
        }
    }

    /// <summary>
    /// 位置历史环形缓冲：每 tick <see cref="Push"/> 一次当前位置，<see cref="AsSpan"/> 给出「旧 → 新」的连续点列直接喂给描边
    /// <br/>给没有 <c>oldPos</c> 的对象（NPC 部件、渲染点、UI 光标）做拖尾；<c>points[0]</c> 是最旧、<c>points[^1]</c> 是最新，
    /// 配合 <see cref="StrokeStyle.Parameterization"/> = <see cref="StrokeParameterization.PointIndex"/> 与 <see cref="StrokeStyle.EndCap"/> 即旧 Trail 的画法
    /// <br/>纯本地表现层数据，不参与网络同步
    /// </summary>
    public sealed class TrailHistory
    {
        private readonly Vector2[] ring;
        private Vector2[] linear;
        private int head;
        private bool linearDirty = true;

        /// <summary>容量（最多保留多少个历史点）</summary>
        public int Capacity => ring.Length;
        /// <summary>当前已记录的点数</summary>
        public int Count { get; private set; }
        /// <summary>最新的点；为空时返回 <see cref="Vector2.Zero"/></summary>
        public Vector2 Newest => Count == 0 ? Vector2.Zero : ring[(head - 1 + ring.Length) % ring.Length];
        /// <summary>最旧的点；为空时返回 <see cref="Vector2.Zero"/></summary>
        public Vector2 Oldest => Count == 0 ? Vector2.Zero : ring[(head - Count + ring.Length) % ring.Length];

        /// <summary>创建容量为 <paramref name="capacity"/> 的历史缓冲</summary>
        public TrailHistory(int capacity) {
            ring = new Vector2[Math.Max(capacity, 2)];
            linear = new Vector2[ring.Length];
        }

        /// <summary>记录一个新位置（满了就顶掉最旧的）</summary>
        public void Push(Vector2 position) {
            ring[head] = position;
            head = (head + 1) % ring.Length;
            if (Count < ring.Length) {
                Count++;
            }
            linearDirty = true;
        }

        /// <summary>只有与最新点距离超过 <paramref name="minDistance"/> 才记录；静止时不会把历史点堆在一处</summary>
        /// <returns>是否记录了</returns>
        public bool PushIfMoved(Vector2 position, float minDistance) {
            if (Count > 0 && Vector2.DistanceSquared(Newest, position) < minDistance * minDistance) {
                return false;
            }
            Push(position);
            return true;
        }

        /// <summary>用同一位置填满整个缓冲（生成瞬间避免拖尾从原点拉出一条线）</summary>
        public void Fill(Vector2 position) {
            Array.Fill(ring, position);
            head = 0;
            Count = ring.Length;
            linearDirty = true;
        }

        /// <summary>清空</summary>
        public void Clear() {
            Count = 0;
            head = 0;
            linearDirty = true;
        }

        /// <summary>取「旧 → 新」顺序的连续点列（内部线性化缓存，下一次 <see cref="Push"/> 前有效）</summary>
        public ReadOnlySpan<Vector2> AsSpan() {
            if (linearDirty) {
                int start = (head - Count + ring.Length) % ring.Length;
                for (int i = 0; i < Count; i++) {
                    linear[i] = ring[(start + i) % ring.Length];
                }
                linearDirty = false;
            }
            return new ReadOnlySpan<Vector2>(linear, 0, Count);
        }

        /// <summary>把「旧 → 新」的点列拷到 <paramref name="destination"/></summary>
        /// <returns>拷贝的点数</returns>
        public int CopyTo(Span<Vector2> destination) {
            ReadOnlySpan<Vector2> src = AsSpan();
            int n = Math.Min(src.Length, destination.Length);
            src[..n].CopyTo(destination);
            return n;
        }
    }
}
