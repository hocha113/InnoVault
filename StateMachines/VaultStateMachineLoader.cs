using System;
using System.Reflection;

namespace InnoVault.StateMachines
{
    /// <summary>
    /// 状态机系统的总加载器：在<c>VaultMod.Load</c>阶段反射收集所有<see cref="VaultStateAttribute"/>标记的类型，<br/>
    /// 自动填充对应的<see cref="VaultStateRegistry{TContext}"/>；卸载时统一清空所有泛型注册表，避免热重载留下脏工厂<br/>
    /// 用户<b>不</b>需要继承或调用本类，框架已通过<see cref="IVaultLoader"/>自动注册到生命周期
    /// </summary>
    internal sealed class VaultStateMachineLoader : IVaultLoader
    {
        /// <summary>反射扫描"<see cref="VaultStateAttribute"/>标记的类型"并把它们登记到对应<see cref="VaultStateRegistry{TContext}"/>中</summary>
        void IVaultLoader.LoadData() {
            //sequentially 扫描所有 mod 类型；性能上一次性 O(N)，N 是 Mod 程序集中的类型总数（数千级别可忽略）
            Type[] allTypes = VaultUtils.GetAnyModCodeType();
            if (allTypes == null) {
                return;
            }

            foreach (Type type in allTypes) {
                if (type == null || !type.IsClass || type.IsAbstract) {
                    continue;
                }

                VaultStateAttribute attr;
                try {
                    attr = type.GetCustomAttribute<VaultStateAttribute>();
                } catch (Exception ex) {
                    VaultMod.LoggerError(
                        $"VaultStateMachineLoader:attr:{type.FullName}",
                        $"Failed to read VaultStateAttribute on {type.FullName}: {ex.Message}");
                    continue;
                }
                if (attr == null) {
                    continue;
                }
                if (attr.ContextType == null) {
                    VaultMod.LoggerError(
                        $"VaultStateMachineLoader:bad_ctx:{type.FullName}",
                        $"VaultStateAttribute on {type.FullName} has null ContextType; skipped.");
                    continue;
                }

                //反射式调用 VaultStateRegistry<TContext>.Register(int, Type)
                try {
                    Type registryType = typeof(VaultStateRegistry<>).MakeGenericType(attr.ContextType);
                    MethodInfo register = registryType.GetMethod(
                        nameof(VaultStateRegistry<object>.Register),
                        BindingFlags.Public | BindingFlags.Static,
                        binder: null,
                        types: [typeof(int), typeof(Type)],
                        modifiers: null);
                    if (register == null) {
                        VaultMod.LoggerError(
                            $"VaultStateMachineLoader:no_register:{attr.ContextType.FullName}",
                            $"Could not find VaultStateRegistry<{attr.ContextType.FullName}>.Register(int, Type); state {type.FullName} skipped.");
                        continue;
                    }
                    register.Invoke(null, [attr.Id, type]);
                } catch (Exception ex) {
                    VaultMod.LoggerError(
                        $"VaultStateMachineLoader:invoke:{type.FullName}",
                        $"Failed to register state {type.FullName} (id={attr.Id}, ctx={attr.ContextType.FullName}): {ex.Message}");
                }
            }
        }

        /// <summary>统一清理所有泛型<see cref="VaultStateRegistry{TContext}"/>与所有探针，避免热重载残留</summary>
        void IVaultLoader.UnLoadData() {
            VaultStateRegistryManager.ClearAll();
            StateMachineDebugger.ClearAll();
        }
    }
}
