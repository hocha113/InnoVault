using System;

namespace InnoVault.Videos
{
    /// <summary>
    /// <see cref="VaultVideoPlayer"/> 每帧取帧调用的主线程耗时统计
    /// <br/>FNA 的解码在取帧调用里同步进行：到了该出新帧的时刻才解码并上传纹理，其余帧只返回上一帧
    /// <br/>所以分开记两类调用：解码调用（真正的每帧成本）与空转调用；首帧单独记，它含着色器首次绘制的开销
    /// </summary>
    public sealed class VideoPlaybackStats
    {
        private const int Window = 300;
        private readonly double[] recent = new double[Window];
        private readonly double[] sortScratch = new double[Window];
        private int recentCount;
        private int recentHead;
        private double decodeSum;
        private double idleSum;

        /// <summary>首帧（含着色器首次绘制）的耗时，毫秒；还没出帧时为 0</summary>
        public double FirstDecodeMs { get; private set; }
        /// <summary>首帧之后解码出的帧数</summary>
        public int DecodedFrames { get; private set; }
        /// <summary>没有解码新帧的取帧调用次数</summary>
        public int IdleCalls { get; private set; }
        /// <summary>循环播放回到开头的次数</summary>
        public int Loops { get; internal set; }
        /// <summary>最近一次解码调用的耗时，毫秒</summary>
        public double LastDecodeMs { get; private set; }
        /// <summary>首帧之后解码调用的最大耗时，毫秒</summary>
        public double MaxDecodeMs { get; private set; }
        /// <summary>首帧之后解码调用的平均耗时，毫秒</summary>
        public double AverageDecodeMs => DecodedFrames == 0 ? 0 : decodeSum / DecodedFrames;
        /// <summary>空转调用的平均耗时，毫秒</summary>
        public double AverageIdleMs => IdleCalls == 0 ? 0 : idleSum / IdleCalls;

        /// <summary>最近 300 次解码调用耗时的 95 分位，毫秒（按需排序计算，供调试显示）</summary>
        public double P95DecodeMs {
            get {
                if (recentCount == 0) {
                    return 0;
                }
                Array.Copy(recent, sortScratch, recentCount);
                Array.Sort(sortScratch, 0, recentCount);
                return sortScratch[Math.Min(recentCount - 1, (int)(recentCount * 0.95))];
            }
        }

        internal void Record(double milliseconds, bool decoded) {
            if (!decoded) {
                IdleCalls++;
                idleSum += milliseconds;
                return;
            }
            LastDecodeMs = milliseconds;
            if (FirstDecodeMs == 0) {
                FirstDecodeMs = Math.Max(milliseconds, double.Epsilon);
                return;
            }
            DecodedFrames++;
            decodeSum += milliseconds;
            MaxDecodeMs = Math.Max(MaxDecodeMs, milliseconds);
            recent[recentHead] = milliseconds;
            recentHead = (recentHead + 1) % Window;
            recentCount = Math.Min(recentCount + 1, Window);
        }

        internal void Reset() {
            FirstDecodeMs = 0;
            DecodedFrames = 0;
            IdleCalls = 0;
            Loops = 0;
            LastDecodeMs = 0;
            MaxDecodeMs = 0;
            decodeSum = 0;
            idleSum = 0;
            recentCount = 0;
            recentHead = 0;
        }

        /// <inheritdoc/>
        public override string ToString()
            => $"decode avg {AverageDecodeMs:0.00} ms, p95 {P95DecodeMs:0.00}, max {MaxDecodeMs:0.00} over {DecodedFrames} frames; first {FirstDecodeMs:0.00} ms; idle avg {AverageIdleMs:0.000} ms";
    }
}
