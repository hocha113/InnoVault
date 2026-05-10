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
        /// 当前类型在<see cref="UIHandleLoader"/>中注册的唯一实例。<br/>
        /// 若类型尚未注册（例如模组未加载或被禁用），将抛出异常；<br/>
        /// 若需要在不确定是否注册的场景下安全访问，请使用<see cref="InstanceOrNull"/>或<see cref="TryGetInstance"/>
        /// </summary>
        public static TSelf Instance => UIHandleLoader.GetUIHandleOfType<TSelf>();

        /// <summary>
        /// 与<see cref="Instance"/>等价，但在类型未注册时返回<see langword="null"/>而不是抛出异常。<br/>
        /// 适合在 <c>using Mod = ...</c> 之类可选依赖场景中使用
        /// </summary>
        public static TSelf InstanceOrNull {
            get {
                if (!UIHandleLoader.UIHandle_Type_To_ID.TryGetValue(typeof(TSelf), out int id)) {
                    return null;
                }
                if (!UIHandleLoader.UIHandle_ID_To_Instance.TryGetValue(id, out UIHandle ui)) {
                    return null;
                }
                return ui as TSelf;
            }
        }

        /// <summary>
        /// 尝试获取当前类型的注册实例
        /// </summary>
        /// <param name="instance">当返回<see langword="true"/>时为该实例，否则为<see langword="null"/></param>
        /// <returns>类型已注册返回<see langword="true"/>，否则<see langword="false"/></returns>
        public static bool TryGetInstance(out TSelf instance) {
            instance = InstanceOrNull;
            return instance != null;
        }
    }
}
