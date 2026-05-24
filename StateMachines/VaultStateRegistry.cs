using System;
using System.Collections.Generic;
using System.Linq.Expressions;

namespace InnoVault.StateMachines
{
    /// <summary>
    /// 每个<typeparamref name="TContext"/>对应一份的"ID ↔ 状态工厂"注册表<br/>
    /// 内容由<see cref="VaultStateMachineLoader"/>在<c>LoadData</c>阶段扫描<see cref="VaultStateAttribute"/>填充<br/>
    /// 也支持运行时手动调用<see cref="Register(int, Type, Func{IVaultState{TContext}})"/>注入不依赖反射的工厂（例如带闭包参数的特殊用例）
    /// </summary>
    /// <typeparam name="TContext">状态机上下文类型</typeparam>
    public static class VaultStateRegistry<TContext>
    {
        //此处使用泛型静态，每个 TContext 是独立的存储。"清空所有泛型注册表"由 VaultStateRegistryManager 维护
        private static readonly Dictionary<int, Func<IVaultState<TContext>>> _idToFactory = [];
        private static readonly Dictionary<Type, int> _typeToId = [];
        private static bool _registeredWithManager;

        /// <summary>
        /// 当前注册表中存在的状态条目数量，仅用于调试展示
        /// </summary>
        public static int Count => _idToFactory.Count;

        /// <summary>
        /// 显式注册一个状态工厂；若<paramref name="id"/>已被占用会覆盖并通过<see cref="VaultMod.LoggerError"/>打出警告<br/>
        /// 通常无需手动调用，<see cref="VaultStateMachineLoader"/>会在 Load 时为所有<see cref="VaultStateAttribute"/>标记的类自动注入
        /// </summary>
        public static void Register(int id, Type stateType, Func<IVaultState<TContext>> factory) {
            EnsureRegisteredWithManager();
            if (_idToFactory.ContainsKey(id)) {
                VaultMod.LoggerError(
                    $"VaultStateRegistry<{typeof(TContext).Name}>:{id}",
                    $"VaultState id {id} already registered for context {typeof(TContext).FullName}; later registration overwrites the previous one.");
            }
            _idToFactory[id] = factory;
            if (stateType != null) {
                _typeToId[stateType] = id;
            }
        }

        /// <summary>
        /// 反射友好的便捷重载：传入状态类型，框架自动编译其无参构造为<see cref="Func{TResult}"/><br/>
        /// 编译失败（无无参构造、抽象类等）会通过<see cref="VaultMod.LoggerError"/>打出警告并跳过
        /// </summary>
        public static void Register(int id, Type stateType) {
            if (stateType == null) {
                return;
            }
            if (!typeof(IVaultState<TContext>).IsAssignableFrom(stateType)) {
                VaultMod.LoggerError(
                    $"VaultStateRegistry<{typeof(TContext).Name}>:bad_type:{stateType.FullName}",
                    $"Type {stateType.FullName} does not implement IVaultState<{typeof(TContext).Name}>; skipped.");
                return;
            }
            if (stateType.IsAbstract || stateType.GetConstructor(Type.EmptyTypes) == null) {
                VaultMod.LoggerError(
                    $"VaultStateRegistry<{typeof(TContext).Name}>:no_ctor:{stateType.FullName}",
                    $"Type {stateType.FullName} does not have a public parameterless constructor; skipped.");
                return;
            }
            Func<IVaultState<TContext>> factory;
            try {
                factory = Expression.Lambda<Func<IVaultState<TContext>>>(
                    Expression.Convert(Expression.New(stateType), typeof(IVaultState<TContext>))).Compile();
            } catch (Exception ex) {
                VaultMod.LoggerError(
                    $"VaultStateRegistry<{typeof(TContext).Name}>:compile:{stateType.FullName}",
                    $"Failed to compile factory for {stateType.FullName}: {ex.Message}");
                return;
            }
            Register(id, stateType, factory);
        }

        /// <summary>
        /// 由 ID 创建一个新的状态实例。未注册的 ID 会返回<see langword="null"/>并打出警告
        /// </summary>
        public static IVaultState<TContext> Create(int id) {
            if (_idToFactory.TryGetValue(id, out Func<IVaultState<TContext>> factory)) {
                return factory();
            }
            VaultMod.LoggerError(
                $"VaultStateRegistry<{typeof(TContext).Name}>:missing:{id}",
                $"VaultState id {id} is not registered for context {typeof(TContext).FullName}.");
            return null;
        }

        /// <summary>
        /// 由具体类型反查它在注册表中的 ID；未注册时返回 -1<br/>
        /// 主要用于<see cref="VaultState{TContext}.StateId"/>的默认实现
        /// </summary>
        public static int GetIdFor(Type stateType) {
            if (stateType != null && _typeToId.TryGetValue(stateType, out int id)) {
                return id;
            }
            return -1;
        }

        /// <summary>
        /// 清空当前注册表。仅供<see cref="VaultStateRegistryManager"/>在卸载时调用，业务代码不应直接使用
        /// </summary>
        internal static void Clear() {
            _idToFactory.Clear();
            _typeToId.Clear();
            //不重置 _registeredWithManager；下一次 Register 时若 Manager 已被卸载，会重新注册一次清理委托
        }

        private static void EnsureRegisteredWithManager() {
            if (_registeredWithManager) {
                return;
            }
            _registeredWithManager = true;
            VaultStateRegistryManager.RegisterClearAction(Clear);
        }
    }
}
