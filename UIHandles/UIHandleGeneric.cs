namespace InnoVault.UIHandles
{
    /// <summary>
    /// <see cref="UIHandle"/>的泛型变体，自带类型安全的<see cref="Instance"/>静态访问器
    /// <para/>
    /// 子类继承时仅需写
    /// <code>class MyUI : UIHandle&lt;MyUI&gt;</code>
    /// 即可省掉
    /// <code>public static MyUI Instance =&gt; UIHandleLoader.GetUIHandleOfType&lt;MyUI&gt;();</code>
    /// 这样的样板代码
    /// </summary>
    /// <remarks>
    /// 该类不引入任何额外开销，仅是在<see cref="UIHandleLoader.GetUIHandleOfType{T}"/>之上提供一个统一的入口<br/>
    /// 由于<see cref="UIHandleLoader"/>的查表本身就是 O(1)，这里没有必要再加静态缓存以避免模组热重载时的悬挂引用
    /// </remarks>
    /// <typeparam name="TSelf">具体子类类型，必须继承<see cref="UIHandle{TSelf}"/></typeparam>
    public abstract class UIHandle<TSelf> : UIHandle where TSelf : UIHandle<TSelf>
    {
        /// <summary>
        /// 当前类型在<see cref="UIHandleLoader"/>中注册的唯一实例
        /// </summary>
        public static TSelf Instance => UIHandleLoader.GetUIHandleOfType<TSelf>();
    }
}
