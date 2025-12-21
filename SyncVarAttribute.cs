using System;

namespace InnoVault.Actors
{
    /// <summary>
    /// 标记应在网络同步的字段或属性
    /// </summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public class SyncVarAttribute : Attribute { }
}
