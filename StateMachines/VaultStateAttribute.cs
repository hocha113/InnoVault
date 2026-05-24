using System;

namespace InnoVault.StateMachines
{
    /// <summary>
    /// 标记一个<see cref="IVaultState{TContext}"/>实现类，用于在<see cref="VaultStateMachineLoader"/>加载阶段被反射收集，<br/>
    /// 然后注入到对应的<see cref="VaultStateRegistry{TContext}"/>中，<b>消除手写的</b><c>CreateStateFromIndex</c><b>工厂 switch</b>
    /// </summary>
    /// <remarks>
    /// 约定：
    /// <list type="bullet">
    /// <item>被标记的类必须实现 <see cref="IVaultState{TContext}"/>（或继承<see cref="VaultState{TContext}"/>）</item>
    /// <item>必须含<b>无参</b>构造函数；带参数的运行时数据应通过<see cref="VaultStateMachine{TContext}.Blackboard"/>或<see cref="IVaultState{TContext}.OnEnter"/>注入</item>
    /// <item><see cref="Id"/>在同一<see cref="ContextType"/>内必须唯一，否则后注册者会覆盖前者并打出 <see cref="VaultMod.LoggerError"/> 警告</item>
    /// </list>
    /// </remarks>
    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public sealed class VaultStateAttribute : Attribute
    {
        /// <summary>
        /// 状态在同一<see cref="ContextType"/>注册表中的唯一整型 ID，建议从 0 起按枚举顺序分配
        /// </summary>
        public int Id { get; }
        /// <summary>
        /// 状态所属的上下文类型；同一类型的所有<see cref="VaultStateAttribute"/>共享同一<see cref="VaultStateRegistry{TContext}"/>
        /// </summary>
        public Type ContextType { get; }

        /// <summary>
        /// 构造一个新的<see cref="VaultStateAttribute"/>
        /// </summary>
        /// <param name="id">状态唯一 ID</param>
        /// <param name="contextType">所属上下文类型，必须与<see cref="IVaultState{TContext}"/>的<c>TContext</c>一致</param>
        public VaultStateAttribute(int id, Type contextType) {
            Id = id;
            ContextType = contextType;
        }
    }
}
