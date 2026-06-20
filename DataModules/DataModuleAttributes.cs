using System;

namespace InnoVault.DataModules
{
    /// <summary>
    /// 标记某个 public 字段 / 属性不参与 <see cref="DataModule"/> 默认反射保存。
    /// 适用于运行时缓存、临时状态、由其它字段派生出的值。
    /// </summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public sealed class DataModuleIgnoreAttribute : Attribute
    {
    }

    /// <summary>
    /// 指定某个 public 字段 / 属性使用自定义持久化键名，并可声明旧键名用于字段改名迁移。<br/>
    /// 保存时始终写入 <see cref="Name"/>；读取时会依次尝试 <see cref="Name"/> 与 <see cref="Aliases"/>。
    /// </summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public sealed class DataModuleNameAttribute : Attribute
    {
        /// <summary>当前持久化键名</summary>
        public string Name { get; }

        /// <summary>旧持久化键名，用于兼容字段改名前的存档</summary>
        public string[] Aliases { get; }

        /// <summary>创建一个自定义持久化键名</summary>
        public DataModuleNameAttribute(string name, params string[] aliases)
        {
            Name = name;
            Aliases = aliases ?? [];
        }
    }
}
