using System;

namespace InnoVault.StateMachines
{
    /// <summary>
    /// <see cref="Blackboard"/>的强类型键<br/>
    /// 键由<see cref="Name"/>与<typeparamref name="T"/>类型签名共同决定：<br/>
    /// 不同泛型参数下即使名称相同也算<b>不同</b>的键，避免类型混淆
    /// </summary>
    /// <typeparam name="T">该键对应值的强类型</typeparam>
    public readonly struct BlackboardKey<T> : IEquatable<BlackboardKey<T>>
    {
        /// <summary>
        /// 该键的字符串名，<see cref="Blackboard"/>内部以"<see cref="Name"/> + 类型签名"作为字典 key
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// 构造一个新的强类型键。<paramref name="name"/>应在同一上下文内保持唯一<br/>
        /// 推荐做法是将<see cref="BlackboardKey{T}"/>声明为<c>static readonly</c>常量，避免每帧分配
        /// </summary>
        /// <param name="name">键名</param>
        public BlackboardKey(string name) {
            Name = name;
        }

        /// <inheritdoc/>
        public bool Equals(BlackboardKey<T> other) => Name == other.Name;
        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is BlackboardKey<T> other && Equals(other);
        /// <inheritdoc/>
        public override int GetHashCode() => Name?.GetHashCode() ?? 0;
        /// <inheritdoc/>
        public override string ToString() => $"{Name}:{typeof(T).Name}";
    }
}
