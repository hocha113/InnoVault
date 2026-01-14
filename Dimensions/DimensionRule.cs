namespace InnoVault.Dimensions
{
    /// <summary>
    /// 维度规则基类,用于定义维度的特殊规则
    /// </summary>
    public abstract class DimensionRule
    {
        /// <summary>
        /// 规则名称
        /// </summary>
        public abstract string Name { get; }

        /// <summary>
        /// 规则是否激活
        /// </summary>
        public virtual bool IsActive => true;

        /// <summary>
        /// 应用规则
        /// </summary>
        public abstract void Apply();

        /// <summary>
        /// 移除规则效果
        /// </summary>
        public abstract void Remove();
    }
}
