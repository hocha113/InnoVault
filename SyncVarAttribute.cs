using System;

namespace InnoVault
{
    /// <summary>
    /// 标记应在网络同步的字段或属性
    /// <br>该API的使用介绍:<see href="https://innovault.wiki/cn/persistence/sync-var/"/></br>
    /// </summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public class SyncVarAttribute : Attribute { }
}
