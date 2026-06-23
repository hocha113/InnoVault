using System;
using System.Collections.Generic;

namespace InnoVault.StateMachines
{
    /// <summary>
    /// 攻击/状态选择策略的抽象<br/>
    /// 用于消除"在<see cref="IVaultState{TContext}.OnUpdate"/>里手写一长串 <c>switch (counter % N)</c>" 的复制<br/>
    /// 三种内置实现：<see cref="FixedSequencePicker{T}"/>（固定序列）、<see cref="WeightedRandomPicker{T}"/>（权重随机）、<see cref="RoundRobinPicker{T}"/>（轮询）<br/>
    /// 多人模式约定：服务端调用<see cref="Pick"/>后，<b>必须</b>把返回索引同步到客户端（通过<c>ai[]</c>或<see cref="Blackboard"/>）；<br/>
    /// 客户端不直接调用<see cref="Pick"/>，而是用同步过来的索引在<see cref="Options"/>上索引取值
    /// </summary>
    /// <typeparam name="T">候选项类型，常见为<see cref="IVaultState{TContext}"/>的派生类型</typeparam>
    public interface IAttackPicker<T>
    {
        /// <summary>
        /// 候选项列表（按插入顺序），客户端可以用同步过来的索引直接取值
        /// </summary>
        IReadOnlyList<T> Options { get; }
        /// <summary>
        /// 服务端在确认要切换攻击/状态时调用：返回("选中的选项", "选中的索引")<br/>
        /// 调用方应当把<paramref name="seed"/>设为"上一帧累计的某个稳定值"（比如 <see cref="VaultStateMachine{TContext}.Blackboard"/>里的<c>AttackCounter</c>），<br/>
        /// 这样服务端与单机端在同一帧拿到相同结果，仅服务端结果会被同步出去
        /// </summary>
        (T Item, int Index) Pick(int seed);
    }

    /// <summary>
    /// 按"固定序列"取下一个选项，使用方需自己维护并同步索引；本类是纯无状态计算器<br/>
    /// 对应 CO <c>DestroyerPatrolState.ChooseNextAttack</c>" 用<c>AttackPhaseIndex % sequence.Length</c>"的写法
    /// </summary>
    public sealed class FixedSequencePicker<T> : IAttackPicker<T>
    {
        private readonly List<T> _options;

        /// <summary>
        /// 构造一个固定序列选择器
        /// </summary>
        public FixedSequencePicker(IEnumerable<T> options) {
            _options = new List<T>(options ?? throw new ArgumentNullException(nameof(options)));
            if (_options.Count == 0) {
                throw new ArgumentException("Options must contain at least one element.", nameof(options));
            }
        }

        /// <inheritdoc/>
        public IReadOnlyList<T> Options => _options;

        /// <inheritdoc/>
        public (T Item, int Index) Pick(int seed) {
            //允许负值的"截断到非负"取模
            int idx = ((seed % _options.Count) + _options.Count) % _options.Count;
            return (_options[idx], idx);
        }
    }

    /// <summary>
    /// 权重随机选择器：基于<see cref="Pick"/>传入的<c>seed</c>构造本地<see cref="Random"/>做"无状态"权重采样<br/>
    /// 同一<c>seed</c>始终返回同一结果——服务端只需把<c>seed</c>同步到客户端即可保证多端一致<br/>
    /// 服务端通常用<see cref="Terraria.Main"/>.<c>rand.Next()</c>作为种子，<b>不要</b>每端各自调用<see cref="Terraria.Main.rand"/>否则会不同步
    /// </summary>
    public sealed class WeightedRandomPicker<T> : IAttackPicker<T>
    {
        private readonly List<T> _options;
        private readonly List<float> _weights;
        private readonly float _totalWeight;

        /// <summary>
        /// 构造一个权重随机选择器
        /// </summary>
        public WeightedRandomPicker(IEnumerable<(T Option, float Weight)> entries) {
            if (entries == null) {
                throw new ArgumentNullException(nameof(entries));
            }
            _options = [];
            _weights = [];
            float total = 0f;
            foreach ((T option, float weight) in entries) {
                if (weight <= 0f) {
                    continue;
                }
                _options.Add(option);
                _weights.Add(weight);
                total += weight;
            }
            if (_options.Count == 0) {
                throw new ArgumentException("Entries must contain at least one option with positive weight.", nameof(entries));
            }
            _totalWeight = total;
        }

        /// <inheritdoc/>
        public IReadOnlyList<T> Options => _options;

        /// <inheritdoc/>
        public (T Item, int Index) Pick(int seed) {
            //本地新建 Random 避免任何外部全局 RNG 干扰；seed 来自调用方（必须同步过来）
            Random rng = new Random(seed);
            float roll = (float)rng.NextDouble() * _totalWeight;
            float acc = 0f;
            for (int i = 0; i < _weights.Count; i++) {
                acc += _weights[i];
                if (roll <= acc) {
                    return (_options[i], i);
                }
            }
            //浮点累加导致最后兜底
            return (_options[^1], _options.Count - 1);
        }
    }

    /// <summary>
    /// 轮询选择器：与<see cref="FixedSequencePicker{T}"/>等价，但在 API 上更明确"每帧只前进一格"<br/>
    /// 调用方应每次 Pick 时传入"递增 1"的 seed
    /// </summary>
    public sealed class RoundRobinPicker<T> : IAttackPicker<T>
    {
        private readonly List<T> _options;

        /// <summary>
        /// 构造一个轮询选择器
        /// </summary>
        public RoundRobinPicker(IEnumerable<T> options) {
            _options = new List<T>(options ?? throw new ArgumentNullException(nameof(options)));
            if (_options.Count == 0) {
                throw new ArgumentException("Options must contain at least one element.", nameof(options));
            }
        }

        /// <inheritdoc/>
        public IReadOnlyList<T> Options => _options;

        /// <inheritdoc/>
        public (T Item, int Index) Pick(int seed) {
            int idx = ((seed % _options.Count) + _options.Count) % _options.Count;
            return (_options[idx], idx);
        }
    }
}
