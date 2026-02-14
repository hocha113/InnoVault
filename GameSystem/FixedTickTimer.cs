using System.Diagnostics;

namespace InnoVault.GameSystem
{
    /// <summary>
    /// 固定步长时间累积器，用于在非固定频率的更新循环（如绘制线程）中模拟稳定的逻辑更新频率<br/>
    /// 典型用途是在主菜单等只有绘制线程可用的场景下，以固定60tick/s的频率驱动逻辑更新<br/>
    /// 原理与 Unity 的 FixedUpdate 相同：每次调用 <see cref="Update"/> 时，累积自上次调用以来的真实时间，<br/>
    /// 当累积时间达到固定步长时，返回 <see langword="true"/> 表示应执行一次逻辑更新
    /// </summary>
    public sealed class FixedTickTimer
    {
        /// <summary>
        /// 固定步长的时间间隔（秒）
        /// </summary>
        public readonly double FixedDeltaTime;
        /// <summary>
        /// 最大允许的帧间隔累积量（秒），防止窗口拖拽等导致的时间暴涨
        /// </summary>
        public readonly double MaxElapsed;

        private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
        private double _accumulator;

        /// <summary>
        /// 创建一个固定步长时间累积器
        /// </summary>
        /// <param name="ticksPerSecond">每秒的逻辑更新次数，默认为60</param>
        /// <param name="maxElapsed">最大允许的帧间隔累积量（秒），默认为0.25秒（即最多补15帧）</param>
        public FixedTickTimer(double ticksPerSecond = 60.0, double maxElapsed = 0.25) {
            FixedDeltaTime = 1.0 / ticksPerSecond;
            MaxElapsed = maxElapsed;
        }

        /// <summary>
        /// 消费自上次调用以来的真实时间并累积到内部计时器<br/>
        /// 之后应在循环中调用 <see cref="Tick"/> 来逐步消费累积的逻辑帧
        /// </summary>
        public void Update() {
            double elapsed = _stopwatch.Elapsed.TotalSeconds;
            _stopwatch.Restart();

            if (elapsed > MaxElapsed) {
                elapsed = MaxElapsed;
            }

            _accumulator += elapsed;
        }

        /// <summary>
        /// 尝试消费一个固定步长的逻辑帧<br/>
        /// 如果累积时间足够，消费一帧并返回 <see langword="true"/>，否则返回 <see langword="false"/><br/>
        /// 典型使用方式：
        /// <code>
        /// timer.Update();
        /// while (timer.Tick()) {
        ///     //执行一次固定步长的逻辑更新
        /// }
        /// </code>
        /// </summary>
        /// <returns>如果消费了一帧返回 <see langword="true"/>，否则返回 <see langword="false"/></returns>
        public bool Tick() {
            if (_accumulator >= FixedDeltaTime) {
                _accumulator -= FixedDeltaTime;
                return true;
            }
            return false;
        }

        /// <summary>
        /// 重置累积器和计时器
        /// </summary>
        public void Reset() {
            _accumulator = 0;
            _stopwatch.Restart();
        }
    }
}
