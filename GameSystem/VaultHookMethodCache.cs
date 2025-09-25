using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Terraria.ModLoader.Core;

namespace InnoVault.GameSystem
{
    /// <summary>
    /// 为某个特定的钩子创建一个预先筛选过的、高性能的实例列表
    /// </summary>
    public class VaultHookMethodCache<TVault> where TVault : VaultType<TVault>
    {
        private TVault[] hookInstanceCache;
        /// <summary>
        /// 用于筛选的钩子查询
        /// </summary>
        public LoaderUtils.MethodOverrideQuery<TVault> HookOverrideQuery { get; }
        /// <summary>
        /// 此钩子列表所对应的方法
        /// </summary>
        public MethodInfo Method => HookOverrideQuery.Method;
        /// <summary>
        /// 创建一个新的钩子列表
        /// </summary>
        /// <param name="hook"></param>
        public VaultHookMethodCache(LoaderUtils.MethodOverrideQuery<TVault> hook) {
            HookOverrideQuery = hook;
            RefreshHookInstances();
        }
        /// <summary>
        /// 枚举此钩子列表中的所有实例
        /// </summary>
        public ReadOnlySpan<TVault> Enumerate() => hookInstanceCache;
        /// <summary>
        /// 重新筛选此钩子列表
        /// </summary>
        public void RefreshHookInstances() => hookInstanceCache = [.. VaultTypeRegistry<TVault>.RegisteredVaults.Where(HookOverrideQuery.HasOverride)];
        /// <summary>
        /// 创建一个新的钩子列表
        /// </summary>
        /// <param name="expr"></param>
        /// <returns></returns>
        public static VaultHookMethodCache<TVault> Create(Expression<Func<TVault, Delegate>> expr) => Create<Delegate>(expr);
        /// <summary>
        /// 创建一个新的钩子列表
        /// </summary>
        /// <typeparam name="F"></typeparam>
        /// <param name="expr"></param>
        /// <returns></returns>
        public static VaultHookMethodCache<TVault> Create<F>(Expression<Func<TVault, F>> expr) where F : Delegate => new VaultHookMethodCache<TVault>(expr.ToOverrideQuery());
    }

    /// <summary>
    /// 负责在Mod加载时注册所有的 VaultType 实例
    /// 这是一个内部管理类，用于建立一个所有 VaultType 实例的“主列表”
    /// </summary>
    public static class VaultTypeRegistry<TVault> where TVault : VaultType<TVault>
    {
        private static bool loadingFinished = false;
        private readonly static List<TVault> _registeredVaults = [];

        ///<summary>
        ///所有已注册的 VaultType 实例在加载完成前为空
        ///</summary>
        public static IReadOnlyList<TVault> RegisteredVaults { get; private set; } = Array.Empty<TVault>();
        /// <summary>
        /// 在内容加载时调用，用于注册一个新的 VaultType 实例
        /// </summary>
        /// <param name="global"></param>
        public static void Register(TVault global) {
            if (loadingFinished) {
                return;
            }
            _registeredVaults.Add(global);
        }

        ///<summary>
        ///在所有内容加载后，但在 SetupContent 之前调用，用于最终确定列表
        ///</summary>
        public static void CompleteLoading() {
            if (loadingFinished) {
                return;
            }
            loadingFinished = true;
            RegisteredVaults = _registeredVaults.ToArray();
        }

        ///<summary>
        ///在卸载时调用，用于清空静态列表和状态
        ///</summary>
        public static void ClearRegisteredVaults() {
            loadingFinished = false;
            _registeredVaults.Clear();
            RegisteredVaults = Array.Empty<TVault>();
        }
    }
}