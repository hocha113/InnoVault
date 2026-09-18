using Microsoft.Xna.Framework;
using System;

namespace InnoVault.Vectors
{
    /// <summary>
    /// 一条基础折线及其按种子抖动后的副本：基线可逐帧改写，抖动只在 <see cref="Reroll()"/> / <see cref="Refresh"/> 时重算，
    /// 因此形状在两次重掷之间保持不变（不会逐帧闪烁）
    /// <br/>抖动规则即 <see cref="VectorPathGenerators.Jitter(ReadOnlySpan{Vector2}, Span{Vector2}, float, int, bool, float)"/>：沿局部法线偏移 <see cref="Amplitude"/>，再叠半径不超过 <see cref="Spread"/> 的各向同性偏移
    /// <br/>纯本地表现层数据，不参与网络同步；<see cref="Reroll()"/> 取库内私有 <see cref="Random"/>（不碰 <c>Main.rand</c>），需要两端一致时用 <see cref="Reroll(int)"/>
    /// </summary>
    public sealed class JitterPath
    {
        //只给无参 Reroll 用：表现层随机，不影响逻辑与同步
        private static readonly Random rand = new();

        private Vector2[] baseline = [];
        private Vector2[] jittered = [];
        private int count;

        /// <summary>沿法线的偏移幅度</summary>
        public float Amplitude { get; set; }
        /// <summary>各向同性扩散半径（0 = 关），让分段沿线方向也不等长</summary>
        public float Spread { get; set; }
        /// <summary>两端是否固定不动</summary>
        public bool PinEnds { get; set; } = true;
        /// <summary>当前抖动用的种子</summary>
        public int Seed { get; private set; }
        /// <summary>当前点数</summary>
        public int Count => count;
        /// <summary>基础点列</summary>
        public ReadOnlySpan<Vector2> Base => new(baseline, 0, count);
        /// <summary>抖动后的点列，直接喂 <c>DrawStroke</c> / <see cref="VectorPen"/>；未重掷过时等于 <see cref="Base"/></summary>
        public ReadOnlySpan<Vector2> Points => new(jittered, 0, count);

        /// <summary>创建一条空的抖动点列</summary>
        public JitterPath() { }

        /// <summary>创建一条空的抖动点列并指定抖动参数</summary>
        public JitterPath(float amplitude, float spread = 0f, bool pinEnds = true) {
            Amplitude = amplitude;
            Spread = spread;
            PinEnds = pinEnds;
        }

        /// <summary>
        /// 覆写基线（复制一份；长度变化时内部数组扩容）。不重算抖动：抖动点列长度随基线同步，
        /// 但新增出来的点在下一次 <see cref="Reroll()"/> / <see cref="Refresh"/> 前取基线值（生长中的折线因此不会整条闪）
        /// </summary>
        public void SetBase(ReadOnlySpan<Vector2> points) {
            int n = points.Length;
            if (n > baseline.Length) {
                int capacity = Math.Max(n, Math.Max(baseline.Length * 2, 8));
                Array.Resize(ref baseline, capacity);
                Array.Resize(ref jittered, capacity);
            }
            for (int i = count; i < n; i++) {
                jittered[i] = points[i];
            }
            points.CopyTo(baseline);
            count = n;
        }

        /// <summary>用新种子重算抖动（种子来自库内私有随机）</summary>
        public void Reroll() => Reroll(rand.Next());

        /// <summary>用指定种子重算抖动（需要两端一致或可复现时用）</summary>
        public void Reroll(int seed) {
            Seed = seed;
            Refresh();
        }

        /// <summary>用当前种子按当前基线重算抖动：形状随基线移动但随机相位不变</summary>
        public void Refresh() => VectorPathGenerators.Jitter(Base, new Span<Vector2>(jittered, 0, count), Amplitude, Seed, PinEnds, Spread);

        /// <summary>基线与抖动点列整体平移</summary>
        public void Translate(Vector2 delta) {
            for (int i = 0; i < count; i++) {
                baseline[i] += delta;
                jittered[i] += delta;
            }
        }

        /// <summary>清空点列（不释放缓冲，种子保留）</summary>
        public void Clear() => count = 0;
    }
}
