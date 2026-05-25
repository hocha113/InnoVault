using System;
using System.Collections.Generic;

namespace InnoVault.StateMachines
{
    /// <summary>
    /// 通用强类型参数存储，被<see cref="VaultStateMachine{TContext}"/>与<c>BehaviorTrees</c>子系统共享<br/>
    /// 模型对齐 Unity Animator 的 Parameters，但保持纯运行时数据结构、无序列化负担<br/>
    /// 用法：声明<c>static readonly BlackboardKey&lt;float&gt; SomeKey = new("some_key");</c>后<c>Set/Get</c>
    /// </summary>
    /// <remarks>
    /// 线程模型：本类<b>非</b>线程安全，假定所有读写都在游戏主循环（Update / AI 钩子）内发生<br/>
    /// 如果将来要从其他线程（如异步贴图加载）写入，请改用<see cref="System.Collections.Concurrent.ConcurrentDictionary{TKey, TValue}"/>
    /// </remarks>
    public sealed class Blackboard
    {
        //内部用 (Type, Name) 作为复合键，避免不同类型同名键冲突
        private readonly Dictionary<(Type, string), object> _entries = [];

        /// <summary>
        /// 任一键发生<see cref="Set{T}"/>时触发，参数为<see cref="BlackboardKey{T}.Name"/><br/>
        /// 转移条件可订阅该事件做"沿"语义触发（也可直接在转移条件里读最新值，事件主要供 UI / 日志使用）
        /// </summary>
        public event Action<string> OnChanged;

        /// <summary>
        /// 写入或更新一个键值。若值类型与已有值不同会被覆盖<br/>
        /// 只有当新旧值不等（按<see cref="EqualityComparer{T}.Default"/>）时才会触发<see cref="OnChanged"/>，<br/>
        /// 避免频繁的"写入相同值"导致 UI / 转移条件出现无意义的重评估
        /// </summary>
        public void Set<T>(BlackboardKey<T> key, T value) {
            (Type, string) lookup = (typeof(T), key.Name);
            if (_entries.TryGetValue(lookup, out object boxed) && boxed is T existing
                && EqualityComparer<T>.Default.Equals(existing, value)) {
                _entries[lookup] = value;
                return;
            }
            _entries[lookup] = value;
            OnChanged?.Invoke(key.Name);
        }

        /// <summary>
        /// 读取指定键的值；若不存在则返回<paramref name="defaultValue"/>，<b>不会</b>抛出异常
        /// </summary>
        public T Get<T>(BlackboardKey<T> key, T defaultValue = default) {
            if (_entries.TryGetValue((typeof(T), key.Name), out object boxed) && boxed is T typed) {
                return typed;
            }
            return defaultValue;
        }

        /// <summary>
        /// 尝试读取指定键的值，返回值表示是否命中
        /// </summary>
        public bool TryGet<T>(BlackboardKey<T> key, out T value) {
            if (_entries.TryGetValue((typeof(T), key.Name), out object boxed) && boxed is T typed) {
                value = typed;
                return true;
            }
            value = default;
            return false;
        }

        /// <summary>
        /// 判断指定键是否已被赋值（类型也要匹配）
        /// </summary>
        public bool Contains<T>(BlackboardKey<T> key)
            => _entries.ContainsKey((typeof(T), key.Name));

        /// <summary>
        /// 移除指定键，返回是否真的存在并被移除<br/>
        /// 注意：移除不会触发<see cref="OnChanged"/>，因为消费者一般用"是否存在"判断，而非"是否被改"
        /// </summary>
        public bool Remove<T>(BlackboardKey<T> key)
            => _entries.Remove((typeof(T), key.Name));

        /// <summary>
        /// 清空所有键值。供热重载或上下文换帧前重置使用
        /// </summary>
        public void Clear() => _entries.Clear();

        /// <summary>
        /// 当前已注册的键值数量，仅用于调试展示
        /// </summary>
        public int Count => _entries.Count;

        /// <summary>
        /// 提供调试器用的只读快照：返回(类型, 键名, 装箱值)三元组序列<br/>
        /// 不要把它当成业务 API 使用——遍历过程会装箱所有值类型
        /// </summary>
        internal IEnumerable<(Type Type, string Name, object Value)> Snapshot() {
            foreach (KeyValuePair<(Type, string), object> kvp in _entries) {
                yield return (kvp.Key.Item1, kvp.Key.Item2, kvp.Value);
            }
        }
    }
}
